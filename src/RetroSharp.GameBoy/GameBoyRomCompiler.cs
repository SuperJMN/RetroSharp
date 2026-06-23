using System.Globalization;
using System.Text.Json;
using RetroSharp.Core;
using RetroSharp.Core.Sdk;
using RetroSharp.Core.Targeting;
using RetroSharp.Parser;
using RetroSharp.Sdk;

namespace RetroSharp.GameBoy;

public static class GameBoyRomCompiler
{
    public static byte[] CompileSource(string source, string? baseDirectory = null)
    {
        var videoProgram = ParseVideoProgram(source, baseDirectory);
        ValidateSdkOperations(videoProgram.SdkOperations);
        ValidateSdkAudioOperations(videoProgram.SdkAudioOperations);
        return GameBoyRomBuilder.Build(videoProgram);
    }

    public static IReadOnlyList<Sdk2DOperation> CollectSdkOperations(string source, string? baseDirectory = null)
    {
        return ParseVideoProgram(source, baseDirectory).SdkOperations;
    }

    public static IReadOnlyList<SdkAudioOperation> CollectSdkAudioOperations(string source, string? baseDirectory = null)
    {
        return ParseVideoProgram(source, baseDirectory).SdkAudioOperations;
    }

    private static GameBoyVideoProgram ParseVideoProgram(string source, string? baseDirectory)
    {
        var parse = new SomeParser().Parse(source);
        if (parse.IsFailure)
        {
            throw new InvalidOperationException(parse.Error);
        }

        ValidateFunctionContracts(parse.Value);
        return GameBoyVideoProgram.FromProgram(parse.Value, baseDirectory);
    }

    private static void ValidateFunctionContracts(ProgramSyntax program)
    {
        var errors = FunctionContractValidator.ValidateProgram(program).ToList();
        if (errors.Count != 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
        }
    }

    private static void ValidateSdkOperations(IEnumerable<Sdk2DOperation> operations)
    {
        foreach (var operation in operations)
        {
            Sdk2DOperationValidator.Validate(GameBoyTarget.Capabilities, operation);
        }
    }

    private static void ValidateSdkAudioOperations(IEnumerable<SdkAudioOperation> operations)
    {
        foreach (var operation in operations)
        {
            SdkAudioOperationValidator.Validate(GameBoyTarget.AudioCapabilities, operation);
        }
    }
}

internal sealed class GameBoyVideoProgram
{
    public const int FirstGeneratedBackgroundTile = 6;

    private readonly List<GameBoyCompiledSpriteAsset> spriteAssetsInLoadOrder = [];
    private readonly Dictionary<string, GameBoyCompiledSpriteAsset> spriteAssets = [];
    private readonly List<GameBoyCompiledMusicAsset> musicAssetsInLoadOrder = [];
    private readonly Dictionary<string, GameBoyCompiledMusicAsset> musicAssets = [];
    private readonly Dictionary<string, SpriteAnimationClip> animationClips = [];
    private readonly List<byte> generatedBackgroundTileData = [];
    private int spriteTileCount;

    private string BaseDirectory { get; init; } = Directory.GetCurrentDirectory();

    public byte BackgroundPalette { get; private set; } = 0xE4;

    public byte ObjectPalette { get; private set; } = 0xE4;

    public byte ObjectPalette1 { get; private set; } = 0xE4;

    public byte[] TileMap { get; } = new byte[1024];

    public byte[] WindowTileMap { get; } = new byte[1024];

    public bool UsesWindowHud { get; private set; }

    public SortedDictionary<int, byte[]> MapColumns { get; } = [];

    public int MapColumnHeight { get; private set; }

    public SortedDictionary<int, byte[]> MapFlagColumns { get; } = [];

    public int MapFlagColumnHeight { get; private set; }

    public SortedDictionary<int, byte[]> BackgroundColumns { get; } = [];

    public int BackgroundStreamHeight { get; private set; }

    public SortedDictionary<int, byte[]> WorldColumns { get; } = [];

    public int WorldColumnHeight { get; private set; }

    public SortedDictionary<int, WorldTileFlags[]> WorldFlagColumns { get; } = [];

    public int WorldFlagColumnHeight { get; private set; }

    public WorldMap2D? WorldMap { get; private set; }

