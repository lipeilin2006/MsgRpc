using System.Diagnostics;
using System.Net;
using MsgRpc;
using MsgRpc.Transports.Tcp;
using MsgRpc.Test;
using Microsoft.Extensions.Logging;
using MessagePack;

// --- 1. Environment Setup ---
// Initialize a console logger factory for diagnostic output.
using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());

// Define the local loopback endpoint for the benchmark.
var endPoint = new IPEndPoint(IPAddress.Loopback, 6000);

// Global control token: Automatically signals cancellation after 10 seconds to stop the test.
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

// Thread-safe counters for tracking performance metrics.
long totalRequests = 0;
long totalErrors = 0;

// --- 2. Server Initialization ---
// Build the server using the fluent API. 
// Note: Compression (LZ4) is enabled here to test performance with serialization overhead.
var server = new MsgRpcServerBuilder()
    .WithTransport<TcpServerTransport>()
    .WithLogging(logging => { logging.AddConsole().SetMinimumLevel(LogLevel.Information); })
    .WithOptions(opt => opt.WithCompression(MessagePackCompression.Lz4BlockArray))
    .WithTestRpcService<TestRpcService>() // Registers the generated dispatcher for TestRpcService
    .Build();

// Start the server in a background task (non-blocking).
_ = server.StartAsync(endPoint);

// --- 3. Client Initialization ---
// Create the client and establish the initial connection.
await using var client = await new MsgRpcClientBuilder()
    .WithEndPoint(endPoint)
    .WithTransport<TcpClientTransport>()
    .WithLogging(logging => { logging.AddConsole().SetMinimumLevel(LogLevel.Information); })
    .WithOptions(opt => opt.WithCompression(MessagePackCompression.Lz4BlockArray))
    .WithRetryPolicy() // Wraps the client in a ReliableMsgRpcClient decorator
    .BuildAsync();

// Use the Source-Generated extension to create a typed proxy.
var service = client.CreateTestRpcService();

// --- 4. Warm-up ---
// JIT (Just-In-Time) compilation and initial connection setup can skew results.
// We perform 10 dummy calls to ensure the pipeline is "hot."
for (int i = 0; i < 10; i++) await service.SumAsync(i, i);
Console.WriteLine("[Bench] Warm-up complete. Starting 100-concurrency stress test...");

// --- 5. Benchmark Execution ---
var sw = Stopwatch.StartNew();

// Launch 100 parallel tasks to simulate high-load concurrency.
var tasks = Enumerable.Range(0, 100).Select(async i =>
{
    while (!cts.IsCancellationRequested)
    {
        try
        {
            // Execute the RPC call. 
            // In a production proxy, pass 'cts.Token' to allow immediate cancellation of pending IO.
            int result = await service.SumAsync(i, 1);

            // Verify the logic result to ensure no data corruption occurred during high load.
            if (result == i + 1) Interlocked.Increment(ref totalRequests);
            else Interlocked.Increment(ref totalErrors);
        }
        catch (OperationCanceledException)
        {
            // Expected behavior when the 10s timer expires.
            break;
        }
        catch
        {
            Interlocked.Increment(ref totalErrors);
            // Brief pause on error to prevent tight-looping if the server is temporarily overwhelmed.
            await Task.Delay(10, CancellationToken.None);
        }
    }
}).ToArray();

// --- Graceful Shutdown Logic ---
// Wait for all tasks to acknowledge the cancellation token.
// A 2-second grace period is added to prevent the bench from hanging on stray tasks.
var completedTask = await Task.WhenAny(Task.WhenAll(tasks), Task.Delay(2000));

if (completedTask != Task.WhenAll(tasks))
{
    Console.WriteLine("[Warning] Some tasks are slow to respond to cancellation. Forcing finalization.");
}

sw.Stop();

// --- 6. Results & Metrics ---
double totalSeconds = sw.Elapsed.TotalSeconds;
double qps = totalRequests / totalSeconds;

// Latency calculation: (Wall Time * Concurrent Threads) / Total Successful Requests.
// This gives a statistical average of how long a single request "felt" to a client thread.
double avgLatency = (totalSeconds * 1000 * 100) / Math.Max(1, totalRequests);

Console.WriteLine("\n" + new string('=', 40));
Console.WriteLine($"Elapsed Time:   {totalSeconds:F2}s");
Console.WriteLine($"Total Success:  {totalRequests:N0}");
Console.WriteLine($"Total Errors:   {totalErrors:N0}");
Console.WriteLine($"Average QPS:    {qps:F0}");
Console.WriteLine($"Avg Latency:    {avgLatency:F3} ms (@100 concurrency)");
Console.WriteLine(new string('=', 40));

// Ensure resources are released properly to avoid port exhaustion or hanging processes.
await server.DisposeAsync();
Console.WriteLine("[Bench] Benchmark finalized.");