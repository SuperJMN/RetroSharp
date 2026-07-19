namespace RetroSharp.NES;

internal enum NesPackedCameraWriteKind
{
    Tile,
    Attribute,
}

internal readonly record struct NesPackedCameraSegment(
    NesPackedCameraWriteKind Kind,
    int TargetStart,
    int WriteCount);

internal readonly record struct NesPackedCameraPhase
{
    internal NesPackedCameraPhase(int tileWrites, int attributeWrites)
        : this(tileWrites, attributeWrites, [], physicalWriteBytes: 0, worstCaseCpuCycles: 0)
    {
    }

    internal NesPackedCameraPhase(
        int tileWrites,
        int attributeWrites,
        IReadOnlyList<NesPackedCameraSegment> segments,
        int physicalWriteBytes,
        long worstCaseCpuCycles)
    {
        TileWrites = tileWrites;
        AttributeWrites = attributeWrites;
        Segments = segments;
        PhysicalWriteBytes = physicalWriteBytes;
        WorstCaseCpuCycles = worstCaseCpuCycles;
    }

    internal int TileWrites { get; }

    internal int AttributeWrites { get; }

    internal IReadOnlyList<NesPackedCameraSegment> Segments { get; }

    internal int PhysicalWriteBytes { get; }

    internal long WorstCaseCpuCycles { get; }
}

internal sealed class NesPackedCameraPhaseSchedule
{
    internal const int MaximumPhysicalFrameCycles = 2_273;
    private const int FixedPhaseCycles = 400;
    private const int WorstCaseTransactionCycles = 55;

    private NesPackedCameraPhaseSchedule(int retainedOamByteCount, long retainedOamCycles)
    {
        RetainedOamByteCount = retainedOamByteCount;
        RetainedOamCycles = retainedOamCycles;
        MaximumWritesPerPhase = checked((int)(
            (MaximumPhysicalFrameCycles - retainedOamCycles - FixedPhaseCycles) /
            WorstCaseTransactionCycles));
        if (MaximumWritesPerPhase < 1)
        {
            throw new InvalidOperationException(
                $"NES packed camera has no physical transaction capacity after {retainedOamCycles} retained-OAM cycles.");
        }

        MaximumColumnFrames = Enumerable.Range(1, 32)
            .SelectMany(payloadLength => Enumerable.Range(0, 60)
                .Select(targetStart => PlanColumn(payloadLength, targetStart).Count))
            .Max();
        MaximumRowFrames = Enumerable.Range(1, 32)
            .SelectMany(payloadLength => Enumerable.Range(0, 64)
                .Select(targetStart => PlanRow(payloadLength, targetStart).Count))
            .Max();
    }

    internal int RetainedOamByteCount { get; }

    internal long RetainedOamCycles { get; }

    internal int MaximumWritesPerPhase { get; }

    internal int ColumnTileWritesPerPhase => MaximumWritesPerPhase;

    internal int ColumnCombinedAttributeWrites => MaximumWritesPerPhase;

    internal int ColumnAttributeWritesPerPhase => MaximumWritesPerPhase;

    internal int RowTileWritesPerPhase => MaximumWritesPerPhase;

    internal int RowAttributeWritesPerPhase => MaximumWritesPerPhase;

    internal int MaximumColumnFrames { get; }

    internal int MaximumRowFrames { get; }

    internal static NesPackedCameraPhaseSchedule Create(int retainedOamByteCount)
    {
        if (retainedOamByteCount is < 0 or > 152)
        {
            throw new ArgumentOutOfRangeException(nameof(retainedOamByteCount));
        }

        var retainedOamCycles = retainedOamByteCount == 0
            ? 0
            : NesOamPublicationSchedule.Create(
                NesRuntimeMemoryLayout.Sprite.OamShadow,
                retainedOamByteCount).CpuCycles;
        return new NesPackedCameraPhaseSchedule(retainedOamByteCount, retainedOamCycles);
    }

    internal IReadOnlyList<NesPackedCameraPhase> PlanColumn(int payloadLength, int targetStart)
    {
        if (payloadLength is < 1 or > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(payloadLength));
        }

        if (targetStart is < 0 or >= 60)
        {
            throw new ArgumentOutOfRangeException(nameof(targetStart));
        }

        var transactions = new List<NesPackedCameraTransaction>();
        for (var index = 0; index < payloadLength; index++)
        {
            var target = (targetStart + index) % 60;
            transactions.Add(new NesPackedCameraTransaction(
                NesPackedCameraWriteKind.Tile,
                target,
                index == 0 || target is 0 or 30));
        }

