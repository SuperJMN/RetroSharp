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

        var segments = new List<NesWorldPackSegment>();
        var physicalBanks = new HashSet<int>();
        var relativeOffset = 0;
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

            if (relativeOffset >= serializedBytes.Length)
            {
                break;
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
            throw new InvalidOperationException(
                $"NES WorldPack requires {serializedBytes.Length} bytes, but the ordered R6 sections provide {relativeOffset} bytes.");
        }

        return new NesWorldPackPlacement(serializedBytes.ToArray(), segments);
    }

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
