using System.Globalization;
using RetroSharp.Parser;

namespace RetroSharp.GameBoy;

public static class GameBoyRomCompiler
{
    public static byte[] CompileSource(string source)
    {
        var parse = new SomeParser().Parse(source);
        if (parse.IsFailure)
        {
            throw new InvalidOperationException(parse.Error);
        }

        var videoProgram = GameBoyVideoProgram.FromProgram(parse.Value);
        return GameBoyRomBuilder.Build(videoProgram);
    }
}

internal sealed class GameBoyVideoProgram
{
    public byte BackgroundPalette { get; private set; } = 0xE4;

    public byte ObjectPalette { get; private set; } = 0xE4;

    public byte[] TileMap { get; } = new byte[1024];

    public SortedDictionary<int, byte[]> MapColumns { get; } = [];

    public int MapColumnHeight { get; private set; }

    public required BlockSyntax MainBlock { get; init; }

    public static GameBoyVideoProgram FromProgram(ProgramSyntax program)
    {
        var main = program.Functions.FirstOrDefault(f => f.Name == "main")
                   ?? throw new InvalidOperationException("Game Boy target requires a main function.");

        var result = new GameBoyVideoProgram
        {
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
            }
        }
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
}
