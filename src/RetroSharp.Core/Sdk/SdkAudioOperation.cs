namespace RetroSharp.Core.Sdk;

public abstract record SdkAudioOperation
{
    public sealed record InitializeAudio : SdkAudioOperation;

    public sealed record PlayMusic(string ThemeId) : SdkAudioOperation;

    public sealed record UpdateAudio : SdkAudioOperation;

    public sealed record StopMusic : SdkAudioOperation;
}
