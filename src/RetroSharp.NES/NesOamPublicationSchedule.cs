namespace RetroSharp.NES;

/// <summary>
/// Sequential retained-OAM publication through <c>$2004</c>, used by profiles that do not
/// publish page <c>$02</c> through <c>$4014</c>.
/// </summary>
/// <remarks>
/// The publication shares one hardware VBlank with the packed background commit, so its cost is
/// on the critical path of every frame. A counted loop pays five extra cycles per byte for
/// <c>INX</c>/<c>BNE</c> plus an indexed page-crossing penalty; the straight-line form pays only
/// the two stores. That is the difference between fitting inside VBlank and spilling the tail of
/// the publication onto rendered scanlines for a program that also commits a full-height
/// background column, so this path is unrolled and its cost is reported from the same shape that
/// is emitted.
/// </remarks>
internal sealed class NesOamPublicationSchedule
{
    private const ushort OamAddress = 0x2003;
    private const ushort OamData = 0x2004;

    // LDA #0 (2) + STA $2003 (4).
    private const long ResetCpuCycles = 6;

    // LDA absolute (4) + STA $2004 (4).
    internal const long CpuCyclesPerByte = 8;

    private readonly ushort shadowAddress;
    private readonly int retainedByteCount;

    private NesOamPublicationSchedule(ushort shadowAddress, int retainedByteCount, long cpuCycles)
    {
        this.shadowAddress = shadowAddress;
        this.retainedByteCount = retainedByteCount;
        CpuCycles = cpuCycles;
    }

    internal long CpuCycles { get; }

    internal static long CpuCyclesFor(int retainedByteCount) =>
        checked(retainedByteCount * CpuCyclesPerByte + ResetCpuCycles);

    internal static NesOamPublicationSchedule Create(ushort shadowAddress, int retainedByteCount)
    {
        if (retainedByteCount is < 1 or > 256)
        {
            throw new ArgumentOutOfRangeException(
                nameof(retainedByteCount),
                retainedByteCount,
                "Sequential retained OAM publication requires between 1 and 256 bytes.");
        }

        _ = checked((ushort)(shadowAddress + retainedByteCount - 1));
        return new NesOamPublicationSchedule(
            shadowAddress,
            retainedByteCount,
            CpuCyclesFor(retainedByteCount));
    }

    internal void Emit(PrgBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.LoadAImmediate(0);
        builder.StoreAAbsolute(OamAddress);
        for (var index = 0; index < retainedByteCount; index++)
        {
            builder.LoadAAbsolute(checked((ushort)(shadowAddress + index)));
            builder.StoreAAbsolute(OamData);
        }
    }
}
