namespace RetroSharp.Core.Tests;

using System.Text.Json;
using System.Text.Json.Nodes;
using RetroSharp.Core.Sdk;
using RetroSharp.Core.Sdk.Tiled;
using Xunit;

public sealed class WorldPackTests
{
    [Fact]
    public void Full_stage1_is_represented_by_canonical_clipped_chunks_without_a_physical_location()
    {
        using var workspace = CreateNormalizedFullStage1();
        var logical = LogicalTiledMapImporter.Load(Path.Combine(workspace.Path, "stage1.worldpack.tmj"));
        var expected = DeriveFullStage1(logical);
        var pack = CreateFullStage1Pack(expected);

        Assert.Equal(156, logical.Geometry.SourceWidth);
        Assert.Equal(20, logical.Geometry.SourceHeight);
        Assert.Equal(2, logical.Geometry.TileScaleX);
        Assert.Equal(2, logical.Geometry.TileScaleY);
        Assert.Equal(53, expected.VisualIdentityCount);
        Assert.Equal(2, expected.CollisionProfiles.Count);
        Assert.Equal(312, pack.Descriptor.HardwareWidth);
        Assert.Equal(40, pack.Descriptor.HardwareHeight);
        Assert.Equal(156, pack.SourceWidth);
        Assert.Equal(20, pack.SourceHeight);
        Assert.Equal(20, pack.Descriptor.ChunkColumns);
        Assert.Equal(3, pack.Descriptor.ChunkRows);
        Assert.Equal(53, pack.Descriptor.VisualMetatileCount);
        Assert.Equal(2, pack.Descriptor.CollisionProfileCount);
        Assert.Equal(60, pack.Chunks.Count);
        Assert.Equal(7_708u, pack.Descriptor.PackLength);

        uint nextOffset = pack.Descriptor.ChunkDataOffset;
        for (var chunkY = 0; chunkY < pack.Descriptor.ChunkRows; chunkY++)
        {
            for (var chunkX = 0; chunkX < pack.Descriptor.ChunkColumns; chunkX++)
            {
                var index = chunkY * pack.Descriptor.ChunkColumns + chunkX;
                var chunk = pack.Chunks[index];
                var expectedWidth = chunkX == 19 ? 4 : 8;
                var expectedHeight = chunkY == 2 ? 4 : 8;

                Assert.Same(chunk, pack.ChunkAt(chunkX, chunkY));
                Assert.Equal(expectedWidth, chunk.Directory.ValidWidth);
                Assert.Equal(expectedHeight, chunk.Directory.ValidHeight);
                Assert.Equal(nextOffset, chunk.Directory.VisualOffset);
                nextOffset += (uint)chunk.Directory.VisualStoredBytes;
                Assert.Equal(nextOffset, chunk.Directory.CollisionOffset);
                nextOffset += (uint)chunk.Directory.CollisionStoredBytes;
            }
        }

        Assert.Equal(pack.Descriptor.PackLength, nextOffset);

        var legacyCollision = pack.ToWorldMap2D();
        Assert.Equal(12_480, legacyCollision.TileCount);
        Assert.Equal(788, logical.WorldFlags.Count(flags => flags == WorldTileFlags.Solid));
        Assert.Equal(788, CountFlags(legacyCollision, WorldTileFlags.Solid));
        Assert.Equal(WorldTileFlags.Solid, legacyCollision.FlagsAt(0, 38));

        var legacyTiles = pack.ToWorldTileGrid(cell => cell.Span[0]);
        Assert.Equal(12_480, legacyTiles.TileCount);
        for (var hardwareY = 0; hardwareY < logical.Geometry.Height; hardwareY++)
        {
            for (var hardwareX = 0; hardwareX < logical.Geometry.Width; hardwareX++)
            {
                var sourceX = hardwareX / logical.Geometry.TileScaleX;
                var sourceY = hardwareY / logical.Geometry.TileScaleY;
                var sourceIndex = sourceY * logical.Geometry.SourceWidth + sourceX;
                var subcellX = hardwareX % logical.Geometry.TileScaleX;
                var subcellY = hardwareY % logical.Geometry.TileScaleY;
                var subcellIndex = subcellY * logical.Geometry.TileScaleX + subcellX;
                var expectedVisualId = expected.VisualIds[sourceIndex];
                var expectedExpansionByte = expectedVisualId * 4 + subcellIndex;

                Assert.Equal(expectedVisualId, pack.VisualIdAt(hardwareX, hardwareY));
                Assert.Equal(expected.CollisionProfileIds[sourceIndex], pack.CollisionProfileIdAt(hardwareX, hardwareY));
                Assert.Equal(logical.WorldFlags[hardwareY * logical.Geometry.Width + hardwareX], pack.CollisionAt(hardwareX, hardwareY));
                Assert.Equal(logical.WorldFlags[hardwareY * logical.Geometry.Width + hardwareX], legacyCollision.FlagsAt(hardwareX, hardwareY));
                Assert.Equal(expectedExpansionByte, legacyTiles.TileIdAt(hardwareX, hardwareY));
            }
        }

        var repeatedExpected = DeriveFullStage1(logical);
        var repeatedPack = CreateFullStage1Pack(repeatedExpected);
        Assert.Equal(expected.VisualIds, repeatedExpected.VisualIds);
        Assert.Equal(expected.CollisionProfileIds, repeatedExpected.CollisionProfileIds);
        Assert.Equal(pack.Descriptor, repeatedPack.Descriptor);
        Assert.Equal(pack.TargetExpansions.ToArray(), repeatedPack.TargetExpansions.ToArray());
        Assert.Equal(pack.Chunks.Select(chunk => chunk.Directory), repeatedPack.Chunks.Select(chunk => chunk.Directory));
        Assert.Equal(pack.Chunks.SelectMany(chunk => chunk.VisualIds), repeatedPack.Chunks.SelectMany(chunk => chunk.VisualIds));
        Assert.Equal(
            pack.Chunks.SelectMany(chunk => chunk.CollisionProfileIds),
            repeatedPack.Chunks.SelectMany(chunk => chunk.CollisionProfileIds));

        var publicNames = typeof(WorldPack).Assembly
            .GetTypes()
            .Where(type => type.Namespace == typeof(WorldPack).Namespace && type.Name.StartsWith("WorldPack", StringComparison.Ordinal))
            .SelectMany(type => type.GetMembers().Select(member => $"{type.Name}.{member.Name}"))
            .ToArray();
        Assert.DoesNotContain(publicNames, name =>
            new[] { "Bank", "Mapper", "Mbc", "Ppu", "Chr", "Prg", "Cartridge", "Address", "Register" }
                .Any(term => name.Contains(term, StringComparison.OrdinalIgnoreCase)));
    }

