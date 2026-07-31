namespace RetroSharp.GameBoy.Tests;

using RetroSharp.Core.Sdk;
using RetroSharp.GameBoy;
using RetroSharp.Sdk;
using Xunit;

public sealed class FullStage1BaselineTests
{
    [Fact]
    public void Full_stage1_fixture_pack_is_embedded_once_with_exact_offsets()
    {
        var canonical = GameBoyTiledMapImporter.CompileWorldPack(
            FullStage1ValidationFixture.MapPath,
            GameBoyVideoProgram.FirstGeneratedBackgroundTile);
        var first = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
            FullStage1ValidationFixture.Source,
            FullStage1ValidationFixture.Directory,
            sdkLibraryImports: [SdkImportResolver.Portable2D]);
        var second = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
            FullStage1ValidationFixture.Source,
            FullStage1ValidationFixture.Directory,
            sdkLibraryImports: [SdkImportResolver.Portable2D]);
        var segment = Assert.Single(first.Report.Segments, item => item.Owner == "worldpack:default");

        Assert.Equal(2_609, canonical.SerializedBytes.Length);
        Assert.Equal(60, canonical.Pack.Chunks.Count);
        Assert.Equal(48u, canonical.Pack.Descriptor.CollisionProfilesOffset);
        Assert.Equal(60u, canonical.Pack.Descriptor.TargetExpansionsOffset);
        Assert.Equal(272u, canonical.Pack.Descriptor.DirectoryOffset);
        Assert.Equal(1_472u, canonical.Pack.Descriptor.ChunkDataOffset);
        Assert.Equal(770, canonical.Pack.Chunks.Sum(chunk => chunk.Directory.VisualStoredBytes));
        Assert.Equal(367, canonical.Pack.Chunks.Sum(chunk => chunk.Directory.CollisionStoredBytes));
        Assert.Equal(49, canonical.Pack.Chunks.Max(chunk => chunk.Directory.VisualStoredBytes + chunk.Directory.CollisionStoredBytes));
        Assert.Equal(canonical.SerializedBytes, first.Rom.AsSpan(segment.PhysicalStart, segment.Length).ToArray());
        Assert.DoesNotContain(first.Report.Segments, item => item.Owner.StartsWith("legacy-world-data", StringComparison.Ordinal));
        Assert.Equal(first.Rom, second.Rom);
        Assert.Equal(first.Report.Segments, second.Report.Segments);
    }

    [Fact]
    public void Full_stage1_fixture_world_pack_matches_the_raw_importer_and_is_deterministic()
    {
        var firstGeneratedTile = GameBoyVideoProgram.FirstGeneratedBackgroundTile;
        var raw = GameBoyTiledMapImporter.Load(FullStage1ValidationFixture.MapPath, firstGeneratedTile);
        var first = GameBoyTiledMapImporter.CompileWorldPack(FullStage1ValidationFixture.MapPath, firstGeneratedTile);
        var second = GameBoyTiledMapImporter.CompileWorldPack(FullStage1ValidationFixture.MapPath, firstGeneratedTile);
        var decoded = WorldPackSerializer.Deserialize(first.SerializedBytes);
        var decodedTiles = decoded.ToWorldTileGrid(cell => cell.Span[0]);
        var decodedCollision = decoded.ToWorldMap2D();

        Assert.Equal(53, first.Pack.Descriptor.VisualMetatileCount);
        Assert.Equal(3, first.Pack.Descriptor.CollisionProfileCount);
        Assert.Equal(60, first.Pack.Chunks.Count);
        Assert.True(first.SerializedBytes.Length <= 7_712, $"GB WorldPack used {first.SerializedBytes.Length} bytes.");
        Assert.Equal(first.SerializedBytes, second.SerializedBytes);
        Assert.Equal(raw.GeneratedTileData, first.GeneratedTileData);
        Assert.Equal(raw.WorldTileIds, Enumerable.Range(0, raw.WorldTileIds.Length)
            .Select(index => decodedTiles.TileIdAt(index % raw.Width, index / raw.Width)));
        Assert.Equal(raw.WorldFlags, Enumerable.Range(0, raw.WorldFlags.Length)
            .Select(index => decodedCollision.FlagsAt(index % raw.Width, index / raw.Width)));
    }

    [Fact]
    public void External_tsj_background_world_map_round_trips_through_game_boy_world_pack()
    {
        var mapPath = RepositoryFile("samples/tiled-cross-target-2d-scroll/cross-target-2d-scroll.tmj");
        var raw = GameBoyTiledMapImporter.Load(mapPath, GameBoyVideoProgram.FirstGeneratedBackgroundTile);
        var compiled = GameBoyTiledMapImporter.CompileWorldPack(mapPath, GameBoyVideoProgram.FirstGeneratedBackgroundTile);
        var decoded = WorldPackSerializer.Deserialize(compiled.SerializedBytes);
        var decodedTiles = decoded.ToWorldTileGrid(cell => cell.Span[0]);

        Assert.Equal(raw.GeneratedTileData, compiled.GeneratedTileData);
        Assert.Equal(raw.WorldTileIds, Enumerable.Range(0, raw.WorldTileIds.Length)
            .Select(index => decodedTiles.TileIdAt(index % raw.Width, index / raw.Width)));
        Assert.Equal(raw.WorldFlags, Enumerable.Range(0, raw.WorldFlags.Length)
            .Select(index => decoded.CollisionAt(index % raw.Width, index / raw.Width)));
        Assert.Contains(compiled.Pack.Chunks, chunk =>
            chunk.Directory.VisualCodec == WorldPackCodec.ElementRle ||
            chunk.Directory.CollisionCodec == WorldPackCodec.ElementRle);
    }

    private static string RepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"Could not find repository file '{relativePath}'.");
    }
}
