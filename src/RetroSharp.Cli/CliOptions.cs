namespace RetroSharp.Cli;

/// <summary>
/// Parsed top-level command-line options accepted by <see cref="CliRunner"/>. Replaces the
/// anonymous 9-field tuple that used to be repeated across several method signatures.
/// </summary>
internal sealed record CliOptions(
    string? InputPath,
    string? OutputPath,
    string? RuntimeAbiOutputPath,
    string? SymbolsOutputPath,
    string? Target,
    IReadOnlyList<string> LibraryPaths,
    IReadOnlyList<string> Plugins,
    bool WorldBudgetReport,
    bool CapacityReport);
