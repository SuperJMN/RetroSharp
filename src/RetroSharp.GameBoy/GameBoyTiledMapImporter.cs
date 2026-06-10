using System.Globalization;
using System.Text.Json;
using RetroSharp.Core.Sdk;

namespace RetroSharp.GameBoy;

internal sealed record GameBoyTiledMap(
    int Width,
    int Height,
    int StreamY,
    int BackgroundWidth,
    int BackgroundHeight,
    byte[]? BackgroundTileIds,
    int[] WorldTileIds,
    WorldTileFlags[] WorldFlags);

internal static class GameBoyTiledMapImporter
{
    private const uint TiledFlipFlagsMask = 0xF0000000;
    private const uint TiledGidMask = 0x0FFFFFFF;

    public static GameBoyTiledMap Load(string path)
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
        if (IntProperty(root, "tilewidth", displayName) != 8 || IntProperty(root, "tileheight", displayName) != 8)
        {
            throw new InvalidOperationException($"Tiled map '{displayName}' must use 8x8 tiles for the Game Boy target.");
        }

        ValidateTilesets(root, displayName);

        var streamY = CustomIntProperty(root, "retrosharpStreamY")
            ?? throw new InvalidOperationException($"Tiled map '{displayName}' requires an integer custom property named 'retrosharpStreamY'.");
        var worldY = CustomIntProperty(root, "retrosharpWorldY") ?? streamY;
        var height = CustomIntProperty(root, "retrosharpWorldHeight") ?? mapHeight - worldY;

        if (streamY is < 0 or > 31)
        {
            throw new InvalidOperationException($"Tiled map '{displayName}' property 'retrosharpStreamY' must be between 0 and 31.");
        }

        if (worldY < 0 || height <= 0 || worldY + height > mapHeight)
        {
            throw new InvalidOperationException($"Tiled map '{displayName}' world slice must fit inside the map height.");
        }

        if (streamY + height > 32)
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
            ? ReadBackgroundTiles(backgroundLayer.Value, width, mapHeight, displayName)
            : null;

        var worldTileIds = new int[width * height];
        var worldFlags = new WorldTileFlags[width * height];
        for (var y = 0; y < height; y++)
        {
            var sourceY = worldY + y;
            for (var x = 0; x < width; x++)
            {
                var sourceIndex = sourceY * width + x;
                var targetIndex = y * width + x;
                worldTileIds[targetIndex] = TileIdFromTiledGid(worldData[sourceIndex], $"{displayName} world layer tile ({x}, {sourceY})");
                worldFlags[targetIndex] = collisionData is null
                    ? WorldTileFlags.Empty
                    : FlagsFromCollisionGid(collisionData[sourceIndex], $"{displayName} collision layer tile ({x}, {sourceY})");
            }
        }

        return new GameBoyTiledMap(width, height, streamY, width, mapHeight, backgroundTiles, worldTileIds, worldFlags);
    }

    private static void ValidateTilesets(JsonElement root, string displayName)
    {
        if (!root.TryGetProperty("tilesets", out var tilesets) || tilesets.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var count = 0;
        foreach (var tileset in tilesets.EnumerateArray())
        {
            count++;
            if (count > 1)
            {
                throw new InvalidOperationException($"Tiled map '{displayName}' currently supports exactly one tileset.");
            }

            if (IntProperty(tileset, "firstgid", displayName) != 1)
            {
                throw new InvalidOperationException($"Tiled map '{displayName}' tileset must use firstgid 1.");
            }
        }
    }

    private static byte[] ReadBackgroundTiles(JsonElement layer, int width, int height, string displayName)
    {
        var data = ReadTileLayerData(layer, width, height, displayName);
        var tiles = new byte[data.Length];
        for (var i = 0; i < data.Length; i++)
        {
            tiles[i] = (byte)TileIdFromTiledGid(data[i], $"{displayName} background layer tile {i}");
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

    private static int TileIdFromTiledGid(uint gid, string context)
    {
        var cleanGid = CleanTiledGid(gid, context);
        if (cleanGid == 0)
        {
            return 0;
        }

        if (cleanGid > 256)
        {
            throw new InvalidOperationException($"{context} must resolve to a Game Boy tile id between 0 and 255.");
        }

        return checked((int)cleanGid - 1);
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
