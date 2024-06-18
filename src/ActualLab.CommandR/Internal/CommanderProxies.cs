using System.Diagnostics.CodeAnalysis;
using ActualLab.CommandR.Interception;
using ActualLab.Interception;

namespace ActualLab.CommandR.Internal;

public static class CommanderProxies
{
    public static object NewProxy(
        IServiceProvider services,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type implementationType,
        bool initialize = true)
    {
        var interceptor = services.GetRequiredService<CommandServiceInterceptor>();
        interceptor.ValidateType(implementationType);
        return services.ActivateProxy(implementationType, interceptor, null, initialize);
    }
}
