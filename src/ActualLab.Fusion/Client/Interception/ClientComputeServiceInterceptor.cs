using System.Diagnostics.CodeAnalysis;
using ActualLab.Fusion.Client.Caching;
using ActualLab.Fusion.Interception;
using ActualLab.Interception;
using ActualLab.Rpc;
using ActualLab.Rpc.Infrastructure;

namespace ActualLab.Fusion.Client.Interception;

public class ClientComputeServiceInterceptor(
        ClientComputeServiceInterceptor.Options settings,
        IServiceProvider services
        ) : ComputeServiceInterceptorBase(settings, services)
{
    public new record Options : ComputeServiceInterceptorBase.Options;

    protected readonly RpcClientInterceptor RpcClientInterceptor
        = services.GetRequiredService<RpcClientInterceptor>();
    protected readonly IClientComputedCache? Cache
        = services.GetService<IClientComputedCache>();

    public virtual void Setup(RpcServiceDef serviceDef)
        => RpcClientInterceptor.Setup(serviceDef);

    public override void Intercept(Invocation invocation)
    {
        var handler = GetHandler(invocation) ?? RpcClientInterceptor.GetHandler(invocation);
        if (handler == null)
            invocation.Intercepted();
        else
            handler(invocation);
    }

    public override TResult Intercept<TResult>(Invocation invocation)
    {
        var handler = GetHandler(invocation) ?? RpcClientInterceptor.GetHandler(invocation);
        return handler == null
            ? invocation.Intercepted<TResult>()
            : (TResult)handler.Invoke(invocation)!;
    }

    protected override ComputeFunctionBase<T> CreateFunction<T>(ComputeMethodDef method)
        => new ClientComputeMethodFunction<T>(method, Cache, Services);

    protected override void ValidateTypeInternal(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
        => Hub.CommandServiceInterceptor.ValidateType(type);
}
