namespace RetroSharp.NES.Tests;

using RetroSharp.Core.Sdk;
using RetroSharp.NES;
using RetroSharp.Sdk;
using Xunit;

public sealed class NesWorldPackPlacementTests
{
    [Fact]
    public void Pack_relative_offsets_cross_ordered_noncontiguous_r6_segments()
    {
        var bytes = Enumerable.Range(0, 8 * 1_024 + 37).Select(value => (byte)value).ToArray();
        var r6Sections = new[]
        {
            new NesPrgSectionLayout(0, 0, 8 * 1_024, NesPrgSectionKind.WorldR6),
            new NesPrgSectionLayout(3, 3 * 8 * 1_024, 8 * 1_024, NesPrgSectionKind.WorldR6),
        };

        var placement = NesWorldPackPlacement.Create(bytes, r6Sections);

        Assert.Equal(
            [
                new NesWorldPackSegment(0, 0, 0, 0x8000, 8 * 1_024),
                new NesWorldPackSegment(8 * 1_024, 3, 3 * 8 * 1_024, 0x8000, 37),
            ],
            placement.Segments);
        Assert.Equal(new NesFarAddress(0, 0x8000), placement.TranslateOffset(0));
        Assert.Equal(new NesFarAddress(0, 0x9FFF), placement.TranslateOffset(8 * 1_024 - 1));
        Assert.Equal(new NesFarAddress(3, 0x8000), placement.TranslateOffset(8 * 1_024));
        Assert.Equal(new NesFarAddress(3, 0x8024), placement.TranslateOffset(bytes.Length - 1));
    }

    [Fact]
    public void Placement_rejects_duplicate_r6_physical_ownership()
    {
        var bytes = new byte[8 * 1_024 + 1];
        var duplicateSections = new[]
        {
            new NesPrgSectionLayout(0, 0, 8 * 1_024, NesPrgSectionKind.WorldR6),
            new NesPrgSectionLayout(0, 0, 8 * 1_024, NesPrgSectionKind.WorldR6),
        };

        var exception = Assert.Throws<InvalidOperationException>(
            () => NesWorldPackPlacement.Create(bytes, duplicateSections));

        Assert.Equal("NES WorldPack R6 layout assigns physical bank 0 more than once.", exception.Message);
    }

    [Fact]
    public void Placement_reports_the_exact_multi_r6_capacity_constraint()
    {
        var bytes = new byte[2 * 8 * 1_024 + 1];
        var sections = new[]
        {
            new NesPrgSectionLayout(0, 0, 8 * 1_024, NesPrgSectionKind.WorldR6),
            new NesPrgSectionLayout(3, 3 * 8 * 1_024, 8 * 1_024, NesPrgSectionKind.WorldR6),
        };

        var exception = Assert.Throws<InvalidOperationException>(
            () => NesWorldPackPlacement.Create(bytes, sections));

        Assert.Equal(
            "NES WorldPack requires 16385 bytes, but the ordered R6 sections provide 16384 bytes.",
            exception.Message);
    }

