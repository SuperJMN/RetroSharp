using System.Globalization;
using System.Text.Json;
using RetroSharp.Parser;

namespace RetroSharp.GameBoy;

public static class GameBoyRomCompiler
{
    public static byte[] CompileSource(string source, string? baseDirectory = null)
    {
        var parse = new SomeParser().Parse(source);
        if (parse.IsFailure)
        {
            throw new InvalidOperationException(parse.Error);
        }

        var videoProgram = GameBoyVideoProgram.FromProgram(parse.Value, baseDirectory);
        return GameBoyRomBuilder.Build(videoProgram);
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

    public IReadOnlyList<GameBoyCompiledSpriteAsset> SpriteAssetsInLoadOrder => spriteAssetsInLoadOrder;

    public IReadOnlyDictionary<string, GameBoyCompiledSpriteAsset> SpriteAssets => spriteAssets;

    public int SpriteTileCount => nextSpriteTile - FirstSpriteTile;

    public required BlockSyntax MainBlock { get; init; }

    public static GameBoyVideoProgram FromProgram(ProgramSyntax program, string? baseDirectory = null)
    {
        var main = program.Functions.FirstOrDefault(f => f.Name == "main")
                   ?? throw new InvalidOperationException("Game Boy target requires a main function.");

        var result = new GameBoyVideoProgram
        {
            BaseDirectory = Path.GetFullPath(baseDirectory ?? Directory.GetCurrentDirectory()),
            MainBlock = main.Block,
        };

        result.ApplyStaticVideoCalls(main.Block);
        return result;
    }

    private void ApplyStaticVideoCalls(BlockSyntax block)
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
                case "sprite_asset":
                    ApplySpriteAsset(call);
                    break;
            }
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
        var args = call.Parameters.ToList();
        if (args.Count < 2)
        {
            throw new InvalidOperationException("map_column expects an index and at least one tile.");
        }

        var index = CheckedRange(ConstValue(args[0], "map_column argument 1"), 0, 255, "map_column argument 1");
        var tiles = args
            .Skip(1)
            .Select((arg, i) => CheckedRange(ConstValue(arg, $"map_column argument {i + 2}"), 0, 255, $"map_column argument {i + 2}"))
            .Select(value => (byte)value)
            .ToArray();

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
