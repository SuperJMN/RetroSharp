namespace RetroSharp.NES;

using RetroSharp.Core.Sdk;

// Single mapping from portable Sdk2DOperation records to their NES emission,
// mirroring GameBoySdkOperationLowerer. Operations migrated here are driven by
// the shared operation model rather than re-derived on the direct AST path.
internal static class NesSdkOperationLowerer
{
    public static void Emit(NesRuntimeCompiler compiler, Sdk2DOperation operation)
    {
        switch (operation)
        {
            case Sdk2DOperation.WaitFrame:
                compiler.EmitWaitFrame();
                break;
            case Sdk2DOperation.PollInput:
                compiler.EmitPollInput();
                break;
            case Sdk2DOperation.SetCameraPosition camera:
                compiler.EmitSetCameraPosition(camera);
                break;
            case Sdk2DOperation.ApplyCamera applyCamera:
                compiler.EmitApplyCamera(applyCamera);
                break;
            case Sdk2DOperation.DrawLogicalSprite draw:
                compiler.EmitDrawLogicalSprite(draw);
                break;
            default:
                throw new NotSupportedException($"NES SDK lowering does not support {operation.GetType().Name} yet.");
        }
    }
}
