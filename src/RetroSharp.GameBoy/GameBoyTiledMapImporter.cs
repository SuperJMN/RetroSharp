using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using RetroSharp.Core.Sdk;

namespace RetroSharp.GameBoy;

internal sealed record GameBoyTiledMap(
    int Width,
    int Height,
    int StreamY,
    int BackgroundWidth,
    int BackgroundHeight,
    int BackgroundOffsetY,
    byte[]? BackgroundTileIds,
    int[] WorldTileIds,
    WorldTileFlags[] WorldFlags,
    byte[] GeneratedTileData);

internal static class GameBoyTiledMapImporter
{
    private const uint TiledFlipFlagsMask = 0xF0000000;
    private const uint TiledGidMask = 0x0FFFFFFF;

    public static GameBoyTiledMap Load(string path, int firstGeneratedTileId = 6)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = document.RootElement;
        var displayName = Path.GetFileName(path);

        if (StringPropertyOrDefault(root, "type", "map") != "map")
        {
            throw new InvalidOperationException($"Tiled map '{displayName}' must have type 'map'.");
        }

        if (StringPropertyOrDefault(root, "orientation", "") != "orthogonal")
        {
            throw new InvalidOperationException($"Tiled map '{displayName}' must be orthogonal for the Game Boy target.");
        }

        if (BoolPropertyOrDefault(root, "infinite", false))
        {
            throw new InvalidOperationException($"Tiled map '{displayName}' must be finite; infinite maps are not supported yet.");
        }

        var width = PositiveIntProperty(root, "width", displayName);
        var mapHeight = PositiveIntProperty(root, "height", displayName);
        var mapTileWidth = IntProperty(root, "tilewidth", displayName);
        var mapTileHeight = IntProperty(root, "tileheight", displayName);
        if (mapTileWidth < 8 || mapTileHeight < 8 || mapTileWidth % 8 != 0 || mapTileHeight % 8 != 0)
        {
            throw new InvalidOperationException($"Tiled map '{displayName}' must use tile sizes that are positive multiples of 8 for the Game Boy target.");
        }

        var tileScaleX = mapTileWidth / 8;
        var tileScaleY = mapTileHeight / 8;
        var tilesets = LoadTilesets(root, path, displayName);
        var resolver = new TiledTilesetResolver(tilesets, firstGeneratedTileId);

        var streamY = CustomIntProperty(root, "retrosharpStreamY")
            ?? throw new InvalidOperationException($"Tiled map '{displayName}' requires an integer custom property named 'retrosharpStreamY'.");
        var worldY = CustomIntProperty(root, "retrosharpWorldY") ?? streamY;
        var height = CustomIntProperty(root, "retrosharpWorldHeight") ?? mapHeight - worldY;

        var expandedWidth = checked(width * tileScaleX);
        var expandedMapHeight = checked(mapHeight * tileScaleY);
        var expandedWorldY = checked(worldY * tileScaleY);
        var expandedStreamY = streamY;
        var expandedHeight = checked(height * tileScaleY);
        var backgroundOffsetY = expandedWorldY - expandedStreamY;

        if (expandedStreamY is < 0 or > 31)
        {
            throw new InvalidOperationException($"Tiled map '{displayName}' property 'retrosharpStreamY' must be between 0 and 31.");
        }

        if (worldY < 0 || height <= 0 || worldY + height > mapHeight)
        {
            throw new InvalidOperationException($"Tiled map '{displayName}' world slice must fit inside the map height.");
        }

        if (expandedStreamY + expandedHeight > 32)
        {
            throw new InvalidOperationException($"Tiled map '{displayName}' world slice exceeds the Game Boy background tilemap height.");
        }

        var worldLayer = FindTileLayer(root, "world")
            ?? throw new InvalidOperationException($"Tiled map '{displayName}' requires a tile layer named 'world'.");
        var worldData = ReadTileLayerData(worldLayer, width, mapHeight, displayName);

        var collisionLayer = FindTileLayer(root, "collision");
        var collisionData = collisionLayer.HasValue ? ReadTileLayerData(collisionLayer.Value, width, mapHeight, displayName) : null;

        var backgroundLayer = FindTileLayer(root, "background");
        var backgroundTiles = backgroundLayer.HasValue
            ? ReadBackgroundTiles(backgroundLayer.Value, width, mapHeight, tileScaleX, tileScaleY, displayName, resolver)
            : null;

