using System.Globalization;
using System.Text.Json;
using RetroSharp.Core.Sdk;
using RetroSharp.Parser;

namespace RetroSharp.GameBoy;

public static class GameBoyRomCompiler
{
    public static byte[] CompileSource(string source, string? baseDirectory = null)
    {
        var videoProgram = ParseVideoProgram(source, baseDirectory);
        return GameBoyRomBuilder.Build(videoProgram);
    }

    public static IReadOnlyList<Sdk2DOperation> CollectSdkOperations(string source, string? baseDirectory = null)
    {
        return ParseVideoProgram(source, baseDirectory).SdkOperations;
    }

    private static GameBoyVideoProgram ParseVideoProgram(string source, string? baseDirectory)
    {
        var parse = new SomeParser().Parse(source);
        if (parse.IsFailure)
        {
            throw new InvalidOperationException(parse.Error);
        }

        return GameBoyVideoProgram.FromProgram(parse.Value, baseDirectory);
    }
}

internal sealed class GameBoyVideoProgram
{
    public const int FirstSpriteTile = 6;

    private readonly List<GameBoyCompiledSpriteAsset> spriteAssetsInLoadOrder = [];
    private readonly Dictionary<string, GameBoyCompiledSpriteAsset> spriteAssets = [];
    private int nextSpriteTile = FirstSpriteTile;

    private string BaseDirectory { get; init; } = Directory.GetCurrentDirectory();

    public byte BackgroundPalette { get; private set; } = 0xE4;

    public byte ObjectPalette { get; private set; } = 0xE4;

    public byte[] TileMap { get; } = new byte[1024];

    public SortedDictionary<int, byte[]> MapColumns { get; } = [];

    public int MapColumnHeight { get; private set; }

    public SortedDictionary<int, byte[]> MapFlagColumns { get; } = [];

    public int MapFlagColumnHeight { get; private set; }

    public SortedDictionary<int, byte[]> WorldColumns { get; } = [];

    public int WorldColumnHeight { get; private set; }

    public SortedDictionary<int, WorldTileFlags[]> WorldFlagColumns { get; } = [];

    public int WorldFlagColumnHeight { get; private set; }

    public WorldMap2D? WorldMap { get; private set; }

    public IReadOnlyList<GameBoyCompiledSpriteAsset> SpriteAssetsInLoadOrder => spriteAssetsInLoadOrder;

    public IReadOnlyDictionary<string, GameBoyCompiledSpriteAsset> SpriteAssets => spriteAssets;

    public int SpriteTileCount => nextSpriteTile - FirstSpriteTile;

    public required IReadOnlyDictionary<string, FunctionSyntax> Functions { get; init; }

    public required BlockSyntax MainBlock { get; init; }

    public required IReadOnlyList<Sdk2DOperation> SdkOperations { get; init; }

    public static GameBoyVideoProgram FromProgram(ProgramSyntax program, string? baseDirectory = null)
    {
        var main = program.Functions.FirstOrDefault(f => f.Name == "main")
                   ?? throw new InvalidOperationException("Game Boy target requires a main function.");

        var functions = BuildFunctionIndex(program.Functions);
        var result = new GameBoyVideoProgram
        {
            BaseDirectory = Path.GetFullPath(baseDirectory ?? Directory.GetCurrentDirectory()),
            Functions = functions,
            MainBlock = main.Block,
            SdkOperations = GameBoySdkOperationCollector.Collect(main.Block, functions),
        };

        result.ApplyStaticVideoCalls(main.Block, []);
        return result;
    }

    private static Dictionary<string, FunctionSyntax> BuildFunctionIndex(IEnumerable<FunctionSyntax> functions)
    {
        var result = new Dictionary<string, FunctionSyntax>();
        foreach (var function in functions)
        {
            if (!result.TryAdd(function.Name, function))
            {
                throw new InvalidOperationException($"Function '{function.Name}' is already declared.");
            }
        }

        return result;
    }

