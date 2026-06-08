using System.Globalization;
using RetroSharp.Parser;

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

        var videoProgram = NesVideoProgram.FromProgram(parse.Value);
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

    public required IReadOnlyDictionary<string, FunctionSyntax> Functions { get; init; }

    public required BlockSyntax MainBlock { get; init; }

    public static NesVideoProgram FromProgram(ProgramSyntax program)
    {
        var main = program.Functions.FirstOrDefault(f => f.Name == "main")
                   ?? throw new InvalidOperationException("NES target requires a main function.");

        var functions = BuildFunctionIndex(program.Functions);
        var result = new NesVideoProgram
        {
            Functions = functions,
            MainBlock = main.Block,
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

            ApplyStaticVideoCall(call, callStack);
        }
    }

    private void ApplyStaticVideoCall(FunctionCall call, HashSet<string> callStack)
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
                ApplyStaticUserFunction(call, callStack);
                break;
        }
    }

    private void ApplyStaticUserFunction(FunctionCall call, HashSet<string> callStack)
    {
        if (!Functions.TryGetValue(call.Name, out var function))
        {
            return;
        }

        RequireParameterlessUserFunction(call, function);

        if (!callStack.Add(function.Name))
        {
            throw new InvalidOperationException($"Recursive NES user function call '{function.Name}' is not supported.");
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

    internal static void RequireParameterlessUserFunction(FunctionCall call, FunctionSyntax function)
    {
        var argumentCount = call.Parameters.Count();
        if (argumentCount != 0 || function.Parameters.Count != 0)
        {
            throw new InvalidOperationException($"NES target only supports parameterless user function calls. '{call.Name}' declares {function.Parameters.Count} parameter(s) and was called with {argumentCount} argument(s).");
        }
    }

    internal static void RequireArity(FunctionCall call, int expected)
    {
        var actual = call.Parameters.Count();
        if (actual != expected)
        {
            throw new InvalidOperationException($"{call.Name} expects {expected} arguments, got {actual}.");
        }
    }

    private static int ConstArg(FunctionCall call, int index, int min, int max)
    {
        return CheckedRange(ConstValue(call.Parameters.ElementAt(index), $"{call.Name} argument {index + 1}"), min, max, $"{call.Name} argument {index + 1}");
    }

    internal static int ConstValue(ExpressionSyntax expression, string context)
    {
        if (expression is not ConstantSyntax constant)
        {
            throw new InvalidOperationException($"{context} must be a constant integer.");
        }

        if (!int.TryParse(Convert.ToString(constant.Value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            throw new InvalidOperationException($"{context} must be a constant integer.");
        }

        return value;
    }

    internal static string IdentifierArg(ExpressionSyntax expression, string context)
    {
        if (expression is IdentifierSyntax identifier)
        {
            return identifier.Identifier;
        }

        throw new InvalidOperationException($"{context} must be an identifier.");
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
