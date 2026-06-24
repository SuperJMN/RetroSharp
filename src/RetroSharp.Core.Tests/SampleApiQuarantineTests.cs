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
        "palette_set(",
        "object_palette_set(",
        "sprite.Set(",
        "scroll.Set(",
        "tilemap.Set(",
        "tilemap.Fill(",
        "palette.Set(",
        "objectPalette.Set(",
    ];

    [Fact]
    public void Sample_manifest_classifies_every_source_sample()
    {
        var manifest = LoadManifest();
        var sampleSources = Directory.GetFiles(RepositoryDirectory("samples"), "*.rs", SearchOption.AllDirectories)
            .Select(path => RelativePath(path))
            .Order(StringComparer.Ordinal)
            .ToArray();

        var manifestSources = manifest.Samples.Select(sample => sample.Path)
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
            var source = File.ReadAllText(RepositoryFile(sample.Path));

            foreach (var forbiddenCall in PortableForbiddenCalls)
            {
                Assert.DoesNotContain(forbiddenCall, source, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void Runner_sample_is_single_source_for_game_boy_and_nes()
    {
        var manifest = LoadManifest();
        var runnerSamples = manifest.Samples
            .Where(sample => sample.Path.StartsWith("samples/runner/runner", StringComparison.Ordinal))
            .ToArray();

        var runner = Assert.Single(runnerSamples);
        Assert.Equal("samples/runner/runner.rs", runner.Path);
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

    private static SampleManifest LoadManifest()
    {
        var json = File.ReadAllText(RepositoryFile("samples/manifest.json"));
        var manifest = JsonSerializer.Deserialize<SampleManifest>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        });

        return manifest ?? throw new InvalidOperationException("samples/manifest.json is empty.");
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

    private sealed record SampleManifest(SampleManifestEntry[] Samples);

    private sealed record SampleManifestEntry(string Path, string Readme, string Layer, string[] Targets);
}
