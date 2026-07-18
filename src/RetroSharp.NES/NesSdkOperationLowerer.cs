namespace RetroSharp.NES;

using RetroSharp.Core.Sdk;
using RetroSharp.Parser;

// Production and focused tests cross this same small operation seam. Feature
// partials own NES emission; the syntax compiler supplies only target operand
// and storage primitives that raw compatibility paths also require.
internal sealed partial class NesSdkOperationLowerer
{
    private const int BottomOverscanInsetPixels = 8;
    private const byte PendingStreamNone = 0;
    private const byte PendingStreamColumn = 1;
    private const byte PendingStreamRow = 2;
    private const string PackedCollisionAtScratchSubroutineLabel = "nes_packed_collision_at_scratch";
    private const string PackedCollisionFlagsSubroutineLabel = "nes_packed_collision_flags";
    private const string PackedWideSourceColumnSubroutineLabel = "nes_packed_wide_source_column";

    private readonly PrgBuilder builder;
    private readonly NesVideoProgram program;
    private readonly NesSdkLoweringContext context;
    private readonly NesPhysicalFrameScheduler frameScheduler;
    private readonly bool useFourScreenNametables;
    private readonly bool usePackedCamera;
    private int nextHardwareSprite;
    private bool packedCollisionAtScratchSubroutineReferenced;
    private bool packedCollisionFlagsSubroutineReferenced;
    private bool packedWideSourceColumnSubroutineReferenced;
    private int packedWideSourceColumnMapWidth;
    private NesCameraConfig? cameraConfig;
    private RuntimeIndexedFieldCursor? activeRuntimeIndexedFieldCursor;

    public NesSdkOperationLowerer(
        PrgBuilder builder,
        NesVideoProgram program,
        NesSdkLoweringContext context,
        bool useFourScreenNametables,
        bool usePackedCamera,
        bool useSequentialOamPublication)
        : this(
            builder,
            program,
            context,
            NesPhysicalFrameScheduler.Create(
                builder,
                program,
                useSequentialOamPublication ? "nes-mmc3-tvrom-v1" : "nes-mapper-0-current",
                useFourScreenNametables,
                usePackedCamera,
                useSequentialOamPublication))
    {
    }

    internal NesSdkOperationLowerer(
        PrgBuilder builder,
        NesVideoProgram program,
        NesSdkLoweringContext context,
        NesPhysicalFrameScheduler frameScheduler)
    {
        ArgumentNullException.ThrowIfNull(frameScheduler);
        this.builder = builder;
        this.program = program;
        this.context = context;
        this.frameScheduler = frameScheduler;
        useFourScreenNametables = frameScheduler.UseFourScreenNametables;
        usePackedCamera = frameScheduler.UsesPackedCameraRuntime;
    }

    public void Emit(Sdk2DOperation operation)
    {
        switch (operation)
        {
            case Sdk2DOperation.WaitFrame:
                frameScheduler.EmitFrameBoundary(NesFrameBoundaryPurpose.Gameplay, this);
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
}
