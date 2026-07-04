namespace RetroSharp.Core.Targeting;

public sealed record TargetAudioCapabilities(
    string Name,
    bool SupportsBgm,
    IReadOnlyList<string> SupportedMusicFormats,
    bool AllowsBgmNoOp = false,
    bool SupportsSfx = false,
    IReadOnlyList<string>? SupportedSfxFormats = null);
