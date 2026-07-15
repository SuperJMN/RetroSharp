namespace RetroSharp.GameBoy.Tests;

using RetroSharp.GameBoy;
using RetroSharp.Core.Sdk;
using RetroSharp.Sdk;
using System.Security.Cryptography;
using Xunit;

public sealed class GameBoyWorldPackPlacementTests
{
    [Fact]
    public void Pack_relative_offsets_continue_across_a_16_kib_window()
    {
        var bytes = Enumerable.Range(0, 16 * 1024 + 37).Select(value => (byte)value).ToArray();

        var placement = GameBoyWorldPackPlacement.Create(
            baseBank: 3,
            baseAddress: 0x7FF0,
            bytes);

        Assert.Equal(
            [
                new GameBoyWorldPackSegment(0, 3, 0x7FF0, 16),
                new GameBoyWorldPackSegment(16, 4, 0x4000, 16 * 1024),
                new GameBoyWorldPackSegment(16 * 1024 + 16, 5, 0x4000, 21),
            ],
            placement.Segments);
        Assert.Equal(new GameBoyFarAddress(3, 0x7FF0), placement.TranslateOffset(0));
        Assert.Equal(new GameBoyFarAddress(4, 0x4000), placement.TranslateOffset(16));
        Assert.Equal(new GameBoyFarAddress(5, 0x4014), placement.TranslateOffset(bytes.Length - 1));
    }

    [Fact]
    public void Synthetic_serialized_pack_crosses_final_mbc1_banks_without_overlap()
    {
        var serialized = CreateSyntheticWorldPack();
        var filler = string.Join(Environment.NewLine, Enumerable.Repeat("    value += 1;", 4_000));
        var source = "void Main() { u8 value = 0;\n" + filler + "\n}";

        var result = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
            source,
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            packedWorldOverride: serialized);
        var packSegments = result.Report.Segments
            .Where(segment => segment.Owner == "worldpack:default")
            .OrderBy(segment => segment.PhysicalStart)
            .ToArray();

        Assert.Equal("gb-simple-mbc1-current", result.Report.SelectedProfile);
        Assert.Equal(1, result.Rom[0x147]);
        Assert.True(packSegments.Length >= 2);
        Assert.True(packSegments.Select(segment => segment.Bank).Distinct().Count() >= 2);
        Assert.Equal(
            serialized,
            packSegments.SelectMany(segment => result.Rom.AsSpan(segment.PhysicalStart, segment.Length).ToArray()).ToArray());

