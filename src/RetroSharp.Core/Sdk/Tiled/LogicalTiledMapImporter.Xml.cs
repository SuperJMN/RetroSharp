using System.Globalization;
using System.Xml.Linq;

namespace RetroSharp.Core.Sdk.Tiled;

public static partial class LogicalTiledMapImporter
{
    private static TiledMapSource ReadXmlMap(string path)
    {
        var document = XDocument.Load(path);
        var root = document.Root ?? throw new InvalidOperationException($"Tiled map '{Path.GetFileName(path)}' is empty.");
        var displayName = Path.GetFileName(path);

        var tileLayers = new List<TiledTileLayer>();
        var objectLayers = new List<TiledObjectLayer>();
        foreach (var layer in EnumerateXmlLayers(root))
        {
            var name = XmlAttributeOrDefault(layer, "name", "<unnamed>");
            if (layer.Name.LocalName == "layer" && IsImportedTileLayer(name))
            {
                tileLayers.Add(ReadXmlTileLayer(layer, displayName));
            }
            else if (layer.Name.LocalName == "objectgroup")
            {
                objectLayers.Add(ReadXmlObjectLayer(layer, name, displayName));
            }
        }

        return new TiledMapSource(
            root.Name.LocalName,
            XmlAttributeOrDefault(root, "orientation", ""),
            XmlBoolAttributeOrDefault(root, "infinite", false, displayName),
            XmlPositiveIntAttribute(root, "width", displayName),
            XmlPositiveIntAttribute(root, "height", displayName),
            XmlIntAttribute(root, "tilewidth", displayName),
            XmlIntAttribute(root, "tileheight", displayName),
            ReadXmlProperties(root),
            ReadXmlTilesets(root, path, displayName),
            tileLayers,
            objectLayers);
    }

    private static IReadOnlyList<LogicalTileset> ReadXmlTilesets(XElement root, string mapPath, string displayName)
    {
        var result = new List<LogicalTileset>();
        var mapDirectory = Path.GetDirectoryName(mapPath) ?? Directory.GetCurrentDirectory();
        foreach (var tileset in XmlElements(root, "tileset"))
        {
            var firstGid = XmlPositiveIntAttribute(tileset, "firstgid", displayName);
            var source = XmlAttributeOrDefault(tileset, "source", "");
            result.Add(string.IsNullOrWhiteSpace(source)
                ? TilesetFromXml(tileset, mapDirectory, firstGid, displayName)
                : LoadTilesetFile(Path.GetFullPath(Path.Combine(mapDirectory, source)), firstGid, displayName));
        }

        return result.OrderBy(tileset => tileset.FirstGid).ToArray();
    }

    private static IEnumerable<XElement> EnumerateXmlLayers(XElement parent)
    {
        foreach (var element in parent.Elements())
        {
            if (element.Name.LocalName == "group")
            {
                foreach (var child in EnumerateXmlLayers(element))
                {
                    yield return child;
                }
            }
            else if (element.Name.LocalName is "layer" or "objectgroup")
            {
                yield return element;
            }
        }
    }

    private static TiledTileLayer ReadXmlTileLayer(XElement layer, string displayName)
    {
        var layerName = XmlAttributeOrDefault(layer, "name", "<unnamed>");
        var width = XmlIntAttribute(layer, "width", displayName);
        var height = XmlIntAttribute(layer, "height", displayName);
        var data = XmlElement(layer, "data")
            ?? throw new InvalidOperationException($"Tiled map '{displayName}' layer '{layerName}' requires tile data.");
        var encoding = XmlAttributeOrDefault(data, "encoding", "");
        var compression = XmlAttributeOrDefault(data, "compression", "");
        if (encoding != "csv" || !string.IsNullOrWhiteSpace(compression))
        {
            throw new InvalidOperationException($"Tiled map '{displayName}' layer '{layerName}' must use CSV-encoded TMX tile data without compression.");
        }

        var values = new List<uint>();
        foreach (var text in data.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                throw new InvalidOperationException($"Tiled map '{displayName}' layer '{layerName}' contains a non-GID tile value.");
            }

            values.Add(value);
        }

        return new TiledTileLayer(layerName, width, height, values.ToArray());
    }

    private static TiledObjectLayer ReadXmlObjectLayer(XElement layer, string layerName, string displayName)
    {
        var result = new List<TiledObject>();
        foreach (var obj in XmlElements(layer, "object"))
        {
            var objectId = XmlIntAttributeOrDefault(obj, "id", result.Count + 1, displayName);
            result.Add(new TiledObject(
                objectId,
                XmlAttributeOrDefault(obj, "type", ""),
                XmlAttributeOrDefault(obj, "class", ""),
                XmlAttributeOrDefault(obj, "name", ""),
                XmlNumberAttribute(obj, "x", displayName, layerName, objectId),
                XmlNumberAttribute(obj, "y", displayName, layerName, objectId),
                ReadXmlProperties(obj)));
        }

        return new TiledObjectLayer(layerName, result);
    }

    private static IReadOnlyDictionary<string, string> ReadXmlProperties(XElement element)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var properties = XmlElement(element, "properties");
        if (properties is null)
        {
            return result;
        }

        foreach (var property in XmlElements(properties, "property"))
        {
            var name = XmlAttributeOrDefault(property, "name", "");
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            result[name] = XmlAttributeOrDefault(property, "value", property.Value);
        }

        return result;
    }

    private static bool XmlBoolAttributeOrDefault(XElement element, string name, bool fallback, string displayName)
    {
        var value = element.Attribute(name)?.Value;
        if (value is null)
        {
            return fallback;
        }

        return value switch
        {
            "0" => false,
            "1" => true,
            _ when bool.TryParse(value, out var boolean) => boolean,
            _ => throw new InvalidOperationException($"Tiled map '{displayName}' attribute '{name}' must be a boolean."),
        };
    }

    private static double XmlNumberAttribute(XElement element, string name, string displayName, string layerName, int objectId)
    {
        var text = element.Attribute(name)?.Value;
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            throw new InvalidOperationException($"Tiled map '{displayName}' object layer '{layerName}' object {objectId} property '{name}' must be a number.");
        }

        return value;
    }

    private static string XmlAttributeOrDefault(XElement element, string name, string fallback)
    {
        return element.Attribute(name)?.Value ?? fallback;
    }

    private static int XmlPositiveIntAttribute(XElement element, string name, string displayName)
    {
        var value = XmlIntAttribute(element, name, displayName);
        if (value <= 0)
        {
            throw new InvalidOperationException($"Tiled map '{displayName}' attribute '{name}' must be positive.");
        }

        return value;
    }

    private static int XmlIntAttribute(XElement element, string name, string displayName)
    {
        var text = element.Attribute(name)?.Value;
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            throw new InvalidOperationException($"Tiled map '{displayName}' attribute '{name}' must be an integer.");
        }

        return value;
    }

    private static int XmlIntAttributeOrDefault(XElement element, string name, int fallback, string displayName)
    {
        var text = element.Attribute(name)?.Value;
        if (text is null)
        {
            return fallback;
        }

        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            throw new InvalidOperationException($"Tiled map '{displayName}' attribute '{name}' must be an integer.");
        }

        return value;
    }

    private static XElement? XmlElement(XElement parent, string name)
    {
        return parent.Elements().FirstOrDefault(element => element.Name.LocalName == name);
    }

    private static IEnumerable<XElement> XmlElements(XElement parent, string name)
    {
        return parent.Elements().Where(element => element.Name.LocalName == name);
    }
}
