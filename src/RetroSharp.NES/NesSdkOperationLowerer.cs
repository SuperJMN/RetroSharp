namespace RetroSharp.NES;

using RetroSharp.Core.Sdk;
using RetroSharp.Core.Targeting;
using RetroSharp.Parser;

// Production and focused tests cross this same small operation seam. Feature
// partials own NES emission; the syntax compiler supplies only target operand
// and storage primitives that raw compatibility paths also require.
internal sealed partial class NesSdkOperationLowerer
{
    private const int BottomOverscanInsetPixels = 8;
    private const string PackedCollisionAtScratchSubroutineLabel = "nes_packed_collision_at_scratch";
    private const string PackedCollisionFlagsSubroutineLabel = "nes_packed_collision_flags";
    private const string PackedWideSourceColumnSubroutineLabel = "nes_packed_wide_source_column";
    private const string PackedColumnRequestSubroutineLabel = "nes_packed_column_request";
    private const string PackedColumnPrefetchSubroutineLabel = "nes_packed_column_prefetch";
    private const string PackedRowRequestSubroutineLabel = "nes_packed_row_request";
    private const string CameraIncrementTileSubroutineLabel = "nes_camera_increment_tile";
    private const string CameraDecrementTileSubroutineLabel = "nes_camera_decrement_tile";
    private const string PublishVisibleCameraXSubroutineLabel = "nes_publish_visible_camera_x";
    private const string PublishVisibleCameraYSubroutineLabel = "nes_publish_visible_camera_y";
    // Every shared SDK operation body carries this prefix so the build report can describe
    // deduplication without knowing which operations participate.
    internal const string SharedSubroutineLabelPrefix = "nes_sdk_shared_";
    private const string SharedDrawLogicalSpriteSubroutinePrefix = SharedSubroutineLabelPrefix + "draw_logical_sprite";
    private const string SharedCameraAabbSubroutinePrefix = SharedSubroutineLabelPrefix + "camera_aabb";

    private readonly PrgBuilder builder;
    private readonly NesVideoProgram program;
    private readonly NesSdkLoweringContext context;
    private readonly NesPhysicalFrameScheduler frameScheduler;
    private readonly bool useFourScreenNametables;
    private readonly bool usePackedCamera;
    private readonly IReadOnlyDictionary<SharedDrawLogicalSpriteShape, string> sharedDrawLogicalSpriteSubroutines;
    private readonly HashSet<SharedDrawLogicalSpriteShape> referencedSharedDrawLogicalSpriteSubroutines = [];
    private readonly bool shareRepeatedSdkOperations;
    private readonly Dictionary<SharedCameraAabbShape, SharedCameraAabbBody> referencedSharedCameraAabbSubroutines = [];
    private IReadOnlyDictionary<SharedCameraAabbShape, string>? sharedCameraAabbSubroutines;
    private bool emittingSharedCameraAabbBody;
    private int nextHardwareSprite;
    private bool packedCollisionAtScratchSubroutineReferenced;
    private bool packedCollisionFlagsSubroutineReferenced;
    private bool packedWideSourceColumnSubroutineReferenced;
    private bool packedColumnRequestSubroutineReferenced;
    private bool packedColumnPrefetchSubroutineReferenced;
    private bool packedRowRequestSubroutineReferenced;
    private bool cameraIncrementTileSubroutineReferenced;
    private bool cameraDecrementTileSubroutineReferenced;
    private bool publishVisibleCameraXSubroutineReferenced;
    private bool publishVisibleCameraYSubroutineReferenced;
    private int packedWideSourceColumnMapWidth;
    private int cameraTileMapWidth;
    private NesCameraConfig? packedColumnRuntimeConfig;
    private NesCameraConfig? cameraConfig;
    private RuntimeIndexedFieldCursor? activeRuntimeIndexedFieldCursor;


    public NesSdkOperationLowerer(
        PrgBuilder builder,
        NesVideoProgram program,
        NesSdkLoweringContext context,
        bool useFourScreenNametables,
        bool usePackedCamera,
        bool useSequentialOamPublication,
        bool shareRepeatedSdkOperations = true)
        : this(
            builder,
            program,
            context,
            NesPhysicalFrameScheduler.Create(
                builder,
                program,
                useFourScreenNametables,
                usePackedCamera,
                useSequentialOamPublication),
            shareRepeatedSdkOperations)
    {
    }

