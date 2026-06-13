namespace RetroSharp.Core.Sdk.Tiled;

using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;

// Target-neutral interpretation of Tiled collision data into portable
// WorldTileFlags. This is shared asset-import logic: it reads Tiled JSON/XML
// objectgroups, custom collision properties, and collision-layer GIDs without
// any Game Boy or NES specifics, so collision modeling does not live inside a
// single target backend.
public static class TiledCollisionFlags
{
    private const uint TiledFlipFlagsMask = 0xF0000000;
    private const uint TiledGidMask = 0x0FFFFFFF;

    public static WorldTileFlags FlagsFromCollisionGid(uint gid, string context)
    {
        var cleanGid = CleanGid(gid, context);
        var allowedFlags = (uint)(WorldTileFlags.Solid | WorldTileFlags.Hazard | WorldTileFlags.Platform);
        if ((cleanGid & ~allowedFlags) != 0)
        {
            throw new InvalidOperationException($"{context} contains unsupported collision flag bits.");
        }

        return (WorldTileFlags)cleanGid;
    }

    public static Dictionary<int, WorldTileFlags> ReadJsonTileFlags(JsonElement root)
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

    public static Dictionary<int, WorldTileFlags> ReadXmlTileFlags(XElement root)
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

    private static uint CleanGid(uint gid, string context)
    {
        if ((gid & TiledFlipFlagsMask) != 0)
        {
            throw new InvalidOperationException($"{context} uses flipped or rotated Tiled tiles, which are not supported yet.");
        }

        return gid & TiledGidMask;
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

    private static int IntProperty(JsonElement element, string name, string displayName)
    {
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.Number || !property.TryGetInt32(out var value))
        {
            throw new InvalidOperationException($"Tiled map '{displayName}' property '{name}' must be an integer.");
        }

        return value;
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

    private static string AttributeOrDefault(XElement element, string name, string fallback)
    {
        return element.Attribute(name)?.Value ?? fallback;
    }

    private static string StringPropertyOrDefault(JsonElement element, string name, string fallback)
    {
        return element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? fallback
            : fallback;
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
