namespace RetroSharp.NES;

internal sealed class NesOamPublicationSchedule
{
    private const int StandardMaximumRetainedBytes = 76;
    private const ushort OamAddress = 0x2003;
    private const ushort OamData = 0x2004;
    private readonly ushort shadowAddress;
    private readonly int retainedByteCount;

    private NesOamPublicationSchedule(ushort shadowAddress, int retainedByteCount, long cpuCycles)
    {
        this.shadowAddress = shadowAddress;
        this.retainedByteCount = retainedByteCount;
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

        if (retainedByteCount > StandardMaximumRetainedBytes)
        {
            return new NesOamPublicationSchedule(
                shadowAddress,
                retainedByteCount,
                cpuCycles: 6 + retainedByteCount * 8L);
        }

        var directHead = Math.Min(4, retainedByteCount);
        var remaining = retainedByteCount - directHead;
        var groups = remaining / 9;
        var directTail = remaining % 9;
        var startIndex = 256 - retainedByteCount + directHead;
        var biasedShadowAddress = checked((ushort)(shadowAddress - (256 - retainedByteCount)));
        var groupedBytes = groups * 9;
        var pageCrossingLoads = Enumerable.Range(0, groupedBytes)
            .Count(offset =>
            {
                var index = startIndex + offset;
                var effectiveAddress = checked((ushort)(biasedShadowAddress + index));
                return (biasedShadowAddress & 0xFF00) != (effectiveAddress & 0xFF00);
            });
        var groupControlCycles = groups == 0
            ? 0
            : groups * (startIndex + groupedBytes == 256 ? 0 : 2) + (groups - 1) * 3 + 2;
        var cpuCycles = checked(
            6L +
            (directHead + directTail) * 8L +
            (groups > 0 ? 2 : 0) +
            groupedBytes * 10L +
            pageCrossingLoads +
            groupControlCycles);
        return new NesOamPublicationSchedule(shadowAddress, retainedByteCount, cpuCycles);
    }

    internal void Emit(PrgBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.LoadAImmediate(0);
        builder.StoreAAbsolute(OamAddress);
        if (retainedByteCount > StandardMaximumRetainedBytes)
        {
            EmitDirect(builder, start: 0, retainedByteCount);
            return;
        }

        var directHead = Math.Min(4, retainedByteCount);
        EmitDirect(builder, start: 0, directHead);
        var remaining = retainedByteCount - directHead;
        var groups = remaining / 9;
        var groupedBytes = groups * 9;
        if (groups > 0)
        {
            var startIndex = 256 - retainedByteCount + directHead;
            var endIndex = startIndex + groupedBytes;
            var biasedShadowAddress = checked((ushort)(shadowAddress - (256 - retainedByteCount)));
            var publish = builder.CreateLabel("oam_shadow_publish_group");
            builder.LoadXImmediate(startIndex);
            builder.Label(publish);
            for (var index = 0; index < 9; index++)
            {
                builder.LoadAAbsoluteX(biasedShadowAddress);
                builder.StoreAAbsolute(OamData);
                builder.IncrementX();
            }

            if (endIndex < 256)
            {
                builder.CompareXImmediate(endIndex);
            }

            builder.BranchRelative(0xD0, publish);
        }

        EmitDirect(builder, directHead + groupedBytes, remaining - groupedBytes);
    }

    private void EmitDirect(PrgBuilder builder, int start, int count)
    {
        for (var index = start; index < start + count; index++)
        {
            builder.LoadAAbsolute(checked((ushort)(shadowAddress + index)));
            builder.StoreAAbsolute(OamData);
        }
    }
}
