namespace RetroSharp.Cli.Tests;

using Xunit;

/// <summary>
/// Unit tests for <see cref="ProjectBuildInputResolver"/> in isolation. This resolution logic
/// used to be a set of static local functions nested inside <c>CliRunner.Run</c> and could only
/// be reached by driving the whole CLI end-to-end.
/// </summary>
public sealed class ProjectBuildInputResolverTests
{
    [Fact]
    public void Resolve_throws_when_the_input_path_is_missing()
    {
        var options = new CliOptions(
            InputPath: null,
            OutputPath: null,
            RuntimeAbiOutputPath: null,
            SymbolsOutputPath: null,
            Target: "gb",
            LibraryPaths: [],
            Plugins: [],
            WorldBudgetReport: false,
            CapacityReport: false);

        var error = Assert.Throws<ArgumentException>(() => ProjectBuildInputResolver.Resolve(options));

        Assert.Equal("No source file has been specified", error.Message);
    }

    [Fact]
    public void Resolve_of_a_plain_source_file_without_a_target_reports_the_missing_target_message()
    {
        using var workspace = TemporaryWorkspace();
        var source = Path.Combine(workspace.Path, "probe.rs");
        File.WriteAllText(source, "void Main() { }");
        var options = new CliOptions(
            InputPath: source,
            OutputPath: null,
            RuntimeAbiOutputPath: null,
            SymbolsOutputPath: null,
            Target: null,
            LibraryPaths: [],
            Plugins: [],
            WorldBudgetReport: false,
            CapacityReport: false);

        var error = Assert.Throws<ArgumentException>(() => ProjectBuildInputResolver.Resolve(options));

        Assert.Equal("No target has been specified. Use --target nes or --target gb.", error.Message);
    }

    [Fact]
    public void Resolve_of_a_plain_source_file_reads_the_file_and_carries_the_options_through()
    {
        using var workspace = TemporaryWorkspace();
        var source = Path.Combine(workspace.Path, "probe.rs");
        File.WriteAllText(source, "void Main() { }");
        var options = new CliOptions(
            InputPath: source,
            OutputPath: "out.gb",
            RuntimeAbiOutputPath: null,
            SymbolsOutputPath: null,
            Target: "gb",
            LibraryPaths: ["libs"],
            Plugins: ["RetroSharp.Sdk.Plugins.Platformer2D"],
            WorldBudgetReport: false,
            CapacityReport: false);

        var buildInputs = ProjectBuildInputResolver.Resolve(options);

        var buildInput = Assert.Single(buildInputs);
        Assert.Equal("void Main() { }", buildInput.Source);
        Assert.Equal(workspace.Path, buildInput.BaseDirectory);
        Assert.Equal("gb", buildInput.Target);
        Assert.Equal("out.gb", buildInput.OutputPath);
        Assert.Equal(["libs"], buildInput.LibraryPaths);
        Assert.Equal(["RetroSharp.Sdk.Plugins.Platformer2D"], buildInput.Plugins);
        Assert.Equal(source, buildInput.PrimaryPath);
    }

    [Fact]
    public void Resolve_of_a_multi_target_project_rejects_out_with_more_than_one_target()
    {
        using var workspace = TemporaryWorkspace();
        var source = Path.Combine(workspace.Path, "probe.rs");
        var project = Path.Combine(workspace.Path, "probe.retrosharp.json");
        File.WriteAllText(source, "void Main() { }");
        File.WriteAllText(
            project,
            """
            {
              "targets": [ "gb", "nes" ],
              "sources": [ "probe.rs" ]
            }
            """);
        var options = new CliOptions(
            InputPath: project,
            OutputPath: "probe.gb",
            RuntimeAbiOutputPath: null,
            SymbolsOutputPath: null,
            Target: null,
            LibraryPaths: [],
            Plugins: [],
            WorldBudgetReport: false,
            CapacityReport: false);

        var error = Assert.Throws<InvalidOperationException>(() => ProjectBuildInputResolver.Resolve(options));

        Assert.Equal(
            "--out can only be used with a single target. Use project outputs for multi-target builds.",
            error.Message);
    }

    private static TemporaryDirectory TemporaryWorkspace()
    {
        var path = Path.Combine(Path.GetTempPath(), "retrosharp-cli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return new TemporaryDirectory(path);
    }

    private sealed class TemporaryDirectory(string path) : IDisposable
    {
        public string Path { get; } = path;

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
