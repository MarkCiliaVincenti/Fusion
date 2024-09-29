using System.Diagnostics.CodeAnalysis;
using ActualLab.Interception;
using ActualLab.Internal;

namespace ActualLab.Rpc.Serialization;

public abstract class RpcArgumentSerializer
{
    [RequiresUnreferencedCode(UnreferencedCode.Serialization)]
    public abstract TextOrBytes Serialize(ArgumentList arguments, bool allowPolymorphism, int sizeHint);
    [RequiresUnreferencedCode(UnreferencedCode.Serialization)]
    public abstract void Deserialize(ref ArgumentList arguments, bool allowPolymorphism, TextOrBytes data);
}
