namespace RetroSharp.GameBoy.Tests;

using System.Text.Json;
using System.Text.Json.Nodes;
using RetroSharp.Core.Sdk;
using RetroSharp.GameBoy;
using Xunit;
using Xunit.Abstractions;

public sealed class FullStage1BaselineTests(ITestOutputHelper output)
{
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
        var addressFailure = Assert.Throws<InvalidOperationException>(
            () => GameBoyRomCompiler.CompileSource(runtimeProbe, RunnerSample.Directory));
        Assert.Equal("camera_init argument 1 must be between 1 and 255.", addressFailure.Message);

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
            collision = new { bytes = world.WorldFlags.Length, solidCells = 788, floorY = 304, noHit = 255 },
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
                new { id = "address-width", status = "blocked", detail = "312 hardware columns exceed the current 255-column camera ABI" },
                new { id = "collision-abi", status = "blocked", detail = "floor Y 304 cannot share an 8-bit hit result whose no-hit sentinel is 255" },
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
