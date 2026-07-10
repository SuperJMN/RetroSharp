namespace RetroSharp.Core.Tests;

using System.Text.Json;
using System.Text.Json.Nodes;
using RetroSharp.Core.Sdk;
using RetroSharp.Core.Sdk.Tiled;
using Xunit;

public sealed class WorldPackFormatAnalysisTests
{
    [Fact]
    public void Full_stage1_v1_costs_are_reproducible()
    {
        using var workspace = CreateNormalizedFullStage1();
        var logical = LogicalTiledMapImporter.Load(Path.Combine(workspace.Path, "stage1.worldpack.tmj"));
        var geometry = logical.Geometry;

        Assert.Equal(156, geometry.SourceWidth);
        Assert.Equal(20, geometry.SourceHeight);
        Assert.Equal(2, geometry.TileScaleX);
        Assert.Equal(2, geometry.TileScaleY);

        var sourceCells = checked(geometry.SourceWidth * geometry.SourceHeight);
        var visualMetatiles = logical.WorldGids.Distinct().Count();
        var collisionProfiles = CollisionProfiles(logical).Count;
        var chunkCount = CeilingDivide(geometry.SourceWidth, 8) * CeilingDivide(geometry.SourceHeight, 8);

        Assert.Equal(3_120, sourceCells);
        Assert.Equal(53, visualMetatiles);
        Assert.Equal(2, collisionProfiles);
        Assert.Equal(60, chunkCount);

        const int headerBytes = 48;
        const int directoryEntryBytes = 20;
        var directoryBytes = chunkCount * directoryEntryBytes;
        var collisionProfileBytes = collisionProfiles * geometry.TileScaleX * geometry.TileScaleY;
        var visualPlaneBytes = sourceCells;
        var collisionPlaneBytes = sourceCells;

        var gameBoyPackBytes = headerBytes
                               + collisionProfileBytes
                               + visualMetatiles * geometry.TileScaleX * geometry.TileScaleY
                               + directoryBytes
                               + visualPlaneBytes
                               + collisionPlaneBytes;
        var nesPackBytes = headerBytes
                           + collisionProfileBytes
                           + visualMetatiles * geometry.TileScaleX * geometry.TileScaleY * 2
                           + directoryBytes
                           + visualPlaneBytes
                           + collisionPlaneBytes;

        Assert.Equal(1_200, directoryBytes);
        Assert.Equal(8, collisionProfileBytes);
        Assert.Equal(7_708, gameBoyPackBytes);
        Assert.Equal(7_920, nesPackBytes);

        const int patternBytes = 16;
        var gameBoyPatternBytes = (6 + 82 + 60) * patternBytes;
        var nesChrBytes = (6 + 95 + 90) * patternBytes;
        var gameBoyKnownPayloadBytes = gameBoyPackBytes + gameBoyPatternBytes + 11_614 + 28;
        var nesKnownPrgBytes = nesPackBytes + 4_126 + 1_282 + 26;
        var nesKnownCombinedBytes = nesKnownPrgBytes + nesChrBytes;

        Assert.Equal(2_368, gameBoyPatternBytes);
        Assert.Equal(21_718, gameBoyKnownPayloadBytes);
        Assert.Equal(13_354, nesKnownPrgBytes);
        Assert.Equal(3_056, nesChrBytes);
        Assert.Equal(16_410, nesKnownCombinedBytes);

        var fourByEight = AnalyzeChunkChoice(geometry, chunkWidth: 4, chunkHeight: 8);
        var eightByFour = AnalyzeChunkChoice(geometry, chunkWidth: 8, chunkHeight: 4);
        var eightByEight = AnalyzeChunkChoice(geometry, chunkWidth: 8, chunkHeight: 8);
        var sixteenByEight = AnalyzeChunkChoice(geometry, chunkWidth: 16, chunkHeight: 8);
        var sixteenBySixteen = AnalyzeChunkChoice(geometry, chunkWidth: 16, chunkHeight: 16);

        Assert.Equal(new ChunkChoice(117, 2_340, 128, 5), fourByEight);
        Assert.Equal(new ChunkChoice(100, 2_000, 128, 5), eightByFour);
        Assert.Equal(new ChunkChoice(60, 1_200, 256, 3), eightByEight);
        Assert.Equal(new ChunkChoice(30, 600, 512, 3), sixteenByEight);
        Assert.Equal(new ChunkChoice(20, 400, 1_024, 2), sixteenBySixteen);

        var decodedChunkSlotsForOneByteIds = eightByEight.DecodedSlotBytes;
        Assert.Equal(298, decodedChunkSlotsForOneByteIds + 2 * 21);
        Assert.Equal(338, decodedChunkSlotsForOneByteIds + 2 * (32 + 9));
        Assert.Equal(554, decodedChunkSlotsForOneByteIds * 2 + 2 * 21);
        Assert.Equal(594, decodedChunkSlotsForOneByteIds * 2 + 2 * (32 + 9));

        var adr = File.ReadAllText(RepositoryFile("docs/WorldPackFormatV1.md"));
        Assert.Contains("7,708", adr, StringComparison.Ordinal);
        Assert.Contains("7,920", adr, StringComparison.Ordinal);
        Assert.Contains("298 bytes", adr, StringComparison.Ordinal);
        Assert.Contains("338 bytes", adr, StringComparison.Ordinal);
        Assert.Contains("21,718", adr, StringComparison.Ordinal);
        Assert.Contains("13,354", adr, StringComparison.Ordinal);
        Assert.Contains("16,410", adr, StringComparison.Ordinal);
    }

