namespace RetroSharp.NES;

internal sealed class NesOamPublicationSchedule
{
    private const ushort OamAddress = 0x2003;
    private const ushort OamData = 0x2004;
    private readonly int startIndex;
    private readonly ushort biasedShadowAddress;

    private NesOamPublicationSchedule(int startIndex, ushort biasedShadowAddress, long cpuCycles)
    {
        this.startIndex = startIndex;
        this.biasedShadowAddress = biasedShadowAddress;
        CpuCycles = cpuCycles;
    }

    internal long CpuCycles { get; }

    internal static NesOamPublicationSchedule Create(ushort shadowAddress, int retainedByteCount)
    {
        if (retainedByteCount is < 1 or > 256)
        {
            throw new ArgumentOutOfRangeException(
                nameof(retainedByteCount),
                retainedByteCount,
                "Sequential retained OAM publication requires between 1 and 256 bytes.");
        }

        var startIndex = 256 - retainedByteCount;
        var biasedShadowAddress = checked((ushort)(shadowAddress - startIndex));
        var pageCrossingLoads = 0;
        for (var index = startIndex; index < 256; index++)
        {
            var effectiveAddress = checked((ushort)(biasedShadowAddress + index));
            if ((biasedShadowAddress & 0xFF00) != (effectiveAddress & 0xFF00))
            {
                pageCrossingLoads++;
            }
        }

        var cpuCycles = checked(retainedByteCount * 13L + 7 + pageCrossingLoads);
        return new NesOamPublicationSchedule(startIndex, biasedShadowAddress, cpuCycles);
    }

    internal void Emit(PrgBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var publish = builder.CreateLabel("oam_shadow_publish");
        builder.LoadAImmediate(0);
        builder.StoreAAbsolute(OamAddress);
        builder.LoadXImmediate(startIndex);
        builder.Label(publish);
        builder.LoadAAbsoluteX(biasedShadowAddress);
        builder.StoreAAbsolute(OamData);
        builder.IncrementX();
        builder.BranchRelative(0xD0, publish);
    }
}
