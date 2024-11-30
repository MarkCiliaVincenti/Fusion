using ActualLab.OS;

namespace ActualLab.Fusion;

public static class FusionDefaults
{
    private static readonly Lock StaticLock = LockFactory.Create();

    public static FusionMode Mode {
        get;
        set {
            if (value is not (FusionMode.Client or FusionMode.Server))
                throw new ArgumentOutOfRangeException(nameof(value), value, null);

            lock (StaticLock) {
                field = value;
                Recompute();
            }
        }
    }

    public static int TimeoutsConcurrencyLevel { get; set; }
    public static int ComputedRegistryConcurrencyLevel { get; set; }
    public static int ComputedRegistryInitialCapacity { get; set; }
    public static int ComputedGraphPrunerBatchSize { get; set; }

    static FusionDefaults()
        => Mode = OSInfo.IsAnyClient ? FusionMode.Client : FusionMode.Server;

    // Private & internal methods

    private static void Recompute()
    {
        var isServer = Mode is FusionMode.Server;
        var cpuCountPo2 = HardwareInfo.ProcessorCountPo2;
        TimeoutsConcurrencyLevel = (cpuCountPo2 / (isServer ? 2 : 16)).Clamp(1, 256);
        ComputedRegistryConcurrencyLevel = (cpuCountPo2 * (isServer ? 8 : 1)).Clamp(1, 8192);
        var computedRegistryCapacityBase = (ComputedRegistryConcurrencyLevel * 32).Clamp(256, 8192);
        ComputedRegistryInitialCapacity = PrimeSieve.GetPrecomputedPrime(computedRegistryCapacityBase);
        ComputedGraphPrunerBatchSize = cpuCountPo2 * 512;
    }
}