    public IReadOnlyList<GameBoyCompiledSpriteAsset> SpriteAssetsInLoadOrder => spriteAssetsInLoadOrder;

    public IReadOnlyDictionary<string, GameBoyCompiledSpriteAsset> SpriteAssets => spriteAssets;

    public IReadOnlyList<GameBoyCompiledMusicAsset> MusicAssetsInLoadOrder => musicAssetsInLoadOrder;

    public IReadOnlyDictionary<string, GameBoyCompiledMusicAsset> MusicAssets => musicAssets;

    public IReadOnlyDictionary<string, SpriteAnimationClip> AnimationClips => animationClips;

    public int FirstSpriteTile => AlignToEven(FirstGeneratedBackgroundTile + GeneratedBackgroundTileCount);

    public int GeneratedBackgroundTileCount => generatedBackgroundTileData.Count / 16;

    public byte[] GeneratedBackgroundTileData => generatedBackgroundTileData.ToArray();

    public int SpriteTileCount => spriteTileCount;

    public required IReadOnlyDictionary<string, FunctionSyntax> Functions { get; init; }

    public required IReadOnlyDictionary<string, EnumSyntax> Enums { get; init; }

    public required IReadOnlyDictionary<string, StructSyntax> Structs { get; init; }

    public required BlockSyntax MainBlock { get; init; }

    public required IReadOnlyList<Sdk2DOperation> SdkOperations { get; init; }

    public required IReadOnlyList<SdkAudioOperation> SdkAudioOperations { get; init; }

    public static GameBoyVideoProgram FromProgram(ProgramSyntax program, string? baseDirectory = null)
    {
        program = ConstantFolder.Fold(program);

        var main = program.Functions.FirstOrDefault(f => f.Name == "main")
                   ?? throw new InvalidOperationException("Game Boy target requires a main function.");

        var functions = BuildFunctionIndex(program.Functions);
        var enums = BuildEnumIndex(program.Enums);
        var structs = BuildStructIndex(program.Structs);
        var result = new GameBoyVideoProgram
        {
            BaseDirectory = Path.GetFullPath(baseDirectory ?? Directory.GetCurrentDirectory()),
            Functions = functions,
            Enums = enums,
            Structs = structs,
            MainBlock = main.Block,
            SdkOperations = Sdk2DOperationCollector.Collect(main.Block, functions, "Game Boy"),
            SdkAudioOperations = SdkAudioOperationCollector.Collect(main.Block, functions, "Game Boy"),
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
                case "palette_background":
                    ApplyLogicalPalette(call, PaletteKind.Background);
                    break;
                case "palette_sprite":
                    ApplyLogicalPalette(call, PaletteKind.Sprite);
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
                case "world_load":
                    ApplyWorldLoad(call);
                    break;
                case "sprite_asset":
                    ApplySpriteAsset(call);
                    break;
                case "music_asset":
                    ApplyMusicAsset(call);
                    break;
                case "animation_clip":
                    ApplyAnimationClip(call);
                    break;
                case "hud_set_tile":
                    ApplyHudSetTile(call);
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

        if (!callStack.Add(function.Name))
        {
            throw new InvalidOperationException($"Recursive Game Boy user function call '{function.Name}' is not supported.");
        }

        try
        {
            ApplyStaticVideoCalls(ParameterSubstitution.Substitute(function, call, "Game Boy"), callStack);
        }
        finally
        {
            callStack.Remove(function.Name);
        }
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
            throw new InvalidOperationException("Game Boy member access currently requires an identifier base.");
        }

        parts.Push(identifier.Identifier);
        return string.Join(".", parts);
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
        var path = PlatformAssetPathResolver.ResolvePngVariant(ResolveAssetPath(relativePath), "gb");
        var frameWidth = count == 4 ? ConstArg(call, 2, 1, 160) : (int?)null;
        var frameHeight = count == 4 ? ConstArg(call, 3, 1, 160) : (int?)null;
        var asset = GameBoySpriteAssetCompiler.CompileFromFile(name, path, FirstSpriteTile + spriteTileCount, frameWidth, frameHeight);
        spriteTileCount += asset.TileCount;
        spriteAssets.Add(name, asset);
        spriteAssetsInLoadOrder.Add(asset);
    }

