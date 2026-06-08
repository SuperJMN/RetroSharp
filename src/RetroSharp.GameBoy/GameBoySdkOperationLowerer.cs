namespace RetroSharp.GameBoy;

using RetroSharp.Core.Sdk;

internal static class GameBoySdkOperationLowerer
{
    public static void Emit(GbBuilder builder, Sdk2DOperation operation)
    {
        switch (operation)
        {
            case Sdk2DOperation.WaitFrame:
                EmitWaitFrame(builder);
                break;
            default:
                throw new NotSupportedException($"Game Boy SDK lowering does not support {operation.GetType().Name} yet.");
        }
    }

    private static void EmitWaitFrame(GbBuilder builder)
    {
        GameBoyRomBuilder.EmitWaitVBlank(builder, builder.CreateLabel("wait_vblank"));
    }
}
