using System.Diagnostics.CodeAnalysis;
using ActualLab.Fusion.Interception;
using ActualLab.Fusion.Client.Interception;
using ActualLab.Interception;
using ActualLab.Rpc;
using ActualLab.Rpc.Infrastructure;
using ActualLab.Rpc.Internal;

namespace ActualLab.Fusion.Internal;

public static class FusionProxies
{
    public static object NewProxy(
        IServiceProvider services,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type serviceType)
    {
        var interceptor = services.GetRequiredService<ComputeServiceInterceptor>();
        // We should try to validate it here because if the type doesn't
        // have any virtual methods (which might be a mistake), no calls
        // will be intercepted, so no error will be thrown later.
        interceptor.ValidateType(serviceType);
        var serviceProxy = services.ActivateProxy(serviceType, interceptor);
        return serviceProxy;
    }

    public static object NewClientProxy(
        IServiceProvider services,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type serviceType)
    {
        var rpcHub = services.RpcHub();
        var client = RpcProxies.NewClientProxy(services, serviceType);
        var serviceDef = rpcHub.ServiceRegistry[serviceType];

        var interceptor = services.GetRequiredService<ClientComputeServiceInterceptor>();
        interceptor.Setup(serviceDef);
        interceptor.ValidateType(serviceType);
        var clientProxy = Proxies.New(serviceType, interceptor, client);
        return clientProxy;
    }

    public static object NewHybridProxy(
        IServiceProvider services,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type serviceType,
        ServiceResolver localServiceResolver)
    {
        var rpcHub = services.RpcHub();
        var localService = localServiceResolver.Resolve(services);
        var client = NewClientProxy(services, serviceType);
        var serviceDef = rpcHub.ServiceRegistry[serviceType];

        var interceptor = services.GetRequiredService<RpcHybridInterceptor>();
        interceptor.Setup(serviceDef, localService, client, reuseClientProxy: true);
        return client;
    }
}
