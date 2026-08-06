namespace RetroSharp.NES.Tests;

using System.Text.Json;
using RetroSharp.Core.Sdk;
using RetroSharp.NES;
using RetroSharp.Sdk;

/// <summary>
/// Builds a repository sample or validation fixture for NES the same way the CLI does, but keeps
/// the <see cref="NesRomBuildResult"/> so a test can inspect the build report. The CLI's own
/// project resolution is file-scoped, so the small amount of manifest handling a probe needs is
/// mirrored here rather than widened in production code.
/// </summary>
internal static class NesSampleProjectBuilds
{
    private static readonly JsonSerializerOptions ManifestOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Repository-relative paths of every NES sample and versioned validation fixture.</summary>
    internal static IReadOnlyList<string> NesProjects()
    {
        var projects = new List<string>(NesVideoSafeObserver.NesSampleProjects());
        var fixtures = Directory
            .EnumerateDirectories(NesVideoSafeObserver.RepositoryDirectory("validation/fixtures"))
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.retrosharp.json"))
            .Where(DeclaresNes)
            .Select(path => "validation/fixtures/" + Path.GetFileName(Path.GetDirectoryName(path)!) + "/" +
                            Path.GetFileName(path))
            .OrderBy(path => path, StringComparer.Ordinal);
        projects.AddRange(fixtures);
        return projects;
    }

    internal static NesRomBuildResult Build(string projectRelativePath)
    {
        var (source, baseDirectory, libraryPaths, libraries, plugins) = Compose(projectRelativePath);
        return RetroSharp.NES.NesRomCompiler.CompileSourceWithReport(
            source,
            baseDirectory,
            sdkLibraryRegistry: libraryPaths.Count == 0 ? null : SdkLibraryRegistry.FromDirectories(libraryPaths),
            sdkLibraryImports: libraries,
            sdkPluginRegistry: Plugins(plugins));
    }

    /// <summary>
    /// The prepared program the builder lowers, so a probe can consult a planner such as
    /// <see cref="NesUserFunctionOutliner"/> directly against the same input.
    /// </summary>
    internal static NesVideoProgram Program(string projectRelativePath)
    {
        var (source, baseDirectory, libraryPaths, libraries, plugins) = Compose(projectRelativePath);
        return RetroSharp.NES.NesRomCompiler.PrepareVideoProgram(
            source,
            baseDirectory,
            SdkLibraryImportMode.ExplicitOnly,
            libraryPaths.Count == 0 ? null : SdkLibraryRegistry.FromDirectories(libraryPaths),
            libraries,
            Plugins(plugins)).VideoProgram;
    }

    private static (string Source, string BaseDirectory, IReadOnlyList<string> LibraryPaths,
        IReadOnlyList<string> Libraries, IReadOnlyList<string> Plugins) Compose(string projectRelativePath)
    {
        var fullPath = NesVideoSafeObserver.RepositoryFile(projectRelativePath);
        var directory = Path.GetDirectoryName(fullPath)!;
        if (!fullPath.EndsWith(".retrosharp.json", StringComparison.OrdinalIgnoreCase))
        {
            return (File.ReadAllText(fullPath), directory, [], [], []);
        }

        var manifest = Read(fullPath);
        var sourceFiles = (manifest.Sources ?? [])
            .Select(source => new PhysicalNamespaceSourceFile(
                Path.Combine(directory, source),
                File.ReadAllText(Path.Combine(directory, source))))
            .ToArray();
        var source = string.IsNullOrWhiteSpace(manifest.NamespaceMode)
            ? string.Concat(sourceFiles.Select(file => file.Source.EndsWith('\n') ? file.Source : file.Source + "\n"))
            : PhysicalNamespaceSourceComposer.Compose(
                sourceFiles,
                string.IsNullOrWhiteSpace(manifest.RootNamespace) ? "RetroSharpProject" : manifest.RootNamespace,
                Path.Combine(directory, manifest.SourceRoot ?? "src"));

        return (
            source,
            directory,
            (manifest.LibraryPaths ?? []).Select(path => Path.Combine(directory, path)).ToArray(),
            manifest.Libraries ?? [],
            manifest.Plugins ?? []);
    }

    private static SdkPluginRegistry Plugins(IReadOnlyList<string> pluginIds)
    {
        var registry = SdkPluginRegistry.Empty;
        foreach (var pluginId in pluginIds)
        {
            registry = pluginId switch
            {
                RetroSharp.Sdk.Plugins.Platformer2D.Platformer2DPlugin.PluginId =>
                    registry.Register(RetroSharp.Sdk.Plugins.Platformer2D.Platformer2DPlugin.Create()),
                _ => throw new InvalidOperationException($"Unknown SDK plugin '{pluginId}'."),
            };
        }

        return registry;
    }

    private static bool DeclaresNes(string projectPath) =>
        (Read(projectPath).Targets ?? []).Contains("nes", StringComparer.OrdinalIgnoreCase);

    private static Manifest Read(string projectPath) =>
        JsonSerializer.Deserialize<Manifest>(File.ReadAllText(projectPath), ManifestOptions)
        ?? throw new InvalidOperationException($"RetroSharp project '{projectPath}' is empty.");

    private sealed record Manifest
    {
        public string[]? Targets { get; init; }

        public string[]? Sources { get; init; }

        public string[]? Libraries { get; init; }

        public string[]? LibraryPaths { get; init; }

        public string[]? Plugins { get; init; }

        public string? SourceRoot { get; init; }

        public string? RootNamespace { get; init; }

        public string? NamespaceMode { get; init; }
    }
}
