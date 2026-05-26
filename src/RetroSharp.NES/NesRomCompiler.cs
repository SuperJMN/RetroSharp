using System.Globalization;
using RetroSharp.Parser;
using RetroSharp.SemanticAnalysis;

namespace RetroSharp.NES;

public static class NesRomCompiler
{
    public static byte[] CompileSource(string source)
    {
        var parse = new SomeParser().Parse(source);
        if (parse.IsFailure)
        {
            throw new InvalidOperationException(parse.Error);
        }

        var analyzed = new SemanticAnalyzer().Analyze(parse.Value);
        var root = analyzed.Node;
        var errors = root.AllErrors.ToList();
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
        }

        var videoProgram = NesVideoProgram.FromProgram((ProgramNode)root);
        return NesRomBuilder.Build(videoProgram);
    }
}

internal sealed class NesVideoProgram
{
    public byte[] Palette { get; } =
    [
        0x0F, 0x27, 0x16, 0x30,
        0x0F, 0x01, 0x11, 0x21,
        0x0F, 0x06, 0x16, 0x26,
        0x0F, 0x09, 0x19, 0x29,
        0x0F, 0x27, 0x16, 0x30,
        0x0F, 0x01, 0x11, 0x21,
        0x0F, 0x06, 0x16, 0x26,
        0x0F, 0x09, 0x19, 0x29,
    ];

    public byte[] NameTable { get; } = new byte[1024];

    public static NesVideoProgram FromProgram(ProgramNode program)
    {
        var main = program.Functions.FirstOrDefault(f => f.Name == "main")
                   ?? throw new InvalidOperationException("NES target requires a main function.");

        var result = new NesVideoProgram();

        foreach (var statement in main.Block.Statements)
        {
            if (statement is ReturnNode)
            {
                continue;
            }

            if (statement is not ExpressionStatementNode { Expression: FunctionCallExpressionNode call })
            {
                throw new InvalidOperationException($"NES target only supports video API calls in main. Unsupported statement: {statement.GetType().Name}");
            }

            result.Apply(call);
        }

        return result;
    }

    private void Apply(FunctionCallExpressionNode call)
    {
        switch (call.Name)
        {
            case "video_init":
            case "video_present":
            case "video_wait_vblank":
                RequireArity(call, 0);
                break;
            case "palette_set":
                RequireArity(call, 2);
                Palette[ConstArg(call, 0, 0, 31)] = (byte)ConstArg(call, 1, 0, 63);
                break;
            case "tilemap_set":
                RequireArity(call, 3);
                SetTile(ConstArg(call, 0, 0, 31), ConstArg(call, 1, 0, 29), ConstArg(call, 2, 0, 255));
                break;
            case "tilemap_fill":
                RequireArity(call, 5);
                FillTiles(
                    ConstArg(call, 0, 0, 31),
                    ConstArg(call, 1, 0, 29),
                    ConstArg(call, 2, 1, 32),
                    ConstArg(call, 3, 1, 30),
                    ConstArg(call, 4, 0, 255));
                break;
            default:
                throw new InvalidOperationException($"Unsupported NES video API call '{call.Name}'.");
        }
    }

    private void SetTile(int x, int y, int tile)
    {
        NameTable[y * 32 + x] = (byte)tile;
    }

    private void FillTiles(int x, int y, int width, int height, int tile)
    {
        if (x + width > 32 || y + height > 30)
        {
            throw new InvalidOperationException("tilemap_fill exceeds the visible NES nametable area.");
        }

        for (var yy = y; yy < y + height; yy++)
        {
            for (var xx = x; xx < x + width; xx++)
            {
                SetTile(xx, yy, tile);
            }
        }
    }

    private static void RequireArity(FunctionCallExpressionNode call, int expected)
    {
        if (call.Arguments.Count != expected)
        {
            throw new InvalidOperationException($"{call.Name} expects {expected} arguments, got {call.Arguments.Count}.");
        }
    }

    private static int ConstArg(FunctionCallExpressionNode call, int index, int min, int max)
    {
        var arg = call.Arguments[index];
        if (arg is not ConstantNode constant)
        {
            throw new InvalidOperationException($"{call.Name} argument {index + 1} must be a constant integer.");
        }

        if (!int.TryParse(Convert.ToString(constant.Value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            throw new InvalidOperationException($"{call.Name} argument {index + 1} must be a constant integer.");
        }

        if (value < min || value > max)
        {
            throw new InvalidOperationException($"{call.Name} argument {index + 1} must be between {min} and {max}.");
        }

        return value;
    }
}
