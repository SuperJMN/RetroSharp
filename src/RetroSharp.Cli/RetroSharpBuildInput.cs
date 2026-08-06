namespace RetroSharp.Cli;

/// <summary>
/// A single resolved build unit: one source blob compiled for one target. A plain source file
/// resolves to exactly one <see cref="RetroSharpBuildInput"/>; a multi-target project manifest
/// resolves to one per declared target.
/// </summary>
internal sealed record RetroSharpBuildInput(
    string Source,
    string? BaseDirectory,
    string Target,
    string? OutputPath,
    string? RuntimeAbiOutputPath,
    string? SymbolsOutputPath,
    IReadOnlyList<string> LibraryPaths,
    IReadOnlyList<string> LibraryImports,
    string PrimaryPath,
    IReadOnlyList<string> Plugins,
    bool CapacityReport);
