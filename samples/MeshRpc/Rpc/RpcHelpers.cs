using ActualLab.Interception;
using ActualLab.Rpc;
using ActualLab.Rpc.Clients;

namespace Samples.MeshRpc;

public sealed class RpcHelpers(Host ownHost)
{
    public RpcPeerRef RouteCall(RpcMethodDef method, ArgumentList arguments)
    {
        if (arguments.Length == 0)
            return RpcPeerRef.Local;

        var arg0Type = arguments.GetType(0);
        if (arg0Type == typeof(HostRef))
            return RpcHostPeerRef.Get(arguments.Get<HostRef>(0));
        if (typeof(IHasHostRef).IsAssignableFrom(arg0Type))
            return RpcHostPeerRef.Get(arguments.Get<IHasHostRef>(0).HostRef);

        if (arg0Type == typeof(ShardRef))
            return RpcShardPeerRef.Get(arguments.Get<ShardRef>(0));
        if (typeof(IHasShardRef).IsAssignableFrom(arg0Type))
            return RpcShardPeerRef.Get(arguments.Get<IHasShardRef>(0).ShardRef);

        return RpcShardPeerRef.Get(ShardRef.New(arguments.GetUntyped(0)));

    }

    public string GetHostUrl(RpcWebSocketClient client, RpcClientPeer peer)
    {
        if (peer.Ref is not IMeshPeerRef meshPeerRef)
            return "";

        var host = MeshState.State.Value.HostById.GetValueOrDefault(meshPeerRef.HostId);
        return host?.Url ?? "";
    }
}