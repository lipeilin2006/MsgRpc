namespace MsgRpc.Test;

/// <summary>
/// Defines the contract for the Test RPC service.
/// The <see cref="MsgRpcServiceAttribute"/> marks this interface for Source Generation,
/// mapping it to the "Test" service identifier on the network.
/// </summary>
[MsgRpcService("Test")]
public interface ITestRpcService
{
    /// <summary>
    /// Computes the sum of two integers asynchronously.
    /// The <see cref="MsgRpcAttribute"/> assigns Method ID 0 to this function.
    /// </summary>
    /// <param name="a">The first integer operand.</param>
    /// <param name="b">The second integer operand.</param>
    /// <returns>A task representing the asynchronous operation, containing the sum.</returns>
    [MsgRpc(0)]
    public Task<int> SumAsync(int a, int b);
}

/// <summary>
/// A high-performance implementation of <see cref="ITestRpcService"/>.
/// This class contains the actual business logic executed on the server.
/// </summary>
public class TestRpcService : ITestRpcService
{
    /// <summary>
    /// Performs a pure CPU-bound calculation.
    /// </summary>
    /// <remarks>
    /// Since there is no I/O or blocking logic, we return a cached completed task 
    /// using <see cref="Task.FromResult{TResult}"/> to minimize allocation overhead.
    /// </remarks>
    public Task<int> SumAsync(int a, int b) => Task.FromResult(a + b);
}