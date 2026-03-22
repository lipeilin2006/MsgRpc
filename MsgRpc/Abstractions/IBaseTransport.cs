namespace MsgRpc.Abstractions;

/// <summary>
/// Defines the fundamental contract for data transmission in the MsgRpc framework.
/// Provides low-level asynchronous byte-stream sending capabilities.
/// </summary>
public interface IBaseTransport
{
    /// <summary>
    /// Asynchronously sends a block of data to the remote endpoint.
    /// </summary>
    /// <param name="data">The memory region containing the bytes to be transmitted.</param>
    /// <param name="ct">A cancellation token to observe while waiting for the operation to complete.</param>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous send operation.</returns>
    /// <remarks>
    /// Implementations should ensure that the data is either copied or fully transmitted 
    /// before the returned task completes to avoid memory corruption.
    /// </remarks>
    ValueTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct);
}