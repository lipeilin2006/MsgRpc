using System.Buffers;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MsgRpc.Abstractions;

namespace MsgRpc;

/// <summary>
/// A resilience decorator for <see cref="IMsgRpcClient"/> that provides automatic 
/// retry logic with exponential backoff and jitter for transient failures.
/// </summary>
public sealed partial class ReliableMsgRpcClient : IMsgRpcClient
{
    private readonly IMsgRpcClient _inner;
    private readonly ILogger<ReliableMsgRpcClient> _logger;
    private readonly int _maxRetries;
    private readonly int _baseDelayMs;
    private readonly Random _jitterer = new();
    private bool _isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReliableMsgRpcClient"/> class.
    /// </summary>
    /// <param name="inner">The underlying RPC client to be wrapped.</param>
    /// <param name="maxRetries">The maximum number of retry attempts before giving up.</param>
    /// <param name="baseDelayMs">The initial delay in milliseconds for the exponential backoff.</param>
    /// <param name="logger">Optional logger for monitoring retry attempts.</param>
    public ReliableMsgRpcClient(
        IMsgRpcClient inner, 
        int maxRetries = 3, 
        int baseDelayMs = 200, 
        ILogger<ReliableMsgRpcClient>? logger = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _logger = logger ?? NullLogger<ReliableMsgRpcClient>.Instance;
        _maxRetries = maxRetries;
        _baseDelayMs = baseDelayMs;
    }

    #region High-Performance Logging (LoggerMessage Pattern)

    // Retry Log: Records only the message string without the full Exception object.
    // This maintains high performance and avoids expensive stack trace string allocations 
    // during high-concurrency retry scenarios.
    [LoggerMessage(EventId = 401, Level = LogLevel.Warning, 
        Message = "[RpcRetry] Call to {Service}:{Method} failed (Attempt {Attempt}). Reason: {Error}. Retrying in {Delay}ms...")]
    private static partial void LogRetry(ILogger logger, string service, uint method, int attempt, string error, int delay);

    // Exhausted Log: Passes the full Exception object so the logger can capture 
    // the complete stack trace for terminal failures, aiding in deep troubleshooting.
    [LoggerMessage(EventId = 402, Level = LogLevel.Error, 
        Message = "[RpcRetry] Call to {Service}:{Method} reached max retries ({MaxRetries}). Aborting.")]
    private static partial void LogRetryExhausted(ILogger logger, string service, uint method, int maxRetries, Exception ex);

    #endregion

    /// <summary>
    /// Gets a value indicating whether both the decorator and the underlying client are available.
    /// </summary>
    public bool IsAvailable => !_isDisposed && _inner.IsAvailable;

    /// <summary>
    /// Executes an RPC call with a built-in retry policy for transient errors.
    /// </summary>
    /// <param name="serviceName">The target service name.</param>
    /// <param name="methodId">The target method ID.</param>
    /// <param name="requestPayload">The binary payload for the request.</param>
    /// <param name="ct">The cancellation token for the operation.</param>
    /// <returns>The raw result byte sequence from the remote service.</returns>
    /// <exception cref="ObjectDisposedException">Thrown if the client has been disposed.</exception>
    public async ValueTask<ReadOnlySequence<byte>> CallAsync(
        string serviceName, 
        uint methodId, 
        ReadOnlySequence<byte> requestPayload, 
        CancellationToken ct)
    {
        int attempt = 0;

        while (true)
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(ReliableMsgRpcClient));

            try
            {
                // Attempt the actual RPC call using the inner client
                return await _inner.CallAsync(serviceName, methodId, requestPayload, ct);
            }
            catch (Exception ex) when (IsTransient(ex) && attempt < _maxRetries)
            {
                attempt++;
                
                // Exponential Backoff calculation: base * 2^(attempt-1)
                var delay = _baseDelayMs * Math.Pow(2, attempt - 1);
                
                // Apply Jitter (±20% variance) to prevent the "Thundering Herd" problem, 
                // where multiple clients retry at the exact same moment and overwhelm the server.
                var jitteredDelay = (int)(delay * (0.8 + _jitterer.NextDouble() * 0.4));

                LogRetry(_logger, serviceName, methodId, attempt, ex.Message, jitteredDelay);

                try
                {
                    await Task.Delay(jitteredDelay, ct);
                }
                catch (OperationCanceledException)
                {
                    throw; // Respect the caller's cancellation request immediately.
                }
            }
            catch (Exception ex)
            {
                if (attempt >= _maxRetries)
                {
                    // Log the terminal failure with the full exception stack trace.
                    LogRetryExhausted(_logger, serviceName, methodId, _maxRetries, ex);
                }
                throw; // Rethrow the original exception to maintain the call stack.
            }
        }
    }

    /// <summary>
    /// Determines if an exception represents a transient failure (e.g., network flicker, 
    /// socket reset, or timeout) that is safe to retry.
    /// </summary>
    private static bool IsTransient(Exception ex)
    {
        if (ex is AggregateException ae)
        {
            return ae.InnerExceptions.Any(IsTransient);
        }

        return ex switch
        {
            IOException or SocketException => true,
            TimeoutException => true,
            // If the operation was canceled but the user's token didn't request it,
            // it usually indicates an internal network timeout.
            OperationCanceledException oce when !oce.CancellationToken.IsCancellationRequested => true,
            _ => false
        };
    }

    /// <summary>
    /// Asynchronously disposes the decorator and the underlying inner client.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        await _inner.DisposeAsync();
    }
}