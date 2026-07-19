namespace RetroSharp.NES;

internal readonly record struct NesPackedCameraPhase(int TileWrites, int AttributeWrites);

internal sealed class NesPackedCameraPhaseSchedule
{
    private const int StandardMaximumRetainedOamBytes = 76;

    private NesPackedCameraPhaseSchedule(
        int columnTileWritesPerPhase,
        int columnCombinedAttributeWrites,
        int columnAttributeWritesPerPhase,
        int rowTileWritesPerPhase,
        int rowAttributeWritesPerPhase,
        int maximumColumnFrames,
        int maximumRowFrames)
    {
        ColumnTileWritesPerPhase = columnTileWritesPerPhase;
        ColumnCombinedAttributeWrites = columnCombinedAttributeWrites;
        ColumnAttributeWritesPerPhase = columnAttributeWritesPerPhase;
        RowTileWritesPerPhase = rowTileWritesPerPhase;
        RowAttributeWritesPerPhase = rowAttributeWritesPerPhase;
        MaximumColumnFrames = maximumColumnFrames;
        MaximumRowFrames = maximumRowFrames;
    }

    internal int ColumnTileWritesPerPhase { get; }

    internal int ColumnCombinedAttributeWrites { get; }

    internal int ColumnAttributeWritesPerPhase { get; }

    internal int RowTileWritesPerPhase { get; }

    internal int RowAttributeWritesPerPhase { get; }

    internal int MaximumColumnFrames { get; }

    internal int MaximumRowFrames { get; }

    internal static NesPackedCameraPhaseSchedule Create(int retainedOamByteCount)
    {
        if (retainedOamByteCount is < 0 or > 152)
        {
            throw new ArgumentOutOfRangeException(nameof(retainedOamByteCount));
        }

        return retainedOamByteCount <= StandardMaximumRetainedOamBytes
            ? new NesPackedCameraPhaseSchedule(
                columnTileWritesPerPhase: 20,
                columnCombinedAttributeWrites: 2,
                columnAttributeWritesPerPhase: 7,
                rowTileWritesPerPhase: 8,
                rowAttributeWritesPerPhase: 9,
                maximumColumnFrames: 9,
                maximumRowFrames: 10)
            : new NesPackedCameraPhaseSchedule(
                columnTileWritesPerPhase: 8,
                columnCombinedAttributeWrites: 0,
                columnAttributeWritesPerPhase: 1,
                rowTileWritesPerPhase: 1,
                rowAttributeWritesPerPhase: 1,
                maximumColumnFrames: 29,
                maximumRowFrames: 56);
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

        var phases = new List<NesPackedCameraPhase>();
        var remainingTiles = payloadLength;
        var targetCursor = targetStart;
        while (remainingTiles > 0)
        {
            var physicalBoundary = targetCursor < 30 ? 30 : 60;
            var tileWrites = Math.Min(
                remainingTiles,
                Math.Min(ColumnTileWritesPerPhase, physicalBoundary - targetCursor));
            phases.Add(new NesPackedCameraPhase(tileWrites, 0));
            remainingTiles -= tileWrites;
            targetCursor = (targetCursor + tileWrites) % 60;
        }

        var remainingAttributes = AttributeCount(payloadLength, targetStart);
        if (ColumnCombinedAttributeWrites > 0)
        {
            var combined = Math.Min(ColumnCombinedAttributeWrites, remainingAttributes);
            phases[^1] = phases[^1] with { AttributeWrites = combined };
            remainingAttributes -= combined;
        }

        while (remainingAttributes > 0)
        {
            var attributeWrites = Math.Min(ColumnAttributeWritesPerPhase, remainingAttributes);
            phases.Add(new NesPackedCameraPhase(0, attributeWrites));
            remainingAttributes -= attributeWrites;
        }

        return phases;
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

        var phases = new List<NesPackedCameraPhase>();
        var remainingTiles = payloadLength;
        while (remainingTiles > 0)
        {
            var tileWrites = Math.Min(RowTileWritesPerPhase, remainingTiles);
            phases.Add(new NesPackedCameraPhase(tileWrites, 0));
            remainingTiles -= tileWrites;
        }

        var remainingAttributes = AttributeCount(payloadLength, targetStart);
        while (remainingAttributes > 0)
        {
            var attributeWrites = Math.Min(RowAttributeWritesPerPhase, remainingAttributes);
            phases.Add(new NesPackedCameraPhase(0, attributeWrites));
            remainingAttributes -= attributeWrites;
        }

        return phases;
    }

    private static int AttributeCount(int payloadLength, int targetStart) =>
        (targetStart % 4 + payloadLength + 3) / 4;
}