    private void ApplyStaticVideoCalls(BlockSyntax block, HashSet<string> callStack)
    {
        foreach (var statement in block.Statements)
        {
            if (statement is not ExpressionStatementSyntax { Expression: FunctionCall call })
            {
                continue;
            }

            switch (call.Name)
            {
                case "palette_set":
                    RequireArity(call, 2);
                    SetPaletteColor(ConstArg(call, 0, 0, 3), ConstArg(call, 1, 0, 3));
                    break;
                case "object_palette_set":
                    RequireArity(call, 2);
                    SetObjectPaletteColor(ConstArg(call, 0, 0, 3), ConstArg(call, 1, 0, 3));
                    break;
                case "tilemap_set":
                    RequireArity(call, 3);
                    SetTile(ConstArg(call, 0, 0, 31), ConstArg(call, 1, 0, 31), ConstArg(call, 2, 0, 255));
                    break;
                case "tilemap_fill":
                    RequireArity(call, 5);
                    FillTiles(
                        ConstArg(call, 0, 0, 31),
                        ConstArg(call, 1, 0, 31),
                        ConstArg(call, 2, 1, 32),
                        ConstArg(call, 3, 1, 32),
                        ConstArg(call, 4, 0, 255));
                    break;
                case "map_column":
                    ApplyMapColumn(call);
                    break;
                case "world_column":
                    ApplyWorldColumn(call);
                    break;
                case "world_flags":
                    ApplyWorldFlags(call);
                    break;
                case "world_map":
                    ApplyWorldMap(call);
                    break;
                case "sprite_asset":
                    ApplySpriteAsset(call);
                    break;
                default:
                    ApplyStaticUserFunction(call, callStack);
                    break;
            }
        }
    }

    private void ApplyStaticUserFunction(FunctionCall call, HashSet<string> callStack)
    {
        if (!Functions.TryGetValue(call.Name, out var function))
        {
            return;
        }

        RequireParameterlessUserFunction("Game Boy", call, function);
        if (!callStack.Add(function.Name))
        {
            throw new InvalidOperationException($"Recursive Game Boy user function call '{function.Name}' is not supported.");
        }

        try
        {
            ApplyStaticVideoCalls(function.Block, callStack);
        }
        finally
        {
            callStack.Remove(function.Name);
        }
    }

    internal static void RequireParameterlessUserFunction(string target, FunctionCall call, FunctionSyntax function)
    {
        var argumentCount = call.Parameters.Count();
        if (argumentCount != 0 || function.Parameters.Count != 0)
        {
            throw new InvalidOperationException($"{target} target only supports parameterless user function calls. '{call.Name}' declares {function.Parameters.Count} parameter(s) and was called with {argumentCount} argument(s).");
        }
    }

    private void ApplySpriteAsset(FunctionCall call)
    {
        var count = call.Parameters.Count();
        if (count is not 2 and not 4)
        {
            throw new InvalidOperationException($"sprite_asset expects 2 or 4 arguments, got {count}.");
        }

        var name = IdentifierArg(call.Parameters.ElementAt(0), "sprite_asset argument 1");
        if (spriteAssets.ContainsKey(name))
        {
            throw new InvalidOperationException($"Sprite asset '{name}' is already declared.");
        }

        var relativePath = StringArg(call, 1);
        var path = ResolveAssetPath(relativePath);
        var frameWidth = count == 4 ? ConstArg(call, 2, 1, 160) : (int?)null;
        var frameHeight = count == 4 ? ConstArg(call, 3, 1, 160) : (int?)null;
        var asset = GameBoySpriteAssetCompiler.CompileFromFile(name, path, nextSpriteTile, frameWidth, frameHeight);
        nextSpriteTile += asset.TileCount;
        spriteAssets.Add(name, asset);
        spriteAssetsInLoadOrder.Add(asset);
    }

    private void ApplyMapColumn(FunctionCall call)
    {
        var (index, tiles) = ParseColumnData(call);

        if (MapColumnHeight == 0)
        {
            MapColumnHeight = tiles.Length;
        }
        else if (tiles.Length != MapColumnHeight)
        {
            throw new InvalidOperationException("All map_column calls must use the same tile count.");
        }

        MapColumns[index] = tiles;
    }

    private void ApplyWorldColumn(FunctionCall call)
    {
        var (index, tiles) = ParseColumnData(call);

        if (WorldColumnHeight == 0)
        {
            WorldColumnHeight = tiles.Length;
        }
        else if (tiles.Length != WorldColumnHeight)
        {
            throw new InvalidOperationException("All world_column calls must use the same tile count.");
        }

        WorldColumns[index] = tiles;
    }

    private void ApplyWorldFlags(FunctionCall call)
    {
        var (index, flags) = ParseFlagColumnData(call);

        if (WorldFlagColumnHeight == 0)
        {
            WorldFlagColumnHeight = flags.Length;
        }
        else if (flags.Length != WorldFlagColumnHeight)
        {
            throw new InvalidOperationException("All world_flags calls must use the same flag count.");
        }

        WorldFlagColumns[index] = flags;
    }

