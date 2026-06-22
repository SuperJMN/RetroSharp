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
                    throw new InvalidOperationException("music.Play theme id must not be empty.");
                }

                if (!capabilities.SupportsBgm)
                {
                    throw new InvalidOperationException($"Target '{capabilities.Name}' does not support BGM playback yet.");
                }

                return;
            default:
                throw new InvalidOperationException($"Unsupported SDK audio operation '{operation.GetType().Name}'.");
        }
    }
}
