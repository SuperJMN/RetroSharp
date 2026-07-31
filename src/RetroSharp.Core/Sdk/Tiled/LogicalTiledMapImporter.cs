using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;

namespace RetroSharp.Core.Sdk.Tiled;

// Target-neutral importer for orthogonal, finite Tiled maps. Format adapters
// normalize TMJ and TMX into one private source model; the shared implementation
// then resolves geometry, playable layers, actor spawns, and collision flags.
// Pixel generation and target tile encoding stay in each target backend.
public static partial class LogicalTiledMapImporter
{
    private const uint TiledFlipFlagsMask = 0xF0000000;
    private const uint TiledGidMask = 0x0FFFFFFF;

    public static LogicalTiledMap Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var source = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".tmx" => ReadXmlMap(path),
            ".tmj" or ".json" => ReadJsonMap(path),
            _ => throw new InvalidOperationException(
                $"Tiled map '{Path.GetFileName(path)}' must use the .tmj, .json, or .tmx format."),
        };

        return BuildLogicalMap(source, Path.GetFileName(path));
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

    private static LogicalTiledMap BuildLogicalMap(TiledMapSource source, string displayName)
    {
        if (source.Type != "map")
        {
            throw new InvalidOperationException($"Tiled map '{displayName}' must have type 'map'.");
        }

        if (source.Orientation != "orthogonal")
        {
            throw new InvalidOperationException($"Tiled map '{displayName}' must be orthogonal.");
        }

        if (source.Infinite)
        {
            throw new InvalidOperationException($"Tiled map '{displayName}' must be finite; infinite maps are not supported yet.");
        }

        if (source.TileWidth < 8 || source.TileHeight < 8 || source.TileWidth % 8 != 0 || source.TileHeight % 8 != 0)
        {
            throw new InvalidOperationException($"Tiled map '{displayName}' must use tile sizes that are positive multiples of 8.");
        }

        var tileScaleX = source.TileWidth / 8;
        var tileScaleY = source.TileHeight / 8;
        var streamY = CustomIntProperty(source.Properties, "retrosharpStreamY")
            ?? throw new InvalidOperationException($"Tiled map '{displayName}' requires an integer custom property named 'retrosharpStreamY'.");
        var worldY = CustomIntProperty(source.Properties, "retrosharpWorldY") ?? streamY;
        var height = CustomIntProperty(source.Properties, "retrosharpWorldHeight") ?? source.Height - worldY;

        var expandedWidth = checked(source.Width * tileScaleX);
        var expandedMapHeight = checked(source.Height * tileScaleY);
        var expandedWorldY = checked(worldY * tileScaleY);
        var expandedStreamY = streamY;
        var expandedHeight = checked(height * tileScaleY);
        var backgroundOffsetY = expandedWorldY - expandedStreamY;

        if (worldY < 0 || height <= 0 || worldY + height > source.Height)
        {
            throw new InvalidOperationException($"Tiled map '{displayName}' world slice must fit inside the map height.");
        }

        var worldLayer = FindTileLayer(source.TileLayers, "world")
            ?? throw new InvalidOperationException($"Tiled map '{displayName}' requires a tile layer named 'world'.");
        ValidateTileLayer(worldLayer, source.Width, source.Height, displayName);

        var collisionLayer = FindTileLayer(source.TileLayers, "collision");
        if (collisionLayer is not null)
        {
            ValidateTileLayer(collisionLayer, source.Width, source.Height, displayName);
        }

        var backgroundLayer = FindTileLayer(source.TileLayers, "background");
        if (backgroundLayer is not null)
        {
            ValidateTileLayer(backgroundLayer, source.Width, source.Height, displayName);
        }

        var worldFlags = new WorldTileFlags[expandedWidth * expandedHeight];
        for (var y = 0; y < height; y++)
        {
            var sourceY = worldY + y;
            for (var x = 0; x < source.Width; x++)
            {
                var sourceIndex = sourceY * source.Width + x;
                var flags = collisionLayer is null
                    ? FlagsFromTiledGid(source.Tilesets, worldLayer.Gids[sourceIndex], $"{displayName} world layer tile ({x}, {sourceY})")
                    : TiledCollisionFlags.FlagsFromCollisionGid(collisionLayer.Gids[sourceIndex], $"{displayName} collision layer tile ({x}, {sourceY})");

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
            sourceWidth: source.Width,
            sourceHeight: source.Height,
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

        return new LogicalTiledMap(
            source.Tilesets,
            backgroundLayer?.Gids,
            worldLayer.Gids,
            worldFlags,
            geometry,
            BuildActorSpawnLayers(source.ObjectLayers, displayName));
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

    private static TiledTileLayer? FindTileLayer(IReadOnlyList<TiledTileLayer> layers, string name)
    {
        return layers.FirstOrDefault(layer => string.Equals(layer.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private static void ValidateTileLayer(TiledTileLayer layer, int width, int height, string displayName)
    {
        if (layer.Width != width || layer.Height != height)
        {
            throw new InvalidOperationException($"Tiled map '{displayName}' layer '{layer.Name}' must match the fixed map size.");
        }

        var expectedLength = checked(width * height);
        if (layer.Gids.Length != expectedLength)
        {
            throw new InvalidOperationException($"Tiled map '{displayName}' layer '{layer.Name}' must contain exactly {expectedLength} tiles.");
        }
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<LogicalActorSpawn>> BuildActorSpawnLayers(
        IReadOnlyList<TiledObjectLayer> layers,
        string displayName)
    {
        var result = new Dictionary<string, IReadOnlyList<LogicalActorSpawn>>(StringComparer.Ordinal);
        foreach (var layer in layers)
        {
            var spawns = new List<LogicalActorSpawn>();
            foreach (var obj in layer.Objects)
            {
                obj.Properties.TryGetValue("kind", out var propertyKind);
                var kind = FirstNonEmpty(propertyKind, obj.Type, obj.Class, obj.Name);
                if (string.IsNullOrWhiteSpace(kind))
                {
                    throw new InvalidOperationException($"Tiled map '{displayName}' object layer '{layer.Name}' object {obj.Id} requires an actor kind via a 'kind' property, type/class, or name.");
                }

                var x = CheckedUInt16(RoundedCoordinate(obj.X, displayName, layer.Name, obj.Id, "x"), $"Tiled map '{displayName}' object layer '{layer.Name}' object {obj.Id} x");
                var y = CheckedUInt16(RoundedCoordinate(obj.Y, displayName, layer.Name, obj.Id, "y"), $"Tiled map '{displayName}' object layer '{layer.Name}' object {obj.Id} y");
                spawns.Add(new LogicalActorSpawn(kind, x, y, ReadActorSpawnFields(obj, displayName, layer.Name)));
            }

            result[layer.Name] = spawns;
        }

        return result;
    }

    private static IReadOnlyDictionary<string, int> ReadActorSpawnFields(TiledObject obj, string displayName, string layerName)
    {
        var fields = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (name, value) in obj.Properties)
        {
            if (name == "kind" || name is not ("active" or "state" or "timer" or "facing" or "animTick" or "health" or "vx" or "vy"))
            {
                continue;
            }

            var context = $"Tiled map '{displayName}' object layer '{layerName}' object {obj.Id} property '{name}'";
            fields[name] = CheckedByte(PropertyIntValue(value, context), context);
        }

        return fields;
    }

    private static int RoundedCoordinate(double value, string displayName, string layerName, int objectId, string name)
    {
        if (value < int.MinValue || value > int.MaxValue)
        {
            throw new InvalidOperationException($"Tiled map '{displayName}' object layer '{layerName}' object {objectId} property '{name}' is out of range.");
        }

        return (int)Math.Round(value, MidpointRounding.AwayFromZero);
    }

    private static int PropertyIntValue(string value, string context)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        {
            return number;
        }

        if (bool.TryParse(value, out var boolean))
        {
            return boolean ? 1 : 0;
        }

        throw new InvalidOperationException($"{context} must be a byte value.");
    }

    private static int CheckedByte(int value, string context)
    {
        if (value is < 0 or > 255)
        {
            throw new InvalidOperationException($"{context} must be between 0 and 255.");
        }

        return value;
    }

    private static int CheckedUInt16(int value, string context)
    {
        if (value is < 0 or > 65535)
        {
            throw new InvalidOperationException($"{context} must be between 0 and 65535.");
        }

        return value;
    }

    private static int? CustomIntProperty(IReadOnlyDictionary<string, string> properties, string name)
    {
        if (!properties.TryGetValue(name, out var value))
        {
            return null;
        }

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number
            : throw new InvalidOperationException($"Tiled custom property '{name}' must be an integer.");
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
        var name = JsonStringPropertyOrDefault(root, "name", "<inline>");
        var tileWidth = JsonPositiveIntProperty(root, "tilewidth", displayName);
        var tileHeight = JsonPositiveIntProperty(root, "tileheight", displayName);
        var tileCount = JsonPositiveIntProperty(root, "tilecount", displayName);
        var columns = JsonPositiveIntProperty(root, "columns", displayName);
        ValidateTileSize(tileWidth, tileHeight, displayName, name);

        var imagePath = ResolveImagePath(JsonStringPropertyOrDefault(root, "image", ""), baseDirectory);
        return new LogicalTileset(firstGid, name, tileWidth, tileHeight, tileCount, columns, imagePath, TiledCollisionFlags.ReadJsonTileFlags(root));
    }

    private static LogicalTileset TilesetFromTsx(string path, int firstGid, string displayName)
    {
        var document = XDocument.Load(path);
        var root = document.Root ?? throw new InvalidOperationException($"Tiled tileset '{Path.GetFileName(path)}' is empty.");
        var baseDirectory = Path.GetDirectoryName(path) ?? Directory.GetCurrentDirectory();
        return TilesetFromXml(root, baseDirectory, firstGid, displayName);
    }

    private static LogicalTileset TilesetFromXml(XElement root, string baseDirectory, int firstGid, string displayName)
    {
        var name = XmlAttributeOrDefault(root, "name", "<inline>");
        var tileWidth = XmlPositiveIntAttribute(root, "tilewidth", displayName);
        var tileHeight = XmlPositiveIntAttribute(root, "tileheight", displayName);
        var tileCount = XmlPositiveIntAttribute(root, "tilecount", displayName);
        var columns = XmlPositiveIntAttribute(root, "columns", displayName);
        ValidateTileSize(tileWidth, tileHeight, displayName, name);

        var imageSource = XmlElement(root, "image")?.Attribute("source")?.Value ?? "";
        var imagePath = ResolveImagePath(imageSource, baseDirectory);
        return new LogicalTileset(firstGid, name, tileWidth, tileHeight, tileCount, columns, imagePath, TiledCollisionFlags.ReadXmlTileFlags(root));
    }

    private static string? ResolveImagePath(string imageSource, string baseDirectory)
    {
        return string.IsNullOrWhiteSpace(imageSource)
            ? null
            : Path.GetFullPath(Path.Combine(baseDirectory, imageSource));
    }

    private static void ValidateTileSize(int tileWidth, int tileHeight, string displayName, string tilesetName)
    {
        if (tileWidth < 8 || tileHeight < 8 || tileWidth % 8 != 0 || tileHeight % 8 != 0)
        {
            throw new InvalidOperationException($"Tiled map '{displayName}' tileset '{tilesetName}' must use tile sizes that are positive multiples of 8.");
        }
    }

    private sealed record TiledMapSource(
        string Type,
        string Orientation,
        bool Infinite,
        int Width,
        int Height,
        int TileWidth,
        int TileHeight,
        IReadOnlyDictionary<string, string> Properties,
        IReadOnlyList<LogicalTileset> Tilesets,
        IReadOnlyList<TiledTileLayer> TileLayers,
        IReadOnlyList<TiledObjectLayer> ObjectLayers);

    private sealed record TiledTileLayer(string Name, int Width, int Height, uint[] Gids);

    private sealed record TiledObjectLayer(string Name, IReadOnlyList<TiledObject> Objects);

    private sealed record TiledObject(
        int Id,
        string Type,
        string Class,
        string Name,
        double X,
        double Y,
        IReadOnlyDictionary<string, string> Properties);
}