    private void ApplyWorldMap(FunctionCall call)
    {
        RequireArity(call, 3);
        var sourceColumns = WorldColumnHeight == 0 ? MapColumns : WorldColumns;
        var sourceHeight = WorldColumnHeight == 0 ? MapColumnHeight : WorldColumnHeight;
        if (sourceHeight == 0)
        {
            throw new InvalidOperationException("world_map requires at least one world_column or map_column declaration.");
        }

        var width = ConstArg(call, 0, 1, 255);
        var streamY = ConstArg(call, 1, 0, 31);
        var height = ConstArg(call, 2, 1, sourceHeight);
        if (WorldFlagColumnHeight is not 0 && WorldFlagColumnHeight < height)
        {
            throw new InvalidOperationException("world_map height must not exceed the declared world_flags height.");
        }

        if (streamY + height > 32)
        {
            throw new InvalidOperationException("world_map stream area exceeds the Game Boy background tilemap height.");
        }

        var tileIds = new int[width * height];
        var tileFlags = new WorldTileFlags[width * height];
        for (var x = 0; x < width; x++)
        {
            sourceColumns.TryGetValue(x, out var column);
            WorldFlagColumns.TryGetValue(x, out var flagColumn);
            for (var y = 0; y < height; y++)
            {
                tileIds[y * width + x] = column is null ? 0 : column[y];
                tileFlags[y * width + x] = flagColumn is null ? WorldTileFlags.Empty : flagColumn[y];
            }
        }

        WorldMap = new WorldMap2D(width, height, tileIds, tileFlags);
        GenerateMapColumnsFromWorldMap(WorldMap);
        GenerateMapFlagColumnsFromWorldMap(WorldMap);
        ApplyWorldMapToTileMap(WorldMap, streamY);
    }

    private (int Index, byte[] Tiles) ParseColumnData(FunctionCall call)
    {
        var args = call.Parameters.ToList();
        if (args.Count < 2)
        {
            throw new InvalidOperationException($"{call.Name} expects an index and at least one tile.");
        }

        var index = CheckedRange(ConstValue(args[0], $"{call.Name} argument 1"), 0, 255, $"{call.Name} argument 1");
        var tiles = args
            .Skip(1)
            .Select((arg, i) => CheckedRange(ConstValue(arg, $"{call.Name} argument {i + 2}"), 0, 255, $"{call.Name} argument {i + 2}"))
            .Select(value => (byte)value)
            .ToArray();

        return (index, tiles);
    }

    private (int Index, WorldTileFlags[] Flags) ParseFlagColumnData(FunctionCall call)
    {
        var args = call.Parameters.ToList();
        if (args.Count < 2)
        {
            throw new InvalidOperationException("world_flags expects an index and at least one flag value.");
        }

        var index = CheckedRange(ConstValue(args[0], "world_flags argument 1"), 0, 255, "world_flags argument 1");
        var allowedFlags = (int)(WorldTileFlags.Solid | WorldTileFlags.Hazard | WorldTileFlags.Platform);
        var flags = args
            .Skip(1)
            .Select((arg, i) => CheckedRange(ConstValue(arg, $"world_flags argument {i + 2}"), 0, allowedFlags, $"world_flags argument {i + 2}"))
            .Select(value => (WorldTileFlags)value)
            .ToArray();

        return (index, flags);
    }

    private void GenerateMapColumnsFromWorldMap(WorldMap2D worldMap)
    {
        MapColumns.Clear();
        MapColumnHeight = worldMap.Height;
        for (var x = 0; x < worldMap.Width; x++)
        {
            var tiles = new byte[worldMap.Height];
            for (var y = 0; y < worldMap.Height; y++)
            {
                tiles[y] = CheckedByteTileId(worldMap.TileIdAt(x, y), x, y);
            }

            MapColumns[x] = tiles;
        }
    }

    private void GenerateMapFlagColumnsFromWorldMap(WorldMap2D worldMap)
    {
        MapFlagColumns.Clear();
        MapFlagColumnHeight = worldMap.Height;
        for (var x = 0; x < worldMap.Width; x++)
        {
            var flags = new byte[worldMap.Height];
            for (var y = 0; y < worldMap.Height; y++)
            {
                flags[y] = CheckedByteFlags(worldMap.FlagsAt(x, y), x, y);
            }

            MapFlagColumns[x] = flags;
        }
    }