    // Test-only bridge from the live logical fixture to the accepted model. Production
    // Tiled conversion remains owned by LW-1.4.
    private static FullStage1Expected DeriveFullStage1(LogicalTiledMap logical)
    {
        var geometry = logical.Geometry;
        var sourceCellCount = checked(geometry.SourceWidth * geometry.SourceHeight);
        var visualKeys = new string[sourceCellCount];
        var collisionProfileKeys = new string[sourceCellCount];
        var collisionProfilesByKey = new Dictionary<string, WorldTileFlags[]>(StringComparer.Ordinal);

        for (var sourceY = 0; sourceY < geometry.SourceHeight; sourceY++)
        {
            for (var sourceX = 0; sourceX < geometry.SourceWidth; sourceX++)
            {
                var sourceIndex = sourceY * geometry.SourceWidth + sourceX;
                var backgroundGid = logical.BackgroundGids?[sourceIndex] ?? 0;
                visualKeys[sourceIndex] = string.Concat(
                    VisualReferenceKey(logical, backgroundGid, $"background ({sourceX}, {sourceY})"),
                    "|",
                    VisualReferenceKey(logical, logical.WorldGids[sourceIndex], $"world ({sourceX}, {sourceY})"));

                var flags = new WorldTileFlags[geometry.TileScaleX * geometry.TileScaleY];
                for (var subcellY = 0; subcellY < geometry.TileScaleY; subcellY++)
                {
                    for (var subcellX = 0; subcellX < geometry.TileScaleX; subcellX++)
                    {
                        var hardwareX = sourceX * geometry.TileScaleX + subcellX;
                        var hardwareY = sourceY * geometry.TileScaleY + subcellY;
                        flags[subcellY * geometry.TileScaleX + subcellX] =
                            logical.WorldFlags[hardwareY * geometry.Width + hardwareX];
                    }
                }

                var profileKey = Convert.ToHexString(flags.Select(flag => (byte)flag).ToArray());
                collisionProfileKeys[sourceIndex] = profileKey;
                collisionProfilesByKey.TryAdd(profileKey, flags);
            }
        }

        var orderedVisualKeys = visualKeys
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var visualIdsByKey = orderedVisualKeys
            .Select((key, index) => (key, id: checked((ushort)index)))
            .ToDictionary(entry => entry.key, entry => entry.id, StringComparer.Ordinal);
        var orderedCollisionKeys = collisionProfilesByKey.Keys
            .Order(StringComparer.Ordinal)
            .ToArray();
        var collisionIdsByKey = orderedCollisionKeys
            .Select((key, index) => (key, id: checked((ushort)index)))
            .ToDictionary(entry => entry.key, entry => entry.id, StringComparer.Ordinal);

        return new FullStage1Expected(
            visualKeys.Select(key => visualIdsByKey[key]).ToArray(),
            collisionProfileKeys.Select(key => collisionIdsByKey[key]).ToArray(),
            orderedCollisionKeys
                .Select(key => new WorldPackCollisionProfile(collisionProfilesByKey[key]))
                .ToArray(),
            orderedVisualKeys.Length);
    }

