namespace RetroSharp.GameBoy;

using RetroSharp.Core.Sdk;

internal static class GameBoySdkAudioOperationLowerer
{
    public static void Emit(GameBoyRuntimeCompiler compiler, SdkAudioOperation operation)
    {
        switch (operation)
        {
            case SdkAudioOperation.InitializeAudio:
                compiler.EmitInitializeAudio();
                break;
            case SdkAudioOperation.PlayMusic play:
                compiler.EmitPlayMusic(play);
                break;
            case SdkAudioOperation.UpdateAudio:
                compiler.EmitUpdateAudio();
                break;
            case SdkAudioOperation.StopMusic:
                compiler.EmitStopMusic();
                break;
            default:
                throw new NotSupportedException($"Game Boy SDK audio lowering does not support {operation.GetType().Name} yet.");
        }
    }
}