        var worldTileIds = new int[expandedWidth * expandedHeight];
        var worldFlags = new WorldTileFlags[expandedWidth * expandedHeight];
        for (var y = 0; y < height; y++)
        {
            var sourceY = worldY + y;
            for (var x = 0; x < width; x++)
            {
                var sourceIndex = sourceY * width + x;
                var context = $"{displayName} world layer tile ({x}, {sourceY})";
                var tileIds = resolver.TileIdsFromTiledGid(worldData[sourceIndex], tileScaleX, tileScaleY, context);
                var flags = collisionData is null
                    ? resolver.FlagsFromTiledGid(worldData[sourceIndex], $"{displayName} world layer tile ({x}, {sourceY})")
                    : FlagsFromCollisionGid(collisionData[sourceIndex], $"{displayName} collision layer tile ({x}, {sourceY})");

                for (var tileY = 0; tileY < tileScaleY; tileY++)
                {
                    for (var tileX = 0; tileX < tileScaleX; tileX++)
                    {
                        var targetX = x * tileScaleX + tileX;
                        var targetY = y * tileScaleY + tileY;
                        var targetIndex = targetY * expandedWidth + targetX;
                        var tileId = tileIds[tileY * tileScaleX + tileX];
                        if (tileId == 0 && backgroundTiles is not null)
                        {
                            var backgroundY = expandedWorldY + targetY;
                            if (backgroundY >= 0 && backgroundY < expandedMapHeight)
                            {
                                tileId = backgroundTiles[backgroundY * expandedWidth + targetX];
                            }
                        }

                        worldTileIds[targetIndex] = tileId;
                        worldFlags[targetIndex] = flags;
                    }
                }
            }
        }

