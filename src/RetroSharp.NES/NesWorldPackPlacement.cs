namespace RetroSharp.NES;

internal readonly record struct NesFarAddress(int PhysicalBank, ushort Address);

internal sealed record NesWorldPackSegment(
    int RelativeOffset,
    int PhysicalBank,
    int PhysicalOffset,
    ushort CpuAddress,
    int Length);

internal sealed class NesWorldPackPlacement
{
    private const int R6WindowStart = 0x8000;
    private const int R6WindowSize = 8 * 1_024;

    /// <summary>
    /// The banked reader derives the owning segment from bits 13-15 of a 16-bit pack offset, so a
    /// physical pack spans at most eight R6 segments regardless of how many banks the board has.
    /// </summary>
    private const int MaximumAddressableSegments = 8;

    /// <summary>
    /// Largest pack the banked reader can address: eight R6 segments of one 8 KiB window each.
    /// A pack past this size fails identically on every board, so it never escalates.
    /// </summary>
    internal const int MaximumAddressablePackBytes = MaximumAddressableSegments * R6WindowSize;

    private NesWorldPackPlacement(byte[] serializedBytes, IReadOnlyList<NesWorldPackSegment> segments)
    {
        SerializedBytes = serializedBytes;
        Segments = segments;
    }

    public byte[] SerializedBytes { get; }

    public IReadOnlyList<NesWorldPackSegment> Segments { get; }

    public static NesWorldPackPlacement Create(
        byte[] serializedBytes,
        IReadOnlyList<NesPrgSectionLayout> orderedR6Sections)
    {
        ArgumentNullException.ThrowIfNull(serializedBytes);
        ArgumentNullException.ThrowIfNull(orderedR6Sections);
        if (serializedBytes.Length == 0)
        {
            throw new InvalidOperationException("NES WorldPack placement requires at least one serialized byte.");
        }

        if (orderedR6Sections.Count == 0)
        {
            throw new InvalidOperationException("NES WorldPack placement requires at least one R6-owned PRG section.");
        }

        // A pack past the reader's addressing ceiling fails the same way on every board, so it is
        // reported up front as its own constraint instead of being retried on larger PRG sizes.
        if (serializedBytes.Length > MaximumAddressablePackBytes)
        {
            throw AddressabilityFailure(serializedBytes.Length);
        }

        var segments = new List<NesWorldPackSegment>();
        var physicalBanks = new HashSet<int>();
        var relativeOffset = 0;
        var poolBytes = 0;
        foreach (var section in orderedR6Sections)
        {
            if (section.Kind is not NesPrgSectionKind.WorldR6)
            {
                throw new InvalidOperationException(
                    $"NES WorldPack section in physical bank {section.PhysicalBank} is owned by {section.Kind}, not R6 world data.");
            }

            if (!physicalBanks.Add(section.PhysicalBank))
            {
                throw new InvalidOperationException(
                    $"NES WorldPack R6 layout assigns physical bank {section.PhysicalBank} more than once.");
            }

            if (section.Size is <= 0 or > R6WindowSize ||
                section.PhysicalOffset != section.PhysicalBank * R6WindowSize)
            {
                throw new InvalidOperationException(
                    $"NES WorldPack R6 section in physical bank {section.PhysicalBank} must be a positive in-bank range of at most {R6WindowSize} bytes.");
            }

            poolBytes += section.Size;
            if (relativeOffset >= serializedBytes.Length)
            {
                break;
            }

            // Sections smaller than a full window can exhaust the segment budget before the byte
            // ceiling does; that is the same unfixable reader limit, not an R6 pool shortage.
            if (segments.Count == MaximumAddressableSegments)
            {
                throw AddressabilityFailure(serializedBytes.Length);
            }

            var length = Math.Min(section.Size, serializedBytes.Length - relativeOffset);
            segments.Add(new NesWorldPackSegment(
                relativeOffset,
                section.PhysicalBank,
                section.PhysicalOffset,
                R6WindowStart,
                length));
            relativeOffset += length;
        }

        if (relativeOffset != serializedBytes.Length)
        {
            throw NesLinkConstraints.Failure(
                NesLinkConstraint.Mmc3WorldPackCapacity,
                $"NES WorldPack requires {serializedBytes.Length} bytes, but this board's R6 pool of " +
                $"{physicalBanks.Count} section(s) provides {poolBytes} bytes.");
        }

        return new NesWorldPackPlacement(serializedBytes.ToArray(), segments);
    }

    private static InvalidOperationException AddressabilityFailure(int packLength) =>
        NesLinkConstraints.Failure(
            NesLinkConstraint.Mmc3WorldPackAddressability,
            $"NES WorldPack requires {packLength} bytes, but the banked reader derives the owning segment from " +
            $"bits 13-15 of a 16-bit pack offset, so a physical pack spans at most {MaximumAddressableSegments} " +
            $"R6 segments ({MaximumAddressablePackBytes} bytes). A larger PRG board cannot lift this limit.");

    public NesFarAddress TranslateOffset(int relativeOffset)
    {
        if (relativeOffset < 0 || relativeOffset >= SerializedBytes.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(relativeOffset));
        }

        var segment = Segments.First(item =>
            relativeOffset >= item.RelativeOffset &&
            relativeOffset < item.RelativeOffset + item.Length);
        return new NesFarAddress(
            segment.PhysicalBank,
            checked((ushort)(segment.CpuAddress + relativeOffset - segment.RelativeOffset)));
    }
}