    private static string VisualReferenceKey(LogicalTiledMap logical, uint gid, string context)
    {
        var cleanGid = LogicalTiledMapImporter.CleanTiledGid(gid, context);
        if (cleanGid == 0)
        {
            return "0";
        }

        var tileset = LogicalTiledMapImporter.FindTileset(logical.Tilesets, cleanGid, context);
        var tilesetIndex = -1;
        for (var index = 0; index < logical.Tilesets.Count; index++)
        {
            if (ReferenceEquals(logical.Tilesets[index], tileset))
            {
                tilesetIndex = index;
                break;
            }
        }

        Assert.True(tilesetIndex >= 0, $"{context} resolved to a tileset outside importer order.");
        var localTileId = checked((int)cleanGid - tileset.FirstGid);
        return $"1:{tilesetIndex:D5}:{localTileId:D10}";
    }

    private static TemporaryDirectory CreateNormalizedFullStage1()
    {
        var path = Path.Combine(Path.GetTempPath(), "retrosharp-worldpack-model", Guid.NewGuid().ToString("N"));
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

    [Fact]
    public void Hardware_coordinates_resolve_across_255_and_256_and_preserve_floor_collision()
    {
        using var workspace = CreateNormalizedFullStage1();
        var logical = LogicalTiledMapImporter.Load(Path.Combine(workspace.Path, "stage1.worldpack.tmj"));
        var pack = CreateFullStage1Pack(DeriveFullStage1(logical));

        var left = pack.Locate(255, 38);
        Assert.Equal(127, left.MetatileX);
        Assert.Equal(19, left.MetatileY);
        Assert.Equal(1, left.SubcellX);
        Assert.Equal(0, left.SubcellY);
        Assert.Equal(15, left.ChunkX);
        Assert.Equal(2, left.ChunkY);
        Assert.Equal(7, left.LocalX);
        Assert.Equal(3, left.LocalY);
        Assert.Equal(55, left.ChunkIndex);
        Assert.Equal(31, left.CellIndex);
        Assert.Equal(1, left.SubcellIndex);

        var right = pack.Locate(256, 38);
        Assert.Equal(128, right.MetatileX);
        Assert.Equal(0, right.SubcellX);
        Assert.Equal(16, right.ChunkX);
        Assert.Equal(0, right.LocalX);
        Assert.Equal(56, right.ChunkIndex);
        Assert.Equal(logical.WorldFlags[38 * logical.Geometry.Width + 256], pack.CollisionAt(256, 38));
        Assert.Equal(WorldTileFlags.Solid, pack.CollisionAt(0, 38));

        var xError = Assert.Throws<ArgumentOutOfRangeException>(() => pack.Locate(312, 0));
        Assert.Contains("hardware cell x", xError.Message, StringComparison.OrdinalIgnoreCase);
        var yError = Assert.Throws<ArgumentOutOfRangeException>(() => pack.CollisionAt(0, 40));
        Assert.Contains("hardware cell y", yError.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Descriptor_validation_reports_canonical_layout_and_coverage_errors()
    {
        var valid = CreateSmallPackParts();

        var dimensions = Assert.Throws<WorldPackValidationException>(() => valid.Create(
            descriptor: valid.Descriptor with { HardwareWidth = 15 }));
        Assert.Contains("hardwareWidth", dimensions.Message, StringComparison.Ordinal);
        Assert.Contains("metatileWidth", dimensions.Message, StringComparison.Ordinal);

        var section = Assert.Throws<WorldPackValidationException>(() => valid.Create(
            descriptor: valid.Descriptor with { TargetExpansionsOffset = valid.Descriptor.TargetExpansionsOffset + 1 }));
        Assert.Contains("targetExpansionsOffset", section.Message, StringComparison.Ordinal);
        Assert.Contains("canonical", section.Message, StringComparison.OrdinalIgnoreCase);

        var wrongEdge = valid.Chunks.ToArray();
        wrongEdge[0] = CopyChunk(wrongEdge[0], wrongEdge[0].Directory with { ValidWidth = 7 });
        var coverage = Assert.Throws<WorldPackValidationException>(() => valid.Create(chunks: wrongEdge));
        Assert.Contains("chunk 0", coverage.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("validWidth", coverage.Message, StringComparison.Ordinal);

        var gap = valid.Chunks.ToArray();
        gap[0] = CopyChunk(gap[0], gap[0].Directory with { CollisionOffset = gap[0].Directory.CollisionOffset + 1 });
        var ordering = Assert.Throws<WorldPackValidationException>(() => valid.Create(chunks: gap));
        Assert.Contains("chunk 0", ordering.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("collisionOffset", ordering.Message, StringComparison.Ordinal);

        var trailing = Assert.Throws<WorldPackValidationException>(() => valid.Create(
            descriptor: valid.Descriptor with { PackLength = valid.Descriptor.PackLength + 1 }));
        Assert.Contains("packLength", trailing.Message, StringComparison.Ordinal);
        Assert.Contains("trailing", trailing.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Descriptor_validation_rejects_noncanonical_ids_profiles_codecs_and_overflowed_ranges()
    {
        var valid = CreateSmallPackParts();

        var idWidth = Assert.Throws<WorldPackValidationException>(() => valid.Create(
            descriptor: valid.Descriptor with { VisualIdBytes = 2 }));
        Assert.Contains("visualIdBytes", idWidth.Message, StringComparison.Ordinal);
        Assert.Contains("canonical", idWidth.Message, StringComparison.OrdinalIgnoreCase);

        var badVisualIds = valid.Chunks.ToArray();
        var visualIds = badVisualIds[0].VisualIds.ToArray();
        visualIds[0] = 2;
        badVisualIds[0] = new WorldPackChunk(badVisualIds[0].Directory, visualIds, badVisualIds[0].CollisionProfileIds);
        var visualId = Assert.Throws<WorldPackValidationException>(() => valid.Create(chunks: badVisualIds));
        Assert.Contains("visual ID 2", visualId.Message, StringComparison.Ordinal);
        Assert.Contains("chunk 0", visualId.Message, StringComparison.OrdinalIgnoreCase);

        var badCollisionIds = valid.Chunks.ToArray();
        var collisionIds = badCollisionIds[0].CollisionProfileIds.ToArray();
        collisionIds[0] = 2;
        badCollisionIds[0] = new WorldPackChunk(badCollisionIds[0].Directory, badCollisionIds[0].VisualIds, collisionIds);
        var collisionId = Assert.Throws<WorldPackValidationException>(() => valid.Create(chunks: badCollisionIds));
        Assert.Contains("collision profile ID 2", collisionId.Message, StringComparison.Ordinal);

        var unsortedProfiles = new[]
        {
            new WorldPackCollisionProfile([WorldTileFlags.Empty, WorldTileFlags.Empty, WorldTileFlags.Empty, WorldTileFlags.Empty]),
            new WorldPackCollisionProfile([WorldTileFlags.Hazard, WorldTileFlags.Empty, WorldTileFlags.Empty, WorldTileFlags.Empty]),
            new WorldPackCollisionProfile([WorldTileFlags.Solid, WorldTileFlags.Empty, WorldTileFlags.Empty, WorldTileFlags.Empty]),
        };
        var unsorted = CreateSmallPackParts(collisionProfileCount: 3, collisionProfiles: unsortedProfiles);
        var profileOrder = Assert.Throws<WorldPackValidationException>(() => unsorted.Create());
        Assert.Contains("collision profile 2", profileOrder.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("lexicographic", profileOrder.Message, StringComparison.OrdinalIgnoreCase);

        var invalidFlags = new[]
        {
            new WorldPackCollisionProfile([WorldTileFlags.Empty, WorldTileFlags.Empty, WorldTileFlags.Empty, WorldTileFlags.Empty]),
            new WorldPackCollisionProfile([(WorldTileFlags)8, WorldTileFlags.Empty, WorldTileFlags.Empty, WorldTileFlags.Empty]),
        };
        var flags = CreateSmallPackParts(collisionProfiles: invalidFlags);
        var invalidFlag = Assert.Throws<WorldPackValidationException>(() => flags.Create());
        Assert.Contains("undefined WorldTileFlags", invalidFlag.Message, StringComparison.Ordinal);

        var badCodec = valid.Chunks.ToArray();
        badCodec[0] = CopyChunk(badCodec[0], badCodec[0].Directory with { VisualCodec = (WorldPackCodec)2 });
        var codec = Assert.Throws<WorldPackValidationException>(() => valid.Create(chunks: badCodec));
        Assert.Contains("visualCodec", codec.Message, StringComparison.Ordinal);

        var nonCanonicalRle = valid.Chunks.ToArray();
        nonCanonicalRle[0] = CopyChunk(
            nonCanonicalRle[0],
            nonCanonicalRle[0].Directory with { VisualCodec = WorldPackCodec.ElementRle });
        var rle = Assert.Throws<WorldPackValidationException>(() => valid.Create(chunks: nonCanonicalRle));
        Assert.Contains("strictly shorter", rle.Message, StringComparison.OrdinalIgnoreCase);

        var overflow = valid.Chunks.ToArray();
        overflow[0] = CopyChunk(
            overflow[0],
            overflow[0].Directory with
            {
                VisualOffset = uint.MaxValue,
                VisualStoredBytes = ushort.MaxValue,
            });
        var range = Assert.Throws<WorldPackValidationException>(() => valid.Create(chunks: overflow));
        Assert.Contains("visual range", range.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("32-bit", range.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static WorldPack CreateFullStage1Pack(FullStage1Expected expected)
    {
        const int hardwareWidth = 312;
        const int hardwareHeight = 40;
        const int metatileWidth = 2;
        const int metatileHeight = 2;
        const int sourceWidth = hardwareWidth / metatileWidth;
        const int sourceHeight = hardwareHeight / metatileHeight;
        const int chunkColumns = 20;
        const int chunkRows = 3;
        var visualCount = expected.VisualIdentityCount;
        var collisionCount = expected.CollisionProfiles.Count;
        const int targetStride = 1;

        var targetExpansions = Enumerable.Range(0, visualCount * metatileWidth * metatileHeight)
            .Select(value => checked((byte)value))
            .ToArray();

        var collisionProfilesOffset = (uint)WorldPackDescriptor.V1HeaderBytes;
        var targetExpansionsOffset = collisionProfilesOffset + (uint)(collisionCount * metatileWidth * metatileHeight);
        var directoryOffset = targetExpansionsOffset + (uint)targetExpansions.Length;
        var chunkDataOffset = directoryOffset + (uint)(chunkColumns * chunkRows * WorldPackDescriptor.V1DirectoryEntryBytes);
        var nextOffset = chunkDataOffset;
        var chunks = new List<WorldPackChunk>();

        for (var chunkY = 0; chunkY < chunkRows; chunkY++)
        {
            for (var chunkX = 0; chunkX < chunkColumns; chunkX++)
            {
                var validWidth = Math.Min(8, sourceWidth - chunkX * 8);
                var validHeight = Math.Min(8, sourceHeight - chunkY * 8);
                var cellCount = validWidth * validHeight;
                var visualIds = new ushort[cellCount];
                var collisionIds = new ushort[cellCount];
                for (var localY = 0; localY < validHeight; localY++)
                {
                    for (var localX = 0; localX < validWidth; localX++)
                    {
                        var index = localY * validWidth + localX;
                        var sourceX = chunkX * 8 + localX;
                        var sourceY = chunkY * 8 + localY;
                        var sourceIndex = sourceY * sourceWidth + sourceX;
                        visualIds[index] = expected.VisualIds[sourceIndex];
                        collisionIds[index] = expected.CollisionProfileIds[sourceIndex];
                    }
                }

                var visualBytes = checked((ushort)cellCount);
                var collisionBytes = checked((ushort)cellCount);
                var directory = new WorldPackChunkDirectoryEntry(
                    VisualOffset: nextOffset,
                    VisualStoredBytes: visualBytes,
                    VisualDecodedBytes: visualBytes,
                    CollisionOffset: nextOffset + visualBytes,
                    CollisionStoredBytes: collisionBytes,
                    CollisionDecodedBytes: collisionBytes,
                    ValidWidth: (byte)validWidth,
                    ValidHeight: (byte)validHeight,
                    VisualCodec: WorldPackCodec.Raw,
                    CollisionCodec: WorldPackCodec.Raw);
                chunks.Add(new WorldPackChunk(directory, visualIds, collisionIds));
                nextOffset += (uint)(visualBytes + collisionBytes);
            }
        }

        var descriptor = new WorldPackDescriptor
        {
            HardwareWidth = hardwareWidth,
            HardwareHeight = hardwareHeight,
            MetatileWidth = metatileWidth,
            MetatileHeight = metatileHeight,
            ChunkColumns = chunkColumns,
            ChunkRows = chunkRows,
            VisualMetatileCount = visualCount,
            CollisionProfileCount = collisionCount,
            VisualIdBytes = 1,
            CollisionIdBytes = 1,
            TargetCellStride = targetStride,
            CollisionProfilesOffset = collisionProfilesOffset,
            TargetExpansionsOffset = targetExpansionsOffset,
            DirectoryOffset = directoryOffset,
            ChunkDataOffset = chunkDataOffset,
            PackLength = nextOffset,
        };

        return new WorldPack(descriptor, expected.CollisionProfiles, targetExpansions, chunks);
    }

    private static PackParts CreateSmallPackParts(
        int collisionProfileCount = 2,
        IReadOnlyList<WorldPackCollisionProfile>? collisionProfiles = null)
    {
        collisionProfiles ??=
        [
            new WorldPackCollisionProfile([WorldTileFlags.Empty, WorldTileFlags.Empty, WorldTileFlags.Empty, WorldTileFlags.Empty]),
            new WorldPackCollisionProfile([WorldTileFlags.Solid, WorldTileFlags.Solid, WorldTileFlags.Solid, WorldTileFlags.Solid]),
        ];

        var collisionProfilesOffset = (uint)WorldPackDescriptor.V1HeaderBytes;
        var targetExpansionsOffset = collisionProfilesOffset + (uint)(collisionProfileCount * 4);
        var targetExpansions = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var directoryOffset = targetExpansionsOffset + (uint)targetExpansions.Length;
        var chunkDataOffset = directoryOffset + WorldPackDescriptor.V1DirectoryEntryBytes;
        var descriptor = new WorldPackDescriptor
        {
            HardwareWidth = 16,
            HardwareHeight = 8,
            MetatileWidth = 2,
            MetatileHeight = 2,
            ChunkColumns = 1,
            ChunkRows = 1,
            VisualMetatileCount = 2,
            CollisionProfileCount = collisionProfileCount,
            VisualIdBytes = 1,
            CollisionIdBytes = 1,
            TargetCellStride = 1,
            CollisionProfilesOffset = collisionProfilesOffset,
            TargetExpansionsOffset = targetExpansionsOffset,
            DirectoryOffset = directoryOffset,
            ChunkDataOffset = chunkDataOffset,
            PackLength = chunkDataOffset + 64,
        };
        var directory = new WorldPackChunkDirectoryEntry(
            VisualOffset: chunkDataOffset,
            VisualStoredBytes: 32,
            VisualDecodedBytes: 32,
            CollisionOffset: chunkDataOffset + 32,
            CollisionStoredBytes: 32,
            CollisionDecodedBytes: 32,
            ValidWidth: 8,
            ValidHeight: 4,
            VisualCodec: WorldPackCodec.Raw,
            CollisionCodec: WorldPackCodec.Raw);
        var chunks = new[]
        {
            new WorldPackChunk(directory, new ushort[32], new ushort[32]),
        };
        return new PackParts(descriptor, collisionProfiles, targetExpansions, chunks);
    }

    private static WorldPackChunk CopyChunk(WorldPackChunk chunk, WorldPackChunkDirectoryEntry directory) =>
        new(directory, chunk.VisualIds, chunk.CollisionProfileIds);

    private static int CountFlags(WorldMap2D map, WorldTileFlags flags)
    {
        var count = 0;
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                if (map.FlagsAt(x, y) == flags)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private sealed record PackParts(
        WorldPackDescriptor Descriptor,
        IReadOnlyList<WorldPackCollisionProfile> CollisionProfiles,
        byte[] TargetExpansions,
        IReadOnlyList<WorldPackChunk> Chunks)
    {
        public WorldPack Create(
            WorldPackDescriptor? descriptor = null,
            IReadOnlyList<WorldPackCollisionProfile>? collisionProfiles = null,
            IReadOnlyList<WorldPackChunk>? chunks = null) =>
            new(descriptor ?? Descriptor, collisionProfiles ?? CollisionProfiles, TargetExpansions, chunks ?? Chunks);
    }

    private sealed record FullStage1Expected(
        ushort[] VisualIds,
        ushort[] CollisionProfileIds,
        IReadOnlyList<WorldPackCollisionProfile> CollisionProfiles,
        int VisualIdentityCount);

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
