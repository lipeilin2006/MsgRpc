using System.IO.Pipelines;
using System.Net.Sockets;

namespace MsgRpc.Transports.Tcp;

/// <summary>
/// Represents an active TCP network connection, encapsulating the underlying socket, 
/// high-performance IO pipelines, and synchronization primitives.
/// </summary>
internal sealed class ConnectionContext : IAsyncDisposable
{
    private bool _isDisposed;

    /// <summary>
    /// Gets the unique identifier for this specific connection instance.
    /// </summary>
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>
    /// Gets the underlying <see cref="TcpClient"/> instance.
    /// </summary>
    public TcpClient Client { get; }

    /// <summary>
    /// Gets the <see cref="PipeReader"/> used to asynchronously read incoming data streams.
    /// </summary>
    public PipeReader Reader { get; }

    /// <summary>
    /// Gets the <see cref="PipeWriter"/> used to asynchronously write outgoing data streams.
    /// </summary>
    public PipeWriter Writer { get; }
    
    /// <summary>
    /// Ensures that concurrent write operations to this connection are atomic and thread-safe.
    /// Prevents frame interleaving during high-concurrency transmission.
    /// </summary>
    public SemaphoreSlim SendLock { get; } = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="ConnectionContext"/> class.
    /// </summary>
    /// <param name="client">The established TCP client connection.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="client"/> is null.</exception>
    public ConnectionContext(TcpClient client)
    {
        Client = client ?? throw new ArgumentNullException(nameof(client));
        var stream = client.GetStream();
        
        // Initialize the IO pipelines using the network stream.
        // The lifecycle of the reader and writer is managed collectively by this context.
        Reader = PipeReader.Create(stream);
        Writer = PipeWriter.Create(stream);
    }

    /// <summary>
    /// Asynchronously releases all resources used by the connection, 
    /// performing a coordinated shutdown of the IO pipelines and the underlying socket.
    /// </summary>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous disposal operation.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        // 1. Complete the reader first to stop accepting new incoming requests.
        try
        {
            await Reader.CompleteAsync();
        }
        catch 
        { 
            /* Ignore reader closure exceptions to ensure full cleanup continues */ 
        }

        // 2. Attempt to gracefully complete the writer.
        try
        {
            // During high-pressure benchmark exits, the underlying socket might already 
            // be closed (ObjectDisposedException) or there may be residual data in the buffer.
            await Writer.CompleteAsync();
        }
        catch (Exception ex) when (ex is ObjectDisposedException or IOException or SocketException)
        {
            // Expected exceptions during abrupt connection teardown; suppressed to maintain stability.
        }
        catch (Exception)
        {
            // Re-throw unknown or critical exceptions for logging/debugging.
            throw;
        }

        // 3. Final cleanup of low-level resources.
        // The sequence is important: complete pipelines before disposing the client.
        Client.Dispose();
        SendLock.Dispose();
    }
}