namespace RetroSharp.Core.Sdk;

public abstract record SdkAudioStreamItem : ISdkStreamItem<SdkAudioOperation>
{
    SdkAudioOperation? ISdkStreamItem<SdkAudioOperation>.Operation => OperationOrDefault;

    string? ISdkStreamItem<SdkAudioOperation>.SubroutineCallName => SubroutineCallNameOrDefault;

    private protected virtual SdkAudioOperation? OperationOrDefault => null;

    private protected virtual string? SubroutineCallNameOrDefault => null;

    public sealed record Op(SdkAudioOperation Operation) : SdkAudioStreamItem
    {
        private protected override SdkAudioOperation? OperationOrDefault => Operation;
    }

    public sealed record CallSubroutine(string Name) : SdkAudioStreamItem
    {
        private protected override string? SubroutineCallNameOrDefault => Name;
    }
}

public sealed record SdkAudioProgram(
    IReadOnlyList<SdkAudioStreamItem> Main,
    IReadOnlyDictionary<string, IReadOnlyList<SdkAudioStreamItem>> Subroutines);