    internal NesSdkOperationLowerer(
        PrgBuilder builder,
        NesVideoProgram program,
        NesSdkLoweringContext context,
        NesPhysicalFrameScheduler frameScheduler,
        bool shareRepeatedSdkOperations = true)
    {
        ArgumentNullException.ThrowIfNull(frameScheduler);
        this.builder = builder;
        this.program = program;
        this.context = context;
        this.frameScheduler = frameScheduler;
        useFourScreenNametables = frameScheduler.UseFourScreenNametables;
        usePackedCamera = frameScheduler.UsesPackedCameraRuntime;
        this.shareRepeatedSdkOperations = shareRepeatedSdkOperations;
        sharedDrawLogicalSpriteSubroutines = shareRepeatedSdkOperations
            ? CreateSharedDrawLogicalSpriteSubroutines()
            : new Dictionary<SharedDrawLogicalSpriteShape, string>();
    }

    public void Emit(Sdk2DOperation operation)
    {
        switch (operation)
        {
            case Sdk2DOperation.WaitFrame:
                frameScheduler.EmitFrameBoundary(NesFrameBoundaryPurpose.Gameplay, this, cameraConfig);
                break;
            case Sdk2DOperation.PollInput:
                EmitPollInput();
                break;
            case Sdk2DOperation.SetCameraPosition camera:
                EmitSetCameraPosition(camera);
                break;
            case Sdk2DOperation.ApplyCamera applyCamera:
                EmitApplyCamera(applyCamera);
                break;
            case Sdk2DOperation.DrawLogicalSprite draw:
                EmitDrawLogicalSprite(draw);
                break;
            case Sdk2DOperation.StreamMapColumn column:
                EmitStreamMapColumn(column);
                break;
            case Sdk2DOperation.StreamMapRow row:
                EmitStreamMapRow(row);
                break;
            case Sdk2DOperation.CameraAabbTiles cameraAabb:
                EmitCameraAabbTiles(cameraAabb);
                break;
            case Sdk2DOperation.CameraAabbHitTop cameraAabb:
                EmitCameraAabbHitTop(cameraAabb);
                break;
            case Sdk2DOperation.CameraScreenAabbTiles cameraAabb:
                EmitCameraScreenAabbTiles(cameraAabb);
                break;
            case Sdk2DOperation.CameraScreenAabbHitTop cameraAabb:
                EmitCameraScreenAabbHitTop(cameraAabb);
                break;
            default:
                throw new NotSupportedException($"NES SDK lowering does not support {operation.GetType().Name} yet.");
        }
    }

    private readonly record struct RuntimeIndexedFieldCursor(string BaseName, SdkByteExpression Index);

    private readonly record struct SharedDrawLogicalSpriteShape(
        string SpriteId,
        int PhysicalPaletteSlot,
        SpriteTransform StaticTransform,
        SharedByteOperandShape Frame,
        SharedByteOperandShape FlipX);

    private enum SharedByteOperandKind
    {
        None,
        Constant,
        Runtime,
    }

    private readonly record struct SharedByteOperandShape(SharedByteOperandKind Kind, int Constant)
    {
        internal static SharedByteOperandShape From(SdkByteExpression? expression) => expression switch
        {
            null => new(SharedByteOperandKind.None, 0),
            SdkByteExpression.Constant constant => new(SharedByteOperandKind.Constant, constant.Value),
            _ => new(SharedByteOperandKind.Runtime, 0),
        };
    }

    private IReadOnlyDictionary<SharedDrawLogicalSpriteShape, string> CreateSharedDrawLogicalSpriteSubroutines()
    {
        var shapes = NesSdkProgramOperations.Collected(program.SdkProgram)
            .OfType<Sdk2DOperation.DrawLogicalSprite>()
            .Select(SharedDrawLogicalSpriteShapeFor)
            .ToArray();
        // A one-piece draw is smaller and cheaper inline than operand stores plus JSR/RTS.
        // Multi-piece shapes amortize the call while removing the repeated metasprite body.
        var repeated = shapes
            .GroupBy(shape => shape)
            .Where(group =>
                group.Count() > 1 &&
                program.SpriteAssets.TryGetValue(group.Key.SpriteId, out var spriteAsset) &&
                spriteAsset.Pieces.Count > 1)
            .Select(group => group.Key)
            .ToHashSet();
        var result = new Dictionary<SharedDrawLogicalSpriteShape, string>();
        foreach (var shape in shapes)
        {
            if (repeated.Contains(shape) && !result.ContainsKey(shape))
            {
                result.Add(shape, $"{SharedDrawLogicalSpriteSubroutinePrefix}_{result.Count}");
            }
        }

        return result;
    }