    private static byte CheckedByteTileId(int tileId, int x, int y)
    {
        if (tileId is < 0 or > 255)
        {
            throw new InvalidOperationException($"world_map tile ({x}, {y}) must be between 0 and 255 for the Game Boy target.");
        }

        return (byte)tileId;
    }

    private static byte CheckedByteFlags(WorldTileFlags flags, int x, int y)
    {
        var value = (int)flags;
        var allowedFlags = (int)(WorldTileFlags.Solid | WorldTileFlags.Hazard | WorldTileFlags.Platform);
        if (value < 0 || (value & ~allowedFlags) != 0)
        {
            throw new InvalidOperationException($"world_map flags ({x}, {y}) contain unsupported Game Boy flag bits.");
        }

        return (byte)value;
    }

    private void ApplyWorldMapToTileMap(WorldMap2D worldMap, int streamY)
    {
        for (var y = 0; y < worldMap.Height; y++)
        {
            for (var x = 0; x < 32; x++)
            {
                SetTile(x, streamY + y, worldMap.TileIdAt(x % worldMap.Width, y));
            }
        }
    }

    private void SetPaletteColor(int index, int color)
    {
        var shift = index * 2;
        BackgroundPalette = (byte)((BackgroundPalette & ~(0x03 << shift)) | ((color & 0x03) << shift));
    }

    private void SetObjectPaletteColor(int index, int color)
    {
        var shift = index * 2;
        ObjectPalette = (byte)((ObjectPalette & ~(0x03 << shift)) | ((color & 0x03) << shift));
    }

    private void SetTile(int x, int y, int tile)
    {
        TileMap[y * 32 + x] = (byte)tile;
    }

    private void FillTiles(int x, int y, int width, int height, int tile)
    {
        if (x + width > 32 || y + height > 32)
        {
            throw new InvalidOperationException("tilemap_fill exceeds the Game Boy background tilemap area.");
        }

        for (var yy = y; yy < y + height; yy++)
        {
            for (var xx = x; xx < x + width; xx++)
            {
                SetTile(xx, yy, tile);
            }
        }
    }

    internal static void RequireArity(FunctionCall call, int expected)
    {
        var count = call.Parameters.Count();
        if (count != expected)
        {
            throw new InvalidOperationException($"{call.Name} expects {expected} arguments, got {count}.");
        }
    }

    internal static int ConstArg(FunctionCall call, int index, int min, int max)
    {
        var arg = call.Parameters.ElementAt(index);
        var value = ConstValue(arg, $"{call.Name} argument {index + 1}");
        if (value < min || value > max)
        {
            throw new InvalidOperationException($"{call.Name} argument {index + 1} must be between {min} and {max}.");
        }

        return value;
    }

    internal static string IdentifierArg(ExpressionSyntax expression, string context)
    {
        if (expression is not IdentifierSyntax identifier)
        {
            throw new InvalidOperationException($"{context} must be an identifier.");
        }

        return identifier.Identifier;
    }

    internal static string StringArg(FunctionCall call, int index)
    {
        var context = $"{call.Name} argument {index + 1}";
        if (call.Parameters.ElementAt(index) is not ConstantSyntax constant)
        {
            throw new InvalidOperationException($"{context} must be a string literal.");
        }

        var text = Convert.ToString(constant.Value, CultureInfo.InvariantCulture);
        if (text is null || !text.StartsWith('"'))
        {
            throw new InvalidOperationException($"{context} must be a string literal.");
        }

        try
        {
            return JsonSerializer.Deserialize<string>(text) ?? throw new InvalidOperationException($"{context} must be a string literal.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"{context} must be a valid string literal.", ex);
        }
    }

    internal static int ConstValue(ExpressionSyntax expression, string context)
    {
        if (expression is not ConstantSyntax constant)
        {
            throw new InvalidOperationException($"{context} must be a constant integer.");
        }

        var text = Convert.ToString(constant.Value, CultureInfo.InvariantCulture);
        if (text == "true")
        {
            return 1;
        }

        if (text == "false")
        {
            return 0;
        }

        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            throw new InvalidOperationException($"{context} must be a constant integer.");
        }

        return value;
    }

    private static int CheckedRange(int value, int min, int max, string context)
    {
        if (value < min || value > max)
        {
            throw new InvalidOperationException($"{context} must be between {min} and {max}.");
        }

        return value;
    }

    private string ResolveAssetPath(string path)
    {
        var resolved = Path.IsPathRooted(path) ? path : Path.Combine(BaseDirectory, path);
        return Path.GetFullPath(resolved);
    }
}
