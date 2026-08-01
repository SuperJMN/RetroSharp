namespace RetroSharp.NES.Tests;

using RetroSharp.Core.Sdk;
using RetroSharp.NES;
using RetroSharp.Sdk;
using Xunit;

public sealed class NesPreparedColumnBehaviorTests
{
    public static TheoryData<int, WorldPackCodec, int, byte> ColumnCases
    {
        get
        {
            var cases = new TheoryData<int, WorldPackCodec, int, byte>();
            foreach (var visualIdBytes in new[] { 1, 2 })
            {
                foreach (var visualCodec in new[] { WorldPackCodec.Raw, WorldPackCodec.ElementRle })
                {
                    foreach (var startY in new[] { 14, 15 })
                    {
                        cases.Add(visualIdBytes, visualCodec, startY, NesPackedCameraRuntime.Negative);
                        cases.Add(visualIdBytes, visualCodec, startY, NesPackedCameraRuntime.Positive);
                    }
                }
            }

            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(ColumnCases))]
    public void Resumable_column_matches_managed_tiles_and_attributes_across_lookup_boundaries(
        int visualIdBytes,
        WorldPackCodec visualCodec,
        int startY,
        byte direction)
    {
        const byte worldX = 15;
        const byte payloadLength = 30;
        var serialized = CreateColumnFixture(visualIdBytes, visualCodec);
        var runtime = NesWorldPackRuntimePlan.Create(serialized);
        var pack = runtime.Pack;
        Assert.Equal(visualIdBytes == 1, runtime.UsesFastLookup);
        Assert.All(
            runtime.Planes.Where((_, index) => index % 2 == 0),
            plane => Assert.Equal(visualCodec, plane.Codec));

        const string source = """
            void Main() {
                World.Column(0, 0, 0, 0, 0, 0, 0, 0, 0);
                World.Map(63, 0, 8);
                Camera.Init(63, 0, 8);
                Camera.SetPosition(0, 0);
                while (true) {
                    Video.WaitVBlank();
                    Camera.Apply();
                }
            }
            """;
        var result = RetroSharp.NES.NesRomCompiler.CompileSourceForMmc3TvromTestsWithReport(
            source,
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            packedWorldOverride: serialized);
        var cpu = new NesTestCpu(result.Rom);
        cpu.SetR6Bank(5);
        cpu.SetRam(NesRuntimeMemoryLayout.Banking.Mmc3R6Shadow, 5);
        Assert.Equal(
            (byte)NesWorldPackResult.Success,
            cpu.RunRoutine(result.Report.FixedSymbols[NesRomBuilder.WorldPackValidateLabel], 5_000_000).A);
        Assert.Equal(
            (byte)NesWorldPackResult.Success,
            cpu.RunRoutine(result.Report.FixedSymbols[NesRomBuilder.WorldPackInitializeLabel], 5_000_000).A);

        cpu.SetRam(NesRuntimeMemoryLayout.PackedCamera.CommitAxis, NesPackedCameraRuntime.Column);
        cpu.SetRam(NesRuntimeMemoryLayout.PackedCamera.CommitDirection, direction);
        cpu.SetRam(NesRuntimeMemoryLayout.PackedCamera.CommitWorldEdgeLow, worldX);
        cpu.SetRam(NesRuntimeMemoryLayout.PackedCamera.CommitWorldEdgeHigh, 0);
        cpu.SetRam(NesRuntimeMemoryLayout.PackedCamera.CommitTarget, worldX);
        cpu.SetRam(NesRuntimeMemoryLayout.PackedCamera.CommitTargetStart, checked((byte)startY));
        cpu.SetRam(NesRuntimeMemoryLayout.PackedCamera.CommitOrthogonalLow, checked((byte)startY));
        cpu.SetRam(NesRuntimeMemoryLayout.PackedCamera.CommitOrthogonalHigh, 0);
        cpu.SetRam(NesRuntimeMemoryLayout.PackedCamera.CommitPayloadLength, payloadLength);

        var prepareCalls = NesPackedCameraRuntime.ColumnPrepareCalls(payloadLength);
        var prepareResults = Enumerable.Range(0, prepareCalls)
            .Select(_ => cpu.RunRoutine(
                result.Report.FixedSymbols[NesRomBuilder.WorldPackPrepareEdgeLabel],
                maxInstructions: 5_000_000).A)
            .ToArray();

        Assert.Equal(
            Enumerable.Range(0, prepareCalls)
                .Select(index => index == prepareCalls - 1
                    ? (byte)NesWorldPackResult.Success
                    : NesPackedCameraRuntime.PreparePending)
                .ToArray(),
            prepareResults);
        Assert.Equal(1, cpu.Ram(NesRuntimeMemoryLayout.PackedCamera.RequestCount));
        Assert.Equal(1, cpu.Ram(NesRuntimeMemoryLayout.PackedCamera.PrepareCount));
        Assert.Equal(1, cpu.Ram(NesRuntimeMemoryLayout.PackedCamera.ResidentCount));
        Assert.Equal(
            direction,
            cpu.Ram(NesRuntimeMemoryLayout.PackedCamera.Slot0 + NesPackedCameraRuntime.DirectionOffset));
        Assert.Equal(
            direction,
            (byte)(cpu.Ram(NesRuntimeMemoryLayout.PackedCamera.PrefetchedColumnDirection) & 0x03));

        var edge = runtime.Layout.EdgeSlots[0];
        var metatileCells = pack.Descriptor.MetatileWidth * pack.Descriptor.MetatileHeight;
        var expectedTiles = Enumerable.Range(startY, payloadLength)
            .Select(y =>
            {
                var coordinate = pack.Locate(worldX, y);
                var visualId = pack.VisualIdAt(worldX, y);
                var expansion = (visualId * metatileCells + coordinate.SubcellIndex) * 2;
                return pack.TargetExpansions.Span[expansion];
            })
            .ToArray();
        var actualTiles = Enumerable.Range(0, payloadLength)
            .Select(index => cpu.Ram((ushort)(edge.Start + index)))
            .ToArray();
        Assert.Equal(expectedTiles, actualTiles);

        var attributeColumn = worldX / 4;
        var attributeStartRow = (startY - (startY & 0x03)) / 4;
        var attributeCount = ((startY & 0x03) + payloadLength + 3) / 4;
        var expectedAttributes = Enumerable.Range(attributeStartRow, attributeCount)
            .Select(row => runtime.Attributes.ColumnBytes[row * runtime.Attributes.Columns + attributeColumn])
            .ToArray();
        var actualAttributes = Enumerable.Range(0, attributeCount)
            .Select(index => cpu.Ram((ushort)(edge.Start + NesPackedCameraRuntime.AttributeStagingOffset + index)))
            .ToArray();
        Assert.Equal(expectedAttributes, actualAttributes);
    }

