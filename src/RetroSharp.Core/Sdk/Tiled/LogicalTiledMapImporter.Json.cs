using System.Globalization;
using System.Text.Json;

namespace RetroSharp.Core.Sdk.Tiled;

public static partial class LogicalTiledMapImporter
{
    private static TiledMapSource ReadJsonMap(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = document.RootElement;
        var displayName = Path.GetFileName(path);

        var tileLayers = new List<TiledTileLayer>();
        var objectLayers = new List<TiledObjectLayer>();
        if (root.TryGetProperty("layers", out var layers) && layers.ValueKind == JsonValueKind.Array)
        {
            foreach (var layer in EnumerateJsonLayers(layers))
            {
                var type = JsonStringPropertyOrDefault(layer, "type", "");
                var name = JsonStringPropertyOrDefault(layer, "name", "<unnamed>");
                if (type == "tilelayer" && IsImportedTileLayer(name))
                {
                    tileLayers.Add(ReadJsonTileLayer(layer, displayName));
                }
                else if (type == "objectgroup")
                {
                    objectLayers.Add(ReadJsonObjectLayer(layer, name, displayName));
                }
            }
        }

        return new TiledMapSource(
            JsonStringPropertyOrDefault(root, "type", "map"),
            JsonStringPropertyOrDefault(root, "orientation", ""),
            JsonBoolPropertyOrDefault(root, "infinite", false),
            JsonPositiveIntProperty(root, "width", displayName),
            JsonPositiveIntProperty(root, "height", displayName),
            JsonIntProperty(root, "tilewidth", displayName),
            JsonIntProperty(root, "tileheight", displayName),
            ReadJsonProperties(root),
            ReadJsonTilesets(root, path, displayName),
            tileLayers,
            objectLayers);
    }

    private static IReadOnlyList<LogicalTileset> ReadJsonTilesets(JsonElement root, string mapPath, string displayName)
    {
        if (!root.TryGetProperty("tilesets", out var tilesets) || tilesets.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<LogicalTileset>();
        var mapDirectory = Path.GetDirectoryName(mapPath) ?? Directory.GetCurrentDirectory();
        foreach (var tileset in tilesets.EnumerateArray())
        {
            var firstGid = JsonPositiveIntProperty(tileset, "firstgid", displayName);
            var source = JsonStringPropertyOrDefault(tileset, "source", "");
            result.Add(string.IsNullOrWhiteSpace(source)
                ? TilesetFromJson(tileset, mapDirectory, firstGid, displayName)
                : LoadTilesetFile(Path.GetFullPath(Path.Combine(mapDirectory, source)), firstGid, displayName));
        }

        return result.OrderBy(tileset => tileset.FirstGid).ToArray();
    }

    private static IEnumerable<JsonElement> EnumerateJsonLayers(JsonElement layers)
    {
        foreach (var layer in layers.EnumerateArray())
        {
            if (JsonStringPropertyOrDefault(layer, "type", "") == "group" &&
                layer.TryGetProperty("layers", out var childLayers) &&
                childLayers.ValueKind == JsonValueKind.Array)
            {
                foreach (var childLayer in EnumerateJsonLayers(childLayers))
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

    private static bool IsImportedTileLayer(string name)
    {
        return name.Equals("world", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("collision", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("background", StringComparison.OrdinalIgnoreCase);
    }

    private static TiledTileLayer ReadJsonTileLayer(JsonElement layer, string displayName)
    {
        var layerName = JsonStringPropertyOrDefault(layer, "name", "<unnamed>");
        var width = JsonIntProperty(layer, "width", displayName);
        var height = JsonIntProperty(layer, "height", displayName);
        if (!layer.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"Tiled map '{displayName}' layer '{layerName}' must use unencoded JSON array data.");
        }

        var values = new List<uint>();
        foreach (var element in data.EnumerateArray())
        {
            if (!element.TryGetUInt32(out var value))
            {
                throw new InvalidOperationException($"Tiled map '{displayName}' layer '{layerName}' contains a non-GID tile value.");
            }

            values.Add(value);
        }

        return new TiledTileLayer(layerName, width, height, values.ToArray());
    }

    private static TiledObjectLayer ReadJsonObjectLayer(JsonElement layer, string layerName, string displayName)
    {
        if (!layer.TryGetProperty("objects", out var objects) || objects.ValueKind != JsonValueKind.Array)
        {
            return new TiledObjectLayer(layerName, []);
        }

        var result = new List<TiledObject>();
        foreach (var obj in objects.EnumerateArray())
        {
            var objectId = JsonIntPropertyOrDefault(obj, "id", result.Count + 1);
            result.Add(new TiledObject(
                objectId,
                JsonStringPropertyOrDefault(obj, "type", ""),
                JsonStringPropertyOrDefault(obj, "class", ""),
                JsonStringPropertyOrDefault(obj, "name", ""),
                JsonCoordinateProperty(obj, "x", displayName, layerName, objectId),
                JsonCoordinateProperty(obj, "y", displayName, layerName, objectId),
                ReadJsonProperties(obj)));
        }

        return new TiledObjectLayer(layerName, result);
    }

    private static IReadOnlyDictionary<string, string> ReadJsonProperties(JsonElement element)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!element.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var property in properties.EnumerateArray())
        {
            var name = JsonStringPropertyOrDefault(property, "name", "");
            if (string.IsNullOrEmpty(name) || !property.TryGetProperty("value", out var value))
            {
                continue;
            }

            result[name] = value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? "",
                JsonValueKind.Number => value.GetRawText(),
                JsonValueKind.True => bool.TrueString,
                JsonValueKind.False => bool.FalseString,
                _ => value.GetRawText(),
            };
        }

        return result;
    }

    private static int JsonPositiveIntProperty(JsonElement element, string name, string displayName)
    {
        var value = JsonIntProperty(element, name, displayName);
        if (value <= 0)
        {
            throw new InvalidOperationException($"Tiled map '{displayName}' property '{name}' must be positive.");
        }

        return value;
    }

    private static int JsonIntProperty(JsonElement element, string name, string displayName)
    {
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.Number || !property.TryGetInt32(out var value))
        {
            throw new InvalidOperationException($"Tiled map '{displayName}' property '{name}' must be an integer.");
        }

        return value;
    }

    private static int JsonIntPropertyOrDefault(JsonElement element, string name, int fallback)
    {
        return element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value)
            ? value
            : fallback;
    }

    private static double JsonCoordinateProperty(JsonElement element, string name, string displayName, string layerName, int objectId)
    {
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.Number || !property.TryGetDouble(out var value))
        {
            throw new InvalidOperationException($"Tiled map '{displayName}' object layer '{layerName}' object {objectId} property '{name}' must be a number.");
        }

        return value;
    }

    private static bool JsonBoolPropertyOrDefault(JsonElement element, string name, bool fallback)
    {
        return element.TryGetProperty(name, out var property) && property.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? property.GetBoolean()
            : fallback;
    }

    private static string JsonStringPropertyOrDefault(JsonElement element, string name, string fallback)
    {
        return element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? fallback
            : fallback;
    }
}