    [Fact]
    public void Canonical_pack_larger_than_one_r6_window_selects_mmc3_and_reconstructs_exactly()
    {
        var serialized = CreateSyntheticWorldPack();

        var result = RetroSharp.NES.NesRomCompiler.CompileSourceWithReport(
            "void Main() { }",
            packedWorldOverride: serialized);
        var packSegments = result.Report.Segments
            .Where(segment => segment.Owner == "worldpack:default")
            .OrderBy(segment => segment.RelativeOffset)
            .ToArray();

        Assert.True(serialized.Length > 8 * 1_024);
        Assert.Equal("nes-mmc3-tvrom-v1", result.Report.SelectedProfile);
        Assert.Equal(new byte[] { 0x04, 0x02, 0x48, 0x00 }, result.Rom[4..8]);
        Assert.Equal(new[] { 0, 3, 4, 5 }, packSegments.Select(segment => segment.PhysicalBank).ToArray());
        Assert.Equal(
            new[] { 0, 8 * 1_024, 16 * 1_024, 24 * 1_024 },
            packSegments.Select(segment => segment.RelativeOffset).ToArray());
        Assert.Equal(
            serialized,
            packSegments
                .SelectMany(segment => result.Rom.AsSpan(16 + segment.PhysicalStart, segment.Length).ToArray())
                .ToArray());

        var decoded = WorldPackSerializer.Deserialize(serialized);
        var reconstructed = WorldPackSerializer.Deserialize(
            packSegments
                .SelectMany(segment => result.Rom.AsSpan(16 + segment.PhysicalStart, segment.Length).ToArray())
                .ToArray());
        Assert.Equal(decoded.Descriptor, reconstructed.Descriptor);
        Assert.Equal(decoded.Chunks.Select(chunk => chunk.Directory), reconstructed.Chunks.Select(chunk => chunk.Directory));

        var ordered = result.Report.Segments.OrderBy(segment => segment.PhysicalStart).ToArray();
        Assert.All(
            ordered.Zip(ordered.Skip(1)),
            pair => Assert.True(
                pair.First.PhysicalStart + pair.First.Length <= pair.Second.PhysicalStart,
                $"{pair.First.Owner} overlaps {pair.Second.Owner}"));
    }

    [Fact]
    public void Small_world_load_preserves_the_exact_deterministic_mapper0_link_before_banking()
    {
        var directory = RepositoryDirectory("samples/actor-framework");
        const string source = """
            void Main() {
                World.Load("actors.tmj");
            }
            """;
        var first = RetroSharp.NES.NesRomCompiler.CompileSourceWithReport(
            source,
            directory,
            sdkLibraryImports: [SdkImportResolver.Portable2D]);
        var second = RetroSharp.NES.NesRomCompiler.CompileSourceWithReport(
            source,
            directory,
            sdkLibraryImports: [SdkImportResolver.Portable2D]);

        Assert.Equal("nes-mapper-0-current", first.Report.SelectedProfile);
        Assert.Equal(32 * 1_024, first.Report.PrgRomSize);
        Assert.Equal(8 * 1_024, first.Report.ChrRomSize);
        Assert.DoesNotContain(first.Report.Segments, item => item.Owner == "worldpack:default");
        Assert.Equal(first.Rom, second.Rom);
        Assert.Equal(first.Report.SelectedProfile, second.Report.SelectedProfile);
        Assert.Equal(first.Report.PrgRomSize, second.Report.PrgRomSize);
        Assert.Equal(first.Report.ChrRomSize, second.Report.ChrRomSize);
        Assert.Equal(first.Report.Segments, second.Report.Segments);
    }

    [Fact]
    public void Forced_mmc3_places_palette_and_four_screen_nametables_in_boot_r7()
    {
        var result = RetroSharp.NES.NesRomCompiler.CompileSourceForMmc3TvromTestsWithReport(
            "void Main() { }");
        var palette = Assert.Single(result.Report.Segments, item => item.Owner == "boot:palette");
        var nametable = Assert.Single(result.Report.Segments, item => item.Owner == "boot:nametable");

        Assert.Equal("R7 boot-only $A000-$BFFF", palette.Window);
        Assert.Equal(2, palette.PhysicalBank);
        Assert.Equal(0xA000, palette.CpuAddress);
        Assert.Equal(32, palette.Length);
        Assert.Equal("R7 boot-only $A000-$BFFF", nametable.Window);
        Assert.Equal(2, nametable.PhysicalBank);
        Assert.Equal(0xA020, nametable.CpuAddress);
        Assert.Equal(4_096, nametable.Length);
        Assert.Equal(4_128, palette.Length + nametable.Length);
        Assert.True(palette.PhysicalStart + palette.Length <= nametable.PhysicalStart);
    }

