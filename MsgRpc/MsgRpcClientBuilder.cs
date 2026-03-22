using System.Net;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MsgRpc.Abstractions;
using MsgRpc.Transports.Tcp;

namespace MsgRpc;

/// <summary>
/// A fluent builder for creating and configuring <see cref="IMsgRpcClient"/> instances.
/// Manages dependency registration, transport connectivity, and resilience layers.
/// </summary>
public sealed class MsgRpcClientBuilder
{
    private readonly IServiceCollection _services = new ServiceCollection();
    private EndPoint? _endPoint;

    // Default serialization settings
    private MessagePackSerializerOptions _options = MessagePackSerializerOptions.Standard;

    // Default reliability parameters
    private bool _useReliableLayer = true;
    private int _maxRetries = 3;
    private int _baseDelayMs = 200;

    /// <summary>
    /// Initializes a new instance of the <see cref="MsgRpcClientBuilder"/> with default services.
    /// </summary>
    public MsgRpcClientBuilder()
    {
        // 1. Register core components by default
        _services.AddSingleton<IClientTransport, TcpClientTransport>();
        _services.AddSingleton<IMsgRpcClient, MsgRpcClient>();
        
        // Register basic logging infrastructure to prevent errors if WithLogging is not called
        _services.AddLogging(); 
    }

    /// <summary>
    /// Configures the MessagePack serialization options, ensuring symmetry with the server.
    /// </summary>
    /// <param name="configure">A delegate to modify the default <see cref="MessagePackSerializerOptions"/>.</param>
    /// <returns>The current builder instance for method chaining.</returns>
    public MsgRpcClientBuilder WithOptions(Func<MessagePackSerializerOptions, MessagePackSerializerOptions> configure)
    {
        _options = configure(_options) ?? throw new ArgumentNullException(nameof(configure));
        return this;
    }

    /// <summary>
    /// Integrates standard .NET logging into the client.
    /// </summary>
    /// <param name="configure">A delegate to configure the <see cref="ILoggingBuilder"/>.</param>
    /// <returns>The current builder instance for method chaining.</returns>
    public MsgRpcClientBuilder WithLogging(Action<ILoggingBuilder> configure)
    {
        _services.AddLogging(configure);
        return this;
    }

    /// <summary>
    /// Sets the target server endpoint for the client connection.
    /// </summary>
    /// <param name="endPoint">The network address of the RPC server.</param>
    /// <returns>The current builder instance for method chaining.</returns>
    public MsgRpcClientBuilder WithEndPoint(EndPoint endPoint)
    {
        _endPoint = endPoint ?? throw new ArgumentNullException(nameof(endPoint));
        return this;
    }

    /// <summary>
    /// Overrides the default transport implementation with a custom one.
    /// </summary>
    /// <typeparam name="TTransport">The implementation type of <see cref="IClientTransport"/>.</typeparam>
    /// <returns>The current builder instance for method chaining.</returns>
    public MsgRpcClientBuilder WithTransport<TTransport>() where TTransport : class, IClientTransport
    {
        _services.AddSingleton<IClientTransport, TTransport>();
        return this;
    }

    /// <summary>
    /// Configures the retry policy and enables the reliability decorator.
    /// </summary>
    /// <param name="maxRetries">The maximum number of retry attempts for transient failures.</param>
    /// <param name="baseDelayMs">The initial delay for the exponential backoff strategy.</param>
    /// <returns>The current builder instance for method chaining.</returns>
    public MsgRpcClientBuilder WithRetryPolicy(int maxRetries = 3, int baseDelayMs = 200)
    {
        _useReliableLayer = maxRetries > 0;
        _maxRetries = maxRetries;
        _baseDelayMs = baseDelayMs;
        return this;
    }

    /// <summary>
    /// Builds and initializes a ready-to-use <see cref="IMsgRpcClient"/>.
    /// This method resolves the internal DI container and establishes the initial connection.
    /// </summary>
    /// <param name="ct">A cancellation token for the build and connection process.</param>
    /// <returns>A fully configured and connected RPC client instance.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a required configuration (e.g., EndPoint) is missing.</exception>
    public async Task<IMsgRpcClient> BuildAsync(CancellationToken ct = default)
    {
        if (_endPoint == null)
            throw new InvalidOperationException("The target endpoint must be configured using WithEndPoint().");

        // 1. Register Options in DI to allow automatic injection into MsgRpcClient
        _services.AddSingleton(_options);

        // 2. Build the ServiceProvider (internal DI container)
        var serviceProvider = _services.BuildServiceProvider();

        // 3. Resolve and connect the transport layer
        var transport = serviceProvider.GetRequiredService<IClientTransport>();
        
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        connectCts.CancelAfter(TimeSpan.FromSeconds(10)); // Enforce a 10-second connection timeout
        await transport.ConnectAsync(_endPoint, connectCts.Token);

        // 4. Resolve the base client (DI automatically injects Transport, Options, and Logger)
        IMsgRpcClient client = serviceProvider.GetRequiredService<IMsgRpcClient>();

        // 5. Wrap the client with the Reliability Decorator if enabled
        if (_useReliableLayer)
        {
            var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
            var reliableLogger = loggerFactory.CreateLogger<ReliableMsgRpcClient>();
            
            client = new ReliableMsgRpcClient(client, _maxRetries, _baseDelayMs, reliableLogger);
        }

        return client;
    }
}