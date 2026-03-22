using MessagePack;

namespace MsgRpc;

/// <summary>
/// Represents the standard request data contract transmitted over the network.
/// This structure encapsulates the routing information and method arguments for an RPC call.
/// </summary>
/// <remarks>
/// This is a high-performance, immutable <see langword="readonly struct"/> designed for 
/// efficient MessagePack serialization. The <see cref="KeyAttribute"/> indices are fixed 
/// to ensure binary compatibility between the client and server.
/// </remarks>
[MessagePackObject]
public readonly struct MsgRpcRequest
{
    /// <summary>
    /// Gets the unique name of the target service to be invoked.
    /// </summary>
    [Key(0)] 
    public string ServiceName { get; init; }

    /// <summary>
    /// Gets the unique numeric identifier for the specific method within the service.
    /// </summary>
    [Key(1)] 
    public uint MethodId { get; init; }

    /// <summary>
    /// Gets the serialized binary payload containing the method's input parameters.
    /// </summary>
    [Key(2)] 
    public byte[] Parameters { get; init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MsgRpcRequest"/> struct.
    /// </summary>
    /// <param name="serviceName">The target service name.</param>
    /// <param name="methodId">The target method ID.</param>
    /// <param name="parameters">The serialized parameter byte array.</param>
    public MsgRpcRequest(string serviceName, uint methodId, byte[] parameters)
    {
        ServiceName = serviceName;
        MethodId = methodId;
        Parameters = parameters;
    }
}