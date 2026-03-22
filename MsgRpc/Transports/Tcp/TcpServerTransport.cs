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
/// A high-performance TCP server transport implementation using <see cref="System.IO.Pipelines"/>.
/// Handles connection management, packet framing, and asynchronous request dispatching.
/// </summary>
public sealed partial class TcpServerTransport : IServerTransport
{
    private TcpListener? _listener;
    private readonly ConcurrentDictionary<Guid, ConnectionContext> _connections = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly ILogger<TcpServerTransport> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TcpServerTransport"/> class.
    /// </summary>
    /// <param name="logger">The logger instance provided by Dependency Injection.</param>
    public TcpServerTransport(ILogger<TcpServerTransport>? logger = null)
    {
        _logger = logger ?? NullLogger<TcpServerTransport>.Instance;
    }

    #region High-Performance Logging (LoggerMessage Pattern)

    [LoggerMessage(EventId = 101, Level = LogLevel.Information, Message = "[Transport] Server started listening on: {EndPoint}")]
    private static partial void LogListening(ILogger logger, EndPoint endPoint);

    [LoggerMessage(EventId = 102, Level = LogLevel.Debug, Message = "[Transport] New connection established: {ConnectionId}")]
    private static partial void LogConnectionAccepted(ILogger logger, Guid connectionId);

    [LoggerMessage(EventId = 103, Level = LogLevel.Warning, Message = "[Transport] Exception in connection {ConnectionId}: {Message}")]
    private static partial void LogConnectionError(ILogger logger, Guid connectionId, string message);

    [LoggerMessage(EventId = 104, Level = LogLevel.Debug, Message = "[Transport] Connection closed gracefully: {ConnectionId}")]
    private static partial void LogConnectionClosed(ILogger logger, Guid connectionId);

    #endregion

    /// <summary>
    /// Starts the TCP listener and begins accepting incoming client connections.
    /// </summary>
    public async Task StartAsync(EndPoint endPoint, Func<Guid, uint, ReadOnlySequence<byte>, Task> onMessage, CancellationToken ct)
    {
        _listener = new TcpListener((IPEndPoint)endPoint);
        _listener.Start();
        
        LogListening(_logger, endPoint);

        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
            while (!linkedCts.Token.IsCancellationRequested)
            {
                // Accept new client connection
                var client = await _listener.AcceptTcpClientAsync(linkedCts.Token);
                client.NoDelay = true; // Disable Nagle's algorithm for immediate packet transmission
                
                var ctx = new ConnectionContext(client);
                _connections[ctx.Id] = ctx;
                
                LogConnectionAccepted(_logger, ctx.Id);

                // Start the receive loop for this specific connection in a separate task
                _ = Task.Run(() => HandleLoop(ctx, onMessage, linkedCts.Token), linkedCts.Token);
            }
        }
        catch (OperationCanceledException) { /* Expected shutdown behavior */ }
        finally { _listener.Stop(); }
    }

    /// <summary>
    /// Dedicated receive loop for an individual connection. 
    /// Manages packet framing and invokes the message received callback.
    /// </summary>
    private async Task HandleLoop(ConnectionContext ctx, Func<Guid, uint, ReadOnlySequence<byte>, Task> onMessage, CancellationToken ct)
    {
        // Rent a small buffer from the pool for efficient header parsing
        byte[] headerBuffer = ArrayPool<byte>.Shared.Rent(8);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var result = await ctx.Reader.ReadAsync(ct);
                var buffer = result.Buffer;

                while (true)
                {
                    // Minimum header size: 4 (Length) + 4 (RequestId) = 8 bytes
                    if (buffer.Length < 8) break;

                    buffer.Slice(0, 8).CopyTo(headerBuffer);
                    int totalLength = BinaryPrimitives.ReadInt32LittleEndian(headerBuffer.AsSpan(0, 4));
                    uint requestId = BinaryPrimitives.ReadUInt32LittleEndian(headerBuffer.AsSpan(4, 4));

                    // Basic security check: Validate packet size (Max 10MB)
                    if (totalLength < 4 || totalLength > 10 * 1024 * 1024)
                        throw new InvalidDataException($"Abnormal packet header length: {totalLength}");

                    int bodyLen = totalLength - 4;
                    long totalPacketSize = 8 + bodyLen;

                    // Ensure the full packet body is available in the pipe buffer
                    if (buffer.Length < totalPacketSize) break;

                    var payload = buffer.Slice(8, bodyLen);
                    
                    // Direct invocation of the message callback. 
                    // Note: No Task.Run here to reduce overhead, as the server implementation 
                    // handles its own threading strategy.
                    await onMessage(ctx.Id, requestId, payload);

                    // Advance the buffer window
                    buffer = buffer.Slice(totalPacketSize);
                }

                // Signal to the PipeReader how much data was consumed and examined
                ctx.Reader.AdvanceTo(buffer.Start, buffer.End);
                
                if (result.IsCompleted) break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogConnectionError(_logger, ctx.Id, ex.Message);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(headerBuffer);
            if (_connections.TryRemove(ctx.Id, out var closingCtx))
            {
                LogConnectionClosed(_logger, ctx.Id);
                await closingCtx.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Asynchronously sends a response back to the client. 
    /// Uses a per-connection lock to ensure atomic writes to the pipe.
    /// </summary>
    public async ValueTask SendResponseAsync(Guid connectionId, uint requestId, ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        if (!_connections.TryGetValue(connectionId, out var ctx)) return;

        // Acquire the send lock to prevent frame interleaving
        await ctx.SendLock.WaitAsync(ct);
        try
        {
            // Calculate total packet length (Body + 4 bytes for RequestId)
            int totalLength = data.Length + 4;
            var header = ctx.Writer.GetMemory(8).Span;
            
            BinaryPrimitives.WriteInt32LittleEndian(header[..4], totalLength);
            BinaryPrimitives.WriteUInt32LittleEndian(header[4..8], requestId);
            ctx.Writer.Advance(8);

            // Write the payload directly to the PipeWriter buffer
            ctx.Writer.Write(data.Span);
            
            // Asynchronously flush the data to the underlying socket stream
            await ctx.Writer.FlushAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Failed to send response [ReqId:{RequestId}]", requestId);
        }
        finally { ctx.SendLock.Release(); }
    }

    /// <summary>
    /// Disposes the transport, cancelling all active connection loops and stopping the listener.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        foreach (var ctx in _connections.Values) await ctx.DisposeAsync();
        _connections.Clear();
        _cts.Dispose();
    }
}