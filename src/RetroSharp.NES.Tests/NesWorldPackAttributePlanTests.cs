namespace RetroSharp.NES.Tests;

using RetroSharp.Core.Sdk;
using RetroSharp.NES;
using Xunit;

public sealed class NesWorldPackAttributePlanTests
{
    [Fact]
    public void Packed_column_attributes_follow_physical_30_row_nametable_boundaries()
    {
        var plan = NesWorldPackAttributePlan.Create(CreatePaletteBandPack());

        Assert.Equal(1, plan.Columns);
        Assert.Equal(10, plan.Rows);
        Assert.Equal(11, plan.ColumnBytes.Length);
        Assert.Equal(0, plan.ColumnOffset % 256);

        // The portable row table groups world rows 28-31, 32-35, and 36-39.
        Assert.Equal(new byte[] { 0xA5, 0xFA, 0x5F }, plan.Bytes[7..]);

        // Physical NES column attributes instead cover 28-29, 30-33, 34-37,
        // and 38-39. The missing lower quadrants at both map edges stay zero.
        Assert.Equal(new byte[] { 0x05, 0xAA, 0xFF, 0x05 }, plan.ColumnBytes[7..]);
    }

    private static WorldPack CreatePaletteBandPack()
    {
        const int width = 4;
        const int height = 40;
        const int chunkRows = 5;
        const int cellsPerChunk = width * WorldPackDescriptor.V1ChunkHeight;
        var collisionProfilesOffset = (uint)WorldPackDescriptor.V1HeaderBytes;
        var targetExpansionsOffset = collisionProfilesOffset + 1;
        var targetExpansions = new byte[]
        {
            0, 0x04,
            0, 0x05,
            0, 0x06,
            0, 0x07,
        };
        var directoryOffset = targetExpansionsOffset + (uint)targetExpansions.Length;
        var chunkDataOffset = directoryOffset + chunkRows * WorldPackDescriptor.V1DirectoryEntryBytes;
        var nextOffset = (uint)chunkDataOffset;
        var chunks = new List<WorldPackChunk>(chunkRows);
        for (var chunkY = 0; chunkY < chunkRows; chunkY++)
        {
            var visualIds = Enumerable.Range(0, cellsPerChunk)
                .Select(index => PaletteVisualId(chunkY * WorldPackDescriptor.V1ChunkHeight + index / width))
                .ToArray();
            var directory = new WorldPackChunkDirectoryEntry(
                VisualOffset: nextOffset,
                VisualStoredBytes: cellsPerChunk,
                VisualDecodedBytes: cellsPerChunk,
                CollisionOffset: nextOffset + cellsPerChunk,
                CollisionStoredBytes: cellsPerChunk,
                CollisionDecodedBytes: cellsPerChunk,
                ValidWidth: width,
                ValidHeight: WorldPackDescriptor.V1ChunkHeight,
                VisualCodec: WorldPackCodec.Raw,
                CollisionCodec: WorldPackCodec.Raw);
            chunks.Add(new WorldPackChunk(directory, visualIds, new ushort[cellsPerChunk]));
            nextOffset += cellsPerChunk * 2;
        }

        var descriptor = new WorldPackDescriptor
        {
            HardwareWidth = width,
            HardwareHeight = height,
            MetatileWidth = 1,
            MetatileHeight = 1,
            ChunkColumns = 1,
            ChunkRows = chunkRows,
            VisualMetatileCount = 4,
            CollisionProfileCount = 1,
            VisualIdBytes = 1,
            CollisionIdBytes = 1,
            TargetCellStride = 2,
            CollisionProfilesOffset = collisionProfilesOffset,
            TargetExpansionsOffset = targetExpansionsOffset,
            DirectoryOffset = directoryOffset,
            ChunkDataOffset = (uint)chunkDataOffset,
            PackLength = nextOffset,
        };
        return new WorldPack(
            descriptor,
            [new WorldPackCollisionProfile([WorldTileFlags.Empty])],
            targetExpansions,
            chunks);
    }

    private static ushort PaletteVisualId(int row) => row switch
    {
        < 28 => 0,
        < 30 => 1,
        < 34 => 2,
        < 38 => 3,
        _ => 1,
    };
}