    [Fact]
    public void Forced_mmc3_places_bgm_and_sfx_in_pinned_r7_while_dpcm_stays_fixed()
    {
        var directory = RepositoryDirectory("samples/runner");
        const string source = """
            void Main() {
                Music.Asset(theme, "assets/music/runner.vgz");
                Sfx.Asset(jump, "assets/sfx/smb-jump.vgm");
                Audio.Init();
                Music.Play(theme);
                Sfx.Play(jump);
                Audio.Update();
            }
            """;

        var result = RetroSharp.NES.NesRomCompiler.CompileSourceForMmc3TvromTestsWithReport(
            source,
            directory,
            sdkLibraryImports: [SdkImportResolver.Portable2D]);
        var bgm = Assert.Single(result.Report.Segments, item => item.Owner == "pinned:bgm:theme");
        var sfx = Assert.Single(result.Report.Segments, item => item.Owner == "pinned:sfx:jump");
        var dpcm = result.Report.Segments.Where(item => item.Owner.StartsWith("fixed:dpcm:", StringComparison.Ordinal)).ToArray();

        Assert.Equal("R7 pinned $A000-$BFFF", bgm.Window);
        Assert.Equal(1, bgm.PhysicalBank);
        Assert.Equal(4_126, bgm.Length);
        Assert.Equal("R7 pinned $A000-$BFFF", sfx.Window);
        Assert.Equal(1, sfx.PhysicalBank);
        Assert.Equal(26, sfx.Length);
        Assert.True(bgm.Length + sfx.Length <= 5_012);
        Assert.All(dpcm, segment =>
        {
            Assert.Equal("fixed $C000-$FFF9", segment.Window);
            Assert.InRange(segment.CpuAddress, 0xC000, 0xFFF9);
            Assert.Equal(0, (segment.CpuAddress - 0xC000) % 64);
            Assert.True(segment.CpuAddress + segment.Length <= 0xFFFA);
        });
        Assert.Equal(new[] { 129, 1_153 }, dpcm.Select(segment => segment.Length).Order().ToArray());
    }

    private static byte[] CreateSyntheticWorldPack(int chunkColumns = 208)
    {
        const int cellsPerChunk = 64;
        var collisionProfilesOffset = (uint)WorldPackDescriptor.V1HeaderBytes;
        var targetExpansionsOffset = collisionProfilesOffset + 2;
        var directoryOffset = targetExpansionsOffset + 4;
        var chunkDataOffset = directoryOffset + chunkColumns * WorldPackDescriptor.V1DirectoryEntryBytes;
        var nextOffset = (uint)chunkDataOffset;
        var ids = Enumerable.Range(0, cellsPerChunk).Select(index => (ushort)(index & 1)).ToArray();
        var chunks = new List<WorldPackChunk>(chunkColumns);
        for (var chunk = 0; chunk < chunkColumns; chunk++)
        {
            var directory = new WorldPackChunkDirectoryEntry(
                nextOffset,
                cellsPerChunk,
                cellsPerChunk,
                nextOffset + cellsPerChunk,
                cellsPerChunk,
                cellsPerChunk,
                8,
                8,
                WorldPackCodec.Raw,
                WorldPackCodec.Raw);
            chunks.Add(new WorldPackChunk(directory, ids, ids));
            nextOffset += cellsPerChunk * 2;
        }

        var descriptor = new WorldPackDescriptor
        {
            HardwareWidth = chunkColumns * 8,
            HardwareHeight = 8,
            MetatileWidth = 1,
            MetatileHeight = 1,
            ChunkColumns = chunkColumns,
            ChunkRows = 1,
            VisualMetatileCount = 2,
            CollisionProfileCount = 2,
            VisualIdBytes = 1,
            CollisionIdBytes = 1,
            TargetCellStride = 2,
            CollisionProfilesOffset = collisionProfilesOffset,
            TargetExpansionsOffset = targetExpansionsOffset,
            DirectoryOffset = directoryOffset,
            ChunkDataOffset = (uint)chunkDataOffset,
            PackLength = nextOffset,
        };
        var pack = new WorldPack(
            descriptor,
            [
                new WorldPackCollisionProfile([WorldTileFlags.Empty]),
                new WorldPackCollisionProfile([WorldTileFlags.Solid]),
            ],
            new byte[] { 0, 0, 1, 0 },
            chunks);
        var serialized = WorldPackSerializer.Serialize(pack);
        Assert.True(serialized.Length > 8 * 1_024);
        return serialized;
    }

    private static string RepositoryDirectory(string relativePath)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        return Path.Combine(root, relativePath);
    }
}
