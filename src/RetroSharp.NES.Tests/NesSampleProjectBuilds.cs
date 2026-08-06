namespace RetroSharp.NES.Tests;

using System.Text.Json;
using RetroSharp.Core.Sdk;
using RetroSharp.NES;
using RetroSharp.Sdk;

/// <summary>
/// A NES-capable repository sample or versioned validation fixture, paired with a stable id so a
/// survey can label a row without restating the path.
/// </summary>
internal sealed record NesSample(string Id, string RelativePath);

/// <summary>
/// Builds a repository sample or validation fixture for NES the same way the CLI does, but keeps
/// the <see cref="NesRomBuildResult"/> so a test can inspect the build report. The CLI's own
/// project resolution is file-scoped, so the small amount of manifest handling a probe needs is
/// mirrored here rather than widened in production code.
/// <para>
/// A sample is either a bare source file or a <c>.retrosharp.json</c> project; callers do not need
/// to know which, because <see cref="Build"/> and <see cref="Program"/> both accept either and hide
/// the source composition, library and plugin plumbing behind them.
/// </para>
/// </summary>
internal static class NesSampleProjectBuilds
{
    private static readonly JsonSerializerOptions ManifestOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Every NES-capable entry of <c>samples/manifest.json</c>, in manifest order.</summary>
    internal static IReadOnlyList<NesSample> NesSamples()
    {
        using var manifest = JsonDocument.Parse(
            File.ReadAllText(NesVideoSafeObserver.RepositoryFile("samples/manifest.json")));
        return manifest.RootElement.GetProperty("samples").EnumerateArray()
            .Where(sample => sample.GetProperty("targets").EnumerateArray()
                .Any(target => string.Equals(target.GetString(), "nes", StringComparison.Ordinal)))
            .Select(sample => new NesSample(
                sample.GetProperty("id").GetString()!,
                sample.GetProperty("path").GetString()!))
            .ToArray();
    }

    /// <summary>
    /// Every NES sample plus every versioned validation fixture that declares the target, which is
    /// the widest set a whole-repository survey can speak for.
    /// </summary>
    internal static IReadOnlyList<NesSample> NesSamplesAndFixtures()
    {
        var samples = new List<NesSample>(NesSamples());
        samples.AddRange(Directory
            .EnumerateDirectories(NesVideoSafeObserver.RepositoryDirectory("validation/fixtures"))
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.retrosharp.json"))
            .Where(DeclaresNes)
            .Select(path => new NesSample(
                "fixture:" + Path.GetFileName(Path.GetDirectoryName(path)!),
                "validation/fixtures/" + Path.GetFileName(Path.GetDirectoryName(path)!) + "/" +
                Path.GetFileName(path)))
            .OrderBy(sample => sample.RelativePath, StringComparer.Ordinal));
        return samples;
    }

    internal static NesRomBuildResult Build(string relativePath)
    {
        var (source, baseDirectory, libraryPaths, libraries, plugins) = Compose(relativePath);
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
    internal static NesVideoProgram Program(string relativePath)
    {
        var (source, baseDirectory, libraryPaths, libraries, plugins) = Compose(relativePath);
        return RetroSharp.NES.NesRomCompiler.PrepareVideoProgram(
            source,
            baseDirectory,
            SdkLibraryImportMode.ExplicitOnly,
            libraryPaths.Count == 0 ? null : SdkLibraryRegistry.FromDirectories(libraryPaths),
            libraries,
            Plugins(plugins)).VideoProgram;
    }

    private static (string Source, string BaseDirectory, IReadOnlyList<string> LibraryPaths,
        IReadOnlyList<string> Libraries, IReadOnlyList<string> Plugins) Compose(string relativePath)
    {
        var fullPath = NesVideoSafeObserver.RepositoryFile(relativePath);
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
