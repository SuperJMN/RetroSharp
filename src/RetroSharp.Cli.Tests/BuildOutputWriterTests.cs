namespace RetroSharp.Cli.Tests;

using Xunit;

/// <summary>
/// Unit tests for <see cref="BuildOutputWriter"/>'s pure path-resolution helpers. These used to
/// be static local functions nested inside <c>CliRunner.Run</c> and could not be exercised
/// without writing files through the whole CLI pipeline.
/// </summary>
public sealed class BuildOutputWriterTests
{
    [Fact]
    public void Default_output_path_replaces_the_retrosharp_json_suffix()
    {
        var buildInput = CreateBuildInput(Path.Combine("project", "game.retrosharp.json"));

        var outputPath = BuildOutputWriter.DefaultOutputPath(buildInput, ".nes");

        Assert.Equal(Path.Combine("project", "game.nes"), outputPath);
    }

    [Fact]
    public void Default_output_path_changes_the_extension_of_a_plain_source_file()
    {
        var buildInput = CreateBuildInput(Path.Combine("project", "probe.rs"));

        var outputPath = BuildOutputWriter.DefaultOutputPath(buildInput, ".gb");

        Assert.Equal(Path.Combine("project", "probe.gb"), outputPath);
    }

    [Fact]
    public void Resolve_project_output_path_returns_null_for_a_blank_output()
    {
        var resolved = BuildOutputWriter.ResolveProjectOutputPath("/project", "   ");

        Assert.Null(resolved);
    }

    [Fact]
    public void Resolve_project_output_path_combines_a_relative_output_with_the_project_directory()
    {
        var projectDirectory = Path.GetFullPath("project");

        var resolved = BuildOutputWriter.ResolveProjectOutputPath(projectDirectory, Path.Combine("bin", "game.nes"));

        Assert.Equal(Path.GetFullPath(Path.Combine(projectDirectory, "bin", "game.nes")), resolved);
    }

    [Fact]
    public void Resolve_project_output_path_keeps_a_rooted_output_as_is()
    {
        var rooted = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "elsewhere", "game.nes"));

        var resolved = BuildOutputWriter.ResolveProjectOutputPath("/project", rooted);

        Assert.Equal(rooted, resolved);
    }

    private static RetroSharpBuildInput CreateBuildInput(string primaryPath)
    {
        return new RetroSharpBuildInput(
            Source: "void Main() { }",
            BaseDirectory: null,
            Target: "gb",
            OutputPath: null,
            RuntimeAbiOutputPath: null,
            SymbolsOutputPath: null,
            LibraryPaths: [],
            LibraryImports: [],
            PrimaryPath: primaryPath,
            Plugins: [],
            CapacityReport: false);
    }
}
