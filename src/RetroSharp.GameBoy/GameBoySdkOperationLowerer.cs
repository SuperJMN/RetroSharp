namespace RetroSharp.GameBoy;

using RetroSharp.Core.Sdk;

// Production and focused tests cross this same small operation seam. Feature
// partials own Game Boy emission; the syntax compiler supplies only target
// operand/read primitives that are also required by raw compatibility paths.
internal sealed partial class GameBoySdkOperationLowerer
{
    private readonly GbBuilder builder;
    private readonly GameBoyVideoProgram program;
    private readonly GameBoySdkLoweringState state;
    private readonly GameBoySdkLoweringContext context;
    private readonly GameBoyRomLayout romLayout;
    private readonly GameBoyWorldPackRuntimeLayout? packedWorldRuntimeLayout;
    private readonly bool usesPackedCameraRuntime;
    private readonly bool usesPackedCollisionRuntime;

    public GameBoySdkOperationLowerer(
        GbBuilder builder,
        GameBoyVideoProgram program,
        GameBoySdkLoweringState state,
        GameBoySdkLoweringContext context,
        GameBoyRomLayout romLayout,
        GameBoyWorldPackRuntimeLayout? packedWorldRuntimeLayout,
        bool usesPackedCameraRuntime)
    {
        this.builder = builder;
        this.program = program;
        this.state = state;
        this.context = context;
        this.romLayout = romLayout;
        this.packedWorldRuntimeLayout = packedWorldRuntimeLayout;
        this.usesPackedCameraRuntime = usesPackedCameraRuntime;
        usesPackedCollisionRuntime = usesPackedCameraRuntime
                                     && program.SdkOperations.Any(operation => operation is
                                         Sdk2DOperation.CameraAabbTiles or
                                         Sdk2DOperation.CameraAabbHitTop or
                                         Sdk2DOperation.CameraScreenAabbTiles or
                                         Sdk2DOperation.CameraScreenAabbHitTop);
    }

    public void Emit(Sdk2DOperation operation)
    {
        switch (operation)
        {
            case Sdk2DOperation.WaitFrame:
                EmitWaitFrame();
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
            case Sdk2DOperation.ReadWorldTileFlags flags:
                EmitReadWorldTileFlags(flags);
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
                throw new NotSupportedException($"Game Boy SDK lowering does not support {operation.GetType().Name} yet.");
        }
    }
}

internal sealed class GameBoySdkLoweringState
{
    public int NextHardwareSprite { get; set; }

    public int? CameraMapWidth { get; set; }

    public int? CameraStreamY { get; set; }

    public int? CameraStreamHeight { get; set; }
}