    private static byte[] CreateColumnFixture(int visualIdBytes, WorldPackCodec visualCodec)
    {
        const int chunkColumns = 4;
        const int chunkRows = 4;
        const int cellsPerChunk = 64;
        const int metatileCells = 4;
        var visualMetatileCount = visualIdBytes == 1 ? 64 : 257;
        var collisionProfilesOffset = (uint)WorldPackDescriptor.V1HeaderBytes;
        var targetExpansionsOffset = collisionProfilesOffset + metatileCells;
        var targetExpansions = new byte[visualMetatileCount * metatileCells * 2];
        for (var visualId = 0; visualId < visualMetatileCount; visualId++)
        {
            for (var subcell = 0; subcell < metatileCells; subcell++)
            {
                var offset = (visualId * metatileCells + subcell) * 2;
                targetExpansions[offset] = (byte)((visualId * 7 + subcell * 3) % 240);
                targetExpansions[offset + 1] = (byte)((visualId + subcell) & 0x03);
            }
        }

        var directoryOffset = targetExpansionsOffset + checked((uint)targetExpansions.Length);
        var chunkDataOffset = directoryOffset
                              + chunkColumns * chunkRows * WorldPackDescriptor.V1DirectoryEntryBytes;
        var nextOffset = chunkDataOffset;
        var chunks = new List<WorldPackChunk>(chunkColumns * chunkRows);
        var highVisualId = checked((ushort)(visualMetatileCount - 1));
        for (var chunkIndex = 0; chunkIndex < chunkColumns * chunkRows; chunkIndex++)
        {
            var visualIds = visualCodec == WorldPackCodec.Raw
                ? Enumerable.Range(0, cellsPerChunk)
                    .Select(index => index % 2 == 0 ? (ushort)0 : highVisualId)
                    .ToArray()
                : Enumerable.Range(0, cellsPerChunk)
                    .Select(index => checked((ushort)(
                        (highVisualId - chunkIndex * 8 - index / 8 + visualMetatileCount)
                        % visualMetatileCount)))
                    .ToArray();
            var visualDecodedBytes = checked((ushort)(cellsPerChunk * visualIdBytes));
            var visualStoredBytes = visualCodec == WorldPackCodec.Raw
                ? visualDecodedBytes
                : checked((ushort)(8 * (visualIdBytes + 1)));
            var collisionIds = Enumerable.Repeat((ushort)0, cellsPerChunk).ToArray();
            const ushort collisionStoredBytes = 2;
            const ushort collisionDecodedBytes = cellsPerChunk;
            var directory = new WorldPackChunkDirectoryEntry(
                nextOffset,
                visualStoredBytes,
                visualDecodedBytes,
                nextOffset + visualStoredBytes,
                collisionStoredBytes,
                collisionDecodedBytes,
                8,
                8,
                visualCodec,
                WorldPackCodec.ElementRle);
            chunks.Add(new WorldPackChunk(directory, visualIds, collisionIds));
            nextOffset += checked((uint)(visualStoredBytes + collisionStoredBytes));
        }

        var descriptor = new WorldPackDescriptor
        {
            HardwareWidth = chunkColumns * 8 * 2,
            HardwareHeight = chunkRows * 8 * 2,
            MetatileWidth = 2,
            MetatileHeight = 2,
            ChunkColumns = chunkColumns,
            ChunkRows = chunkRows,
            VisualMetatileCount = visualMetatileCount,
            CollisionProfileCount = 1,
            VisualIdBytes = visualIdBytes,
            CollisionIdBytes = 1,
            TargetCellStride = 2,
            CollisionProfilesOffset = collisionProfilesOffset,
            TargetExpansionsOffset = targetExpansionsOffset,
            DirectoryOffset = directoryOffset,
            ChunkDataOffset = chunkDataOffset,
            PackLength = nextOffset,
        };
        var pack = new WorldPack(
            descriptor,
            [new WorldPackCollisionProfile(Enumerable.Repeat(WorldTileFlags.Empty, metatileCells).ToArray())],
            targetExpansions,
            chunks);
        return WorldPackSerializer.Serialize(pack);
    }
}
