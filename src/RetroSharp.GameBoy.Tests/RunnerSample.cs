namespace RetroSharp.GameBoy.Tests;

using System.Text.Json;

internal static class RunnerSample
{
    public static string Directory => LocateDirectory();

    public static string SourcePath => Path.Combine(Directory, "src", "main.rs");

    public static string ProjectPath => Path.Combine(Directory, "runner.retrosharp.json");

    public static string FlattenedSource()
    {
        var directory = Directory;
        var json = File.ReadAllText(ProjectPath);
        var project = JsonSerializer.Deserialize<RunnerProject>(json, JsonOptions)
            ?? throw new InvalidOperationException($"{ProjectPath} is empty.");
        var parts = project.Sources.Select(name => File.ReadAllText(Path.Combine(directory, name)));
        return string.Join(Environment.NewLine + Environment.NewLine, parts);
    }

    public static string CompiledSource()
    {
        var directory = Directory;
        var json = File.ReadAllText(ProjectPath);
        var project = JsonSerializer.Deserialize<RunnerProject>(json, JsonOptions)
            ?? throw new InvalidOperationException($"{ProjectPath} is empty.");
        var sourceFiles = project.Sources
            .Select(name =>
            {
                var path = Path.Combine(directory, name);
                return new RetroSharp.Sdk.PhysicalNamespaceSourceFile(path, File.ReadAllText(path));
            })
            .ToArray();
        if (string.IsNullOrWhiteSpace(project.NamespaceMode))
        {
            var parts = project.Sources.Select(name => File.ReadAllText(Path.Combine(directory, name)));
            return string.Join(Environment.NewLine + Environment.NewLine, parts);
        }

        if (!string.Equals(project.NamespaceMode, "physical", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{ProjectPath} declares unsupported namespaceMode '{project.NamespaceMode}'.");
        }

        var rootNamespace = string.IsNullOrWhiteSpace(project.RootNamespace)
            ? "Runner"
            : project.RootNamespace;
        var sourceRoot = Path.Combine(directory, project.SourceRoot ?? "src");
        return RetroSharp.Sdk.PhysicalNamespaceSourceComposer.Compose(sourceFiles, rootNamespace, sourceRoot);
    }

    private static string LocateDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "samples", "runner");
            if (File.Exists(Path.Combine(candidate, "runner.retrosharp.json")))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate samples/runner/runner.retrosharp.json from the test output directory.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private sealed record RunnerProject(
        string[] Sources,
        string? RootNamespace = null,
        string? SourceRoot = null,
        string? NamespaceMode = null);
}
