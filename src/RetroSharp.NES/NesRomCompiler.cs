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
    public static byte[] CompileSource(
        string source,
        string? baseDirectory = null,
        SdkLibraryImportMode sdkImportMode = SdkLibraryImportMode.LegacyAutoImport,
        SdkLibraryRegistry? sdkLibraryRegistry = null,
        IReadOnlyList<string>? sdkLibraryImports = null)
    {
        var parse = new SomeParser().Parse(SdkLibrarySource.Merge(NesTarget.Intrinsics, source, sdkImportMode, sdkLibraryRegistry, sdkLibraryImports));
        if (parse.IsFailure)
        {
            throw new InvalidOperationException(parse.Error);
        }

        var targetProgram = TargetProgramSelector.Select(parse.Value, NesTarget.Intrinsics);
        SdkImportResolver.ValidateImports(targetProgram, sdkLibraryRegistry);
        SdkImportResolver.ValidateSdkUsage(targetProgram, sdkImportMode, sdkLibraryImports);
        var actorProgram = ActorFrameworkLowerer.Lower(targetProgram, NesTarget.Capabilities, supportsUpdate: true, supportsDraw: true, baseDirectory);
        var loweredProgram = SdkSourcePackageFacadeLowerer.Lower(actorProgram);
        ValidateFunctionContracts(loweredProgram);
        var videoProgram = NesVideoProgram.FromProgram(loweredProgram, baseDirectory);
        ActorFrameworkLowerer.ValidatePoolSpriteBudgets(
            targetProgram,
            NesTarget.Capabilities,
            spriteId => ActorMetaspriteGeometry(videoProgram, spriteId),
            baseDirectory);
        var sdkOperations = ValidateSdkOperations(videoProgram);
        var useFourScreenNametables = UsesVerticalCamera(sdkOperations);
        return NesRomBuilder.Build(videoProgram, useFourScreenNametables);
    }

    public static IReadOnlyList<Sdk2DOperation> CollectSdkOperations(
        string source,
        string? baseDirectory = null,
        SdkLibraryImportMode sdkImportMode = SdkLibraryImportMode.LegacyAutoImport,
        SdkLibraryRegistry? sdkLibraryRegistry = null,
        IReadOnlyList<string>? sdkLibraryImports = null)
    {
        var parse = new SomeParser().Parse(SdkLibrarySource.Merge(NesTarget.Intrinsics, source, sdkImportMode, sdkLibraryRegistry, sdkLibraryImports));
        if (parse.IsFailure)
        {
            throw new InvalidOperationException(parse.Error);
        }

        var targetProgram = TargetProgramSelector.Select(parse.Value, NesTarget.Intrinsics);
        SdkImportResolver.ValidateImports(targetProgram, sdkLibraryRegistry);
        SdkImportResolver.ValidateSdkUsage(targetProgram, sdkImportMode, sdkLibraryImports);
        var actorProgram = ActorFrameworkLowerer.Lower(targetProgram, NesTarget.Capabilities, supportsUpdate: true, supportsDraw: true, baseDirectory);
        var loweredProgram = SdkSourcePackageFacadeLowerer.Lower(actorProgram);
        ValidateFunctionContracts(loweredProgram);
        var videoProgram = NesVideoProgram.FromProgram(loweredProgram, baseDirectory);
        return Sdk2DOperationCollector.Collect(
            videoProgram.MainBlock,
            videoProgram.Functions,
            "NES",
            NesTarget.Capabilities,
            NesTarget.Intrinsics);
    }

    public static IReadOnlyList<SdkAudioOperation> CollectSdkAudioOperations(
        string source,
        string? baseDirectory = null,
        SdkLibraryImportMode sdkImportMode = SdkLibraryImportMode.LegacyAutoImport,
        SdkLibraryRegistry? sdkLibraryRegistry = null,
        IReadOnlyList<string>? sdkLibraryImports = null)
    {
        var parse = new SomeParser().Parse(SdkLibrarySource.Merge(NesTarget.Intrinsics, source, sdkImportMode, sdkLibraryRegistry, sdkLibraryImports));
        if (parse.IsFailure)
        {
            throw new InvalidOperationException(parse.Error);
        }

        var targetProgram = TargetProgramSelector.Select(parse.Value, NesTarget.Intrinsics);
        SdkImportResolver.ValidateImports(targetProgram, sdkLibraryRegistry);
        SdkImportResolver.ValidateSdkUsage(targetProgram, sdkImportMode, sdkLibraryImports);
        var actorProgram = ActorFrameworkLowerer.Lower(targetProgram, NesTarget.Capabilities, supportsUpdate: true, supportsDraw: true, baseDirectory);
        var loweredProgram = SdkSourcePackageFacadeLowerer.Lower(actorProgram);
        ValidateFunctionContracts(loweredProgram);
        var videoProgram = NesVideoProgram.FromProgram(loweredProgram, baseDirectory);
        return SdkAudioOperationCollector.Collect(videoProgram.MainBlock, videoProgram.Functions, "NES", NesTarget.Intrinsics);
    }

    private static IReadOnlyList<Sdk2DOperation> ValidateSdkOperations(NesVideoProgram videoProgram)
    {
        var operations = Sdk2DOperationCollector.Collect(
            videoProgram.MainBlock,
            videoProgram.Functions,
            "NES",
            NesTarget.Capabilities,
            NesTarget.Intrinsics);
        foreach (var operation in operations)
        {
            Sdk2DOperationValidator.Validate(NesTarget.Capabilities, operation);
        }

        var frameBudgets = Sdk2DOperationCollector.CollectFrameBudgets(
            videoProgram.MainBlock,
            videoProgram.Functions,
            "NES",
            draw => DrawSpriteBudget(videoProgram, draw),
            NesTarget.Intrinsics);
        foreach (var budget in frameBudgets)
        {
            Sdk2DOperationValidator.ValidateFrameBudget(NesTarget.Capabilities, budget);
        }

        var audioOperations = SdkAudioOperationCollector.Collect(videoProgram.MainBlock, videoProgram.Functions, "NES", NesTarget.Intrinsics);
        foreach (var operation in audioOperations)
        {
            SdkAudioOperationValidator.Validate(NesTarget.AudioCapabilities, operation);
        }

        return operations;
    }

    private static bool UsesVerticalCamera(IEnumerable<Sdk2DOperation> operations)
    {
        return operations
            .OfType<Sdk2DOperation.SetCameraPosition>()
            .Any(operation => (operation.Axes & ScrollAxes.Vertical) != 0);
    }

    private static Sdk2DFrameBudget DrawSpriteBudget(NesVideoProgram videoProgram, Sdk2DOperation.DrawLogicalSprite draw)
    {
        if (!videoProgram.SpriteAssets.TryGetValue(draw.SpriteId, out var asset))
        {
            throw new InvalidOperationException($"Unknown NES sprite asset '{draw.SpriteId}'. Declare it with sprite_asset(...).");
        }

        return new Sdk2DFrameBudget(
            hardwareSprites: asset.Pieces.Count,
            spriteSizeModes: SpriteSizeMode.Sprite8x8,
            hardwareSpritesByScanline: HardwareSpriteScanlineCounts(
                draw.Y,
                asset.Pieces.Select(piece => piece.YOffset),
                spriteHeight: 8,
                screenHeight: NesTarget.Capabilities.ScreenPixels.Height));
    }

    private static ActorMetaspriteGeometry ActorMetaspriteGeometry(NesVideoProgram videoProgram, string spriteId)
    {
        if (!videoProgram.SpriteAssets.TryGetValue(spriteId, out var asset))
        {
            throw new InvalidOperationException($"Unknown NES sprite asset '{spriteId}'. Declare it with sprite_asset(...).");
        }

        return new ActorMetaspriteGeometry(
            asset.Pieces.Count,
            asset.Pieces.Select(piece => piece.YOffset).ToArray(),
            HardwareSpriteHeight: 8);
    }

    private static IReadOnlyDictionary<int, int> HardwareSpriteScanlineCounts(
        SdkByteExpression y,
        IEnumerable<int> pieceOffsets,
        int spriteHeight,
        int screenHeight)
    {
        var offsets = pieceOffsets.ToList();
        if (y is SdkByteExpression.Variable { Location: SdkStorageLocation.RuntimeIndexedField })
        {
            return WorstCaseDynamicScanlineCounts(offsets);
        }

        if (y is not SdkByteExpression.Constant constant)
        {
            return new Dictionary<int, int>();
        }

        var result = new Dictionary<int, int>();
        foreach (var offset in offsets)
        {
            var top = constant.Value + offset;
            var bottom = top + spriteHeight;
            for (var scanline = Math.Max(0, top); scanline < Math.Min(screenHeight, bottom); scanline++)
            {
                result[scanline] = result.GetValueOrDefault(scanline) + 1;
            }
        }

        return result;
    }

    private static IReadOnlyDictionary<int, int> WorstCaseDynamicScanlineCounts(IEnumerable<int> pieceOffsets)
    {
        var result = new Dictionary<int, int>();
        foreach (var offset in pieceOffsets)
        {
            result[offset] = result.GetValueOrDefault(offset) + 1;
        }

        return result;
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
    private readonly List<NesCompiledMusicAsset> musicAssetsInLoadOrder = [];
    private readonly Dictionary<string, NesCompiledMusicAsset> musicAssets = [];
    private readonly List<(int FirstTile, byte[] Data)> generatedBackgroundTiles = [];
    private readonly Dictionary<string, SpriteAnimationClip> animationClips = [];
    private readonly SortedDictionary<int, byte[]> mapColumns = [];
    private readonly SortedDictionary<int, byte[]> worldColumns = [];
    private readonly SortedDictionary<int, WorldTileFlags[]> worldFlagColumns = [];
    private readonly HashSet<int> rawPaletteIndexes = [];
    private int nextSpriteTile = FirstSpriteTile;

    private string BaseDirectory { get; init; } = Directory.GetCurrentDirectory();

    private bool UseFourScreenNametables { get; init; }

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

    public byte[] NameTable { get; } = new byte[4096];

    public int MapColumnHeight { get; private set; }

    public int WorldColumnHeight { get; private set; }

    public int WorldFlagColumnHeight { get; private set; }

    public WorldMap2D? WorldMap { get; private set; }

    public WorldTileGrid? WorldTileGrid { get; private set; }

    public IReadOnlyList<NesCompiledSpriteAsset> SpriteAssetsInLoadOrder => spriteAssetsInLoadOrder;

    public IReadOnlyDictionary<string, NesCompiledSpriteAsset> SpriteAssets => spriteAssets;

    public IReadOnlyList<NesCompiledMusicAsset> MusicAssetsInLoadOrder => musicAssetsInLoadOrder;

    public IReadOnlyDictionary<string, NesCompiledMusicAsset> MusicAssets => musicAssets;

    public IReadOnlyList<(int FirstTile, byte[] Data)> GeneratedBackgroundTiles => generatedBackgroundTiles;

    public IReadOnlyDictionary<string, SpriteAnimationClip> AnimationClips => animationClips;

    public required IReadOnlyDictionary<string, FunctionSyntax> Functions { get; init; }

    public required IReadOnlyDictionary<string, EnumSyntax> Enums { get; init; }

    public required IReadOnlyDictionary<string, StructSyntax> Structs { get; init; }

    public required BlockSyntax MainBlock { get; init; }

    public static NesVideoProgram FromProgram(ProgramSyntax program, string? baseDirectory = null)
    {
        program = ConstantFolder.Fold(program);

        var main = program.Functions.FirstOrDefault(f => f.Name == "Main")
                   ?? throw new InvalidOperationException("NES target requires a Main function.");

        var functions = BuildFunctionIndex(program.Functions);
        var enums = BuildEnumIndex(program.Enums);
        var structs = BuildStructIndex(program.Structs);
        var result = new NesVideoProgram
        {
            BaseDirectory = Path.GetFullPath(baseDirectory ?? Directory.GetCurrentDirectory()),
            UseFourScreenNametables = UsesVerticalCamera(main.Block, functions),
            Functions = functions,
            Enums = enums,
            Structs = structs,
            MainBlock = main.Block,
        };

        result.ApplyStaticVideoCalls(main.Block, []);
        result.ApplyDerivedSpritePalettes();
        return result;
    }

    private static bool UsesVerticalCamera(BlockSyntax mainBlock, IReadOnlyDictionary<string, FunctionSyntax> functions)
    {
        var operations = Sdk2DOperationCollector.Collect(
            mainBlock,
            functions,
            "NES",
            NesTarget.Capabilities,
            NesTarget.Intrinsics);
        return operations
            .OfType<Sdk2DOperation.SetCameraPosition>()
            .Any(operation => (operation.Axes & ScrollAxes.Vertical) != 0);
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
        if (TryApplySdkResourceDeclaration(call))
        {
            return;
        }

        switch (call.Name)
        {
            case "video_init":
            case "video_present":
            case "video_wait_vblank":
                RequireArity(call, 0);
                break;
            case "palette_set":
                RequireArity(call, 2);
                var index = ConstArg(call, 0, 0, 31);
                Palette[index] = (byte)ConstArg(call, 1, 0, 63);
                rawPaletteIndexes.Add(index);
                break;
            case "palette_background":
                ApplyLogicalPalette(call, PaletteKind.Background);
                break;
            case "palette_sprite":
                ApplyLogicalPalette(call, PaletteKind.Sprite);
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
            case "world_load":
                ApplyWorldLoad(call);
                break;
            case "sprite_asset":
                ApplySpriteAsset(call);
                break;
            case "animation_clip":
                ApplyAnimationClip(call);
                break;
            case "music_asset":
                ApplyMusicAsset(call);
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

    private bool TryApplySdkResourceDeclaration(FunctionCall call)
    {
        if (!Functions.TryGetValue(call.Name, out var function) ||
            !SdkResourceDeclarationResolver.TryResolve(function, out var descriptor))
        {
            return false;
        }

        ApplySdkResourceDeclaration(call, descriptor);
        return true;
    }

    private void ApplySdkResourceDeclaration(FunctionCall call, SdkResourceDeclarationDescriptor descriptor)
    {
        switch (descriptor.Kind)
        {
            case SdkResourceDeclarationKind.BackgroundPalette:
                ApplyLogicalPalette(call, PaletteKind.Background);
                break;
            case SdkResourceDeclarationKind.SpritePalette:
                ApplyLogicalPalette(call, PaletteKind.Sprite);
                break;
            case SdkResourceDeclarationKind.WorldLoad:
                ApplyWorldLoad(call);
                break;
            case SdkResourceDeclarationKind.SpriteAsset:
                ApplySpriteAsset(call);
                break;
            case SdkResourceDeclarationKind.MusicAsset:
                ApplyMusicAsset(call);
                break;
            case SdkResourceDeclarationKind.AnimationClip:
                ApplyAnimationClip(call);
                break;
            default:
                throw new InvalidOperationException($"Unsupported SDK resource declaration '{descriptor.ResourceId}'.");
        }
    }

    private void ApplyLogicalPalette(FunctionCall call, PaletteKind kind)
    {
        RequireArity(call, 5);
        var slot = ConstArg(call, 0, 0, 255);
        const int colorCount = 4;
        SdkPaletteValidator.Validate(NesTarget.Capabilities, kind, slot, colorCount);

        var colors = call.Parameters.Skip(1)
            .Select((_, index) => LogicalPaletteToneToNesGray(ConstArg(call, index + 1, 0, 3)))
            .ToArray();

        var baseIndex = kind == PaletteKind.Background
            ? slot * 4
            : 16 + (slot * 4);
        for (var i = 0; i < colors.Length; i++)
        {
            Palette[baseIndex + i] = (byte)colors[i];
        }
    }

    private void ApplyDerivedSpritePalettes()
    {
        var appliedPalettes = new Dictionary<int, byte[]>();
        var operations = Sdk2DOperationCollector.Collect(MainBlock, Functions, "NES", NesTarget.Capabilities, NesTarget.Intrinsics);
        foreach (var operation in operations.OfType<Sdk2DOperation.DrawLogicalSprite>())
        {
            if (!spriteAssets.TryGetValue(operation.SpriteId, out var asset)
                || asset.SuggestedPalette is null
                || operation.PaletteSlot < 0
                || operation.PaletteSlot >= NesTarget.Capabilities.SpritePaletteSlots
                || SpritePaletteSlotHasRawOverrides(operation.PaletteSlot))
            {
                continue;
            }

            if (appliedPalettes.TryGetValue(operation.PaletteSlot, out var existingPalette)
                && !existingPalette.SequenceEqual(asset.SuggestedPalette))
            {
                throw new InvalidOperationException(
                    $"NES sprite palette slot {operation.PaletteSlot} is used by multiple PNG sprite assets with different derived palettes. Use Palette.Set(...) or draw them with different palette slots.");
            }

            ApplySpritePalette(operation.PaletteSlot, asset.SuggestedPalette);
            appliedPalettes[operation.PaletteSlot] = asset.SuggestedPalette;
        }
    }

    private bool SpritePaletteSlotHasRawOverrides(int slot)
    {
        var baseIndex = 16 + (slot * 4);
        return Enumerable.Range(baseIndex, 4).Any(rawPaletteIndexes.Contains);
    }

    private void ApplySpritePalette(int slot, IReadOnlyList<byte> colors)
    {
        var baseIndex = 16 + (slot * 4);
        Palette[baseIndex] = Palette[0];
        for (var i = 1; i < colors.Count; i++)
        {
            Palette[baseIndex + i] = colors[i];
        }
    }

    private static int LogicalPaletteToneToNesGray(int tone)
    {
        return tone switch
        {
            0 => 0x30,
            1 => 0x10,
            2 => 0x00,
            3 => 0x0F,
            _ => throw new InvalidOperationException("NES logical palette tones must be between 0 and 3."),
        };
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

    private void ApplyWorldLoad(FunctionCall call)
    {
        RequireArity(call, 1);
        var path = ResolveAssetPath(StringArg(call, 0));
        var world = NesTiledWorldImporter.Load(path, nextSpriteTile);

        var generatedCount = world.GeneratedTileData.Length / 16;
        if (nextSpriteTile + generatedCount > 256)
        {
            throw new InvalidOperationException("NES generated background tiles exceed the available CHR pattern table.");
        }

        if (generatedCount > 0)
        {
            generatedBackgroundTiles.Add((nextSpriteTile, world.GeneratedTileData));
            nextSpriteTile += generatedCount;
        }

        ApplyBackgroundPalettes(world.BackgroundPalette);
        ApplyBackgroundTiles(world);

        if (UseFourScreenNametables)
        {
            var seededRows = 60 - world.StreamY;
            for (var y = 0; y < seededRows; y++)
            {
                var targetY = world.StreamY + y;
                var sourceY = world.Height > y ? y : y % world.Height;

                for (var x = 0; x < 64; x++)
                {
                    var sourceX = x % world.Width;
                    SetTile(x, targetY, world.WorldTileIds[sourceY * world.Width + sourceX]);
                }
            }
        }
        else
        {
            for (var y = 0; y < world.Height; y++)
            {
                var targetY = world.StreamY + y;
                if (targetY >= 60)
                {
                    continue;
                }

                for (var x = 0; x < Math.Min(world.Width, 64); x++)
                {
                    SetTile(x, targetY, world.WorldTileIds[y * world.Width + x]);
                }
            }
        }

        ApplyWorldAttributes(world);

        WorldMap = new WorldMap2D(world.Width, world.Height, world.WorldFlags);
        WorldTileGrid = new WorldTileGrid(world.Width, world.Height, world.WorldTileIds);
    }

    private void ApplyBackgroundTiles(NesTiledWorld world)
    {
        if (world.BackgroundTileIds is null)
        {
            return;
        }

        for (var y = 0; y < world.BackgroundHeight; y++)
        {
            var targetY = y - world.BackgroundOffsetY;
            if (targetY is < 0 or >= 30)
            {
                continue;
            }

            for (var x = 0; x < 64; x++)
            {
                SetTile(x, targetY, world.BackgroundTileIds[y * world.BackgroundWidth + x % world.BackgroundWidth]);
            }
        }
    }

    private void ApplyBackgroundPalettes(IReadOnlyList<byte> colors)
    {
        if (colors.Count != 16)
        {
            throw new InvalidOperationException("NES generated background palette derivation must produce four background palettes.");
        }

        for (var slot = 0; slot < 4; slot++)
        {
            if (!BackgroundPaletteSlotHasRawOverrides(slot))
            {
                ApplyBackgroundPalette(slot, colors.Skip(slot * 4).Take(4).ToArray());
            }
        }
    }

    private bool BackgroundPaletteSlotHasRawOverrides(int slot)
    {
        var baseIndex = slot * 4;
        return Enumerable.Range(baseIndex, 4).Any(rawPaletteIndexes.Contains);
    }

    private void ApplyBackgroundPalette(int slot, IReadOnlyList<byte> colors)
    {
        var baseIndex = slot * 4;
        for (var i = 0; i < colors.Count; i++)
        {
            Palette[baseIndex + i] = colors[i];
        }
    }

    private void ApplyWorldAttributes(NesTiledWorld world)
    {
        for (var nameTableY = 0; nameTableY < 2; nameTableY++)
        {
            for (var nameTableX = 0; nameTableX < 2; nameTableX++)
            {
                var nameTable = nameTableY * 2 + nameTableX;
                for (var attributeY = 0; attributeY < 8; attributeY++)
                {
                    for (var attributeX = 0; attributeX < 8; attributeX++)
                    {
                        var attributeByte = 0;
                        for (var quadrantY = 0; quadrantY < 2; quadrantY++)
                        {
                            for (var quadrantX = 0; quadrantX < 2; quadrantX++)
                            {
                                var baseX = nameTableX * 32 + attributeX * 4 + quadrantX * 2;
                                var baseY = nameTableY * 30 + attributeY * 4 + quadrantY * 2;
                                var slot = MostCommonPaletteSlot(world, baseX, baseY, UseFourScreenNametables);
                                var shift = (quadrantY * 2 + quadrantX) * 2;
                                attributeByte |= (slot & 0x03) << shift;
                            }
                        }

                        NameTable[nameTable * 1024 + 960 + attributeY * 8 + attributeX] = (byte)attributeByte;
                    }
                }
            }
        }
    }

    private static int MostCommonPaletteSlot(NesTiledWorld world, int baseX, int baseY, bool useFourScreenNametables)
    {
        Span<int> counts = stackalloc int[4];
        Span<int> worldCounts = stackalloc int[4];
        Span<int> upperWorldCounts = stackalloc int[4];
        Span<int> lowerRowCounts = stackalloc int[4];
        for (var y = baseY; y < baseY + 2; y++)
        {
            for (var x = baseX; x < baseX + 2; x++)
            {
                var cell = PaletteCellAtScreenTile(world, x, y, useFourScreenNametables);
                var slot = cell.Slot;
                counts[slot]++;
                if (cell.IsWorldLayer)
                {
                    worldCounts[slot]++;
                    if (y == baseY)
                    {
                        upperWorldCounts[slot]++;
                    }
                }
                if (y == baseY + 1)
                {
                    lowerRowCounts[slot]++;
                }
            }
        }

        var bestSlot = 0;
        for (var slot = 1; slot < counts.Length; slot++)
        {
            if (IsBetterAttributePaletteSlot(slot, bestSlot, counts, worldCounts, upperWorldCounts, lowerRowCounts))
            {
                bestSlot = slot;
            }
        }

        return bestSlot;
    }

    private static bool IsBetterAttributePaletteSlot(
        int candidate,
        int incumbent,
        ReadOnlySpan<int> counts,
        ReadOnlySpan<int> worldCounts,
        ReadOnlySpan<int> upperWorldCounts,
        ReadOnlySpan<int> lowerRowCounts)
    {
        if (counts[candidate] != counts[incumbent])
        {
            return counts[candidate] > counts[incumbent];
        }

        if (worldCounts[candidate] != worldCounts[incumbent])
        {
            return worldCounts[candidate] > worldCounts[incumbent];
        }

        if (upperWorldCounts[candidate] != upperWorldCounts[incumbent])
        {
            return upperWorldCounts[candidate] > upperWorldCounts[incumbent];
        }

        return lowerRowCounts[candidate] > lowerRowCounts[incumbent];
    }

    private static PaletteCell PaletteCellAtScreenTile(NesTiledWorld world, int x, int y, bool useFourScreenNametables)
    {
        var worldY = y - world.StreamY;
        if (useFourScreenNametables)
        {
            var seededRows = 60 - world.StreamY;
            if (worldY >= 0 && worldY < seededRows && x >= 0 && x < 64 && world.Width > 0)
            {
                var sourceY = world.Height > worldY ? worldY : worldY % world.Height;
                var sourceX = x % world.Width;
                var index = sourceY * world.Width + sourceX;
                return new PaletteCell(world.WorldPaletteSlots[index], world.WorldSourceTiles[index] != 0);
            }
        }
        else if (worldY >= 0 && worldY < world.Height && x >= 0 && x < world.Width)
        {
            var index = worldY * world.Width + x;
            return new PaletteCell(world.WorldPaletteSlots[index], world.WorldSourceTiles[index] != 0);
        }

        if (world.BackgroundPaletteSlots is null || x < 0 || world.BackgroundWidth <= 0)
        {
            return new PaletteCell(0, false);
        }

        var backgroundY = y + world.BackgroundOffsetY;
        if (backgroundY < 0 || backgroundY >= world.BackgroundHeight)
        {
            return new PaletteCell(0, false);
        }

        return new PaletteCell(world.BackgroundPaletteSlots[backgroundY * world.BackgroundWidth + x % world.BackgroundWidth], false);
    }

    private readonly record struct PaletteCell(int Slot, bool IsWorldLayer);

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
        var streamY = ConstArg(call, 1, 0, 29);
        var height = ConstArg(call, 2, 1, sourceHeight);
        if (WorldFlagColumnHeight is not 0 && WorldFlagColumnHeight < height)
        {
            throw new InvalidOperationException("world_map height must not exceed the declared world_flags height.");
        }

        var tileIds = new int[width * height];
        var tileFlags = new WorldTileFlags[width * height];
        var seededRows = 60 - streamY;
        for (var x = 0; x < width; x++)
        {
            sourceColumns.TryGetValue(x, out var column);
            worldFlagColumns.TryGetValue(x, out var flagColumn);
            for (var y = 0; y < height; y++)
            {
                var tile = column is null ? (byte)0 : column[y];
                tileIds[y * width + x] = tile;
                tileFlags[y * width + x] = flagColumn is null ? WorldTileFlags.Empty : flagColumn[y];
                if (x < 64 && streamY + y < 60)
                {
                    SetTile(x, streamY + y, tile);
                }
            }
        }

        if (UseFourScreenNametables)
        {
            for (var x = width; x < 64; x++)
            {
                var sourceX = x % width;
                sourceColumns.TryGetValue(sourceX, out var column);
                for (var y = 0; y < seededRows; y++)
                {
                    var sourceY = height > y ? y : y % height;
                    var tile = column is null ? (byte)0 : column[sourceY];
                    SetTile(x, streamY + y, tile);
                }
            }

            for (var y = height; y < seededRows; y++)
            {
                var sourceY = y % height;
                for (var x = 0; x < Math.Min(width, 64); x++)
                {
                    sourceColumns.TryGetValue(x, out var column);
                    var tile = column is null ? (byte)0 : column[sourceY];
                    SetTile(x, streamY + y, tile);
                }
            }
        }

        WorldMap = new WorldMap2D(width, height, tileFlags);
        WorldTileGrid = new WorldTileGrid(width, height, tileIds);
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
        if (count is not 2 and not 4)
        {
            throw new InvalidOperationException($"NES sprite_asset expects 2 or 4 arguments, got {count}.");
        }

        var name = IdentifierArg(call.Parameters.ElementAt(0), "sprite_asset argument 1");
        if (spriteAssets.ContainsKey(name))
        {
            throw new InvalidOperationException($"Sprite asset '{name}' is already declared.");
        }

        var path = PlatformAssetPathResolver.ResolvePngVariant(ResolveAssetPath(StringArg(call, 1)), "nes");
        var frameWidth = count == 4 ? ConstArg(call, 2, 1, 256) : (int?)null;
        var frameHeight = count == 4 ? ConstArg(call, 3, 1, 240) : (int?)null;
        var asset = NesSpriteAssetCompiler.CompileFromFile(name, path, nextSpriteTile, frameWidth, frameHeight);
        nextSpriteTile += asset.TileCount;
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

        var path = PlatformAssetPathResolver.ResolveVariant(ResolveAssetPath(StringArg(call, 1)), "nes");
        var asset = NesMusicAssetCompiler.CompileFromFile(name, path);
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
            throw new InvalidOperationException($"Animation clip '{name}' frame indices must fit in one byte for the NES target.");
        }

        if (clip.DurationTicks > 255)
        {
            throw new InvalidOperationException($"Animation clip '{name}' total duration must be 255 ticks or less for the NES target.");
        }

        animationClips.Add(name, clip);
    }

    private void SetTile(int x, int y, int tile)
    {
        NameTable[NameTableTileOffset(x, y)] = (byte)tile;
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

    private static int NameTableTileOffset(int x, int y)
    {
        if (x is < 0 or > 63)
        {
            throw new InvalidOperationException("NES nametable tile X must be between 0 and 63.");
        }

        if (y is < 0 or > 59)
        {
            throw new InvalidOperationException("NES nametable tile Y must be between 0 and 59.");
        }

        var nameTableX = x / 32;
        var nameTableY = y / 30;
        var nameTableBase = (nameTableY * 2 + nameTableX) * 1024;
        return nameTableBase + (y % 30 * 32) + (x % 32);
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

        if (current is IndexExpressionSyntax indexExpression)
        {
            var index = CheckedRange(ConstValue(indexExpression.Index, $"{indexExpression.BaseIdentifier} array index"), 0, 255, $"{indexExpression.BaseIdentifier} array index");
            parts.Push($"{indexExpression.BaseIdentifier}[{index}]");
            return string.Join(".", parts);
        }

        if (current is not IdentifierSyntax identifier)
        {
            throw new InvalidOperationException("NES member access currently requires an identifier or constant indexed base.");
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
