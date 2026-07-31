namespace RetroSharp.NES.Tests;

using RetroSharp.Core.Sdk;
using RetroSharp.NES;
using RetroSharp.Sdk;
using Xunit;

public sealed class FullStage1BaselineTests
{
    [Fact]
    public void Full_stage1_fixture_world_pack_matches_raw_chr_palette_provenance_and_is_deterministic()
    {
        var firstGeneratedTile = NesVideoProgram.FirstSpriteTile;
        var raw = NesTiledWorldImporter.Load(FullStage1ValidationFixture.MapPath, firstGeneratedTile);
        var first = NesTiledWorldImporter.CompileWorldPack(FullStage1ValidationFixture.MapPath, firstGeneratedTile);
        var second = NesTiledWorldImporter.CompileWorldPack(FullStage1ValidationFixture.MapPath, firstGeneratedTile);
        var decoded = WorldPackSerializer.Deserialize(first.SerializedBytes);
        var decodedTiles = decoded.ToWorldTileGrid(cell => cell.Span[0]);
        var decodedCollision = decoded.ToWorldMap2D();

        Assert.Equal(first.SerializedBytes, second.SerializedBytes);
        Assert.Equal(raw.GeneratedTileData, first.GeneratedTileData);
        Assert.Equal(raw.BackgroundPalette, first.BackgroundPalette);
        Assert.Equal(raw.WorldTileIds, Enumerable.Range(0, raw.WorldTileIds.Length)
            .Select(index => decodedTiles.TileIdAt(index % raw.Width, index / raw.Width)));
        Assert.Equal(raw.WorldFlags, Enumerable.Range(0, raw.WorldFlags.Length)
            .Select(index => decodedCollision.FlagsAt(index % raw.Width, index / raw.Width)));
        Assert.All(Enumerable.Range(0, raw.WorldTileIds.Length), index =>
        {
            var coordinate = decoded.Locate(index % raw.Width, index / raw.Width);
            var visualId = decoded.VisualIdAt(index % raw.Width, index / raw.Width);
            var metadata = decoded.TargetExpansions.Span[(visualId * 4 + coordinate.SubcellIndex) * 2 + 1];
            Assert.Equal((byte)(raw.WorldPaletteSlots[index] | (raw.WorldSourceTiles[index] << 2)), metadata);
            Assert.Equal(0, metadata & 0xF8);
        });
    }

    [Fact]
    public void Full_stage1_fixture_pack_is_embedded_exactly_in_the_selected_final_link()
    {
        var canonical = NesTiledWorldImporter.CompileWorldPack(
            FullStage1ValidationFixture.MapPath,
            NesVideoProgram.FirstSpriteTile);
        var result = RetroSharp.NES.NesRomCompiler.CompileSourceWithReport(
            FullStage1ValidationFixture.Source,
            FullStage1ValidationFixture.Directory,
            sdkLibraryImports: [SdkImportResolver.Portable2D]);
        var rebuilt = RetroSharp.NES.NesRomCompiler.CompileSourceWithReport(
            FullStage1ValidationFixture.Source,
            FullStage1ValidationFixture.Directory,
            sdkLibraryImports: [SdkImportResolver.Portable2D]);
        var segment = Assert.Single(result.Report.Segments, item => item.Owner == "worldpack:default");

        Assert.True(canonical.SerializedBytes.Length <= 8 * 1_024);
        Assert.Equal(0, segment.RelativeOffset);
        Assert.Equal(canonical.SerializedBytes, result.Rom.AsSpan(16 + segment.PhysicalStart, segment.Length).ToArray());
        Assert.Equal(result.Rom, rebuilt.Rom);
        Assert.Equal(result.Report.Segments, rebuilt.Report.Segments);
        Assert.DoesNotContain(result.Report.Segments, item => item.Owner == "pinned:world-flag-pointers");
        Assert.DoesNotContain(
            result.Report.Segments,
            item => item.Owner.StartsWith("pinned:world-column-attributes:", StringComparison.Ordinal));
        Assert.InRange(
            Assert.Single(result.Report.Segments, item => item.Owner == "pinned:worldpack-runtime-index").Length,
            1,
            8 * 1_024);
        Assert.True(result.Report.FixedPayloadBytes <= 16 * 1_024);
        Assert.True(result.Report.PinnedR7Bytes <= 8 * 1_024);
        Assert.True(result.Report.BootR7Bytes <= 8 * 1_024);
        Assert.True(result.Report.ResidentChrBytes <= 8 * 1_024);
        Assert.Equal(16 + result.Report.PrgRomSize + result.Report.ChrRomSize, result.Rom.Length);
    }

    [Fact]
    public void External_tsj_background_world_map_round_trips_through_nes_world_pack()
    {
        var mapPath = RepositoryFile("samples/tiled-cross-target-2d-scroll/cross-target-2d-scroll.tmj");
        var firstGeneratedTile = NesVideoProgram.FirstSpriteTile;
        var raw = NesTiledWorldImporter.Load(mapPath, firstGeneratedTile);
        var compiled = NesTiledWorldImporter.CompileWorldPack(mapPath, firstGeneratedTile);
        var decoded = WorldPackSerializer.Deserialize(compiled.SerializedBytes);
        var decodedTiles = decoded.ToWorldTileGrid(cell => cell.Span[0]);

        Assert.Equal(raw.GeneratedTileData, compiled.GeneratedTileData);
        Assert.Equal(raw.BackgroundPalette, compiled.BackgroundPalette);
        for (var index = 0; index < raw.WorldTileIds.Length; index++)
        {
            var x = index % raw.Width;
            var y = index / raw.Width;
            Assert.Equal(raw.WorldTileIds[index], decodedTiles.TileIdAt(x, y));
            Assert.Equal(raw.WorldFlags[index], decoded.CollisionAt(x, y));
        }
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
