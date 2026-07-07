namespace RetroSharp.Core.Tests;

using System.Text.Json;
using Xunit;

public sealed class SampleApiQuarantineTests
{
    private static readonly string[] PortableForbiddenCalls =
    [
        "scroll_set(",
        "sprite_set(",
        "camera_move_right(",
        "camera_move_left(",
        "camera_span_",
        "map_stream_column(",
        "tilemap_set(",
        "tilemap_fill(",
        "Palette.Set(",
        "ObjectPalette.Set(",
        "Sprite.Set(",
        "Scroll.Set(",
        "Tilemap.Set(",
        "Tilemap.Fill(",
        "Palette.Set(",
        "ObjectPalette.Set(",
    ];

    [Fact]
    public void Sample_manifest_classifies_every_source_sample()
    {
        var manifest = LoadManifest();
        var librarySources = SampleLibrarySources(manifest).ToHashSet(StringComparer.Ordinal);
        var sampleSources = Directory.GetFiles(RepositoryDirectory("samples"), "*.rs", SearchOption.AllDirectories)
            .Select(path => RelativePath(path))
            .Where(path => !librarySources.Contains(path))
            .Order(StringComparer.Ordinal)
            .ToArray();

        var manifestSources = manifest.Samples.SelectMany(SampleSourcePaths)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(sampleSources, manifestSources);
    }

    [Fact]
    public void Portable_samples_do_not_use_transitional_or_target_intrinsic_calls()
    {
        var manifest = LoadManifest();

        foreach (var sample in manifest.Samples.Where(sample => sample.Layer == "portable-sdk"))
        {
            foreach (var sourcePath in SampleSourcePaths(sample))
            {
                var source = File.ReadAllText(RepositoryFile(sourcePath));

                foreach (var forbiddenCall in PortableForbiddenCalls)
                {
                    Assert.DoesNotContain(forbiddenCall, source, StringComparison.Ordinal);
                }
            }
        }
    }

    [Fact]
    public void Runner_sample_is_project_for_game_boy_and_nes()
    {
        var manifest = LoadManifest();
        var runnerSamples = manifest.Samples
            .Where(sample => sample.Path.StartsWith("samples/runner/runner", StringComparison.Ordinal))
            .ToArray();

        var runner = Assert.Single(runnerSamples);
        Assert.Equal("samples/runner/runner.retrosharp.json", runner.Path);
        Assert.Null(runner.LibraryPaths);
        Assert.Equal(new[] { "gb", "nes" }, runner.Targets);
    }