    private SharedDrawLogicalSpriteShape SharedDrawLogicalSpriteShapeFor(
        Sdk2DOperation.DrawLogicalSprite operation) =>
        new(
            operation.SpriteId,
            program.ResolveSpritePaletteBaseSlot(operation.SpriteId, operation.PaletteSlot),
            operation.StaticTransform,
            SharedByteOperandShape.From(operation.Frame),
            SharedByteOperandShape.From(operation.FlipX));

    private void EmitExpressionToA(ExpressionSyntax expression) => context.EmitExpressionToA(expression);

    private bool TryConst(ExpressionSyntax expression, out int value) =>
        context.TrySourceConstant(expression, out value);

    private string VariableStorageType(string name) => context.VariableStorageType(name);

    private byte VariableAddress(string name) => context.VariableAddress(name);

    private byte RuntimeIndexedMemberBaseAddress(string baseName, string fieldName) =>
        context.RuntimeIndexedMemberBaseAddress(baseName, fieldName);

    private void EmitRuntimeMemberIndexToX(string baseName, SdkByteExpression index) =>
        context.EmitRuntimeMemberIndexToX(baseName, index);

    private static byte HighAddress(byte lowAddress) => (byte)(lowAddress + 1);

    private static bool IsWordBackedType(string type) => type is "i16" or "u16";

    private static string IndexedElementName(string baseIdentifier, int index) => $"{baseIdentifier}[{index}]";

    private static string IndexedMemberName(string baseIdentifier, int index, string fieldName) =>
        $"{IndexedElementName(baseIdentifier, index)}.{fieldName}";

    private static string StorageKey(SdkStorageLocation location)
    {
        return location switch
        {
            SdkStorageLocation.Local local => local.Name,
            SdkStorageLocation.Field field => $"{StorageKey(field.Target)}.{field.FieldName}",
            SdkStorageLocation.IndexedElement indexed => IndexedElementName(indexed.BaseName, indexed.Index),
            SdkStorageLocation.RuntimeIndexedField => throw new InvalidOperationException("Runtime indexed SDK fields must be emitted directly."),
            _ => throw new InvalidOperationException($"Unsupported SDK storage location '{location.GetType().Name}'."),
        };
    }

    private static int CheckedRange(int value, int min, int max, string contextName)
    {
        if (value < min || value > max)
        {
            throw new InvalidOperationException($"{contextName} must be between {min} and {max}.");
        }

        return value;
    }

    internal void EmitReferencedSubroutines()
    {
        EmitReferencedSharedDrawLogicalSpriteSubroutines();
        EmitReferencedSharedCameraAabbSubroutines();

        if (packedColumnRequestSubroutineReferenced)
        {
            EmitPackedColumnRequestSubroutine();
        }

        if (packedColumnPrefetchSubroutineReferenced)
        {
            EmitPackedColumnPrefetchSubroutine();
        }

        if (packedRowRequestSubroutineReferenced)
        {
            EmitPackedRowRequestSubroutine();
        }

        if (packedWideSourceColumnSubroutineReferenced)
        {
            EmitPackedWideSourceColumnSubroutine();
        }

        if (packedCollisionAtScratchSubroutineReferenced)
        {
            EmitPackedCollisionAtScratchSubroutine();
        }

        if (packedCollisionFlagsSubroutineReferenced)
        {
            EmitPackedCollisionFlagsSubroutine();
        }

        if (cameraIncrementTileSubroutineReferenced)
        {
            EmitIncrementCameraTileSubroutine();
        }

        if (cameraDecrementTileSubroutineReferenced)
        {
            EmitDecrementCameraTileSubroutine();
        }

        if (publishVisibleCameraXSubroutineReferenced)
        {
            EmitPublishVisibleCameraXSubroutine();
        }

        if (publishVisibleCameraYSubroutineReferenced)
        {
            EmitPublishVisibleCameraYSubroutine();
        }
    }
}
