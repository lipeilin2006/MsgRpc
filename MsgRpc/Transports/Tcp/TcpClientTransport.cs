using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MsgRpc.Abstractions;

namespace MsgRpc.Transports.Tcp;

/// <summary>
/// A high-performance TCP transport implementation for the MsgRpc client.
/// Leverages <see cref="System.IO.Pipelines"/> for efficient memory management 
/// and supports request-response multiplexing over a single connection.
/// </summary>
public sealed partial class TcpClientTransport : IClientTransport
{
    private readonly ILogger<TcpClientTransport> _logger;
    private TcpClient? _client;
    private PipeReader? _reader;
    private PipeWriter? _writer;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    
    /// <summary>
    /// Tracks active requests waiting for a response from the server.
    /// Key: RequestId, Value: TaskCompletionSource to be resolved by the receive loop.
    /// </summary>
    private readonly ConcurrentDictionary<uint, TaskCompletionSource<ReadOnlySequence<byte>>> _pendingRequests = new();
    private readonly CancellationTokenSource _cts = new();

    private EndPoint? _remoteEndPoint;
    private bool _isDisposed;

    /// <summary>
    /// Gets a value indicating whether the underlying TCP client is connected and ready for transmission.
    /// </summary>
    public bool IsConnected => _client != null && _client.Connected && _writer != null;

    /// <summary>
    /// Initializes a new instance of the <see cref="TcpClientTransport"/> class.
    /// </summary>
    /// <param name="logger">The logger instance provided by Dependency Injection.</param>
    public TcpClientTransport(ILogger<TcpClientTransport>? logger = null)
    {
        _logger = logger ?? NullLogger<TcpClientTransport>.Instance;
    }

    #region High-Performance Logging (LoggerMessage)

    [LoggerMessage(EventId = 201, Level = LogLevel.Information, Message = "[Transport] Connecting to {EndPoint}...")]
    private static partial void LogConnecting(ILogger logger, EndPoint? endPoint);

    [LoggerMessage(EventId = 202, Level = LogLevel.Information, Message = "[Transport] Connection established: {EndPoint}")]
    private static partial void LogConnected(ILogger logger, EndPoint? endPoint);

    [LoggerMessage(EventId = 203, Level = LogLevel.Error, Message = "[Transport] Connection failed: {Message}")]
    private static partial void LogConnectionFailed(ILogger logger, string message, Exception ex);

    [LoggerMessage(EventId = 204, Level = LogLevel.Warning, Message = "[Transport] Receive loop exited abnormally: {Message}")]
    private static partial void LogReadLoopError(ILogger logger, string message);

    #endregion

    /// <summary>
    /// Initiates a connection to the specified remote endpoint.
    /// </summary>
    public async Task ConnectAsync(EndPoint endPoint, CancellationToken ct)
    {
        _remoteEndPoint = endPoint;
        await EnsureConnectedAsync(ct);
    }