    [Fact]
    public void Sample_readmes_mark_their_layer()
    {
        var manifest = LoadManifest();

        foreach (var sample in manifest.Samples)
        {
            var readme = File.ReadAllText(RepositoryFile(sample.Readme));

            Assert.Contains($"Sample Layer: `{sample.Layer}`", readme, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Source_library_package_sample_declares_local_library_path_and_import()
    {
        var manifest = LoadManifest();
        var sample = Assert.Single(
            manifest.Samples,
            sample => sample.Path == "samples/source-library-package/source-library.retrosharp.json");

        Assert.Equal("portable-sdk", sample.Layer);
        Assert.Equal(new[] { "gb", "nes" }, sample.Targets);
        Assert.Equal(new[] { "samples/source-library-package/lib" }, sample.LibraryPaths);

        using var projectJson = JsonDocument.Parse(File.ReadAllText(RepositoryFile(sample.Path)));
        var project = projectJson.RootElement;
        Assert.Equal(new[] { "src/main.rs" }, project.GetProperty("sources").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.Equal(new[] { "lib" }, project.GetProperty("libraryPaths").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.Equal(new[] { "Acme.Timing" }, project.GetProperty("libraries").EnumerateArray().Select(value => value.GetString()).ToArray());

        using var libraryJson = JsonDocument.Parse(File.ReadAllText(RepositoryFile("samples/source-library-package/lib/acme-timing/retrosharp-library.json")));
        var library = libraryJson.RootElement;
        Assert.Equal("Acme.Timing", library.GetProperty("import").GetString());
        Assert.Equal("physical", library.GetProperty("namespaceMode").GetString());
        Assert.Equal("Acme.Timing", library.GetProperty("rootNamespace").GetString());
        Assert.Equal("src", library.GetProperty("sourceRoot").GetString());
        Assert.Equal(new[] { "gb", "nes" }, library.GetProperty("targets").EnumerateArray().Select(value => value.GetString()).ToArray());
    }

    private static SampleManifest LoadManifest()
    {
        var json = File.ReadAllText(RepositoryFile("samples/manifest.json"));
        var manifest = JsonSerializer.Deserialize<SampleManifest>(json, JsonOptions);

        return manifest ?? throw new InvalidOperationException("samples/manifest.json is empty.");
    }

    private static IEnumerable<string> SampleLibrarySources(SampleManifest manifest)
    {
        foreach (var libraryPath in manifest.Samples.SelectMany(sample => sample.LibraryPaths ?? []))
        {
            foreach (var libraryManifestPath in SampleLibraryManifestPaths(libraryPath))
            {
                var json = File.ReadAllText(libraryManifestPath);
                var library = JsonSerializer.Deserialize<SampleLibraryManifest>(json, JsonOptions)
                    ?? throw new InvalidOperationException($"{RelativePath(libraryManifestPath)} is empty.");
                var directory = Path.GetDirectoryName(libraryManifestPath)
                    ?? throw new InvalidOperationException($"Could not resolve directory for '{libraryManifestPath}'.");

                foreach (var source in library.Sources)
                {
                    yield return RelativePath(Path.GetFullPath(Path.Combine(directory, source)));
                }
            }
        }
    }

    private static IEnumerable<string> SampleLibraryManifestPaths(string libraryPath)
    {
        var root = RepositoryPath(libraryPath);
        var directManifest = Path.Combine(root, "retrosharp-library.json");
        if (File.Exists(directManifest))
        {
            yield return directManifest;
            yield break;
        }

        foreach (var directory in Directory.EnumerateDirectories(root).Order(StringComparer.Ordinal))
        {
            var childManifest = Path.Combine(directory, "retrosharp-library.json");
            if (File.Exists(childManifest))
            {
                yield return childManifest;
            }
        }
    }

    private static IEnumerable<string> SampleSourcePaths(SampleManifestEntry sample)
    {
        if (!IsProjectFile(sample.Path))
        {
            yield return sample.Path;
            yield break;
        }

        var projectPath = RepositoryFile(sample.Path);
        var json = File.ReadAllText(projectPath);
        var project = JsonSerializer.Deserialize<SampleProjectManifest>(json, JsonOptions)
            ?? throw new InvalidOperationException($"{sample.Path} is empty.");
        var directory = Path.GetDirectoryName(projectPath)
            ?? throw new InvalidOperationException($"Could not resolve directory for '{projectPath}'.");

        foreach (var source in project.Sources)
        {
            yield return RelativePath(Path.GetFullPath(Path.Combine(directory, source)));
        }
    }

    private static bool IsProjectFile(string path)
    {
        var fileName = Path.GetFileName(path);
        return string.Equals(fileName, "retrosharp.json", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".retrosharp.json", StringComparison.OrdinalIgnoreCase);
    }

    private static string RepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"Could not find repository file '{relativePath}'.");
    }

    private static string RepositoryDirectory(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"Could not find repository directory '{relativePath}'.");
    }

    private static string RepositoryPath(string relativePath)
    {
        var path = Path.Combine(RepositoryRoot(), relativePath);
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            throw new InvalidOperationException($"Could not find repository path '{relativePath}'.");
        }

        return path;
    }

    private static string RelativePath(string absolutePath)
    {
        var root = RepositoryRoot();
        return Path.GetRelativePath(root, absolutePath).Replace(Path.DirectorySeparatorChar, '/');
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "RetroSharp.sln")) &&
                Directory.Exists(Path.Combine(directory.FullName, "samples")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find repository root.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private sealed record SampleManifest(SampleManifestEntry[] Samples);

    private sealed record SampleManifestEntry(string Path, string Readme, string Layer, string[] Targets, string[]? LibraryPaths = null);

    private sealed record SampleLibraryManifest(string[] Sources);

    private sealed record SampleProjectManifest(string[] Sources);
}
