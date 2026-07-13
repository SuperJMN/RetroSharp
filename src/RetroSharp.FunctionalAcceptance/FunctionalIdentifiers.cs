namespace RetroSharp.FunctionalAcceptance;

internal static class FunctionalIdentifiers
{
    public static string StableId(this FunctionalTarget target) => target switch
    {
        FunctionalTarget.GameBoy => "gb",
        FunctionalTarget.Nes => "nes",
        _ => throw new ArgumentOutOfRangeException(nameof(target)),
    };

    public static string StableId(this FunctionalExecutionSource source) => source switch
    {
        FunctionalExecutionSource.InProcess => "in-process",
        FunctionalExecutionSource.GameboyMcp => "gameboy-mcp",
        FunctionalExecutionSource.NesMcp => "nes-mcp",
        FunctionalExecutionSource.ExternalEmulator => "external-emulator",
        _ => throw new ArgumentOutOfRangeException(nameof(source)),
    };

    public static string DiagnosticText(this FunctionalWriteTimingObservation? timing, bool safe) => timing is null
        ? $"safe={Boolean(safe)}:timing=missing"
        : $"safe={Boolean(safe)}:cycle={timing.Cycle}:scanline={timing.Scanline}:dot={timing.Dot}:phase={timing.Phase}:display={Boolean(timing.DisplayEnabled)}";

    private static string Boolean(bool value) => value ? "true" : "false";
}
