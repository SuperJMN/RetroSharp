namespace RetroSharp.GameBoy.Tests;

using System.Text.Json;
using System.Text.Json.Nodes;
using RetroSharp.Core.Sdk;
using RetroSharp.GameBoy;
using RetroSharp.Sdk;
using Xunit;
using Xunit.Abstractions;

public sealed class FullStage1BaselineTests(ITestOutputHelper output)
{
    [Fact]
    public void Full_stage1_canonical_pack_is_embedded_once_with_exact_offsets()
    {
        using var workspace = CreateNormalizedFullStage1();
        var mapPath = Path.Combine(workspace.Path, "stage1.full-baseline.tmj");
        var canonical = GameBoyTiledMapImporter.CompileWorldPack(
            mapPath,
            GameBoyVideoProgram.FirstGeneratedBackgroundTile);
        var source = $$"""
            void Main() {
                World.Load("{{mapPath.Replace('\\', '/')}}");
            }
            """;

        var first = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
            source,
            sdkLibraryImports: [SdkImportResolver.Portable2D]);
        var second = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
            source,
            sdkLibraryImports: [SdkImportResolver.Portable2D]);
        var segment = Assert.Single(first.Report.Segments, item => item.Owner == "worldpack:default");

        Assert.Equal(2_568, canonical.SerializedBytes.Length);
        Assert.Equal(60, canonical.Pack.Chunks.Count);
        Assert.Equal(48u, canonical.Pack.Descriptor.CollisionProfilesOffset);
        Assert.Equal(60u, canonical.Pack.Descriptor.TargetExpansionsOffset);
        Assert.Equal(272u, canonical.Pack.Descriptor.DirectoryOffset);
        Assert.Equal(1_472u, canonical.Pack.Descriptor.ChunkDataOffset);
        Assert.Equal(770, canonical.Pack.Chunks.Sum(chunk => chunk.Directory.VisualStoredBytes));
        Assert.Equal(326, canonical.Pack.Chunks.Sum(chunk => chunk.Directory.CollisionStoredBytes));
        Assert.Equal(49, canonical.Pack.Chunks.Max(chunk => chunk.Directory.VisualStoredBytes + chunk.Directory.CollisionStoredBytes));
        var largestChunkIndex = canonical.Pack.Chunks
            .Select((chunk, index) => (chunk, index))
            .MaxBy(item => item.chunk.Directory.VisualStoredBytes + item.chunk.Directory.CollisionStoredBytes)
            .index;
        var cpu = new GameBoyTestCpu(first.Rom);
        cpu.SetCurrentRomBank(1);
        cpu.SetWram(GameBoyRuntimeMemoryLayout.Banking.ActualVisibleBank, 1);
        Assert.Equal(
            GameBoyWorldPackResult.Success,
            cpu.RunWorldPackDecode(
                first.Report.FixedSymbols[GameBoyRomBuilder.WorldPackVisualDecodeLabel],
                checked((ushort)largestChunkIndex),
                slot: 0));
        Assert.Equal(
            GameBoyWorldPackResult.Success,
            cpu.RunWorldPackDecode(
                first.Report.FixedSymbols[GameBoyRomBuilder.WorldPackCollisionDecodeLabel],
                checked((ushort)largestChunkIndex),
                slot: 0));
        Assert.Equal(
            canonical.Pack.Chunks[largestChunkIndex].VisualIds.Select(id => (byte)id),
            Enumerable.Range(0xC300, canonical.Pack.Chunks[largestChunkIndex].VisualIds.Count)
                .Select(address => cpu.Wram((ushort)address)));
        Assert.Equal(
            canonical.Pack.Chunks[largestChunkIndex].CollisionProfileIds.Select(id => (byte)id),
            Enumerable.Range(0xC380, canonical.Pack.Chunks[largestChunkIndex].CollisionProfileIds.Count)
                .Select(address => cpu.Wram((ushort)address)));
        foreach (var (hardwareX, hardwareY) in new[]
                 {
                     (0, 0),
                     (255, canonical.Pack.Descriptor.HardwareHeight / 2),
                     (256, canonical.Pack.Descriptor.HardwareHeight / 2),
                     (canonical.Pack.Descriptor.HardwareWidth - 1, canonical.Pack.Descriptor.HardwareHeight - 1),
                 })
        {
            var collision = cpu.RunWorldPackCollisionLookup(
                first.Report.FixedSymbols[GameBoyRomBuilder.WorldPackCollisionLookupLabel],
                checked((ushort)hardwareX),
                checked((ushort)hardwareY));
            Assert.Equal(GameBoyWorldPackResult.Success, collision.Status);
            Assert.Equal((byte)canonical.Pack.CollisionAt(hardwareX, hardwareY), collision.Value);

            var visual = cpu.RunWorldPackCollisionLookup(
                first.Report.FixedSymbols[GameBoyRomBuilder.WorldPackVisualLookupLabel],
                checked((ushort)hardwareX),
                checked((ushort)hardwareY));
            var coordinate = canonical.Pack.Locate(hardwareX, hardwareY);
            var visualId = canonical.Pack.VisualIdAt(hardwareX, hardwareY);
            var expansionIndex = checked(
                (visualId * canonical.Pack.Descriptor.MetatileWidth * canonical.Pack.Descriptor.MetatileHeight
                 + coordinate.SubcellIndex) * canonical.Pack.Descriptor.TargetCellStride);
            Assert.Equal(GameBoyWorldPackResult.Success, visual.Status);
            Assert.Equal(canonical.Pack.TargetExpansions.Span[expansionIndex], visual.Value);
        }
        Assert.Equal(canonical.SerializedBytes, first.Rom.AsSpan(segment.PhysicalStart, segment.Length).ToArray());
        Assert.DoesNotContain(first.Report.Segments, item => item.Owner.StartsWith("legacy-world-data", StringComparison.Ordinal));
        Assert.Equal(first.Rom, second.Rom);
        Assert.Equal(first.Report.Segments, second.Report.Segments);
    }

    [Fact]
    public void External_tsj_background_world_map_round_trips_through_game_boy_world_pack()
    {
        var mapPath = RepositoryFile("samples/tiled-free-scroll/free-scroll.tmj");
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

    [Fact]
    public void Full_stage1_game_boy_world_pack_matches_the_raw_importer_and_is_byte_deterministic()
    {
        using var workspace = CreateNormalizedFullStage1();
        var mapPath = Path.Combine(workspace.Path, "stage1.full-baseline.tmj");
        var firstGeneratedTile = GameBoyVideoProgram.FirstGeneratedBackgroundTile;
        var raw = GameBoyTiledMapImporter.Load(mapPath, firstGeneratedTile);

        var first = GameBoyTiledMapImporter.CompileWorldPack(mapPath, firstGeneratedTile);
        var second = GameBoyTiledMapImporter.CompileWorldPack(mapPath, firstGeneratedTile);
        var decoded = WorldPackSerializer.Deserialize(first.SerializedBytes);
        var decodedTiles = decoded.ToWorldTileGrid(cell => cell.Span[0]);
        var decodedCollision = decoded.ToWorldMap2D();

        Assert.Equal(53, first.Pack.Descriptor.VisualMetatileCount);
        Assert.Equal(3, first.Pack.Descriptor.CollisionProfileCount);
        Assert.Equal(60, first.Pack.Chunks.Count);
        Assert.Equal(2_568, first.SerializedBytes.Length);
        Assert.True(first.SerializedBytes.Length <= 7_712, $"GB WorldPack used {first.SerializedBytes.Length} bytes.");
        Assert.Equal(first.SerializedBytes, second.SerializedBytes);
        Assert.Equal(raw.GeneratedTileData, first.GeneratedTileData);
        Assert.Equal(raw.GeneratedTileData, second.GeneratedTileData);
        Assert.Equal(raw.WorldTileIds, Enumerable.Range(0, raw.WorldTileIds.Length)
            .Select(index => decodedTiles.TileIdAt(index % raw.Width, index / raw.Width)));
        Assert.Equal(raw.WorldFlags, Enumerable.Range(0, raw.WorldFlags.Length)
            .Select(index => decodedCollision.FlagsAt(index % raw.Width, index / raw.Width)));
        output.WriteLine($"GB full stage1 WorldPack: {first.SerializedBytes.Length} bytes");
    }

    [Fact]
    public void Full_stage1_game_boy_baseline_is_frozen()
    {
        using var workspace = CreateNormalizedFullStage1();
        var mapPath = Path.Combine(workspace.Path, "stage1.full-baseline.tmj");
        var world = GameBoyTiledMapImporter.Load(mapPath, GameBoyVideoProgram.FirstGeneratedBackgroundTile);
        var sprite = GameBoySpriteAssetCompiler.CompileFromFile(
            "mario_player",
            RepositoryFile("samples/runner/assets/mario-player.gb.png"),
            firstTile: 88,
            frameWidth: 18,
            frameHeight: 32);
        var music = GameBoyMusicAssetCompiler.CompileFromFile(
            "runner_theme",
            RepositoryFile("samples/runner/assets/music/runner.gb.vgz"));
        var sfx = GameBoySoundEffectAssetCompiler.CompileFromFile(
            "jump_sfx",
            RepositoryFile("samples/runner/assets/sfx/smb-jump.gb.vgm"));

        Assert.Equal(312, world.Width);
        Assert.Equal(40, world.Height);
        Assert.Equal(12_480, world.WorldTileIds.Length);
        Assert.Equal(12_480, world.WorldFlags.Length);
        Assert.Equal(788, world.WorldFlags.Count(flags => flags == WorldTileFlags.Solid));
        Assert.Equal(56, world.WorldFlags.Count(flags => flags == WorldTileFlags.Platform));
        Assert.Equal(WorldTileFlags.Solid, world.WorldFlags[38 * world.Width]);
        Assert.Equal(82, world.GeneratedTileData.Length / 16);
        Assert.Equal(1_312, world.GeneratedTileData.Length);
        Assert.Equal(60, sprite.TileCount);
        Assert.Equal(960, sprite.TileData.Length);
        Assert.Equal(11_614, music.Data.Length);
        Assert.Equal(28, sfx.Data.Length);

        var fullSource = FullStage1RunnerSource(mapPath);
        var capacityProbeRom = GameBoyRomCompiler.CompileSource(fullSource, RunnerSample.Directory);
        Assert.Equal(131_072, capacityProbeRom.Length);

        var runtimeProbe = FullStage1RuntimeSource(fullSource);
        var runtimeProbeRom = GameBoyRomCompiler.CompileSource(runtimeProbe, RunnerSample.Directory);
        Assert.Equal(131_072, runtimeProbeRom.Length);

        Assert.Equal(21, GameBoyTarget.Capabilities.MaxBackgroundTileWritesPerFrame);
        Sdk2DOperationValidator.Validate(
            GameBoyTarget.Capabilities,
            new Sdk2DOperation.StreamMapColumn(0, 0, 0, 19));
        Sdk2DOperationValidator.Validate(
            GameBoyTarget.Capabilities,
            new Sdk2DOperation.StreamMapRow(0, 0, 0, 21));

        var facts = new
        {
            target = "gb",
            sourceCells = new { width = 156, height = 20, tilePixels = 16 },
            hardwareTiles = new { width = world.Width, height = world.Height, cells = world.WorldTileIds.Length },
            collision = new { bytes = world.WorldFlags.Length, solidCells = 788, platformCells = 56, platformTopY = 272, floorY = 304, noHit = -1 },
            resources = new
            {
                visualBytes = world.WorldTileIds.Length,
                backgroundPatternTiles = world.GeneratedTileData.Length / 16,
                backgroundPatternBytes = world.GeneratedTileData.Length,
                spriteTiles = sprite.TileCount,
                spriteBytes = sprite.TileData.Length,
                musicBytes = music.Data.Length,
                sfxBytes = sfx.Data.Length,
                capacityProbeRomBytes = capacityProbeRom.Length,
            },
            checks = new[]
            {
                new { id = "address-width", status = "passes", detail = "312 hardware columns and word camera/source-edge positions compile without truncation" },
                new { id = "collision-abi", status = "passes-after-lw-1.2", detail = "world hit-top returns 304 as 30 01 and no hit as FF FF" },
                new { id = "rom-capacity", status = "capacity-probe-passes", detail = "the unchanged runner payload with only World.Load redirected emits a 131072-byte MBC1 ROM" },
                new { id = "tile-patterns", status = "passes", detail = "6 reserved + 82 background + 60 sprite tiles use 148 of 256 indexes" },
                new { id = "ram-staging", status = "blocked", detail = "no fixed large-world chunk staging contract exists" },
                new { id = "vblank", status = "bounded-current-edge", detail = "visible column and row commits remain within the 21-tile write budget" },
            },
        };

        output.WriteLine(JsonSerializer.Serialize(facts, JsonOptions));
        AssertReportDocuments("Game Boy", "131,072", "11,614", "82", "60");
    }

    private static string FullStage1RunnerSource(string mapPath)
    {
        var portablePath = mapPath.Replace('\\', '/');
        return RunnerSample.CompiledSource().Replace(
            "assets/maps/stage1.playable.tmj",
            portablePath,
            StringComparison.Ordinal);
    }

    private static string FullStage1RuntimeSource(string source)
    {
        return source
            .Replace("const i16 Width = 176;", "const i16 Width = 312;", StringComparison.Ordinal)
            .Replace("const i16 Height = 30;", "const i16 Height = 40;", StringComparison.Ordinal)
            .Replace("const i16 StreamHeight = 30;", "const i16 StreamHeight = 40;", StringComparison.Ordinal)
            .Replace("const i16 PixelWidth = 1408;", "const i16 PixelWidth = 2496;", StringComparison.Ordinal);
    }

    private static TemporaryDirectory CreateNormalizedFullStage1()
    {
        var path = Path.Combine(Path.GetTempPath(), "retrosharp-full-stage1-gb", Guid.NewGuid().ToString("N"));
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
