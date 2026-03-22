using System.Buffers;

namespace MsgRpc.Abstractions;

/// <summary>
/// Defines the core RPC client contract for initiating standardized calls to remote services.
/// </summary>
public interface IMsgRpcClient : IAsyncDisposable
{
    /// <summary>
    /// Executes an asynchronous RPC invocation.
    /// </summary>
    /// <param name="serviceName">The unique name of the target service to call.</param>
    /// <param name="methodId">The identifier for the specific method to be executed on the server.</param>
    /// <param name="requestPayload">The serialized request parameters represented as a <see cref="ReadOnlySequence{Byte}"/>.</param>
    /// <param name="ct">A <see cref="CancellationToken"/> to control the call's lifecycle (e.g., hard timeouts).</param>
    /// <returns>A <see cref="ValueTask{T}"/> containing the raw response byte sequence.</returns>
    /// <exception cref="TimeoutException">Thrown when the call does not receive a response within the specified duration.</exception>
    /// <exception cref="IOException">Thrown when the underlying network connection is severed and cannot be recovered.</exception>
    /// <exception cref="MsgRpcException">Thrown when the server returns a functional or business-level error.</exception>
    ValueTask<ReadOnlySequence<byte>> CallAsync(
        string serviceName, 
        uint methodId, 
        ReadOnlySequence<byte> requestPayload, 
        CancellationToken ct);

    /// <summary>
    /// Gets a value indicating the current connectivity and availability status of the client.
    /// </summary>
    bool IsAvailable { get; }
}