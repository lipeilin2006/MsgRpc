using System.Buffers;
using System.Net;

namespace MsgRpc.Abstractions;

/// <summary>
/// Defines the client-side transport layer responsible for asynchronous communication 
/// with the underlying RPC server.
/// </summary>
public interface IClientTransport : IAsyncDisposable
{    
    /// <summary>
    /// Gets a value indicating whether the transport is currently connected to the server.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Establases an asynchronous connection to the specified network endpoint.
    /// </summary>
    /// <param name="endPoint">The target server address and port.</param>
    /// <param name="ct">A cancellation token to observe while connecting.</param>
    /// <returns>A <see cref="Task"/> that completes when the connection is established.</returns>
    Task ConnectAsync(EndPoint endPoint, CancellationToken ct);

    /// <summary>
    /// Sends a request payload to the server associated with a specific request identifier.
    /// </summary>
    /// <param name="requestId">The unique identifier for the request, used for multiplexing.</param>
    /// <param name="data">The serialized request data as a <see cref="ReadOnlySequence{Byte}"/>.</param>
    /// <param name="ct">A cancellation token to observe during the send operation.</param>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous send operation.</returns>
    ValueTask SendAsync(uint requestId, ReadOnlySequence<byte> data, CancellationToken ct);

    /// <summary>
    /// Waits for and receives the response corresponding to the specified request identifier.
    /// </summary>
    /// <param name="requestId">The ID used to match the response with the original request.</param>
    /// <param name="ct">A cancellation token to observe while waiting for the response.</param>
    /// <returns>A <see cref="ValueTask"/> containing the received response payload.</returns>
    /// <remarks>
    /// The implementation should handle response multiplexing, allowing multiple concurrent 
    /// requests to wait for their respective responses on the same transport.
    /// </remarks>
    ValueTask<ReadOnlySequence<byte>> ReceiveResponseAsync(uint requestId, CancellationToken ct);
}