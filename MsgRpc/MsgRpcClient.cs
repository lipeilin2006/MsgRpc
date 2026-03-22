using System.Buffers;
using MessagePack;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MsgRpc.Abstractions;

namespace MsgRpc;

/// <summary>
/// A high-performance RPC client implementation that manages service invocations,
/// serialization via MessagePack, and request-response correlation.
/// </summary>
public sealed partial class MsgRpcClient : IMsgRpcClient
{
    private readonly IClientTransport _transport;
    private readonly ILogger<MsgRpcClient> _logger;
    private readonly MessagePackSerializerOptions _options;
    private uint _nextRequestId;

    /// <summary>
    /// Initializes a new instance of the <see cref="MsgRpcClient"/> class.
    /// </summary>
    /// <param name="transport">The underlying transport layer for network communication.</param>
    /// <param name="options">The MessagePack serialization settings to ensure compatibility with the server.</param>
    /// <param name="logger">Optional logger for diagnostic information.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="transport"/> is null.</exception>
    public MsgRpcClient(
        IClientTransport transport, 
        MessagePackSerializerOptions options,
        ILogger<MsgRpcClient>? logger = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _options = options ?? MessagePackSerializerOptions.Standard;
        _logger = logger ?? NullLogger<MsgRpcClient>.Instance;
    }

    #region High-Performance Logging (LoggerMessage Pattern)

    [LoggerMessage(EventId = 301, Level = LogLevel.Debug, Message = "[Client] Initiating call: {ServiceName}.{MethodId} [ReqId:{RequestId}]")]
    private static partial void LogCalling(ILogger logger, string serviceName, uint methodId, uint requestId);

    [LoggerMessage(EventId = 302, Level = LogLevel.Error, Message = "[Client] Call failed [ReqId:{RequestId}]: {Error}")]
    private static partial void LogCallError(ILogger logger, uint requestId, string error);

    #endregion

    /// <summary>
    /// Gets a value indicating whether the client is ready to send requests.
    /// </summary>
    public bool IsAvailable => _transport.IsConnected;

    /// <summary>
    /// Executes a remote procedure call asynchronously.
    /// </summary>
    /// <param name="serviceName">The name of the service to be invoked.</param>
    /// <param name="methodId">The specific method identifier within the service.</param>
    /// <param name="requestPayload">The binary payload representing method arguments.</param>
    /// <param name="ct">A cancellation token for the request lifecycle.</param>
    /// <returns>The raw binary sequence containing the method result.</returns>
    /// <exception cref="MsgRpcException">Thrown when the remote server returns a functional error.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the request is canceled via the <paramref name="ct"/>.</exception>
    public async ValueTask<ReadOnlySequence<byte>> CallAsync(
        string serviceName, 
        uint methodId, 
        ReadOnlySequence<byte> requestPayload, 
        CancellationToken ct)
    {
        // Atomically increment the request ID for multiplexing correlation
        uint requestId = Interlocked.Increment(ref _nextRequestId);
        LogCalling(_logger, serviceName, methodId, requestId);

        try
        {
            // 1. Encapsulate the request DTO
            var request = new MsgRpcRequest
            {
                ServiceName = serviceName,
                MethodId = methodId,
                // Note: ToArray() is used for simplicity; consider using BufferWriter for 0-copy optimization.
                Parameters = requestPayload.ToArray() 
            };

            // 2. Serialize using the injected MessagePack options
            var writer = new ArrayBufferWriter<byte>(512);
            MessagePackSerializer.Serialize(writer, request, _options);
            
            // 3. Hand over the data to the transport layer
            await _transport.SendAsync(requestId, new ReadOnlySequence<byte>(writer.WrittenMemory), ct);

            // 4. Wait for the specific response (transport handles matching the ID)
            var responseBuffer = await _transport.ReceiveResponseAsync(requestId, ct);

            // 5. Deserialize the response DTO
            var response = MessagePackSerializer.Deserialize<MsgRpcResponse>(responseBuffer, _options);

            if (!response.IsSuccess)
            {
                var error = response.ErrorMessage ?? "Unknown server-side error.";
                LogCallError(_logger, requestId, error);
                throw new MsgRpcException($"Server returned an exception: {error}");
            }

            // 6. Return the raw result payload to the caller
            return new ReadOnlySequence<byte>(response.ResultData);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogCallError(_logger, requestId, ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Asynchronously releases the resources used by the client and its transport.
    /// </summary>
    public ValueTask DisposeAsync() => _transport.DisposeAsync();
}