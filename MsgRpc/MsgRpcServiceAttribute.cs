namespace MsgRpc;

/// <summary>
/// Marks an interface as an RPC-exposed service.
/// </summary>
/// <remarks>
/// This attribute triggers the MsgRpc Source Generator to analyze the interface 
/// at compile-time and automatically generate the corresponding dispatcher 
/// and registration extension methods.
/// </remarks>
/// <param name="serviceName">
/// An optional custom name for the service. If not provided, the fully qualified name 
/// of the interface is typically used as the default.
/// </param>
[AttributeUsage(AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
public class MsgRpcServiceAttribute(string? serviceName = null) : Attribute
{
    /// <summary>
    /// Gets or sets the unique name used to register and identify the service over the network.
    /// </summary>
    public string? ServiceName { get; set; } = serviceName;
}