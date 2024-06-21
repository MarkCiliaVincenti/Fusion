using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ActualLab.Interception;
using ActualLab.Rpc.Caching;
using ActualLab.Rpc.Clients;
using ActualLab.Rpc.Diagnostics;
using ActualLab.Rpc.Infrastructure;
using ActualLab.Rpc.Internal;
using ActualLab.Rpc.Serialization;

namespace ActualLab.Rpc;

public readonly struct RpcBuilder
{
    public IServiceCollection Services { get; }
    public RpcConfiguration Configuration { get; }

    [RequiresUnreferencedCode(UnreferencedCode.Rpc)]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Proxies))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RpcDefaultDelegates))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RpcMethodDef))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RpcServiceDef))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RpcServiceRegistry))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RpcConfiguration))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RpcByteArgumentSerializer))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RpcMethodTracer))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RpcMethodActivityCounters))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RpcClientInterceptor))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RpcInboundContext))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RpcInboundContextFactory))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RpcOutboundContext))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RpcInboundCall<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RpcInbound404Call<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RpcOutboundCall<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RpcMiddlewares<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RpcInboundMiddleware))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RpcOutboundMiddleware))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RpcServerPeer))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RpcClientPeer))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RpcHub))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RpcRemoteObjectTracker))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RpcSharedObjectTracker))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RpcSharedStream))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RpcCacheInfoCapture))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RpcCacheEntry))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RpcSystemCalls))]
    internal RpcBuilder(
        IServiceCollection services,
        Action<RpcBuilder>? configure)
    {
        Services = services;
        if (services.FindInstance<RpcConfiguration>() is { } configuration) {
            // Already configured
            Configuration = configuration;
            configure?.Invoke(this);
            return;
        }

        Configuration = services.AddInstance(new RpcConfiguration());
        services.AddSingleton(c => new RpcHub(c));

        // Common services
        services.TryAddSingleton(c => c.Clocks().SystemClock);
        services.AddSingleton(c => new RpcServiceRegistry(c));
        services.AddSingleton(_ => RpcDefaultDelegates.ServiceDefBuilder);
        services.AddSingleton(_ => RpcDefaultDelegates.MethodDefBuilder);
        services.AddSingleton(_ => RpcDefaultDelegates.ServiceScopeResolver);
        services.AddSingleton(_ => RpcDefaultDelegates.InboundCallFilter);
        services.AddSingleton(_ => RpcDefaultDelegates.CallRouter);
        services.AddSingleton(_ => RpcDefaultDelegates.RerouteDelayer);
        services.AddSingleton(_ => RpcDefaultDelegates.InboundContextFactory);
        services.AddSingleton(_ => RpcDefaultDelegates.PeerFactory);
        services.AddSingleton(_ => RpcDefaultDelegates.ClientConnectionFactory);
        services.AddSingleton(_ => RpcDefaultDelegates.ServerConnectionFactory);
        services.AddSingleton(_ => RpcDefaultDelegates.BackendServiceDetector);
        services.AddSingleton(_ => RpcDefaultDelegates.UnrecoverableErrorDetector);
        services.AddSingleton(_ => RpcDefaultDelegates.MethodTracerFactory);
        services.AddSingleton(_ => RpcDefaultDelegates.CallLoggerFactory);
        services.AddSingleton(_ => RpcDefaultDelegates.CallLoggerFilter);
        services.AddSingleton(_ => RpcArgumentSerializer.Default);
        services.AddSingleton(c => new RpcInboundMiddlewares(c));
        services.AddSingleton(c => new RpcOutboundMiddlewares(c));
        services.AddTransient(_ => new RpcInboundCallTracker());
        services.AddTransient(_ => new RpcOutboundCallTracker());
        services.AddTransient(_ => new RpcRemoteObjectTracker());
        services.AddTransient(_ => new RpcSharedObjectTracker());
        services.AddSingleton(c => new RpcClientPeerReconnectDelayer(c));
        services.AddSingleton(_ => RpcLimits.Default);

        // Interceptor options (the instances are created by RpcProxies)
        services.AddSingleton(_ => RpcClientInterceptor.Options.Default);
        services.AddSingleton(_ => RpcRoutingInterceptor.Options.Default);

        // System services
        if (!Configuration.Services.ContainsKey(typeof(IRpcSystemCalls))) {
            Service<IRpcSystemCalls>().HasServer<RpcSystemCalls>().HasName(RpcSystemCalls.Name);
            services.AddSingleton(c => new RpcSystemCalls(c));
            services.AddSingleton(c => new RpcSystemCallSender(c));
        }
    }

    // WebSocket client

    public RpcBuilder AddWebSocketClient(Uri hostUri)
        => AddWebSocketClient(_ => hostUri.ToString());

    public RpcBuilder AddWebSocketClient(string hostUrl)
        => AddWebSocketClient(_ => hostUrl);

    public RpcBuilder AddWebSocketClient(Func<IServiceProvider, string> hostUrlFactory)
        => AddWebSocketClient(c => RpcWebSocketClient.Options.Default with {
            HostUrlResolver = (_, _) => hostUrlFactory.Invoke(c),
        });

    public RpcBuilder AddWebSocketClient(Func<IServiceProvider, RpcWebSocketClient.Options>? optionsFactory = null)
    {
        var services = Services;
        services.AddSingleton(optionsFactory, _ => RpcWebSocketClient.Options.Default);
        if (services.HasService<RpcWebSocketClient>())
            return this;

        services.AddSingleton(c => new RpcWebSocketClient(
            c.GetRequiredService<RpcWebSocketClient.Options>(), c));
        services.AddAlias<RpcClient, RpcWebSocketClient>();
        return this;
    }

    // Share, Connect, Route

    [RequiresUnreferencedCode(UnreferencedCode.Rpc)]
    public RpcBuilder AddService<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TService>
        (RpcServiceMode mode, Symbol name = default)
        where TService : class
        => AddService(typeof(TService), mode, name);
    [RequiresUnreferencedCode(UnreferencedCode.Rpc)]
    public RpcBuilder AddService<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TService,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TServer>
        (RpcServiceMode mode, Symbol name = default)
        where TService : class
        where TServer : class, TService
        => AddService(typeof(TService), typeof(TServer), mode, name);
    [RequiresUnreferencedCode(UnreferencedCode.Rpc)]
    public RpcBuilder AddService(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type serviceType,
        RpcServiceMode mode, Symbol name = default)
        => AddService(serviceType, serviceType, mode, name);
    [RequiresUnreferencedCode(UnreferencedCode.Rpc)]
    public RpcBuilder AddService(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type serviceType,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type serverType,
        RpcServiceMode mode, Symbol name = default)
        => mode switch {
            RpcServiceMode.Local => this,
            RpcServiceMode.Server // IServer -> TServer
                => AddServer(serviceType, serverType, name),
            RpcServiceMode.ServerAndRouter // IServer -> routing client proxy, TServer -> server instance
                => AddServer(serviceType, serverType, name).AddClient(serviceType, serviceType, name),
            RpcServiceMode.Hybrid // TServer -> routing client proxy extending TServer
                => AddHybrid(serviceType, serverType, name),
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };

    [RequiresUnreferencedCode(UnreferencedCode.Rpc)]
    public RpcBuilder AddClient<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TService>
        (Symbol name = default)
        where TService : class
        => AddClient(typeof(TService), typeof(TService), name);
    [RequiresUnreferencedCode(UnreferencedCode.Rpc)]
    public RpcBuilder AddClient<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TService,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TProxyBase>
        (Symbol name = default)
        where TService : class
        where TProxyBase : class, TService
        => AddClient(typeof(TService), typeof(TProxyBase), name);
    [RequiresUnreferencedCode(UnreferencedCode.Rpc)]
    public RpcBuilder AddClient(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type serviceType,
        Symbol name = default)
        => AddClient(serviceType, serviceType, name);
    [RequiresUnreferencedCode(UnreferencedCode.Rpc)]
    public RpcBuilder AddClient(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type serviceType,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type proxyBaseType,
        Symbol name = default)
    {
        if (!serviceType.IsInterface)
            throw ActualLab.Internal.Errors.MustBeInterface(serviceType, nameof(serviceType));
        if (!typeof(IRpcService).IsAssignableFrom(serviceType))
            throw ActualLab.Internal.Errors.MustImplement<IRpcService>(serviceType, nameof(serviceType));
        if (!serviceType.IsAssignableFrom(proxyBaseType))
            throw ActualLab.Internal.Errors.MustBeAssignableTo(proxyBaseType, serviceType, nameof(proxyBaseType));

        Service(serviceType).HasName(name);
        Services.AddSingleton(proxyBaseType, c => RpcProxies.NewClientProxy(c, serviceType, proxyBaseType));
        if (serviceType != proxyBaseType)
            Services.AddAlias(serviceType, proxyBaseType);
        return this;
    }

    public RpcBuilder AddServer<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TService>
        (Symbol name = default)
        where TService : class
        => AddServer(typeof(TService), name);
    public RpcBuilder AddServer<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TService,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TServer>
        (Symbol name = default)
        where TService : class
        where TServer : class, TService
        => AddServer(typeof(TService), typeof(TServer), name);
    public RpcBuilder AddServer(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type serviceType,
        Symbol name = default)
        => AddServer(serviceType, serviceType, name);
    public RpcBuilder AddServer(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type serviceType,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type serverType,
        Symbol name = default)
    {
        if (!serviceType.IsInterface)
            throw ActualLab.Internal.Errors.MustBeInterface(serviceType, nameof(serviceType));
        if (!typeof(IRpcService).IsAssignableFrom(serviceType))
            throw ActualLab.Internal.Errors.MustImplement<IRpcService>(serviceType, nameof(serviceType));
        if (!serviceType.IsAssignableFrom(serverType))
            throw ActualLab.Internal.Errors.MustBeAssignableTo(serverType, serviceType, nameof(serverType));

        Service(serviceType).HasServer(serverType).HasName(name);
        if (!serverType.IsInterface)
            Services.AddSingleton(serverType);
        if (serviceType != serverType)
            Services.AddAlias(serviceType, serverType);
        return this;
    }

    [RequiresUnreferencedCode(UnreferencedCode.Rpc)]
    public RpcBuilder AddHybrid<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TService,
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TServer>
        (Symbol name = default)
        where TService : class
        where TServer : class, TService
        => AddHybrid(typeof(TService), typeof(TServer), name);
    [RequiresUnreferencedCode(UnreferencedCode.Rpc)]
    public RpcBuilder AddHybrid(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type serviceType,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type serverType,
        Symbol name = default)
    {
        if (!serviceType.IsInterface)
            throw ActualLab.Internal.Errors.MustBeInterface(serviceType, nameof(serviceType));
        if (!typeof(IRpcService).IsAssignableFrom(serviceType))
            throw ActualLab.Internal.Errors.MustImplement<IRpcService>(serviceType, nameof(serviceType));
        if (!serverType.IsClass)
            throw ActualLab.Internal.Errors.MustBeClass(serverType, nameof(serverType));
        if (!serviceType.IsAssignableFrom(serverType))
            throw ActualLab.Internal.Errors.MustBeAssignableTo(serverType, serviceType, nameof(serverType));

        Service(serviceType).HasServer(serverType).HasName(name);
        Services.AddSingleton(serverType, c => RpcProxies.NewHybridProxy(c, serviceType, serverType));
        if (serviceType != serverType)
            Services.AddAlias(serviceType, serverType);
        return this;
    }

    // More low-level configuration options stuff

    public RpcServiceBuilder Service<TService>()
        => Service(typeof(TService));

    public RpcServiceBuilder Service(Type serviceType)
    {
        if (Configuration.Services.TryGetValue(serviceType, out var service))
            return service;

        service = new RpcServiceBuilder(this, serviceType);
        Configuration.Services.Add(serviceType, service);
        return service;
    }

    public RpcBuilder AddInboundMiddleware<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMiddleware>
        (Func<IServiceProvider, TMiddleware>? factory = null)
        where TMiddleware : RpcInboundMiddleware
        => AddInboundMiddleware(typeof(TMiddleware), factory);

    public RpcBuilder AddInboundMiddleware(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type middlewareType,
        Func<IServiceProvider, object>? factory = null)
    {
        if (!typeof(RpcInboundMiddleware).IsAssignableFrom(middlewareType))
            throw ActualLab.Internal.Errors.MustBeAssignableTo<RpcInboundMiddleware>(middlewareType, nameof(middlewareType));

        var descriptor = factory != null
            ? ServiceDescriptor.Singleton(typeof(RpcInboundMiddleware), factory)
            : ServiceDescriptor.Singleton(typeof(RpcInboundMiddleware), middlewareType);
        Services.TryAddEnumerable(descriptor);
        return this;
    }

    public RpcBuilder RemoveInboundMiddleware<TMiddleware>()
        where TMiddleware : RpcInboundMiddleware
        => RemoveInboundMiddleware(typeof(TMiddleware));

    public RpcBuilder RemoveInboundMiddleware(Type middlewareType)
    {
        if (!typeof(RpcInboundMiddleware).IsAssignableFrom(middlewareType))
            throw ActualLab.Internal.Errors.MustBeAssignableTo<RpcInboundMiddleware>(middlewareType, nameof(middlewareType));

        Services.RemoveAll(d =>
            d.ImplementationType == middlewareType
            && d.ServiceType == typeof(RpcInboundMiddleware));
        return this;
    }

    public RpcBuilder AddOutboundMiddleware<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMiddleware>
        (Func<IServiceProvider, TMiddleware>? factory = null)
        where TMiddleware : RpcOutboundMiddleware
        => AddOutboundMiddleware(typeof(TMiddleware), factory);

    public RpcBuilder AddOutboundMiddleware(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type middlewareType,
        Func<IServiceProvider, object>? factory = null)
    {
        if (!typeof(RpcOutboundMiddleware).IsAssignableFrom(middlewareType))
            throw ActualLab.Internal.Errors.MustBeAssignableTo<RpcOutboundMiddleware>(middlewareType, nameof(middlewareType));

        var descriptor = factory != null
            ? ServiceDescriptor.Singleton(typeof(RpcOutboundMiddleware), factory)
            : ServiceDescriptor.Singleton(typeof(RpcOutboundMiddleware), middlewareType);
        Services.TryAddEnumerable(descriptor);
        return this;
    }

    public RpcBuilder RemoveOutboundMiddleware<TMiddleware>()
        where TMiddleware : RpcOutboundMiddleware
        => RemoveOutboundMiddleware(typeof(TMiddleware));

    public RpcBuilder RemoveOutboundMiddleware(Type middlewareType)
    {
        if (!typeof(RpcOutboundMiddleware).IsAssignableFrom(middlewareType))
            throw ActualLab.Internal.Errors.MustBeAssignableTo<RpcOutboundMiddleware>(middlewareType, nameof(middlewareType));

        Services.RemoveAll(d =>
            d.ImplementationType == middlewareType
            && d.ServiceType == typeof(RpcOutboundMiddleware));
        return this;
    }
}
