using System.Globalization;
using System.Text.Json;
using RetroSharp.Core;
using RetroSharp.Core.Sdk;
using RetroSharp.Core.Targeting;
using RetroSharp.Parser;
using RetroSharp.Sdk;

namespace RetroSharp.NES;

public static class NesRomCompiler
{
    public static byte[] CompileSource(string source, string? baseDirectory = null)
    {
        var parse = new SomeParser().Parse(source);
        if (parse.IsFailure)
        {
            throw new InvalidOperationException(parse.Error);
        }

        ValidateFunctionContracts(parse.Value);
        var videoProgram = NesVideoProgram.FromProgram(parse.Value, baseDirectory);
        ValidateSdkOperations(videoProgram);
        return NesRomBuilder.Build(videoProgram);
    }

    public static IReadOnlyList<Sdk2DOperation> CollectSdkOperations(string source, string? baseDirectory = null)
    {
        var parse = new SomeParser().Parse(source);
        if (parse.IsFailure)
        {
            throw new InvalidOperationException(parse.Error);
        }

        ValidateFunctionContracts(parse.Value);
        var videoProgram = NesVideoProgram.FromProgram(parse.Value, baseDirectory);
        return Sdk2DOperationCollector.Collect(videoProgram.MainBlock, videoProgram.Functions, "NES");
    }

    public static IReadOnlyList<SdkAudioOperation> CollectSdkAudioOperations(string source, string? baseDirectory = null)
    {
        var parse = new SomeParser().Parse(source);
        if (parse.IsFailure)
        {
            throw new InvalidOperationException(parse.Error);
        }

        ValidateFunctionContracts(parse.Value);
        var videoProgram = NesVideoProgram.FromProgram(parse.Value, baseDirectory);
        return SdkAudioOperationCollector.Collect(videoProgram.MainBlock, videoProgram.Functions, "NES");
    }

    private static void ValidateSdkOperations(NesVideoProgram videoProgram)
    {
        var operations = Sdk2DOperationCollector.Collect(videoProgram.MainBlock, videoProgram.Functions, "NES");
        foreach (var operation in operations)
        {
            Sdk2DOperationValidator.Validate(NesTarget.Capabilities, operation);
        }

        var audioOperations = SdkAudioOperationCollector.Collect(videoProgram.MainBlock, videoProgram.Functions, "NES");
        foreach (var operation in audioOperations)
        {
            SdkAudioOperationValidator.Validate(NesTarget.AudioCapabilities, operation);
        }
    }

    private static void ValidateFunctionContracts(ProgramSyntax program)
    {
        var errors = FunctionContractValidator.ValidateProgram(program).ToList();
        if (errors.Count != 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
        }
    }
}

internal sealed class NesVideoProgram
{
    public const int FirstSpriteTile = 6;

    private readonly List<NesCompiledSpriteAsset> spriteAssetsInLoadOrder = [];
    private readonly Dictionary<string, NesCompiledSpriteAsset> spriteAssets = [];
    private readonly SortedDictionary<int, byte[]> mapColumns = [];
    private readonly SortedDictionary<int, byte[]> worldColumns = [];
    private readonly SortedDictionary<int, WorldTileFlags[]> worldFlagColumns = [];
    private int nextSpriteTile = FirstSpriteTile;

    private string BaseDirectory { get; init; } = Directory.GetCurrentDirectory();

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

    public int MapColumnHeight { get; private set; }

    public int WorldColumnHeight { get; private set; }

    public int WorldFlagColumnHeight { get; private set; }

    public WorldMap2D? WorldMap { get; private set; }

    public IReadOnlyList<NesCompiledSpriteAsset> SpriteAssetsInLoadOrder => spriteAssetsInLoadOrder;

    public IReadOnlyDictionary<string, NesCompiledSpriteAsset> SpriteAssets => spriteAssets;

    public required IReadOnlyDictionary<string, FunctionSyntax> Functions { get; init; }

    public required IReadOnlyDictionary<string, EnumSyntax> Enums { get; init; }

    public required IReadOnlyDictionary<string, StructSyntax> Structs { get; init; }

    public required BlockSyntax MainBlock { get; init; }

    public static NesVideoProgram FromProgram(ProgramSyntax program, string? baseDirectory = null)
    {
        program = ConstantFolder.Fold(program);

        var main = program.Functions.FirstOrDefault(f => f.Name == "main")
                   ?? throw new InvalidOperationException("NES target requires a main function.");

        var functions = BuildFunctionIndex(program.Functions);
        var enums = BuildEnumIndex(program.Enums);
        var structs = BuildStructIndex(program.Structs);
        var result = new NesVideoProgram
        {
            BaseDirectory = Path.GetFullPath(baseDirectory ?? Directory.GetCurrentDirectory()),
            Functions = functions,
            Enums = enums,
            Structs = structs,
            MainBlock = main.Block,
        };

        result.ApplyStaticVideoCalls(main.Block, []);
        return result;
    }

    private static Dictionary<string, EnumSyntax> BuildEnumIndex(IEnumerable<EnumSyntax> enums)
    {
        var result = new Dictionary<string, EnumSyntax>();
        foreach (var enumSyntax in enums)
        {
            if (!result.TryAdd(enumSyntax.Name, enumSyntax))
            {
                throw new InvalidOperationException($"Enum '{enumSyntax.Name}' is already declared.");
            }
        }

        return result;
    }

