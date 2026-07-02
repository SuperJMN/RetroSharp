namespace RetroSharp.Sdk;

using System.Text.Json;

public sealed class SdkLibraryRegistry
{
    private const string ManifestFileName = "retrosharp-library.json";

    private readonly Dictionary<string, SdkLibrary> libraries;

    private static IReadOnlyList<SdkLibrary> BuiltInLibraries { get; } =
    [
        new SdkLibrary(SdkImportResolver.Portable2D, SdkLibrarySource.ForTarget),
    ];

    public static SdkLibraryRegistry Default { get; } = new(BuiltInLibraries);

    public SdkLibraryRegistry(IEnumerable<SdkLibrary> libraries)
    {
        this.libraries = new Dictionary<string, SdkLibrary>(StringComparer.Ordinal);
        foreach (var library in libraries)
        {
            if (this.libraries.ContainsKey(library.ImportPath))
            {
                throw new InvalidOperationException($"Library import '{library.ImportPath}' is declared more than once.");
            }

            this.libraries.Add(library.ImportPath, library);
        }
    }

    public bool TryResolve(string importPath, out SdkLibrary? library)
    {
        return libraries.TryGetValue(importPath, out library);
    }

    public static SdkLibraryRegistry FromDirectories(IEnumerable<string> libraryPaths, bool includeDefaultLibraries = true)
    {
        var resolvedLibraries = includeDefaultLibraries
            ? BuiltInLibraries.ToList()
            : [];

        foreach (var manifestPath in libraryPaths.SelectMany(DiscoverManifestFiles))
        {
            resolvedLibraries.Add(LoadManifest(manifestPath));
        }

        return new SdkLibraryRegistry(resolvedLibraries);
    }

    private static IEnumerable<string> DiscoverManifestFiles(string libraryPath)
    {
        var fullPath = Path.GetFullPath(libraryPath);
        if (File.Exists(fullPath))
        {
            yield return fullPath;
            yield break;
        }

        if (!Directory.Exists(fullPath))
        {
            throw new InvalidOperationException($"Library path '{libraryPath}' does not exist.");
        }

        var manifestPath = Path.Combine(fullPath, ManifestFileName);
        if (File.Exists(manifestPath))
        {
            yield return manifestPath;
            yield break;
        }

        foreach (var directory in Directory.EnumerateDirectories(fullPath).OrderBy(path => path, StringComparer.Ordinal))
        {
            var childManifestPath = Path.Combine(directory, ManifestFileName);
            if (File.Exists(childManifestPath))
            {
                yield return childManifestPath;
            }
        }
    }

    private static SdkLibrary LoadManifest(string manifestPath)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };
        SdkLibraryManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<SdkLibraryManifest>(File.ReadAllText(manifestPath), options);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Invalid RetroSharp library manifest '{manifestPath}': {ex.Message}", ex);
        }

        var importPath = (manifest?.ImportPath ?? manifest?.Import)?.Trim();
        if (string.IsNullOrWhiteSpace(importPath))
        {
            throw new InvalidOperationException($"RetroSharp library manifest '{manifestPath}' must declare an import path.");
        }

        var sources = manifest!.Sources ?? [];
        if (sources.Length == 0)
        {
            throw new InvalidOperationException($"RetroSharp library manifest '{manifestPath}' must list at least one source file.");
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(manifestPath))
            ?? throw new InvalidOperationException($"Could not resolve directory for RetroSharp library manifest '{manifestPath}'.");
        var source = string.Concat(sources.Select(sourcePath => ReadPackageSource(directory, manifestPath, sourcePath)));
        var targets = (manifest.Targets ?? [])
            .Where(target => !string.IsNullOrWhiteSpace(target))
            .ToHashSet(StringComparer.Ordinal);

        return new SdkLibrary(
            importPath,
            catalog =>
            {
                if (targets.Count != 0 && !targets.Contains(catalog.TargetId))
                {
                    throw new InvalidOperationException($"Library '{importPath}' does not support target '{catalog.TargetId}'.");
                }

                return source;
            });
    }

    private static string ReadPackageSource(string directory, string manifestPath, string sourcePath)
    {
        if (Path.IsPathRooted(sourcePath))
        {
            throw new InvalidOperationException($"RetroSharp library manifest '{manifestPath}' source path '{sourcePath}' must be relative.");
        }

        var fullSourcePath = Path.GetFullPath(Path.Combine(directory, sourcePath));
        var fullDirectory = Path.GetFullPath(directory);
        if (!fullSourcePath.StartsWith(fullDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"RetroSharp library manifest '{manifestPath}' source path '{sourcePath}' must stay inside the package directory.");
        }

        if (!File.Exists(fullSourcePath))
        {
            throw new InvalidOperationException($"RetroSharp library source '{sourcePath}' declared by '{manifestPath}' was not found.");
        }

        var source = File.ReadAllText(fullSourcePath);
        return source.EndsWith('\n') ? source : source + Environment.NewLine;
    }

    private sealed record SdkLibraryManifest
    {
        public string? Import { get; init; }
        public string? ImportPath { get; init; }
        public string[]? Sources { get; init; }
        public string[]? Targets { get; init; }
    }
}
