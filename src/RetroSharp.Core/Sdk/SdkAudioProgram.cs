namespace RetroSharp.Core.Sdk;

public abstract record SdkAudioStreamItem
{
    public sealed record Op(SdkAudioOperation Operation) : SdkAudioStreamItem;

    public sealed record CallSubroutine(string Name) : SdkAudioStreamItem;
}

public sealed record SdkAudioProgram(
    IReadOnlyList<SdkAudioStreamItem> Main,
    IReadOnlyDictionary<string, IReadOnlyList<SdkAudioStreamItem>> Subroutines);