        var attributeCount = AttributeCount(payloadLength, targetStart);
        var attributeStart = targetStart & 0xFC;
        for (var index = 0; index < attributeCount; index++)
        {
            transactions.Add(new NesPackedCameraTransaction(
                NesPackedCameraWriteKind.Attribute,
                (attributeStart + index * 4) % 60,
                StartsSegment: true));
        }

        return Plan(transactions);
    }

    internal IReadOnlyList<NesPackedCameraPhase> PlanRow(int payloadLength, int targetStart)
    {
        if (payloadLength is < 1 or > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(payloadLength));
        }

        if (targetStart is < 0 or >= 64)
        {
            throw new ArgumentOutOfRangeException(nameof(targetStart));
        }

        var transactions = new List<NesPackedCameraTransaction>();
        for (var index = 0; index < payloadLength; index++)
        {
            var target = (targetStart + index) % 64;
            transactions.Add(new NesPackedCameraTransaction(
                NesPackedCameraWriteKind.Tile,
                target,
                index == 0 || target is 0 or 32));
        }

        var attributeCount = AttributeCount(payloadLength, targetStart);
        var attributeStart = targetStart / 4;
        for (var index = 0; index < attributeCount; index++)
        {
            var target = (attributeStart + index) % 16;
            transactions.Add(new NesPackedCameraTransaction(
                NesPackedCameraWriteKind.Attribute,
                target,
                index == 0 || target is 0 or 8));
        }

        return Plan(transactions);
    }

    private IReadOnlyList<NesPackedCameraPhase> Plan(IReadOnlyList<NesPackedCameraTransaction> transactions)
    {
        var tileCount = transactions.TakeWhile(transaction => transaction.Kind == NesPackedCameraWriteKind.Tile).Count();
        var phaseTransactions = new List<List<NesPackedCameraTransaction>>();
        for (var offset = 0; offset < tileCount; offset += MaximumWritesPerPhase)
        {
            phaseTransactions.Add(transactions
                .Skip(offset)
                .Take(Math.Min(MaximumWritesPerPhase, tileCount - offset))
                .ToList());
        }

        var remainingAttributes = transactions.Skip(tileCount).ToArray();
        if (remainingAttributes.Length > 0 &&
            phaseTransactions.Count > 0 &&
            remainingAttributes.Length <= MaximumWritesPerPhase - phaseTransactions[^1].Count)
        {
            phaseTransactions[^1].AddRange(remainingAttributes);
            remainingAttributes = [];
        }

        for (var offset = 0; offset < remainingAttributes.Length; offset += MaximumWritesPerPhase)
        {
            phaseTransactions.Add(remainingAttributes
                .Skip(offset)
                .Take(MaximumWritesPerPhase)
                .ToList());
        }

        return phaseTransactions.Select(CreatePhase).ToArray();
    }

    private NesPackedCameraPhase CreatePhase(IReadOnlyList<NesPackedCameraTransaction> phaseTransactions)
    {
        var segments = new List<NesPackedCameraSegment>();
        foreach (var transaction in phaseTransactions)
        {
            if (segments.Count == 0 || transaction.StartsSegment)
            {
                segments.Add(new NesPackedCameraSegment(transaction.Kind, transaction.Target, WriteCount: 1));
            }
            else
            {
                segments[^1] = segments[^1] with { WriteCount = segments[^1].WriteCount + 1 };
            }
        }

        var tileWrites = phaseTransactions.Count(transaction => transaction.Kind == NesPackedCameraWriteKind.Tile);
        var attributeWrites = phaseTransactions.Count - tileWrites;
        var physicalWriteBytes = checked(
            RetainedOamByteCount +
            segments.Count * 2 +
            phaseTransactions.Count +
            3); // PPUCTRL plus both PPUSCROLL bytes.
        var worstCaseCpuCycles = checked(
            RetainedOamCycles +
            FixedPhaseCycles +
            phaseTransactions.Count * WorstCaseTransactionCycles);
        return new NesPackedCameraPhase(
            tileWrites,
            attributeWrites,
            segments,
            physicalWriteBytes,
            worstCaseCpuCycles);
    }

    private static int AttributeCount(int payloadLength, int targetStart) =>
        (targetStart % 4 + payloadLength + 3) / 4;

    private readonly record struct NesPackedCameraTransaction(
        NesPackedCameraWriteKind Kind,
        int Target,
        bool StartsSegment);
}