    private static Dictionary<string, StructSyntax> BuildStructIndex(IEnumerable<StructSyntax> structs)
    {
        var result = new Dictionary<string, StructSyntax>();
        foreach (var structSyntax in structs)
        {
            if (!result.TryAdd(structSyntax.Name, structSyntax))
            {
                throw new InvalidOperationException($"Struct '{structSyntax.Name}' is already declared.");
            }
        }

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
            case "music_asset":
                RequireArity(call, 2);
                break;
            case "audio_init":
            case "audio_update":
            case "music_stop":
                RequireArity(call, 0);
                break;
            case "music_play":
                RequireArity(call, 1);
                break;
            case "hud_set_tile":
                ValidateHudSetTile(call);
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

        if (!callStack.Add(function.Name))
        {
            throw new InvalidOperationException($"Recursive NES user function call '{function.Name}' is not supported.");
        }

        try
        {
            ApplyStaticVideoCalls(ParameterSubstitution.Substitute(function, call, "NES"), callStack);
        }
        finally
        {
            callStack.Remove(function.Name);
        }
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

        mapColumns[index] = tiles;
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

        worldColumns[index] = tiles;
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

        worldFlagColumns[index] = flags;
    }

    private void ApplyWorldMap(FunctionCall call)
    {
        RequireArity(call, 3);
        var sourceColumns = WorldColumnHeight == 0 ? mapColumns : worldColumns;
        var sourceHeight = WorldColumnHeight == 0 ? MapColumnHeight : WorldColumnHeight;
        if (sourceHeight == 0)
        {
            throw new InvalidOperationException("world_map requires at least one world_column or map_column declaration.");
        }

        var width = ConstArg(call, 0, 1, 255);
        if (width > 32)
        {
            throw new InvalidOperationException("NES world_map width must fit the visible 32-column nametable until runtime streaming lands.");
        }

        var streamY = ConstArg(call, 1, 0, 29);
        var height = ConstArg(call, 2, 1, sourceHeight);
        if (streamY + height > 30)
        {
            throw new InvalidOperationException("world_map stream area exceeds the NES visible nametable height.");
        }

        if (WorldFlagColumnHeight is not 0 && WorldFlagColumnHeight < height)
        {
            throw new InvalidOperationException("world_map height must not exceed the declared world_flags height.");
        }

        var tileIds = new int[width * height];
        var tileFlags = new WorldTileFlags[width * height];
        for (var x = 0; x < width; x++)
        {
            sourceColumns.TryGetValue(x, out var column);
            worldFlagColumns.TryGetValue(x, out var flagColumn);
            for (var y = 0; y < height; y++)
            {
                var tile = column is null ? (byte)0 : column[y];
                tileIds[y * width + x] = tile;
                tileFlags[y * width + x] = flagColumn is null ? WorldTileFlags.Empty : flagColumn[y];
                SetTile(x, streamY + y, tile);
            }
        }

        WorldMap = new WorldMap2D(width, height, tileIds, tileFlags);
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

    private void ApplySpriteAsset(FunctionCall call)
    {
        var count = call.Parameters.Count();
        if (count != 2)
        {
            throw new InvalidOperationException($"NES sprite_asset expects 2 arguments for the current JSON sprite spike, got {count}.");
        }

        var name = IdentifierArg(call.Parameters.ElementAt(0), "sprite_asset argument 1");
        if (spriteAssets.ContainsKey(name))
        {
            throw new InvalidOperationException($"Sprite asset '{name}' is already declared.");
        }

        var path = ResolveAssetPath(StringArg(call, 1));
        var asset = NesSpriteAssetCompiler.CompileFromFile(name, path, nextSpriteTile);
        nextSpriteTile += asset.TileCount;
        spriteAssets.Add(name, asset);
        spriteAssetsInLoadOrder.Add(asset);
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
        if (expression is CastSyntax cast)
        {
            return ConstValue(cast.Expression, context);
        }

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

        if (!IntegerLiteral.TryParse(text, out var value))
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

    internal static string MemberAccessName(MemberAccessSyntax memberAccess)
    {
        var parts = new Stack<string>();
        ExpressionSyntax current = memberAccess;
        while (current is MemberAccessSyntax member)
        {
            parts.Push(member.Member);
            current = member.Target;
        }

        if (current is not IdentifierSyntax identifier)
        {
            throw new InvalidOperationException("NES member access currently requires an identifier base.");
        }

        parts.Push(identifier.Identifier);
        return string.Join(".", parts);
    }

    internal static void ValidateHudSetTile(FunctionCall call)
    {
        RequireArity(call, 4);
        var mode = HudModeArg(call.Parameters.ElementAt(0), "hud_set_tile argument 1");
        TargetCapabilityChecks.RequireHudMode(NesTarget.Capabilities, mode);
    }

    internal static HudMode HudModeArg(ExpressionSyntax expression, string context)
    {
        var identifier = IdentifierArg(expression, context);
        return NormalizeMode(identifier) switch
        {
            "window" => HudMode.Window,
            "splitscroll" => HudMode.SplitScroll,
            "sprite" or "spritehud" => HudMode.Sprite,
            "none" => HudMode.None,
            _ => throw new InvalidOperationException($"{context} must be one of window, split_scroll, sprite_hud, or none."),
        };

        static string NormalizeMode(string value)
        {
            return value.Replace("_", string.Empty, StringComparison.Ordinal)
                .Replace("-", string.Empty, StringComparison.Ordinal)
                .ToLowerInvariant();
        }
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
