using System.Buffers;
using System.Net;

namespace MsgRpc.Abstractions;

/// <summary>
/// Defines the server-side transport layer responsible for listening for incoming connections, 
/// receiving requests, and routing responses back to specific clients.
/// </summary>
public interface IServerTransport : IAsyncDisposable
{
    /// <summary>
    /// Starts the transport listener on the specified network endpoint and provides a 
    /// callback for processing received messages.
    /// </summary>
    /// <param name="endPoint">The network address (IP/Port or Unix Socket) to listen on.</param>
    /// <param name="onMessageReceived">
    /// An asynchronous callback invoked whenever a complete request is received.
    /// Parameters: ConnectionId (Guid), RequestId (uint), and Payload (ReadOnlySequence).
    /// </param>
    /// <param name="ct">A cancellation token to observe while the transport is running.</param>
    /// <returns>A <see cref="Task"/> that represents the listener's execution lifecycle.</returns>
    Task StartAsync(
        EndPoint endPoint, 
        Func<Guid, uint, ReadOnlySequence<byte>, Task> onMessageReceived, 
        CancellationToken ct);

    /// <summary>
    /// Asynchronously transmits a response payload back to a specific client connection.
    /// </summary>
    /// <param name="connectionId">The unique identifier of the target client connection.</param>
    /// <param name="requestId">The sequence ID used to correlate the response with the original request.</param>
    /// <param name="data">The serialized response data to be transmitted.</param>
    /// <param name="ct">A cancellation token to observe during the send operation.</param>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous transmit operation.</returns>
    /// <remarks>
    /// Implementations should use <paramref name="connectionId"/> to route the packet 
    /// through the correct underlying socket or channel.
    /// </remarks>
    ValueTask SendResponseAsync(
        Guid connectionId, 
        uint requestId, 
        ReadOnlyMemory<byte> data, 
        CancellationToken ct);
}