using MessagePack;

namespace MsgRpc;

/// <summary>
/// Represents the standard response data contract returned from the RPC server.
/// Encapsulates the execution status, the result payload, and error details if applicable.
/// </summary>
/// <remarks>
/// This is a high-performance, immutable <see langword="readonly struct"/> designed for 
/// efficient MessagePack serialization. The <see cref="KeyAttribute"/> indices are fixed 
/// to ensure binary compatibility between the client and server.
/// </remarks>
[MessagePackObject]
public readonly struct MsgRpcResponse
{
    /// <summary>
    /// Gets a value indicating whether the RPC method was executed successfully on the server.
    /// </summary>
    [Key(0)] 
    public bool IsSuccess { get; init; }

    /// <summary>
    /// Gets the serialized binary payload containing the method's return value.
    /// This field is typically empty or null if <see cref="IsSuccess"/> is false.
    /// </summary>
    [Key(1)] 
    public byte[] ResultData { get; init; }

    /// <summary>
    /// Gets the detailed error message if the RPC call failed. 
    /// This field is null if <see cref="IsSuccess"/> is true.
    /// </summary>
    [Key(2)] 
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MsgRpcResponse"/> struct.
    /// </summary>
    /// <param name="isSuccess">Indicates the success status of the operation.</param>
    /// <param name="resultData">The serialized result byte array.</param>
    /// <param name="errorMessage">An optional error message for failed invocations.</param>
    public MsgRpcResponse(bool isSuccess, byte[] resultData, string? errorMessage = null)
    {
        IsSuccess = isSuccess;
        ResultData = resultData;
        ErrorMessage = errorMessage;
    }
}