    private static HashSet<string> CollisionProfiles(LogicalTiledMap logical)
    {
        var geometry = logical.Geometry;
        var profiles = new HashSet<string>(StringComparer.Ordinal);
        for (var sourceY = 0; sourceY < geometry.WorldHeight; sourceY++)
        {
            for (var sourceX = 0; sourceX < geometry.SourceWidth; sourceX++)
            {
                var bytes = new byte[geometry.TileScaleX * geometry.TileScaleY];
                for (var subcellY = 0; subcellY < geometry.TileScaleY; subcellY++)
                {
                    for (var subcellX = 0; subcellX < geometry.TileScaleX; subcellX++)
                    {
                        var x = sourceX * geometry.TileScaleX + subcellX;
                        var y = sourceY * geometry.TileScaleY + subcellY;
                        bytes[subcellY * geometry.TileScaleX + subcellX] =
                            (byte)logical.WorldFlags[y * geometry.Width + x];
                    }
                }

                profiles.Add(Convert.ToHexString(bytes));
            }
        }

        return profiles;
    }

    private static int CeilingDivide(int value, int divisor) => (value + divisor - 1) / divisor;

    private static ChunkChoice AnalyzeChunkChoice(
        LogicalTiledMapGeometry geometry,
        int chunkWidth,
        int chunkHeight)
    {
        const int directoryEntryBytes = 20;
        const int decodedSlotCount = 4;
        const int worstTargetEdgeCells = 32;

        var chunkCount = checked(
            CeilingDivide(geometry.SourceWidth, chunkWidth)
            * CeilingDivide(geometry.SourceHeight, chunkHeight));
        var directoryBytes = checked(chunkCount * directoryEntryBytes);
        var decodedSlotBytes = checked(decodedSlotCount * chunkWidth * chunkHeight);
        var hardwareChunkWidth = checked(chunkWidth * geometry.TileScaleX);
        var hardwareChunkHeight = checked(chunkHeight * geometry.TileScaleY);
        var horizontalVisits = CeilingDivide(worstTargetEdgeCells + hardwareChunkWidth - 1, hardwareChunkWidth);
        var verticalVisits = CeilingDivide(worstTargetEdgeCells + hardwareChunkHeight - 1, hardwareChunkHeight);
        return new ChunkChoice(chunkCount, directoryBytes, decodedSlotBytes, Math.Max(horizontalVisits, verticalVisits));
    }

    private static TemporaryDirectory CreateNormalizedFullStage1()
    {
        var path = Path.Combine(Path.GetTempPath(), "retrosharp-worldpack-analysis", Guid.NewGuid().ToString("N"));
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
            Path.Combine(path, "stage1.worldpack.tmj"),
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
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

    private readonly record struct ChunkChoice(
        int ChunkCount,
        int DirectoryBytes,
        int DecodedSlotBytes,
        int WorstEdgeChunkVisits);
}
