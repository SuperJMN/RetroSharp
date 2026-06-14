namespace RetroSharp.GameBoy;

using RetroSharp.Core.Sdk;

// Single mapping from portable Sdk2DOperation records to their Game Boy
// emission. Operations migrated here are driven by the shared operation model
// rather than re-derived on the direct AST path. Game Boy-specific emission
// lives on GameBoyRuntimeCompiler; this lowerer only dispatches per operation.
internal static class GameBoySdkOperationLowerer
{
    public static void Emit(GameBoyRuntimeCompiler compiler, Sdk2DOperation operation)
    {
        switch (operation)
        {
            case Sdk2DOperation.WaitFrame:
                compiler.EmitWaitFrame();
                break;
            case Sdk2DOperation.PollInput:
                compiler.EmitPollInput();
                break;
            default:
                throw new NotSupportedException($"Game Boy SDK lowering does not support {operation.GetType().Name} yet.");
        }
    }
}
