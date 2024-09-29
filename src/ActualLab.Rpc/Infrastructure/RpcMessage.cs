using ActualLab.Interception;

namespace ActualLab.Rpc.Infrastructure;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record RpcMessage
{
    private RpcMethodRef _methodRef;

    [DataMember(Order = 0), MemoryPackOrder(0)]
    public byte CallTypeId { get; init; }
    [DataMember(Order = 1), MemoryPackOrder(1)]
    public long RelatedId { get; init; }
    [DataMember(Order = 2), MemoryPackOrder(2)]
    public RpcMethodRef MethodRef { get => _methodRef; init => _methodRef = value; }
    [DataMember(Order = 3), MemoryPackOrder(3)]
    public TextOrBytes ArgumentData { get; init; }
    [DataMember(Order = 4), MemoryPackOrder(4)]
    public RpcHeader[]? Headers { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public ArgumentList? Arguments { get; init; }

    [MemoryPackConstructor]
    // ReSharper disable once ConvertToPrimaryConstructor
    public RpcMessage(byte callTypeId, long relatedId, RpcMethodRef methodRef, TextOrBytes argumentData, RpcHeader[]? headers)
    {
        CallTypeId = callTypeId;
        RelatedId = relatedId;
        MethodRef = methodRef;
        ArgumentData = argumentData;
        Headers = headers;
    }

    public override string ToString()
    {
        var headers = Headers.OrEmpty();
        return $"{nameof(RpcMessageV1)} #{RelatedId}/{CallTypeId}: {MethodRef.GetFullMethodName()}, "
            + (Arguments != null
                ? $"Arguments: {Arguments}"
                : $"ArgumentData: {ArgumentData.ToString(16)}")
            + (headers.Length > 0 ? $", Headers: {headers.ToDelimitedString()}" : "");
    }

    public void SetMethodRef(RpcMethodRef methodRef)
        => _methodRef = methodRef;

    // This record relies on referential equality
    public bool Equals(RpcMessage? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