    private void ApplyMusicAsset(FunctionCall call)
    {
        RequireArity(call, 2);
        var name = IdentifierArg(call.Parameters.ElementAt(0), "music_asset argument 1");
        if (musicAssets.ContainsKey(name))
        {
            throw new InvalidOperationException($"Music asset '{name}' is already declared.");
        }

        var path = ResolveAssetPath(StringArg(call, 1));
        var asset = GameBoyMusicAssetCompiler.CompileFromFile(name, path);
        musicAssets.Add(name, asset);
        musicAssetsInLoadOrder.Add(asset);
    }

    private void ApplyAnimationClip(FunctionCall call)
    {
        var args = call.Parameters.ToList();
        if (args.Count < 3)
        {
            throw new InvalidOperationException($"animation_clip expects at least 3 arguments, got {args.Count}.");
        }

        var name = IdentifierArg(args[0], "animation_clip argument 1");
        if (animationClips.ContainsKey(name))
        {
            throw new InvalidOperationException($"Animation clip '{name}' is already declared.");
        }

        var firstFrame = CheckedRange(ConstValue(args[1], "animation_clip argument 2"), 0, 255, "animation_clip argument 2");
        var durations = args
            .Skip(2)
            .Select((arg, i) => CheckedRange(ConstValue(arg, $"animation_clip argument {i + 3}"), 1, 255, $"animation_clip argument {i + 3}"))
            .ToArray();
        var clip = new SpriteAnimationClip(name, firstFrame, durations);
        if (clip.FrameIndices[^1] > 255)
        {
            throw new InvalidOperationException($"Animation clip '{name}' frame indices must fit in one byte for the Game Boy target.");
        }

        if (clip.DurationTicks > 255)
        {
            throw new InvalidOperationException($"Animation clip '{name}' total duration must be 255 ticks or less for the Game Boy target.");
        }

        animationClips.Add(name, clip);
    }

    private void ApplyHudSetTile(FunctionCall call)
    {
        RequireArity(call, 4);
        var args = call.Parameters.ToList();
        var mode = HudModeArg(args[0], "hud_set_tile argument 1");

        TargetCapabilityChecks.RequireHudMode(GameBoyTarget.Capabilities, mode);
        if (mode == HudMode.None)
        {
            return;
        }

        if (mode != HudMode.Window)
        {
            throw new InvalidOperationException("Game Boy hud_set_tile currently supports only Window HUD lowering.");
        }

        var x = CheckedRange(ConstValue(args[1], "hud_set_tile argument 2"), 0, 31, "hud_set_tile argument 2");
        var y = CheckedRange(ConstValue(args[2], "hud_set_tile argument 3"), 0, 31, "hud_set_tile argument 3");
        var tile = CheckedRange(ConstValue(args[3], "hud_set_tile argument 4"), 0, 255, "hud_set_tile argument 4");

        WindowTileMap[y * 32 + x] = (byte)tile;
        UsesWindowHud = true;
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

        UseWorldMap(new WorldMap2D(width, height, tileIds, tileFlags), streamY);
    }

    private void ApplyWorldLoad(FunctionCall call)
    {
        RequireArity(call, 1);
        var relativePath = StringArg(call, 0);
        var map = GameBoyTiledMapImporter.Load(ResolveAssetPath(relativePath), FirstGeneratedBackgroundTile + GeneratedBackgroundTileCount);
        AppendGeneratedBackgroundTileData(map.GeneratedTileData);

        ApplyBackgroundTiles(map);
        PopulateBackgroundStreamRows(map);
        var worldMap = new WorldMap2D(map.Width, map.Height, map.WorldTileIds, map.WorldFlags);
        PopulateWorldColumnsFromWorldMap(worldMap);
        UseWorldMap(worldMap, map.StreamY);
    }

    private void UseWorldMap(WorldMap2D worldMap, int streamY)
    {
        WorldMap = worldMap;
        GenerateMapColumnsFromWorldMap(worldMap);
        GenerateMapFlagColumnsFromWorldMap(worldMap);
        ApplyWorldMapToTileMap(worldMap, streamY);
    }