    /// <summary>
    /// Ensures a connection is active, performing a thread-safe reconnection if necessary.
    /// </summary>
    private async Task EnsureConnectedAsync(CancellationToken ct)
    {
        if (IsConnected) return;

        await _sendLock.WaitAsync(ct);
        try
        {
            if (IsConnected) return;

            await CleanupInternalAsync();

            LogConnecting(_logger, _remoteEndPoint);
            _client = new TcpClient { NoDelay = true }; // Disable Nagle's algorithm for low latency

            using var timeoutCt = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCt.CancelAfter(5000); // 5-second connection timeout

            await _client.ConnectAsync((IPEndPoint)_remoteEndPoint!, timeoutCt.Token);

            var stream = _client.GetStream();
            _reader = PipeReader.Create(stream);
            _writer = PipeWriter.Create(stream);

            // Start the background background receive loop to handle incoming responses
            _ = ReceiveLoopAsync(_cts.Token);
            LogConnected(_logger, _remoteEndPoint);
        }
        catch (Exception ex)
        {
            LogConnectionFailed(_logger, ex.Message, ex);
            throw;
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>
    /// Serializes and sends a request packet to the server.
    /// Automatically manages the packet header (TotalLength + RequestId).
    /// </summary>
    public async ValueTask SendAsync(uint requestId, ReadOnlySequence<byte> data, CancellationToken ct)
    {
        if (!IsConnected) await EnsureConnectedAsync(ct);

        await _sendLock.WaitAsync(ct);
        try
        {
            if (_writer == null) throw new IOException("Transport is not ready for transmission.");

            // TotalLength includes the 4 bytes of RequestId + BodyLength
            int totalLength = (int)data.Length + 4;
            var headerSpan = _writer.GetMemory(8).Span;
            BinaryPrimitives.WriteInt32LittleEndian(headerSpan[..4], totalLength);
            BinaryPrimitives.WriteUInt32LittleEndian(headerSpan[4..8], requestId);
            _writer.Advance(8);

            // Copy data segments into the pipe buffer
            foreach (var segment in data)
                _writer.Write(segment.Span);

            var result = await _writer.FlushAsync(ct);
            if (result.IsCompleted) HandleConnectionLoss();
        }
        catch (Exception)
        {
            HandleConnectionLoss();
            throw;
        }
        finally { _sendLock.Release(); }
    }

    /// <summary>
    /// Dedicated background loop to read incoming data from the socket, 
    /// parse packet headers, and resolve pending request tasks.
    /// </summary>
    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var reader = _reader;
        if (reader == null) return;

        // Reuse a small buffer for parsing fixed-length headers
        byte[] headerBuffer = ArrayPool<byte>.Shared.Rent(8);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var result = await reader.ReadAsync(ct);
                var buffer = result.Buffer;

                var consumed = buffer.Start;
                var examined = buffer.End;

                try
                {
                    while (true)
                    {
                        // Header must be at least 8 bytes (Length:4 + RequestId:4)
                        if (buffer.Length < 8)
                        {
                            examined = buffer.End;
                            break;
                        }

                        var headerSpan = headerBuffer.AsSpan(0, 8);
                        buffer.Slice(0, 8).CopyTo(headerSpan);

                        int totalLen = BinaryPrimitives.ReadInt32LittleEndian(headerSpan[..4]);
                        uint reqId = BinaryPrimitives.ReadUInt32LittleEndian(headerSpan[4..8]);

                        int bodyLen = totalLen - 4;
                        long totalPacketSize = 8 + bodyLen;

                        // Ensure the full packet body has arrived
                        if (buffer.Length < totalPacketSize)
                        {
                            examined = buffer.End;
                            break;
                        }

                        // Process payload: We must copy here because the Task completion 
                        // might be awaited asynchronously outside of this loop.
                        var payload = buffer.Slice(8, bodyLen);
                        var payloadCopy = new ReadOnlySequence<byte>(payload.ToArray());

                        if (_pendingRequests.TryRemove(reqId, out var tcs))
                        {
                            tcs.TrySetResult(payloadCopy);
                        }

                        buffer = buffer.Slice(totalPacketSize);
                        consumed = buffer.Start;
                        examined = consumed; // Reset examined position after successful packet parsing
                    }
                }
                finally
                {
                    reader.AdvanceTo(consumed, examined);
                }

                if (result.IsCompleted) break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogReadLoopError(_logger, ex.Message);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(headerBuffer);
            HandleConnectionLoss();
        }
    }

    /// <summary>
    /// Handles abrupt connection loss by failing all currently pending requests.
    /// </summary>
    private void HandleConnectionLoss()
    {
        if (_pendingRequests.IsEmpty) return;

        var ex = new IOException("The network connection has been severed.");
        foreach (var key in _pendingRequests.Keys)
        {
            if (_pendingRequests.TryRemove(key, out var tcs))
                tcs.TrySetException(ex);
        }
    }

    /// <summary>
    /// Performs internal resource cleanup for pipes and the underlying socket.
    /// </summary>
    private async Task CleanupInternalAsync()
    {
        try
        {
            if (_reader != null) await _reader.CompleteAsync();
            if (_writer != null) await _writer.CompleteAsync();
            _client?.Close();
            _client?.Dispose();
        }
        catch { /* Ignore cleanup errors to prevent cascading failures */ }
        finally
        {
            _reader = null;
            _writer = null;
            _client = null;
        }
    }

    /// <summary>
    /// Returns a task that completes when a response with the specified ID is received.
    /// Supports CancellationTokens for per-request timeouts.
    /// </summary>
    public async ValueTask<ReadOnlySequence<byte>> ReceiveResponseAsync(uint requestId, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<ReadOnlySequence<byte>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRequests[requestId] = tcs;

        // Register cancellation to avoid hanging tasks if the client times out
        using var registration = ct.Register(() => tcs.TrySetCanceled());

        try
        {
            return await tcs.Task;
        }
        catch (OperationCanceledException)
        {
            _pendingRequests.TryRemove(requestId, out _);
            throw;
        }
    }

    /// <summary>
    /// Asynchronously disposes the transport, cancelling the receive loop and closing connections.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _cts.Cancel();
        await CleanupInternalAsync();
        _sendLock.Dispose();
        _cts.Dispose();
    }
}