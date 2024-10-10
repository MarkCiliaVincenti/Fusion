using System.Diagnostics.CodeAnalysis;
using ActualLab.Internal;
using MessagePack;

namespace ActualLab.Serialization;

public static class TypeDecoratingMessagePackSerialized
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TypeDecoratingMessagePackSerialized<TValue> New<TValue>(TValue value = default!)
        => new() { Value = value };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [RequiresUnreferencedCode(UnreferencedCode.Serialization)]
    public static TypeDecoratingMessagePackSerialized<TValue> New<TValue>(byte[] data)
        => new() { Data = data };
}

#if !NET5_0
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
#endif
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
[Newtonsoft.Json.JsonObject(Newtonsoft.Json.MemberSerialization.OptOut)]
public partial class TypeDecoratingMessagePackSerialized<T> : ByteSerialized<T>
{
    private static IByteSerializer<T>? _serializer;

    [RequiresUnreferencedCode(UnreferencedCode.Serialization)]
    protected override IByteSerializer<T> GetSerializer()
        => _serializer ??= MessagePackByteSerializer.DefaultTypeDecorating.ToTyped<T>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator TypeDecoratingMessagePackSerialized<T>(T value) => new() { Value = value };
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator TypeDecoratingMessagePackSerialized<T>(byte[] data) => new() { Data = data };
}
