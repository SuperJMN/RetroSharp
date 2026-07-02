namespace RetroSharp.NES.Tests;

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

    private sealed record RunnerProject(string[] Sources);
}