        var ordered = result.Report.Segments.OrderBy(segment => segment.PhysicalStart).ToArray();
        Assert.All(
            ordered.Zip(ordered.Skip(1)),
            pair => Assert.True(
                pair.First.PhysicalStart + pair.First.Length <= pair.Second.PhysicalStart,
                $"{pair.First.Owner} overlaps {pair.Second.Owner}"));
    }

    [Fact]
    public void Data_only_mbc1_build_initializes_the_banking_foundation()
    {
        var serialized = CreateSyntheticWorldPack(chunkColumns: 256);

        var result = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
            "void Main() { }",
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            packedWorldOverride: serialized);
        var cpu = new GameBoyTestCpu(result.Rom);

        cpu.RunFrames(2);

        Assert.Equal("gb-simple-mbc1-current", result.Report.SelectedProfile);
        Assert.Equal(1, result.Rom[0x147]);
        Assert.Equal(1, cpu.CurrentRomBank);
        Assert.Equal(1, cpu.Wram(GameBoyRuntimeMemoryLayout.Banking.ActualVisibleBank));
        Assert.Equal(0, cpu.Wram(GameBoyRuntimeMemoryLayout.Banking.ProgramCurrentBank));
    }

    [Fact]
    public void Packed_world_coexists_with_bgm_and_sfx_in_disjoint_reported_ranges()
    {
        var directory = RepositoryDirectory("samples/runner");
        var mapPath = Path.Combine(directory, "assets/maps/stage1.playable.tmj");
        var canonical = GameBoyTiledMapImporter.CompileWorldPack(
            mapPath,
            GameBoyVideoProgram.FirstGeneratedBackgroundTile);
        var filler = string.Join(Environment.NewLine, Enumerable.Repeat("    value += 1;", 4_000));
        var source = """
            void Main() {
                World.Load("assets/maps/stage1.playable.tmj");
                Music.Asset(theme, "assets/music/runner.vgz");
                Sfx.Asset(jump, "assets/sfx/smb-jump.gb.vgm");
                u8 value = 0;
            """ + filler + """

                Audio.Init();
                Music.Play(theme);
                Sfx.Play(jump);
                while (true) {
                    Video.WaitVBlank();
                    Audio.Update();
                }
            }
            """;

        var result = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
            source,
            directory,
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            packedWorldOverride: canonical.SerializedBytes);
        var packSegments = result.Report.Segments.Where(item => item.Owner == "worldpack:default").ToArray();
        var bgmSegments = result.Report.Segments.Where(item => item.Owner == "bgm:theme").ToArray();
        var sfxSegments = result.Report.Segments.Where(item => item.Owner == "sfx:jump").ToArray();

        Assert.Equal("gb-simple-mbc1-current", result.Report.SelectedProfile);
        Assert.Equal(11_614, bgmSegments.Sum(item => item.Length));
        Assert.Equal(28, sfxSegments.Sum(item => item.Length));
        Assert.Equal(
            canonical.SerializedBytes,
            packSegments.OrderBy(item => item.PhysicalStart)
                .SelectMany(item => result.Rom.AsSpan(item.PhysicalStart, item.Length).ToArray())
                .ToArray());
        var inlineProgramEnd = result.Report.Segments
            .Where(item => item.Owner is "program" or "fixed-bank/header")
            .Max(item => item.PhysicalStart + item.Length);
        Assert.True(packSegments.Min(item => item.PhysicalStart) >= inlineProgramEnd);
        Assert.True(bgmSegments.Min(item => item.PhysicalStart) >= packSegments.Max(item => item.PhysicalStart + item.Length));
        AssertReportedSegmentsDoNotOverlap(result.Report);

        var cpu = new GameBoyTestCpu(result.Rom);
        Assert.Equal(4, cpu.RunUntilRegisterWrites(0xFF14, count: 4, maxInstructions: 50_000_000).Count);
    }

    [Fact]
    public void Explicit_raw_world_keeps_the_rom_only_golden_and_has_no_pack_segment()
    {
        const string source = """
            void DefineWorld() {
                World.Column(0, 0, 4);
                World.Column(1, 0, 4);
                World.Column(2, 0, 4);
                World.Flags(0, 0, 1);
                World.Flags(1, 0, 1);
                World.Flags(2, 0, 1);
                World.Map(3, 11, 2);
                Camera.Init(3, 11, 2);
            }

            void Main() {
                DefineWorld();
                i16 footY = 16;
                i16 hit = Camera.AabbTiles(72, footY - 8, 16, 16, 1);
                i16 hitTop = Camera.AabbHitTop(72, footY - 8, 16, 16, 1);
            }
            """;

        var result = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
            source,
            sdkLibraryImports: [SdkImportResolver.Portable2D]);

        Assert.Equal("927F804320BC973C4139D33F5010C236EE595EE39AEC17DA0A2D02D05B42099F", Convert.ToHexString(SHA256.HashData(result.Rom)));
        Assert.DoesNotContain(result.Report.Segments, item => item.Owner == "worldpack:default");
        Assert.Contains(result.Report.Segments, item => item.Owner == "legacy-world-data:default");
    }

    [Fact]
    public void Small_world_load_embeds_the_canonical_pack_and_stays_rom_only()
    {
        var directory = RepositoryDirectory("samples/actor-framework");
        const string source = """
            void Main() {
                World.Load("actors.tmj");
            }
            """;
        var canonical = GameBoyTiledMapImporter.CompileWorldPack(
            Path.Combine(directory, "actors.tmj"),
            GameBoyVideoProgram.FirstGeneratedBackgroundTile);

        var first = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
            source,
            directory,
            sdkLibraryImports: [SdkImportResolver.Portable2D]);
        var second = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
            source,
            directory,
            sdkLibraryImports: [SdkImportResolver.Portable2D]);
        var segment = Assert.Single(first.Report.Segments, item => item.Owner == "worldpack:default");

        Assert.Equal("gb-rom-only-current", first.Report.SelectedProfile);
        Assert.Equal(32 * 1024, first.Rom.Length);
        Assert.Equal(0, first.Rom[0x147]);
        Assert.Equal(canonical.SerializedBytes.Length, segment.Length);
        Assert.Equal(canonical.SerializedBytes, first.Rom.AsSpan(segment.PhysicalStart, segment.Length).ToArray());
        Assert.Equal(first.Rom, second.Rom);
        Assert.Equal(first.Report.SelectedProfile, second.Report.SelectedProfile);
        Assert.Equal(first.Report.RomSize, second.Report.RomSize);
        Assert.Equal(first.Report.Segments, second.Report.Segments);
        Assert.Equal(first.Report.OccupiedBanks, second.Report.OccupiedBanks);
    }

    [Fact]
    public void World_load_with_camera_runtime_references_uses_the_packed_edge_scheduler_path()
    {
        var directory = RepositoryDirectory("samples/tiled-tall");
        const string source = """
            void Main() {
                World.Load("tall.tmj");
                Camera.Init(16, 0, 40);
                Camera.SetPosition(0, 8);
                Camera.Apply();
            }
            """;

        var result = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
            source,
            directory,
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            packedWorldOverride: GameBoyTiledMapImporter.CompileWorldPack(
                Path.Combine(directory, "tall.tmj"),
                GameBoyVideoProgram.FirstGeneratedBackgroundTile).SerializedBytes);

        Assert.Contains(result.Report.Segments, item => item.Owner == "worldpack:default");
        Assert.DoesNotContain(result.Report.Segments, item => item.Owner == "legacy-world-data:default");
    }

    private static string RepositoryDirectory(string relativePath)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        return Path.Combine(root, relativePath);
    }

    private static void AssertReportedSegmentsDoNotOverlap(GameBoyRomBuildReport report)
    {
        var ordered = report.Segments.OrderBy(segment => segment.PhysicalStart).ToArray();
        Assert.All(
            ordered.Zip(ordered.Skip(1)),
            pair => Assert.True(
                pair.First.PhysicalStart + pair.First.Length <= pair.Second.PhysicalStart,
                $"{pair.First.Owner} overlaps {pair.Second.Owner}"));
    }

    internal static byte[] CreateSyntheticWorldPack(int chunkColumns = 128)
    {
        const int cellsPerChunk = 64;
        var collisionProfilesOffset = (uint)WorldPackDescriptor.V1HeaderBytes;
        var targetExpansionsOffset = collisionProfilesOffset + 2;
        var directoryOffset = targetExpansionsOffset + 2;
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
            TargetCellStride = 1,
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
            new byte[] { 0, 1 },
            chunks);
        var serialized = WorldPackSerializer.Serialize(pack);
        Assert.True(serialized.Length > 16 * 1024);
        return serialized;
    }
}
