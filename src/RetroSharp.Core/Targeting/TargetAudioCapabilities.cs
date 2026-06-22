namespace RetroSharp.Core.Targeting;

public sealed record TargetAudioCapabilities(
    string Name,
    bool SupportsBgm,
    IReadOnlyList<string> SupportedMusicFormats);
