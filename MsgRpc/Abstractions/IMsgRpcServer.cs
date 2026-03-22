using System.Buffers;
using System.Net;

namespace MsgRpc.Abstractions;

/// <summary>
/// Defines the core contract for the MsgRpc server, responsible for 
/// service registration, lifecycle management, and request dispatching.
/// </summary>
public interface IMsgRpcServer : IAsyncDisposable
{
    /// <summary>
    /// Starts listening for incoming RPC requests on the specified network endpoint.
    /// </summary>
    /// <param name="endPoint">The <see cref="EndPoint"/> (e.g., IP/Port or Unix Domain Socket) to bind the server to.</param>
    /// <returns>A <see cref="Task"/> that represents the asynchronous startup operation.</returns>
    Task StartAsync(EndPoint endPoint);

    /// <summary>
    /// Registers a high-level service implementation.
    /// This is typically invoked by a Source Generator or a reflection-based dispatcher.
    /// </summary>
    /// <typeparam name="TService">The type of the service interface.</typeparam>
    /// <param name="serviceName">The unique name used to identify the service across the network.</param>
    /// <param name="implementation">The concrete instance that provides the service logic.</param>
    void RegisterService<TService>(string serviceName, TService implementation) where TService : class;

    /// <summary>
    /// The entry point for request processing, intended for internal framework use.
    /// </summary>
    /// <param name="connectionId">A unique identifier for the underlying transport connection.</param>
    /// <param name="requestId">The sequence number of the request, used to align the response on the client side.</param>
    /// <param name="payload">The raw byte sequence representing the request parameters.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous processing of the request.</returns>
    Task HandleRequestAsync(Guid connectionId, uint requestId, ReadOnlySequence<byte> payload);

    /// <summary>
    /// Registers a low-level service dispatcher.
    /// This allows the framework to bypass reflection by using pre-compiled method mappings, 
    /// which is ideal for performance and AOT compatibility.
    /// </summary>
    /// <param name="serviceName">The unique name of the service.</param>
    /// <param name="dispatcher">The pre-generated dispatcher instance responsible for routing method calls.</param>
    void RegisterServiceInternal(string serviceName, IServiceDispatcher dispatcher);
}