        return new GameBoyTiledMap(expandedWidth, expandedHeight, expandedStreamY, expandedWidth, expandedMapHeight, backgroundOffsetY, backgroundTiles, worldTileIds, worldFlags, resolver.GeneratedTileData);
    }

    private static IReadOnlyList<TiledTileset> LoadTilesets(JsonElement root, string mapPath, string displayName)
    {
        if (!root.TryGetProperty("tilesets", out var tilesets) || tilesets.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<TiledTileset>();
        var mapDirectory = Path.GetDirectoryName(mapPath) ?? Directory.GetCurrentDirectory();
        foreach (var tileset in tilesets.EnumerateArray())
        {
            var firstGid = PositiveIntProperty(tileset, "firstgid", displayName);
            var source = StringPropertyOrDefault(tileset, "source", "");
            if (string.IsNullOrWhiteSpace(source))
            {
                result.Add(TiledTileset.FromJson(tileset, mapDirectory, firstGid, displayName));
                continue;
            }

            var path = Path.GetFullPath(Path.Combine(mapDirectory, source));
            result.Add(TiledTileset.Load(path, firstGid, displayName));
        }

        return result.OrderBy(tileset => tileset.FirstGid).ToArray();
    }

    private static byte[] ReadBackgroundTiles(JsonElement layer, int width, int height, int tileScaleX, int tileScaleY, string displayName, TiledTilesetResolver resolver)
    {
        var data = ReadTileLayerData(layer, width, height, displayName);
        var expandedWidth = checked(width * tileScaleX);
        var tiles = new byte[expandedWidth * height * tileScaleY];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var sourceIndex = y * width + x;
                var tileIds = resolver.TileIdsFromTiledGid(data[sourceIndex], tileScaleX, tileScaleY, $"{displayName} background layer tile ({x}, {y})");
                for (var tileY = 0; tileY < tileScaleY; tileY++)
                {
                    for (var tileX = 0; tileX < tileScaleX; tileX++)
                    {
                        var targetX = x * tileScaleX + tileX;
                        var targetY = y * tileScaleY + tileY;
                        tiles[targetY * expandedWidth + targetX] = (byte)tileIds[tileY * tileScaleX + tileX];
                    }
                }
            }
        }

        return tiles;
    }

    private static JsonElement? FindTileLayer(JsonElement root, string name)
    {
        if (!root.TryGetProperty("layers", out var layers) || layers.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var layer in EnumerateLayers(layers))
        {
            if (StringPropertyOrDefault(layer, "type", "") == "tilelayer" &&
                string.Equals(StringPropertyOrDefault(layer, "name", ""), name, StringComparison.OrdinalIgnoreCase))
            {
                return layer;
            }
        }

        return null;
    }

    private static IEnumerable<JsonElement> EnumerateLayers(JsonElement layers)
    {
        foreach (var layer in layers.EnumerateArray())
        {
            if (StringPropertyOrDefault(layer, "type", "") == "group" &&
                layer.TryGetProperty("layers", out var childLayers) &&
                childLayers.ValueKind == JsonValueKind.Array)
            {
                foreach (var childLayer in EnumerateLayers(childLayers))
                {
                    yield return childLayer;
                }
            }
            else
            {
                yield return layer;
            }
        }
    }

    private static uint[] ReadTileLayerData(JsonElement layer, int width, int height, string displayName)
    {
        var layerName = StringPropertyOrDefault(layer, "name", "<unnamed>");
        var layerWidth = IntProperty(layer, "width", displayName);
        var layerHeight = IntProperty(layer, "height", displayName);
        if (layerWidth != width || layerHeight != height)
        {
            throw new InvalidOperationException($"Tiled map '{displayName}' layer '{layerName}' must match the fixed map size.");
        }

        if (StringPropertyOrDefault(layer, "type", "") != "tilelayer")
        {
            throw new InvalidOperationException($"Tiled map '{displayName}' layer '{layerName}' must be a tile layer.");
        }

        if (!layer.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"Tiled map '{displayName}' layer '{layerName}' must use unencoded JSON array data.");
        }

        var expectedLength = width * height;
        var result = new uint[expectedLength];
        var index = 0;
        foreach (var element in data.EnumerateArray())
        {
            if (index >= expectedLength)
            {
                throw new InvalidOperationException($"Tiled map '{displayName}' layer '{layerName}' has too many tiles.");
            }

            if (!element.TryGetUInt32(out var value))
            {
                throw new InvalidOperationException($"Tiled map '{displayName}' layer '{layerName}' contains a non-GID tile value.");
            }

            result[index++] = value;
        }

        if (index != expectedLength)
        {
            throw new InvalidOperationException($"Tiled map '{displayName}' layer '{layerName}' must contain exactly {expectedLength} tiles.");
        }

        return result;
    }

    private static WorldTileFlags FlagsFromCollisionGid(uint gid, string context)
    {
        var cleanGid = CleanTiledGid(gid, context);
        var allowedFlags = (uint)(WorldTileFlags.Solid | WorldTileFlags.Hazard | WorldTileFlags.Platform);
        if ((cleanGid & ~allowedFlags) != 0)
        {
            throw new InvalidOperationException($"{context} contains unsupported collision flag bits.");
        }

        return (WorldTileFlags)cleanGid;
    }

    private static uint CleanTiledGid(uint gid, string context)
    {
        if ((gid & TiledFlipFlagsMask) != 0)
        {
            throw new InvalidOperationException($"{context} uses flipped or rotated Tiled tiles, which are not supported yet.");
        }

        return gid & TiledGidMask;
    }

    private sealed class TiledTilesetResolver(IReadOnlyList<TiledTileset> tilesets, int firstGeneratedTileId)
    {
        private readonly Dictionary<string, int> generatedTileIds = [];
        private readonly List<byte> generatedTileData = [];

        public byte[] GeneratedTileData => generatedTileData.ToArray();

        public int[] TileIdsFromTiledGid(uint gid, int tilesWide, int tilesHigh, string context)
        {
            if (tilesWide <= 0 || tilesHigh <= 0)
            {
                throw new InvalidOperationException($"{context} must expand to at least one Game Boy tile.");
            }

            var cleanGid = CleanTiledGid(gid, context);
            if (cleanGid == 0)
            {
                return new int[tilesWide * tilesHigh];
            }

            var tileset = FindTileset(cleanGid, context);
            var localId = checked((int)cleanGid - tileset.FirstGid);
            return tileset.BuildGameBoyTiles(localId, tilesWide, tilesHigh, context)
                .Select(pattern => TileIdForPattern(pattern, context))
                .ToArray();
        }

        private int TileIdForPattern(byte[] pattern, string context)
        {
            if (pattern.All(value => value == 0))
            {
                return 0;
            }

            var key = Convert.ToHexString(pattern);
            if (generatedTileIds.TryGetValue(key, out var existing))
            {
                return existing;
            }

            var tileId = firstGeneratedTileId + generatedTileData.Count / 16;
            if (tileId > 255)
            {
                throw new InvalidOperationException($"{context} exceeds the Game Boy 256 tile index range.");
            }

            generatedTileIds.Add(key, tileId);
            generatedTileData.AddRange(pattern);
            return tileId;
        }

        public WorldTileFlags FlagsFromTiledGid(uint gid, string context)
        {
            var cleanGid = CleanTiledGid(gid, context);
            if (cleanGid == 0)
            {
                return WorldTileFlags.Empty;
            }

            var tileset = FindTileset(cleanGid, context);
            var localId = checked((int)cleanGid - tileset.FirstGid);
            return tileset.FlagsForTile(localId);
        }

        private TiledTileset FindTileset(uint cleanGid, string context)
        {
            for (var i = tilesets.Count - 1; i >= 0; i--)
            {
                var tileset = tilesets[i];
                var gid = checked((int)cleanGid);
                if (gid >= tileset.FirstGid && gid < tileset.FirstGid + tileset.TileCount)
                {
                    return tileset;
                }
            }

            throw new InvalidOperationException($"{context} references GID {cleanGid}, but no Tiled tileset contains it.");
        }
    }

    private sealed class TiledTileset
    {
        private TiledTileset(
            int firstGid,
            string name,
            int tileWidth,
            int tileHeight,
            int tileCount,
            int columns,
            GameBoyPngImage? image,
            Dictionary<int, WorldTileFlags> tileFlags)
        {
            FirstGid = firstGid;
            Name = name;
            TileWidth = tileWidth;
            TileHeight = tileHeight;
            TileCount = tileCount;
            Columns = columns;
            Image = image;
            this.tileFlags = tileFlags;
        }

        private readonly Dictionary<int, WorldTileFlags> tileFlags;

        public int FirstGid { get; }

        public string Name { get; }

        public int TileWidth { get; }

        public int TileHeight { get; }

        public int TileCount { get; }

        public int Columns { get; }

        public GameBoyPngImage? Image { get; }

        public static TiledTileset Load(string path, int firstGid, string displayName)
        {
            return Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".tsx" => FromTsx(path, firstGid, displayName),
                ".tsj" or ".json" => FromJsonFile(path, firstGid, displayName),
                _ => throw new InvalidOperationException($"Tiled map '{displayName}' references unsupported tileset file '{Path.GetFileName(path)}'."),
            };
        }

        public static TiledTileset FromJson(JsonElement root, string baseDirectory, int firstGid, string displayName)
        {
            var name = StringPropertyOrDefault(root, "name", "<inline>");
            var tileWidth = PositiveIntProperty(root, "tilewidth", displayName);
            var tileHeight = PositiveIntProperty(root, "tileheight", displayName);
            var tileCount = PositiveIntProperty(root, "tilecount", displayName);
            var columns = PositiveIntProperty(root, "columns", displayName);
            ValidateTileSize(tileWidth, tileHeight, displayName, name);

            var image = LoadImage(StringPropertyOrDefault(root, "image", ""), baseDirectory);
            return new TiledTileset(firstGid, name, tileWidth, tileHeight, tileCount, columns, image, ReadJsonTileFlags(root));
        }

        public IReadOnlyList<byte[]> BuildGameBoyTiles(int localId, int tilesWide, int tilesHigh, string context)
        {
            if (localId < 0 || localId >= TileCount)
            {
                throw new InvalidOperationException($"{context} references tile id {localId}, which is outside tileset '{Name}'.");
            }

            if (Image is null)
            {
                throw new InvalidOperationException($"{context} references tileset '{Name}', but it has no image source.");
            }

            var sourceX = localId % Columns * TileWidth;
            var sourceY = localId / Columns * TileHeight;
            if (sourceX + TileWidth > Image.Width || sourceY + TileHeight > Image.Height)
            {
                throw new InvalidOperationException($"{context} references tile id {localId}, which exceeds tileset image '{Name}'.");
            }

            var targetPixelWidth = tilesWide * 8;
            var targetPixelHeight = tilesHigh * 8;
            var tiles = new List<byte[]>(tilesWide * tilesHigh);
            for (var tileY = 0; tileY < tilesHigh; tileY++)
            {
                for (var tileX = 0; tileX < tilesWide; tileX++)
                {
                    var tile = new byte[16];
                    for (var outY = 0; outY < 8; outY++)
                    {
                        var plane0 = 0;
                        var plane1 = 0;
                        var targetY = tileY * 8 + outY;
                        var yStart = sourceY + targetY * TileHeight / targetPixelHeight;
                        var yEnd = sourceY + Math.Min(TileHeight, Math.Max((targetY + 1) * TileHeight / targetPixelHeight, targetY * TileHeight / targetPixelHeight + 1));
                        for (var outX = 0; outX < 8; outX++)
                        {
                            var targetX = tileX * 8 + outX;
                            var xStart = sourceX + targetX * TileWidth / targetPixelWidth;
                            var xEnd = sourceX + Math.Min(TileWidth, Math.Max((targetX + 1) * TileWidth / targetPixelWidth, targetX * TileWidth / targetPixelWidth + 1));
                            var color = QuantizedGameBoyColor(Image, xStart, xEnd, yStart, yEnd);
                            var bit = 7 - outX;
                            if ((color & 1) != 0) plane0 |= 1 << bit;
                            if ((color & 2) != 0) plane1 |= 1 << bit;
                        }

                        tile[outY * 2] = (byte)plane0;
                        tile[outY * 2 + 1] = (byte)plane1;
                    }

                    tiles.Add(tile);
                }
            }

            return tiles;
        }

        public WorldTileFlags FlagsForTile(int localId)
        {
            return tileFlags.GetValueOrDefault(localId, WorldTileFlags.Empty);
        }

        private static TiledTileset FromJsonFile(string path, int firstGid, string displayName)
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            var baseDirectory = Path.GetDirectoryName(path) ?? Directory.GetCurrentDirectory();
            return FromJson(document.RootElement, baseDirectory, firstGid, displayName);
        }

        private static TiledTileset FromTsx(string path, int firstGid, string displayName)
        {
            var document = XDocument.Load(path);
            var root = document.Root ?? throw new InvalidOperationException($"Tiled tileset '{Path.GetFileName(path)}' is empty.");
            var baseDirectory = Path.GetDirectoryName(path) ?? Directory.GetCurrentDirectory();
            var name = AttributeOrDefault(root, "name", Path.GetFileNameWithoutExtension(path));
            var tileWidth = PositiveIntAttribute(root, "tilewidth", displayName);
            var tileHeight = PositiveIntAttribute(root, "tileheight", displayName);
            var tileCount = PositiveIntAttribute(root, "tilecount", displayName);
            var columns = PositiveIntAttribute(root, "columns", displayName);
            ValidateTileSize(tileWidth, tileHeight, displayName, name);

            var imageSource = root.Element("image")?.Attribute("source")?.Value ?? "";
            var image = LoadImage(imageSource, baseDirectory);
            return new TiledTileset(firstGid, name, tileWidth, tileHeight, tileCount, columns, image, ReadXmlTileFlags(root));
        }

        private static GameBoyPngImage? LoadImage(string imageSource, string baseDirectory)
        {
            if (string.IsNullOrWhiteSpace(imageSource))
            {
                return null;
            }

            return GameBoyPngImage.Read(Path.GetFullPath(Path.Combine(baseDirectory, imageSource)));
        }

        private static void ValidateTileSize(int tileWidth, int tileHeight, string displayName, string tilesetName)
        {
            if (tileWidth < 8 || tileHeight < 8 || tileWidth % 8 != 0 || tileHeight % 8 != 0)
            {
                throw new InvalidOperationException($"Tiled map '{displayName}' tileset '{tilesetName}' must use tile sizes that are positive multiples of 8.");
            }
        }

        private static Dictionary<int, WorldTileFlags> ReadJsonTileFlags(JsonElement root)
        {
            var result = new Dictionary<int, WorldTileFlags>();
            if (!root.TryGetProperty("tiles", out var tiles) || tiles.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (var tile in tiles.EnumerateArray())
            {
                var id = IntProperty(tile, "id", "tileset tile");
                var flags = FlagsFromJsonProperties(tile);
                if (flags == WorldTileFlags.Empty)
                {
                    flags = FlagsFromJsonObjectGroup(tile);
                }

                if (flags != WorldTileFlags.Empty)
                {
                    result[id] = flags;
                }
            }

            return result;
        }

        private static Dictionary<int, WorldTileFlags> ReadXmlTileFlags(XElement root)
        {
            var result = new Dictionary<int, WorldTileFlags>();
            foreach (var tile in root.Elements("tile"))
            {
                var id = PositiveIntAttribute(tile, "id", "tileset tile");
                var flags = FlagsFromXmlProperties(tile);
                if (flags == WorldTileFlags.Empty)
                {
                    flags = FlagsFromXmlObjectGroup(tile);
                }

                if (flags != WorldTileFlags.Empty)
                {
                    result[id] = flags;
                }
            }

            return result;
        }

        private static WorldTileFlags FlagsFromJsonObjectGroup(JsonElement tile)
        {
            if (!tile.TryGetProperty("objectgroup", out var objectGroup) ||
                !objectGroup.TryGetProperty("objects", out var objects) ||
                objects.ValueKind != JsonValueKind.Array)
            {
                return WorldTileFlags.Empty;
            }

            var hasCollisionObject = false;
            foreach (var obj in objects.EnumerateArray())
            {
                var explicitFlags = FlagsFromJsonProperties(obj);
                if (explicitFlags != WorldTileFlags.Empty)
                {
                    return explicitFlags;
                }

                var width = NumberPropertyOrDefault(obj, "width", 0);
                var height = NumberPropertyOrDefault(obj, "height", 0);
                hasCollisionObject |= width > 0 && height > 0;
            }

            return hasCollisionObject ? WorldTileFlags.Solid : WorldTileFlags.Empty;
        }

        private static WorldTileFlags FlagsFromXmlObjectGroup(XElement tile)
        {
            var objectGroup = tile.Element("objectgroup");
            if (objectGroup is null)
            {
                return WorldTileFlags.Empty;
            }

            var hasCollisionObject = false;
            foreach (var obj in objectGroup.Elements("object"))
            {
                var explicitFlags = FlagsFromXmlProperties(obj);
                if (explicitFlags != WorldTileFlags.Empty)
                {
                    return explicitFlags;
                }

                var width = NumberAttributeOrDefault(obj, "width", 0);
                var height = NumberAttributeOrDefault(obj, "height", 0);
                hasCollisionObject |= width > 0 && height > 0;
            }

            return hasCollisionObject ? WorldTileFlags.Solid : WorldTileFlags.Empty;
        }

        private static WorldTileFlags FlagsFromJsonProperties(JsonElement element)
        {
            if (!element.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Array)
            {
                return WorldTileFlags.Empty;
            }

            foreach (var property in properties.EnumerateArray())
            {
                var name = StringPropertyOrDefault(property, "name", "");
                if (!IsCollisionFlagProperty(name) || !property.TryGetProperty("value", out var value))
                {
                    continue;
                }

                return value.ValueKind switch
                {
                    JsonValueKind.Number when value.TryGetInt32(out var number) => CheckedWorldFlags(number, $"Tiled custom property '{name}'"),
                    JsonValueKind.String => ParseWorldFlags(value.GetString() ?? "", $"Tiled custom property '{name}'"),
                    _ => throw new InvalidOperationException($"Tiled custom property '{name}' must be a collision flag value."),
                };
            }

            return WorldTileFlags.Empty;
        }

        private static WorldTileFlags FlagsFromXmlProperties(XElement element)
        {
            var properties = element.Element("properties");
            if (properties is null)
            {
                return WorldTileFlags.Empty;
            }

            foreach (var property in properties.Elements("property"))
            {
                var name = AttributeOrDefault(property, "name", "");
                if (!IsCollisionFlagProperty(name))
                {
                    continue;
                }

                var value = AttributeOrDefault(property, "value", property.Value);
                return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
                    ? CheckedWorldFlags(number, $"Tiled custom property '{name}'")
                    : ParseWorldFlags(value, $"Tiled custom property '{name}'");
            }

            return WorldTileFlags.Empty;
        }

        private static bool IsCollisionFlagProperty(string name)
        {
            return name.Equals("retrosharpCollision", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("retrosharpFlags", StringComparison.OrdinalIgnoreCase);
        }

        private static WorldTileFlags ParseWorldFlags(string value, string context)
        {
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric))
            {
                return CheckedWorldFlags(numeric, context);
            }

            var result = WorldTileFlags.Empty;
            foreach (var part in value.Split([',', '|', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                result |= part.ToLowerInvariant() switch
                {
                    "none" or "empty" => WorldTileFlags.Empty,
                    "solid" => WorldTileFlags.Solid,
                    "hazard" => WorldTileFlags.Hazard,
                    "platform" => WorldTileFlags.Platform,
                    _ => throw new InvalidOperationException($"{context} contains unsupported collision flag '{part}'."),
                };
            }

            return result;
        }

        private static WorldTileFlags CheckedWorldFlags(int value, string context)
        {
            var allowedFlags = (int)(WorldTileFlags.Solid | WorldTileFlags.Hazard | WorldTileFlags.Platform);
            if (value < 0 || (value & ~allowedFlags) != 0)
            {
                throw new InvalidOperationException($"{context} contains unsupported collision flag bits.");
            }

            return (WorldTileFlags)value;
        }

        private static int QuantizedGameBoyColor(GameBoyPngImage image, int xStart, int xEnd, int yStart, int yEnd)
        {
            var totalR = 0;
            var totalG = 0;
            var totalB = 0;
            var count = 0;
            for (var y = yStart; y < yEnd; y++)
            {
                for (var x = xStart; x < xEnd; x++)
                {
                    var offset = image.PixelOffset(x, y);
                    if (image.RgbaPixels[offset + 3] < 128)
                    {
                        continue;
                    }

                    totalR += image.RgbaPixels[offset];
                    totalG += image.RgbaPixels[offset + 1];
                    totalB += image.RgbaPixels[offset + 2];
                    count++;
                }
            }

            if (count == 0)
            {
                return 0;
            }

            var r = totalR / count;
            var g = totalG / count;
            var b = totalB / count;
            var luminance = (r * 299 + g * 587 + b * 114) / 1000;
            return luminance switch
            {
                >= 192 => 0,
                >= 128 => 1,
                >= 64 => 2,
                _ => 3,
            };
        }

        private static string AttributeOrDefault(XElement element, string name, string fallback)
        {
            return element.Attribute(name)?.Value ?? fallback;
        }

        private static int PositiveIntAttribute(XElement element, string name, string displayName)
        {
            var value = IntAttribute(element, name, displayName);
            if (value < 0 && name == "id")
            {
                throw new InvalidOperationException($"Tiled map '{displayName}' attribute '{name}' must be non-negative.");
            }

            if (value <= 0 && name != "id")
            {
                throw new InvalidOperationException($"Tiled map '{displayName}' attribute '{name}' must be positive.");
            }

            return value;
        }

        private static int IntAttribute(XElement element, string name, string displayName)
        {
            var text = element.Attribute(name)?.Value;
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                throw new InvalidOperationException($"Tiled map '{displayName}' attribute '{name}' must be an integer.");
            }

            return value;
        }

        private static double NumberPropertyOrDefault(JsonElement element, string name, double fallback)
        {
            return element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out var value)
                ? value
                : fallback;
        }

        private static double NumberAttributeOrDefault(XElement element, string name, double fallback)
        {
            return double.TryParse(element.Attribute(name)?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : fallback;
        }
    }

    private static int PositiveIntProperty(JsonElement element, string name, string displayName)
    {
        var value = IntProperty(element, name, displayName);
        if (value <= 0)
        {
            throw new InvalidOperationException($"Tiled map '{displayName}' property '{name}' must be positive.");
        }

        return value;
    }

    private static int IntProperty(JsonElement element, string name, string displayName)
    {
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.Number || !property.TryGetInt32(out var value))
        {
            throw new InvalidOperationException($"Tiled map '{displayName}' property '{name}' must be an integer.");
        }

        return value;
    }

    private static int? CustomIntProperty(JsonElement element, string name)
    {
        if (!element.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var property in properties.EnumerateArray())
        {
            if (StringPropertyOrDefault(property, "name", "") != name || !property.TryGetProperty("value", out var value))
            {
                continue;
            }

            return value.ValueKind switch
            {
                JsonValueKind.Number when value.TryGetInt32(out var number) => number,
                JsonValueKind.String when int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) => number,
                _ => throw new InvalidOperationException($"Tiled custom property '{name}' must be an integer."),
            };
        }

        return null;
    }

    private static bool BoolPropertyOrDefault(JsonElement element, string name, bool fallback)
    {
        return element.TryGetProperty(name, out var property) && property.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? property.GetBoolean()
            : fallback;
    }

    private static string StringPropertyOrDefault(JsonElement element, string name, string fallback)
    {
        return element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? fallback
            : fallback;
    }
}
