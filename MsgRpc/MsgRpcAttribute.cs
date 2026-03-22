namespace MsgRpc;

/// <summary>
/// Marks a method as an RPC-callable operation within a service interface.
/// </summary>
/// <remarks>
/// This attribute is detected by the MsgRpc Source Generator at compile-time to 
/// register the method and map it to a specific identifier, enabling 
/// reflection-free RPC calls.
/// </remarks>
/// <param name="methodId">A unique numeric identifier for the method within the service.</param>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public class MsgRpcAttribute(uint methodId) : Attribute
{
    /// <summary>
    /// Gets or sets the unique numeric identifier for the RPC method.
    /// </summary>
    public uint MethodId { get; set; } = methodId;
}