    private void PopulateWorldColumnsFromWorldMap(WorldMap2D worldMap)
    {
        WorldColumns.Clear();
        WorldColumnHeight = worldMap.Height;
        WorldFlagColumns.Clear();
        WorldFlagColumnHeight = worldMap.Height;

        for (var x = 0; x < worldMap.Width; x++)
        {
            var tiles = new byte[worldMap.Height];
            var flags = new WorldTileFlags[worldMap.Height];
            for (var y = 0; y < worldMap.Height; y++)
            {
                tiles[y] = CheckedByteTileId(worldMap.TileIdAt(x, y), x, y);
                flags[y] = worldMap.FlagsAt(x, y);
            }

            WorldColumns[x] = tiles;
            WorldFlagColumns[x] = flags;
        }
    }

    private void ApplyBackgroundTiles(GameBoyTiledMap map)
    {
        if (map.BackgroundTileIds is null)
        {
            return;
        }

        if (map.BackgroundHeight > 32)
        {
            throw new InvalidOperationException("Tiled background layer exceeds the Game Boy background tilemap height.");
        }

        for (var y = 0; y < map.BackgroundHeight; y++)
        {
            var targetY = y - map.BackgroundOffsetY;
            if (targetY is < 0 or >= 32)
            {
                continue;
            }

            for (var x = 0; x < 32; x++)
            {
                SetTile(x, targetY, map.BackgroundTileIds[y * map.BackgroundWidth + x % map.BackgroundWidth]);
            }
        }
    }

    private void PopulateBackgroundStreamRows(GameBoyTiledMap map)
    {
        BackgroundColumns.Clear();
        BackgroundStreamHeight = 0;

        if (map.BackgroundTileIds is null || map.StreamY <= 0)
        {
            return;
        }

        var height = map.StreamY;
        for (var x = 0; x < map.BackgroundWidth; x++)
        {
            var tiles = new byte[height];
            for (var row = 0; row < height; row++)
            {
                var backgroundY = row + map.BackgroundOffsetY;
                tiles[row] = backgroundY >= 0 && backgroundY < map.BackgroundHeight
                    ? map.BackgroundTileIds[backgroundY * map.BackgroundWidth + x]
                    : (byte)0;
            }

            BackgroundColumns[x] = tiles;
        }

        BackgroundStreamHeight = height;
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

    private void AppendGeneratedBackgroundTileData(byte[] tileData)
    {
        if (tileData.Length == 0)
        {
            return;
        }

        if (tileData.Length % 16 != 0)
        {
            throw new InvalidOperationException("Generated Game Boy background tile data must be aligned to 16-byte tiles.");
        }

        generatedBackgroundTileData.AddRange(tileData);
        RebaseSpriteTiles();
    }

    private void RebaseSpriteTiles()
    {
        var nextTile = FirstSpriteTile;
        for (var i = 0; i < spriteAssetsInLoadOrder.Count; i++)
        {
            var asset = spriteAssetsInLoadOrder[i].WithFirstTile(nextTile);
            spriteAssetsInLoadOrder[i] = asset;
            spriteAssets[asset.Name] = asset;
            nextTile += asset.TileCount;
        }

        if (nextTile > 256)
        {
            throw new InvalidOperationException("Generated Game Boy tiles exceed the 256 tile VRAM index range.");
        }

        spriteTileCount = nextTile - FirstSpriteTile;
    }

    private static int AlignToEven(int value)
    {
        return (value & 1) == 0 ? value : value + 1;
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
        SetObjectPaletteColor(0, index, color);
    }

    private void SetObjectPaletteColor(int slot, int index, int color)
    {
        var shift = index * 2;
        if (slot == 0)
        {
            ObjectPalette = (byte)((ObjectPalette & ~(0x03 << shift)) | ((color & 0x03) << shift));
            return;
        }

        ObjectPalette1 = (byte)((ObjectPalette1 & ~(0x03 << shift)) | ((color & 0x03) << shift));
    }

    private void ApplyLogicalPalette(FunctionCall call, PaletteKind kind)
    {
        RequireArity(call, 5);
        var slot = ConstArg(call, 0, 0, 255);
        var colors = call.Parameters.Skip(1)
            .Select((_, index) => ConstArg(call, index + 1, 0, 3))
            .ToArray();

        SdkPaletteValidator.Validate(GameBoyTarget.Capabilities, kind, slot, colors.Length);

        for (var i = 0; i < colors.Length; i++)
        {
            if (kind == PaletteKind.Background)
            {
                SetPaletteColor(i, colors[i]);
                continue;
            }

            SetObjectPaletteColor(slot, i, colors[i]);
        }
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
