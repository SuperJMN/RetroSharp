namespace RetroSharp.Cli;

/// <summary>
/// The JSON shape of a `*.retrosharp.json` project manifest, deserialized as-is by
/// <see cref="ProjectManifestReader"/>.
/// </summary>
internal sealed record RetroSharpProjectManifest
{
    public string? Target { get; init; }
    public string[]? Targets { get; init; }
    public string? Output { get; init; }
    public string? OutputPath { get; init; }
    public Dictionary<string, string>? Outputs { get; init; }
    public string[]? Sources { get; init; }
    public string[]? LibraryPaths { get; init; }
    public string[]? Libraries { get; init; }
    public string[]? Plugins { get; init; }
    public string? RootNamespace { get; init; }
    public string? SourceRoot { get; init; }
    public string? NamespaceMode { get; init; }
}
