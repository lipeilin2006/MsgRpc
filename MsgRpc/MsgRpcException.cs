namespace MsgRpc;

/// <summary>
/// The base exception thrown by the MsgRpc framework when an RPC-specific error occurs.
/// </summary>
/// <remarks>
/// This exception typically represents functional or protocol errors, such as 
/// service mismatch, method execution failure on the server, or malformed RPC packets.
/// </remarks>
public class MsgRpcException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MsgRpcException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public MsgRpcException(string message) : base(message) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="MsgRpcException"/> class with a specified error message 
    /// and a reference to the inner exception that is the cause of this exception.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <param name="innerException">
    /// The exception that is the cause of the current exception, or a null reference if no inner exception is specified.
    /// </param>
    public MsgRpcException(string message, Exception innerException) 
        : base(message, innerException) { }
}