using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;

namespace RetroSharp.Core.Sdk.Tiled;

// Target-neutral importer for orthogonal, finite Tiled maps. It parses the map
// and its tilesets, resolves geometry and the playable world slice, and turns
// collision data into portable WorldTileFlags. It produces logical tile
// references only; pixel generation and target tile encoding stay in each
// target backend.
public static class LogicalTiledMapImporter
{
    private const uint TiledFlipFlagsMask = 0xF0000000;
    private const uint TiledGidMask = 0x0FFFFFFF;

    public static LogicalTiledMap Load(string path)
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
            throw new InvalidOperationException($"Tiled map '{displayName}' must be orthogonal.");
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
            throw new InvalidOperationException($"Tiled map '{displayName}' must use tile sizes that are positive multiples of 8.");
        }

        var tileScaleX = mapTileWidth / 8;
        var tileScaleY = mapTileHeight / 8;
        var tilesets = LoadTilesets(root, path, displayName);

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

        if (worldY < 0 || height <= 0 || worldY + height > mapHeight)
        {
            throw new InvalidOperationException($"Tiled map '{displayName}' world slice must fit inside the map height.");
        }

        var worldLayer = FindTileLayer(root, "world")
            ?? throw new InvalidOperationException($"Tiled map '{displayName}' requires a tile layer named 'world'.");
        var worldData = ReadTileLayerData(worldLayer, width, mapHeight, displayName);

        var collisionLayer = FindTileLayer(root, "collision");
        var collisionData = collisionLayer.HasValue ? ReadTileLayerData(collisionLayer.Value, width, mapHeight, displayName) : null;

        var backgroundLayer = FindTileLayer(root, "background");
        var backgroundData = backgroundLayer.HasValue ? ReadTileLayerData(backgroundLayer.Value, width, mapHeight, displayName) : null;
        var actorSpawnLayers = ReadActorSpawnLayers(root, displayName);

        var worldFlags = new WorldTileFlags[expandedWidth * expandedHeight];
        for (var y = 0; y < height; y++)
        {
            var sourceY = worldY + y;
            for (var x = 0; x < width; x++)
            {
                var sourceIndex = sourceY * width + x;
                var flags = collisionData is null
                    ? FlagsFromTiledGid(tilesets, worldData[sourceIndex], $"{displayName} world layer tile ({x}, {sourceY})")
                    : TiledCollisionFlags.FlagsFromCollisionGid(collisionData[sourceIndex], $"{displayName} collision layer tile ({x}, {sourceY})");

                for (var tileY = 0; tileY < tileScaleY; tileY++)
                {
                    for (var tileX = 0; tileX < tileScaleX; tileX++)
                    {
                        var targetX = x * tileScaleX + tileX;
                        var targetY = y * tileScaleY + tileY;
                        worldFlags[targetY * expandedWidth + targetX] = flags;
                    }
                }
            }
        }

        var geometry = new LogicalTiledMapGeometry(
            sourceWidth: width,
            sourceHeight: mapHeight,
            tileScaleX: tileScaleX,
            tileScaleY: tileScaleY,
            worldY: worldY,
            worldHeight: height,
            streamY: expandedStreamY,
            width: expandedWidth,
            height: expandedHeight,
            expandedWorldY: expandedWorldY,
            backgroundWidth: expandedWidth,
            backgroundHeight: expandedMapHeight,
            backgroundOffsetY: backgroundOffsetY);

        return new LogicalTiledMap(tilesets, backgroundData, worldData, worldFlags, geometry, actorSpawnLayers);
    }

    public static uint CleanTiledGid(uint gid, string context)
    {
        if ((gid & TiledFlipFlagsMask) != 0)
        {
            throw new InvalidOperationException($"{context} uses flipped or rotated Tiled tiles, which are not supported yet.");
        }

        return gid & TiledGidMask;
    }

    public static LogicalTileset FindTileset(IReadOnlyList<LogicalTileset> tilesets, uint cleanGid, string context)
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

        throw new InvalidOperationException($"{context} references tile gid {cleanGid}, which is outside every tileset.");
    }

    private static WorldTileFlags FlagsFromTiledGid(IReadOnlyList<LogicalTileset> tilesets, uint gid, string context)
    {
        var cleanGid = CleanTiledGid(gid, context);
        if (cleanGid == 0)
        {
            return WorldTileFlags.Empty;
        }

        var tileset = FindTileset(tilesets, cleanGid, context);
        var localId = checked((int)cleanGid - tileset.FirstGid);
        return tileset.FlagsForTile(localId);
    }

    private static IReadOnlyList<LogicalTileset> LoadTilesets(JsonElement root, string mapPath, string displayName)
    {
        if (!root.TryGetProperty("tilesets", out var tilesets) || tilesets.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<LogicalTileset>();
        var mapDirectory = Path.GetDirectoryName(mapPath) ?? Directory.GetCurrentDirectory();
        foreach (var tileset in tilesets.EnumerateArray())
        {
            var firstGid = PositiveIntProperty(tileset, "firstgid", displayName);
            var source = StringPropertyOrDefault(tileset, "source", "");
            if (string.IsNullOrWhiteSpace(source))
            {
                result.Add(TilesetFromJson(tileset, mapDirectory, firstGid, displayName));
                continue;
            }

            var path = Path.GetFullPath(Path.Combine(mapDirectory, source));
            result.Add(LoadTilesetFile(path, firstGid, displayName));
        }

        return result.OrderBy(tileset => tileset.FirstGid).ToArray();
    }

    private static LogicalTileset LoadTilesetFile(string path, int firstGid, string displayName)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".tsx" => TilesetFromTsx(path, firstGid, displayName),
            ".tsj" or ".json" => TilesetFromJsonFile(path, firstGid, displayName),
            _ => throw new InvalidOperationException($"Tiled map '{displayName}' references unsupported tileset file '{Path.GetFileName(path)}'."),
        };
    }

    private static LogicalTileset TilesetFromJsonFile(string path, int firstGid, string displayName)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var baseDirectory = Path.GetDirectoryName(path) ?? Directory.GetCurrentDirectory();
        return TilesetFromJson(document.RootElement, baseDirectory, firstGid, displayName);
    }

    private static LogicalTileset TilesetFromJson(JsonElement root, string baseDirectory, int firstGid, string displayName)
    {
        var name = StringPropertyOrDefault(root, "name", "<inline>");
        var tileWidth = PositiveIntProperty(root, "tilewidth", displayName);
        var tileHeight = PositiveIntProperty(root, "tileheight", displayName);
        var tileCount = PositiveIntProperty(root, "tilecount", displayName);
        var columns = PositiveIntProperty(root, "columns", displayName);
        ValidateTileSize(tileWidth, tileHeight, displayName, name);

        var imagePath = ResolveImagePath(StringPropertyOrDefault(root, "image", ""), baseDirectory);
        return new LogicalTileset(firstGid, name, tileWidth, tileHeight, tileCount, columns, imagePath, TiledCollisionFlags.ReadJsonTileFlags(root));
    }

    private static LogicalTileset TilesetFromTsx(string path, int firstGid, string displayName)
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
        var imagePath = ResolveImagePath(imageSource, baseDirectory);
        return new LogicalTileset(firstGid, name, tileWidth, tileHeight, tileCount, columns, imagePath, TiledCollisionFlags.ReadXmlTileFlags(root));
    }

    private static string? ResolveImagePath(string imageSource, string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(imageSource))
        {
            return null;
        }

        return Path.GetFullPath(Path.Combine(baseDirectory, imageSource));
    }

    private static void ValidateTileSize(int tileWidth, int tileHeight, string displayName, string tilesetName)
    {
        if (tileWidth < 8 || tileHeight < 8 || tileWidth % 8 != 0 || tileHeight % 8 != 0)
        {
            throw new InvalidOperationException($"Tiled map '{displayName}' tileset '{tilesetName}' must use tile sizes that are positive multiples of 8.");
        }
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

    private static IReadOnlyDictionary<string, IReadOnlyList<LogicalActorSpawn>> ReadActorSpawnLayers(JsonElement root, string displayName)
    {
        if (!root.TryGetProperty("layers", out var layers) || layers.ValueKind != JsonValueKind.Array)
        {
            return new Dictionary<string, IReadOnlyList<LogicalActorSpawn>>(StringComparer.Ordinal);
        }

        var result = new Dictionary<string, IReadOnlyList<LogicalActorSpawn>>(StringComparer.Ordinal);
        foreach (var layer in EnumerateLayers(layers))
        {
            if (StringPropertyOrDefault(layer, "type", "") != "objectgroup")
            {
                continue;
            }

            var layerName = StringPropertyOrDefault(layer, "name", "<unnamed>");
            var spawns = ReadActorSpawns(layer, layerName, displayName);
            result[layerName] = spawns;
        }

        return result;
    }

    private static IReadOnlyList<LogicalActorSpawn> ReadActorSpawns(JsonElement layer, string layerName, string displayName)
    {
        if (!layer.TryGetProperty("objects", out var objects) || objects.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<LogicalActorSpawn>();
        foreach (var obj in objects.EnumerateArray())
        {
            var objectId = IntPropertyOrDefault(obj, "id", result.Count + 1);
            var kind = FirstNonEmpty(
                CustomStringProperty(obj, "kind"),
                StringPropertyOrDefault(obj, "type", ""),
                StringPropertyOrDefault(obj, "class", ""),
                StringPropertyOrDefault(obj, "name", ""));
            if (string.IsNullOrWhiteSpace(kind))
            {
                throw new InvalidOperationException($"Tiled map '{displayName}' object layer '{layerName}' object {objectId} requires an actor kind via a 'kind' property, type/class, or name.");
            }

            var x = CheckedByte(IntNumberProperty(obj, "x", displayName, layerName, objectId), $"Tiled map '{displayName}' object layer '{layerName}' object {objectId} x");
            var y = CheckedByte(IntNumberProperty(obj, "y", displayName, layerName, objectId), $"Tiled map '{displayName}' object layer '{layerName}' object {objectId} y");
            result.Add(new LogicalActorSpawn(kind, x, y, ReadActorSpawnFields(obj, displayName, layerName, objectId)));
        }

        return result;
    }

    private static IReadOnlyDictionary<string, int> ReadActorSpawnFields(JsonElement obj, string displayName, string layerName, int objectId)
    {
        if (!obj.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Array)
        {
            return new Dictionary<string, int>(StringComparer.Ordinal);
        }

        var fields = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var property in properties.EnumerateArray())
        {
            var name = StringPropertyOrDefault(property, "name", "");
            if (name == "kind")
            {
                continue;
            }

            if (name is not ("active" or "state" or "timer" or "facing" or "animTick" or "health" or "vx" or "vy"))
            {
                continue;
            }

            if (!property.TryGetProperty("value", out var value))
            {
                continue;
            }

            fields[name] = CheckedByte(PropertyIntValue(value, $"Tiled map '{displayName}' object layer '{layerName}' object {objectId} property '{name}'"), $"Tiled map '{displayName}' object layer '{layerName}' object {objectId} property '{name}'");
        }

        return fields;
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

    private static int IntPropertyOrDefault(JsonElement element, string name, int fallback)
    {
        return element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value)
            ? value
            : fallback;
    }

    private static int IntNumberProperty(JsonElement element, string name, string displayName, string layerName, int objectId)
    {
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.Number)
        {
            throw new InvalidOperationException($"Tiled map '{displayName}' object layer '{layerName}' object {objectId} property '{name}' must be an integer.");
        }

        if (property.TryGetInt32(out var value))
        {
            return value;
        }

        var number = property.GetDouble();
        if (Math.Truncate(number) == number && number >= int.MinValue && number <= int.MaxValue)
        {
            return (int)number;
        }

        throw new InvalidOperationException($"Tiled map '{displayName}' object layer '{layerName}' object {objectId} property '{name}' must be an integer.");
    }

    private static int PropertyIntValue(JsonElement value, string context)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) => number,
            JsonValueKind.True => 1,
            JsonValueKind.False => 0,
            _ => throw new InvalidOperationException($"{context} must be a byte value."),
        };
    }

    private static int CheckedByte(int value, string context)
    {
        if (value is < 0 or > 255)
        {
            throw new InvalidOperationException($"{context} must be between 0 and 255.");
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

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return "";
    }

    private static string? CustomStringProperty(JsonElement element, string name)
    {
        if (!element.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var property in properties.EnumerateArray())
        {
            if (StringPropertyOrDefault(property, "name", "") == name &&
                property.TryGetProperty("value", out var value) &&
                value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
    }

    private static string StringPropertyOrDefault(JsonElement element, string name, string fallback)
    {
        return element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? fallback
            : fallback;
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
}
