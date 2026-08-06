using System.Text.Json;

namespace RetroSharp.Cli;

/// <summary>
/// Reads a `*.retrosharp.json` project manifest and composes its declared source files into the
/// single source blob the compiler consumes, applying physical-namespace rewriting when the
/// manifest opts into it.
/// </summary>
internal static class ProjectManifestReader
{
    internal static RetroSharpProjectManifest ReadManifest(string projectPath)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        try
        {
            return JsonSerializer.Deserialize<RetroSharpProjectManifest>(File.ReadAllText(projectPath), options)
                ?? throw new InvalidOperationException($"RetroSharp project '{projectPath}' is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Invalid RetroSharp project '{projectPath}': {ex.Message}", ex);
        }
    }

    internal static RetroSharp.Sdk.PhysicalNamespaceSourceFile ReadSourceFile(string projectDirectory, string projectPath, string sourcePath)
    {
        var fullSourcePath = ResolveItemPath(projectDirectory, projectPath, sourcePath, "source");
        if (!File.Exists(fullSourcePath))
        {
            throw new InvalidOperationException($"RetroSharp project '{projectPath}' source '{sourcePath}' was not found.");
        }

        var source = File.ReadAllText(fullSourcePath);
        return new RetroSharp.Sdk.PhysicalNamespaceSourceFile(fullSourcePath, source);
    }

    internal static string ComposeSource(
        string projectDirectory,
        string projectPath,
        RetroSharpProjectManifest manifest,
        IReadOnlyList<RetroSharp.Sdk.PhysicalNamespaceSourceFile> sourceFiles)
    {
        if (string.IsNullOrWhiteSpace(manifest.NamespaceMode))
        {
            return string.Concat(sourceFiles.Select(sourceFile => EnsureTrailingNewLine(sourceFile.Source)));
        }

        if (!string.Equals(manifest.NamespaceMode, "physical", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"RetroSharp project '{projectPath}' declares unsupported namespaceMode '{manifest.NamespaceMode}'.");
        }

        var rootNamespace = string.IsNullOrWhiteSpace(manifest.RootNamespace)
            ? DefaultRootNamespace(projectPath)
            : manifest.RootNamespace;
        var sourceRoot = ResolveItemPath(projectDirectory, projectPath, manifest.SourceRoot ?? "src", "sourceRoot");
        return RetroSharp.Sdk.PhysicalNamespaceSourceComposer.Compose(sourceFiles, rootNamespace, sourceRoot);
    }

    internal static string ResolveItemPath(string projectDirectory, string projectPath, string itemPath, string itemKind)
    {
        if (string.IsNullOrWhiteSpace(itemPath))
        {
            throw new InvalidOperationException($"RetroSharp project '{projectPath}' declares an empty {itemKind} path.");
        }

        return Path.IsPathRooted(itemPath)
            ? Path.GetFullPath(itemPath)
            : Path.GetFullPath(Path.Combine(projectDirectory, itemPath));
    }

    private static string EnsureTrailingNewLine(string source)
    {
        return source.EndsWith('\n') ? source : source + System.Environment.NewLine;
    }

    private static string DefaultRootNamespace(string projectPath)
    {
        var fileName = Path.GetFileName(projectPath);
        const string projectSuffix = ".retrosharp.json";
        var baseName = fileName.EndsWith(projectSuffix, StringComparison.OrdinalIgnoreCase)
            ? fileName[..^projectSuffix.Length]
            : Path.GetFileNameWithoutExtension(fileName);
        var segments = baseName
            .Split(['-', '_', '.', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(segment => char.ToUpperInvariant(segment[0]) + segment[1..])
            .ToArray();
        var normalized = new string(string.Concat(segments).Where(char.IsLetterOrDigit).ToArray());
        if (string.IsNullOrEmpty(normalized))
        {
            return "RetroSharpProject";
        }

        return char.IsDigit(normalized[0]) ? "_" + normalized : normalized;
    }
}
