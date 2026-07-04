namespace RetroSharp.Core.Sdk;

using RetroSharp.Core.Targeting;

public static class SdkAudioOperationValidator
{
    public static void Validate(TargetAudioCapabilities capabilities, SdkAudioOperation operation)
    {
        switch (operation)
        {
            case SdkAudioOperation.InitializeAudio:
            case SdkAudioOperation.UpdateAudio:
            case SdkAudioOperation.StopMusic:
                return;
            case SdkAudioOperation.PlayMusic play:
                if (string.IsNullOrWhiteSpace(play.ThemeId))
                {
                    throw new InvalidOperationException("BGM playback theme id must not be empty.");
                }

                if (!capabilities.SupportsBgm && !capabilities.AllowsBgmNoOp)
                {
                    throw new InvalidOperationException($"Target '{capabilities.Name}' does not support BGM playback yet.");
                }

                return;
            case SdkAudioOperation.PlaySoundEffect play:
                if (string.IsNullOrWhiteSpace(play.SoundId))
                {
                    throw new InvalidOperationException("SFX playback sound id must not be empty.");
                }

                if (!capabilities.SupportsSfx)
                {
                    throw new InvalidOperationException($"Target '{capabilities.Name}' does not support SFX playback yet.");
                }

                return;
            default:
                throw new InvalidOperationException($"Unsupported SDK audio operation '{operation.GetType().Name}'.");
        }
    }
}
