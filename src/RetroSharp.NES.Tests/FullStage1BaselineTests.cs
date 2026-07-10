namespace RetroSharp.NES.Tests;

using System.Text.Json;
using System.Text.Json.Nodes;
using RetroSharp.Core.Sdk;
using RetroSharp.NES;
using Xunit;
using Xunit.Abstractions;

public sealed class FullStage1BaselineTests(ITestOutputHelper output)
{
    [Fact]
    public void External_tsj_background_world_map_round_trips_through_nes_world_pack()
    {
        var mapPath = RepositoryFile("samples/tiled-free-scroll/free-scroll.tmj");
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

            var coordinate = decoded.Locate(x, y);
            var expansionCell = decoded.VisualIdAt(x, y);
            var expansionOffset = (expansionCell * decoded.Descriptor.MetatileWidth * decoded.Descriptor.MetatileHeight + coordinate.SubcellIndex) * 2;
            Assert.Equal(
                (byte)(raw.WorldPaletteSlots[index] | (raw.WorldSourceTiles[index] << 2)),
                decoded.TargetExpansions.Span[expansionOffset + 1]);
        }
    }

    [Fact]
    public void Full_stage1_nes_world_pack_matches_raw_chr_palette_provenance_and_is_byte_deterministic()
    {
        using var workspace = CreateNormalizedFullStage1();
        var mapPath = Path.Combine(workspace.Path, "stage1.full-baseline.tmj");
        var firstGeneratedTile = NesVideoProgram.FirstSpriteTile + 95;
        var raw = NesTiledWorldImporter.Load(mapPath, firstGeneratedTile);

        var first = NesTiledWorldImporter.CompileWorldPack(mapPath, firstGeneratedTile);
        var second = NesTiledWorldImporter.CompileWorldPack(mapPath, firstGeneratedTile);
        var decoded = WorldPackSerializer.Deserialize(first.SerializedBytes);
        var decodedTiles = decoded.ToWorldTileGrid(cell => cell.Span[0]);
        var decodedCollision = decoded.ToWorldMap2D();

        Assert.Equal(53, first.Pack.Descriptor.VisualMetatileCount);
        Assert.Equal(2, first.Pack.Descriptor.CollisionProfileCount);
        Assert.Equal(60, first.Pack.Chunks.Count);
        Assert.Equal(2, first.Pack.Descriptor.TargetCellStride);
        Assert.Equal(2_762, first.SerializedBytes.Length);
        Assert.True(first.SerializedBytes.Length <= 7_920, $"NES WorldPack used {first.SerializedBytes.Length} bytes.");
        Assert.Equal(first.SerializedBytes, second.SerializedBytes);
        Assert.Equal(raw.GeneratedTileData, first.GeneratedTileData);
        Assert.Equal(raw.BackgroundPalette, first.BackgroundPalette);
        Assert.Equal(raw.GeneratedTileData, second.GeneratedTileData);
        Assert.Equal(raw.BackgroundPalette, second.BackgroundPalette);

        for (var index = 0; index < raw.WorldTileIds.Length; index++)
        {
            var x = index % raw.Width;
            var y = index / raw.Width;
            Assert.Equal(raw.WorldTileIds[index], decodedTiles.TileIdAt(x, y));
            Assert.Equal(raw.WorldFlags[index], decodedCollision.FlagsAt(x, y));

            var coordinate = decoded.Locate(x, y);
            var visualId = decoded.VisualIdAt(x, y);
            var expansionCell = visualId * 4 + coordinate.SubcellIndex;
            var metadata = decoded.TargetExpansions.Span[expansionCell * 2 + 1];
            Assert.Equal((byte)(raw.WorldPaletteSlots[index] | (raw.WorldSourceTiles[index] << 2)), metadata);
            Assert.Equal(0, metadata & 0xF8);
        }

        output.WriteLine($"NES full stage1 WorldPack: {first.SerializedBytes.Length} bytes");
    }

    [Fact]
    public void Full_stage1_nes_baseline_is_frozen()
    {
        using var workspace = CreateNormalizedFullStage1();
        var mapPath = Path.Combine(workspace.Path, "stage1.full-baseline.tmj");
        var sprite = NesSpriteAssetCompiler.CompileFromFile(
            "mario_player",
            RepositoryFile("samples/runner/assets/mario-player.nes.png"),
            NesVideoProgram.FirstSpriteTile,
            frameWidth: 18,
            frameHeight: 32);
        var world = NesTiledWorldImporter.Load(mapPath, NesVideoProgram.FirstSpriteTile + sprite.TileCount);
        var music = NesMusicAssetCompiler.CompileFromFile(
            "runner_theme",
            RepositoryFile("samples/runner/assets/music/runner.nes.vgz"));
        var sfx = NesSoundEffectAssetCompiler.CompileFromFile(
            "jump_sfx",
            RepositoryFile("samples/runner/assets/sfx/smb-jump.vgm"));

        Assert.Equal(312, world.Width);
        Assert.Equal(40, world.Height);
        Assert.Equal(12_480, world.WorldTileIds.Length);
        Assert.Equal(12_480, world.WorldFlags.Length);
        Assert.Equal(788, world.WorldFlags.Count(flags => flags == WorldTileFlags.Solid));
        Assert.Equal(WorldTileFlags.Solid, world.WorldFlags[38 * world.Width]);
        Assert.Equal(90, world.GeneratedTileData.Length / 16);
        Assert.Equal(1_440, world.GeneratedTileData.Length);
        Assert.Equal(95, sprite.TileCount);
        Assert.Equal(1_520, sprite.TileData.Length);
        Assert.Equal(4_126, music.Data.Length);
        Assert.Equal(new[] { 129, 1_153 }, music.DpcmBlocks.Select(block => block.Data.Length));
        Assert.Equal(1_282, music.DpcmBlocks.Sum(block => block.Data.Length));
        Assert.Equal(26, sfx.Data.Length);

        var fullSource = FullStage1RunnerSource(mapPath);
        var fullPayloadFailure = Assert.Throws<InvalidOperationException>(
            () => NesRomCompiler.CompileSource(fullSource, RunnerSample.Directory));
        Assert.Equal(
            "NES DPCM sample block from $E980 with 1153 bytes cannot fit in PRG ROM after music data ending at $134C4.",
            fullPayloadFailure.Message);

        var noAudioFailure = Assert.Throws<InvalidOperationException>(
            () => NesRomCompiler.CompileSource(WithoutAudio(fullSource), RunnerSample.Directory));
        Assert.Equal("NES PRG ROM overflow: 41851 bytes emitted, 32762 bytes available.", noAudioFailure.Message);

        var runtimeProbeFailure = Assert.Throws<InvalidOperationException>(
            () => NesRomCompiler.CompileSource(FullStage1CameraRuntimeSource(mapPath), RunnerSample.Directory));
        Assert.Equal(
            "NES DPCM sample block from $E980 with 1153 bytes cannot fit in PRG ROM after music data ending at $10A06.",
            runtimeProbeFailure.Message);

        Assert.Equal(32, NesTarget.Capabilities.MaxBackgroundTileWritesPerFrame);
        Assert.Equal(9, NesTarget.Capabilities.MaxAttributeWritesPerFrame);
        Sdk2DOperationValidator.Validate(
            NesTarget.Capabilities,
            new Sdk2DOperation.StreamMapColumn(0, 0, 0, 30));
        Sdk2DOperationValidator.Validate(
            NesTarget.Capabilities,
            new Sdk2DOperation.StreamMapRow(0, 0, 0, 32));

        var facts = new
        {
            target = "nes",
            sourceCells = new { width = 156, height = 20, tilePixels = 16 },
            hardwareTiles = new { width = world.Width, height = world.Height, cells = world.WorldTileIds.Length },
            collision = new { bytes = world.WorldFlags.Length, solidCells = 788, floorY = 304, noHit = -1 },
            resources = new
            {
                visualBytes = world.WorldTileIds.Length,
                backgroundPatternTiles = world.GeneratedTileData.Length / 16,
                backgroundPatternBytes = world.GeneratedTileData.Length,
                spriteTiles = sprite.TileCount,
                spriteBytes = sprite.TileData.Length,
                chrBytes = (NesVideoProgram.FirstSpriteTile + sprite.TileCount + world.GeneratedTileData.Length / 16) * 16,
                musicBytes = music.Data.Length,
                dpcmBytes = music.DpcmBlocks.Sum(block => block.Data.Length),
                sfxBytes = sfx.Data.Length,
                prgBytesWithoutAudio = 41_851,
                prgBytesAvailable = 32_762,
            },
            checks = new[]
            {
                new { id = "address-width", status = "passes", detail = "312 hardware columns reach the existing PRG-capacity failure without camera/source-column truncation" },
                new { id = "collision-abi", status = "passes-after-lw-1.2", detail = "world hit-top returns 304 as 30 01 and no hit as FF FF" },
                new { id = "rom-capacity", status = "blocked", detail = "41851 bytes without audio exceed 32762 bytes; full audio then conflicts with DPCM placement at E980" },
                new { id = "tile-patterns", status = "passes", detail = "6 reserved + 95 sprite + 90 background tiles use 191 of 256 indexes and 3056 of 8192 CHR bytes" },
                new { id = "ram-staging", status = "blocked", detail = "mapper-0 has no mapper-backed large-world staging path" },
                new { id = "vblank", status = "bounded-current-phase", detail = "current limits are 32 tile writes and 9 attribute writes per phase" },
            },
        };

        output.WriteLine(JsonSerializer.Serialize(facts, JsonOptions));
        AssertReportDocuments("NES", "41,851", "32,762", "$E980", "$134C4", "$10A06", "3,056", "8,192");
    }

    private static string FullStage1RunnerSource(string mapPath)
    {
        var portablePath = mapPath.Replace('\\', '/');
        return RunnerSample.CompiledSource().Replace(
            "assets/maps/stage1.playable.tmj",
            portablePath,
            StringComparison.Ordinal);
    }

    private static string FullStage1CameraRuntimeSource(string mapPath)
    {
        var portablePath = mapPath.Replace('\\', '/');
        return $$"""
                 void Main() {
                     Video.Init();
                     Music.Asset(runner_theme, "assets/music/runner.vgz");
                     Sfx.Asset(jump_sfx, "assets/sfx/smb-jump.vgm");
                     Audio.Init();
                     Music.Play(runner_theme);
                     World.Load("{{portablePath}}");
                     Camera.Init(312, 0, 30);
                     i16 cameraX = 1888;
                     Camera.SetPosition(cameraX, 0);
                     Camera.Apply();
                     Audio.Update();
                 }
                 """;
    }

    private static string WithoutAudio(string source)
    {
        return source
            .Replace("Music.Asset(runner_theme, \"assets/music/runner.vgz\");", "", StringComparison.Ordinal)
            .Replace("Sfx.Asset(jump_sfx, \"assets/sfx/smb-jump.vgm\");", "", StringComparison.Ordinal)
            .Replace("Audio.Init();", "", StringComparison.Ordinal)
            .Replace("Music.Play(runner_theme);", "", StringComparison.Ordinal)
            .Replace("Audio.Update();", "", StringComparison.Ordinal)
            .Replace("Sfx.Play(jump_sfx);", "", StringComparison.Ordinal);
    }

    private static TemporaryDirectory CreateNormalizedFullStage1()
    {
        var path = Path.Combine(Path.GetTempPath(), "retrosharp-full-stage1-nes", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);

        var sourcePath = RepositoryFile("samples/runner/assets/maps/stage1.tmj");
        var root = JsonNode.Parse(File.ReadAllText(sourcePath))?.AsObject()
                   ?? throw new InvalidOperationException($"{sourcePath} is empty.");
        var height = root["height"]?.GetValue<int>()
                     ?? throw new InvalidOperationException($"{sourcePath} does not declare height.");
        root["properties"] = new JsonArray(
            MapProperty("retrosharpStreamY", 0),
            MapProperty("retrosharpWorldY", 0),
            MapProperty("retrosharpWorldHeight", height));
        foreach (var layer in root["layers"]?.AsArray() ?? [])
        {
            if (layer?["type"]?.GetValue<string>() == "tilelayer")
            {
                layer["name"] = "world";
            }
        }

        File.WriteAllText(
            Path.Combine(path, "stage1.full-baseline.tmj"),
            root.ToJsonString(JsonOptions));
        File.Copy(RepositoryFile("samples/runner/assets/maps/stage1.tsx"), Path.Combine(path, "stage1.tsx"));
        File.Copy(RepositoryFile("samples/runner/assets/maps/stage1.png"), Path.Combine(path, "stage1.png"));
        return new TemporaryDirectory(path);
    }

    private static JsonObject MapProperty(string name, int value)
    {
        return new JsonObject
        {
            ["name"] = name,
            ["type"] = "int",
            ["value"] = value,
        };
    }

    private static void AssertReportDocuments(string target, params string[] facts)
    {
        var report = File.ReadAllText(RepositoryFile("docs/LargeWorldsStage1Baseline.md"));
        Assert.Contains(target, report, StringComparison.Ordinal);
        foreach (var fact in facts)
        {
            Assert.Contains(fact, report, StringComparison.Ordinal);
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

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private sealed class TemporaryDirectory(string path) : IDisposable
    {
        public string Path { get; } = path;

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
