using System.Diagnostics.CodeAnalysis;
using ActualLab.Fusion.Interception;
using ActualLab.Interception;
using ActualLab.Rpc.Infrastructure;

namespace ActualLab.Fusion.Client.Interception;

public class ClientComputeServiceInterceptor : ComputeServiceInterceptor
{
    public new record Options : ComputeServiceInterceptor.Options
    {
        public static new Options Default { get; set; } = new();
    }

    public readonly ComputeServiceInterceptor ComputeServiceInterceptor;
    public readonly RpcClientInterceptor ClientInterceptor;

    // ReSharper disable once ConvertToPrimaryConstructor
    public ClientComputeServiceInterceptor(
        Options settings,
        IServiceProvider services,
        RpcClientInterceptor clientInterceptor
        ) : base(settings, services)
    {
        ComputeServiceInterceptor = Hub.ComputeServiceInterceptor;
        ClientInterceptor = clientInterceptor;
    }

    public override Func<Invocation, object?>? GetHandler(Invocation invocation)
    {
        var handler = GetOwnHandler(invocation);
        if (handler == null) // Not a compute method
            return CommandServiceInterceptor.GetOwnHandler(invocation);

        // If we're here, it's a compute method
        return Invalidation.IsActive
            ? GetSkippingHandler(invocation)
            : handler;
    }

    protected override ComputeFunctionBase<T> CreateFunction<T>(ComputeMethodDef method)
        => new ClientComputeMethodFunction<T>(method, Hub.ClientComputedCache, Services);

    protected override void ValidateTypeInternal(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
        // We redirect this call to ComputeServiceInterceptor to make sure its validation cache is reused here
        => ComputeServiceInterceptor.ValidateType(type);
}
