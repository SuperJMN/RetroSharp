namespace RetroSharp.Cli;

/// <summary>
/// Resolves parsed <see cref="CliOptions"/> into one or more <see cref="RetroSharpBuildInput"/>
/// values: a single input for a plain source file, or one per declared target when the input is
/// a `*.retrosharp.json` project manifest.
/// </summary>
internal static class ProjectBuildInputResolver
{
    internal const string MissingTargetMessage = "No target has been specified. Use --target nes or --target gb.";

    internal static IReadOnlyList<RetroSharpBuildInput> Resolve(CliOptions options)
    {
        if (options.InputPath is null)
        {
            throw new ArgumentException("No source file has been specified");
        }

        return IsProjectFile(options.InputPath)
            ? ResolveProjectBuildInputs(options)
            : [ResolveSourceBuildInput(options)];
    }

    private static RetroSharpBuildInput ResolveSourceBuildInput(CliOptions options)
    {
        var inputPath = options.InputPath ?? throw new ArgumentException("No source file has been specified");
        var fullPath = Path.GetFullPath(inputPath);
        var target = options.Target
            ?? throw new ArgumentException(MissingTargetMessage);
        return new RetroSharpBuildInput(
            File.ReadAllText(inputPath),
            Path.GetDirectoryName(fullPath),
            target,
            options.OutputPath,
            options.RuntimeAbiOutputPath,
            options.SymbolsOutputPath,
            options.LibraryPaths,
            [],
            inputPath,
            options.Plugins,
            options.CapacityReport);
    }

    private static IReadOnlyList<RetroSharpBuildInput> ResolveProjectBuildInputs(CliOptions options)
    {
        var projectPath = Path.GetFullPath(options.InputPath ?? throw new ArgumentException("No project file has been specified"));
        var projectDirectory = Path.GetDirectoryName(projectPath)
            ?? throw new InvalidOperationException($"Could not resolve directory for RetroSharp project '{projectPath}'.");
        var manifest = ProjectManifestReader.ReadManifest(projectPath);
        var sourceItems = manifest.Sources ?? [];
        if (sourceItems.Length == 0)
        {
            throw new InvalidOperationException($"RetroSharp project '{projectPath}' must list at least one source file.");
        }

        var sourceFiles = sourceItems
            .Select(sourcePath => ProjectManifestReader.ReadSourceFile(projectDirectory, projectPath, sourcePath))
            .ToArray();
        var source = ProjectManifestReader.ComposeSource(projectDirectory, projectPath, manifest, sourceFiles);
        var projectLibraryPaths = (manifest.LibraryPaths ?? [])
            .Select(libraryPath => ProjectManifestReader.ResolveItemPath(projectDirectory, projectPath, libraryPath, "library path"))
            .ToArray();
        var libraryPaths = projectLibraryPaths.Concat(options.LibraryPaths).ToArray();
        var libraries = ResolveProjectLibraries(projectPath, manifest);
        var plugins = (manifest.Plugins ?? [])
            .Select(plugin => plugin.Trim())
            .Where(plugin => plugin.Length > 0)
            .Concat(options.Plugins)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var targets = ResolveProjectTargets(options, manifest);
        if (options.OutputPath is not null && targets.Length > 1)
        {
            throw new InvalidOperationException("--out can only be used with a single target. Use project outputs for multi-target builds.");
        }
        if (options.RuntimeAbiOutputPath is not null && targets.Length > 1)
        {
            throw new InvalidOperationException("--runtime-abi-out can only be used with a single target.");
        }
        if (options.SymbolsOutputPath is not null && targets.Length > 1)
        {
            throw new InvalidOperationException("--symbols-out can only be used with a single target.");
        }
        if (options.CapacityReport && targets.Length > 1)
        {
            throw new InvalidOperationException("--capacity-report can only be used with a single target.");
        }

        return targets
            .Select(target => new RetroSharpBuildInput(
                source,
                projectDirectory,
                target,
                options.OutputPath ?? BuildOutputWriter.ResolveProjectOutputPath(projectDirectory, ResolveProjectOutput(manifest, target)),
                options.RuntimeAbiOutputPath,
                options.SymbolsOutputPath,
                libraryPaths,
                libraries,
                projectPath,
                plugins,
                options.CapacityReport))
            .ToArray();
    }

    private static string[] ResolveProjectLibraries(string projectPath, RetroSharpProjectManifest manifest)
    {
        var libraries = manifest.Libraries ?? [];
        for (var i = 0; i < libraries.Length; i++)
        {
            libraries[i] = libraries[i].Trim();
            if (libraries[i].Length == 0)
            {
                throw new InvalidOperationException($"RetroSharp project '{projectPath}' declares an empty library import.");
            }
        }

        return libraries;
    }

    private static string[] ResolveProjectTargets(CliOptions options, RetroSharpProjectManifest manifest)
    {
        if (!string.IsNullOrWhiteSpace(options.Target))
        {
            return [options.Target];
        }

        if (manifest.Targets is { Length: > 0 })
        {
            return manifest.Targets.Select(NormalizeTarget).ToArray();
        }

        if (string.IsNullOrWhiteSpace(manifest.Target))
        {
            throw new ArgumentException(MissingTargetMessage);
        }

        return [NormalizeTarget(manifest.Target)];
    }

    private static string NormalizeTarget(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            throw new InvalidOperationException("RetroSharp project declares an empty target.");
        }

        return target.Trim().ToLowerInvariant();
    }

    private static string? ResolveProjectOutput(RetroSharpProjectManifest manifest, string target)
    {
        if (manifest.Outputs is not null)
        {
            foreach (var output in manifest.Outputs)
            {
                if (string.Equals(output.Key, target, StringComparison.OrdinalIgnoreCase))
                {
                    return output.Value;
                }
            }
        }

        return manifest.Output ?? manifest.OutputPath;
    }

    private static bool IsProjectFile(string path)
    {
        var fileName = Path.GetFileName(path);
        return string.Equals(fileName, "retrosharp.json", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".retrosharp.json", StringComparison.OrdinalIgnoreCase);
    }
}
