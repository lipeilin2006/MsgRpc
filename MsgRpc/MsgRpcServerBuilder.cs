using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MsgRpc.Abstractions;

namespace MsgRpc;

/// <summary>
/// A fluent builder for creating and configuring <see cref="IMsgRpcServer"/> instances.
/// Handles the registration of transports, services, and internal dispatchers within a dedicated DI container.
/// </summary>
public sealed class MsgRpcServerBuilder
{
    private readonly IServiceCollection _services = new ServiceCollection();
    private readonly List<Action<IMsgRpcServer, IServiceProvider>> _registrationActions = new();
    
    // Default serialization settings
    private MessagePackSerializerOptions _options = MessagePackSerializerOptions.Standard;

    /// <summary>
    /// Initializes a new instance of the <see cref="MsgRpcServerBuilder"/> with core server registrations.
    /// </summary>
    public MsgRpcServerBuilder()
    {
        // Register the primary server implementation by default
        _services.AddSingleton<IMsgRpcServer, MsgRpcServer>();
    }

    /// <summary>
    /// Configures the MessagePack serialization options used by the server.
    /// </summary>
    /// <param name="configure">A delegate to modify the default <see cref="MessagePackSerializerOptions"/>.</param>
    /// <returns>The current builder instance for method chaining.</returns>
    public MsgRpcServerBuilder WithOptions(Func<MessagePackSerializerOptions, MessagePackSerializerOptions> configure)
    {
        _options = configure(_options) ?? throw new ArgumentNullException(nameof(configure));
        return this;
    }

    /// <summary>
    /// Sets the transport layer implementation (e.g., TCP, Unix Sockets) for the server.
    /// </summary>
    /// <typeparam name="TTransport">The implementation type of <see cref="IServerTransport"/>.</typeparam>
    /// <returns>The current builder instance for method chaining.</returns>
    public MsgRpcServerBuilder WithTransport<TTransport>() where TTransport : class, IServerTransport
    {
        _services.AddSingleton<IServerTransport, TTransport>();
        return this;
    }

    /// <summary>
    /// Configures the logging infrastructure for the server and its components.
    /// </summary>
    /// <param name="configure">A delegate to configure the <see cref="ILoggingBuilder"/>.</param>
    /// <returns>The current builder instance for method chaining.</returns>
    public MsgRpcServerBuilder WithLogging(Action<ILoggingBuilder> configure)
    {
        _services.AddLogging(configure);
        return this;
    }

    /// <summary>
    /// Registers a business service interface and its implementation into the server's dependency container.
    /// </summary>
    /// <typeparam name="TInterface">The service interface type.</typeparam>
    /// <typeparam name="TImplementation">The concrete class implementing the interface.</typeparam>
    /// <returns>The current builder instance for method chaining.</returns>
    public MsgRpcServerBuilder AddService<TInterface, TImplementation>() 
        where TInterface : class 
        where TImplementation : class, TInterface
    {
        _services.AddSingleton<TInterface, TImplementation>();
        return this;
    }

    /// <summary>
    /// Internal method used by the Source Generator to register method dispatchers 
    /// that bridge transport requests to service implementations.
    /// </summary>
    /// <param name="action">A registration delegate provided by the generated code.</param>
    /// <returns>The current builder instance for method chaining.</returns>
    public MsgRpcServerBuilder RegisterDispatcherInternal(Action<IMsgRpcServer, IServiceProvider> action)
    {
        _registrationActions.Add(action);
        return this;
    }

    /// <summary>
    /// Builds the internal <see cref="IServiceProvider"/> and returns a fully initialized <see cref="IMsgRpcServer"/>.
    /// </summary>
    /// <returns>A configured and ready-to-start RPC server.</returns>
    /// <remarks>
    /// This method resolves all dependencies and executes the dispatcher binding logic 
    /// required for the Source-Generated routing to function.
    /// </remarks>
    public IMsgRpcServer Build()
    {
        // 1. Register the finalized Options into DI so MsgRpcServer can inject it
        _services.AddSingleton(_options);

        // 2. Build the internal DI container
        var serviceProvider = _services.BuildServiceProvider();

        // 3. Resolve the server instance (Dependencies like Transport and Logger are injected here)
        var server = serviceProvider.GetRequiredService<IMsgRpcServer>();

        // 4. Execute all deferred dispatcher registrations
        foreach (var action in _registrationActions)
        {
            action(server, serviceProvider);
        }

        return server;
    }
}