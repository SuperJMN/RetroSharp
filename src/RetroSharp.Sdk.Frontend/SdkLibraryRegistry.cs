namespace RetroSharp.Sdk;

using System.Text.Json;

public sealed class SdkLibraryRegistry
{
    private const string ManifestFileName = "retrosharp-library.json";

    private readonly Dictionary<string, SdkLibrary> libraries;

    private static IReadOnlyList<SdkLibrary> BuiltInLibraries { get; } =
    [
        LoadBuiltInPortable2D(),
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

        foreach (var manifestPath in libraryPaths
            .SelectMany(DiscoverManifestFiles)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.Ordinal))
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
        var targets = (manifest.Targets ?? [])
            .Where(target => !string.IsNullOrWhiteSpace(target))
            .ToHashSet(StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(manifest.NamespaceMode))
        {
            if (!string.Equals(manifest.NamespaceMode, "physical", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"RetroSharp library manifest '{manifestPath}' declares unsupported namespaceMode '{manifest.NamespaceMode}'.");
            }

            var sourceRoot = ResolvePackageItemPath(directory, manifestPath, manifest.SourceRoot ?? "src", "sourceRoot");
            var rootNamespace = string.IsNullOrWhiteSpace(manifest.RootNamespace)
                ? importPath
                : manifest.RootNamespace;
            var sourceGroup = new PhysicalNamespaceSourceGroup(
                sources.Select(sourcePath => ReadPackageSourceFile(directory, manifestPath, sourcePath)).ToArray(),
                rootNamespace,
                sourceRoot);

            return new SdkLibrary(
                importPath,
                catalog =>
                {
                    ValidateTarget(importPath, targets, catalog.TargetId);
                    return SdkLibrarySourceSet.FromPhysicalNamespace(sourceGroup);
                });
        }

        var source = string.Concat(sources.Select(sourcePath => ReadPackageSource(directory, manifestPath, sourcePath)));
        return new SdkLibrary(
            importPath,
            catalog =>
            {
                ValidateTarget(importPath, targets, catalog.TargetId);

                return source;
            });
    }

    private static SdkLibrary LoadBuiltInPortable2D()
    {
        return LoadManifest(FindBuiltInPortable2DManifest());
    }

    private static string FindBuiltInPortable2DManifest()
    {
        var relativePath = Path.Combine("sdk", "RetroSharp.Portable2D", ManifestFileName);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var root in CandidateSearchRoots())
        {
            if (!visited.Add(root))
            {
                continue;
            }

            var candidate = Path.Combine(root, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException($"Could not find built-in SDK library manifest '{relativePath}'.");
    }

    private static IEnumerable<string> CandidateSearchRoots()
    {
        foreach (var root in DirectoryAndAncestors(AppContext.BaseDirectory))
        {
            yield return root;
        }

        foreach (var root in DirectoryAndAncestors(Directory.GetCurrentDirectory()))
        {
            yield return root;
        }
    }

    private static IEnumerable<string> DirectoryAndAncestors(string path)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(path));
        while (directory is not null)
        {
            yield return directory.FullName;
            directory = directory.Parent;
        }
    }

    private static void ValidateTarget(string importPath, IReadOnlySet<string> targets, string targetId)
    {
        if (targets.Count != 0 && !targets.Contains(targetId))
        {
            throw new InvalidOperationException($"Library '{importPath}' does not support target '{targetId}'.");
        }
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

    private static PhysicalNamespaceSourceFile ReadPackageSourceFile(string directory, string manifestPath, string sourcePath)
    {
        var fullSourcePath = ResolvePackageItemPath(directory, manifestPath, sourcePath, "source path");
        if (!File.Exists(fullSourcePath))
        {
            throw new InvalidOperationException($"RetroSharp library source '{sourcePath}' declared by '{manifestPath}' was not found.");
        }

        return new PhysicalNamespaceSourceFile(fullSourcePath, File.ReadAllText(fullSourcePath));
    }

    private static string ResolvePackageItemPath(string directory, string manifestPath, string path, string pathKind)
    {
        if (Path.IsPathRooted(path))
        {
            throw new InvalidOperationException($"RetroSharp library manifest '{manifestPath}' {pathKind} '{path}' must be relative.");
        }

        var fullPath = Path.GetFullPath(Path.Combine(directory, path));
        var fullDirectory = Path.GetFullPath(directory);
        if (fullPath != fullDirectory
            && !fullPath.StartsWith(fullDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"RetroSharp library manifest '{manifestPath}' {pathKind} '{path}' must stay inside the package directory.");
        }

        return fullPath;
    }

    private sealed record SdkLibraryManifest
    {
        public string? Import { get; init; }
        public string? ImportPath { get; init; }
        public string[]? Sources { get; init; }
        public string[]? Targets { get; init; }
        public string? RootNamespace { get; init; }
        public string? SourceRoot { get; init; }
        public string? NamespaceMode { get; init; }
    }
}
