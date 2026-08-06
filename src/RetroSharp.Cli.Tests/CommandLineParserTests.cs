namespace RetroSharp.Cli.Tests;

using Xunit;

/// <summary>
/// Unit tests for <see cref="CommandLineParser"/> in isolation, without invoking
/// <see cref="CliRunner.Run(string[])"/> or touching the file system. Before RetroSharp #548
/// this parser was a static local function nested inside a 716-line method and could not be
/// exercised without going through the full build pipeline.
/// </summary>
public sealed class CommandLineParserTests
{
    [Fact]
    public void Unknown_option_throws()
    {
        var error = Assert.Throws<ArgumentException>(() => CommandLineParser.Parse(["--bogus"]));

        Assert.Equal("Unknown option '--bogus'.", error.Message);
    }

    [Fact]
    public void Target_without_a_value_throws()
    {
        var error = Assert.Throws<ArgumentException>(() => CommandLineParser.Parse(["--target"]));

        Assert.Equal("--target requires a value.", error.Message);
    }

    [Theory]
    [InlineData("--out")]
    [InlineData("-o")]
    public void Out_without_a_value_throws(string option)
    {
        var error = Assert.Throws<ArgumentException>(() => CommandLineParser.Parse([option]));

        Assert.Equal($"{option} requires a value.", error.Message);
    }

    [Fact]
    public void Lib_path_without_a_value_throws()
    {
        var error = Assert.Throws<ArgumentException>(() => CommandLineParser.Parse(["--lib-path"]));

        Assert.Equal("--lib-path requires a value.", error.Message);
    }

    [Fact]
    public void Runtime_abi_out_without_a_value_throws()
    {
        var error = Assert.Throws<ArgumentException>(() => CommandLineParser.Parse(["--runtime-abi-out"]));

        Assert.Equal("--runtime-abi-out requires a value.", error.Message);
    }

    [Fact]
    public void Symbols_out_without_a_value_throws()
    {
        var error = Assert.Throws<ArgumentException>(() => CommandLineParser.Parse(["--symbols-out"]));

        Assert.Equal("--symbols-out requires a value.", error.Message);
    }

    [Fact]
    public void Sdk_plugin_without_a_value_throws()
    {
        var error = Assert.Throws<ArgumentException>(() => CommandLineParser.Parse(["--sdk-plugin"]));

        Assert.Equal("--sdk-plugin requires a value.", error.Message);
    }

    [Fact]
    public void Target_value_is_lowercased_without_being_validated()
    {
        var options = CommandLineParser.Parse(["--target", "Z80"]);

        // The parser only normalizes casing; deciding whether "z80" is a supported target is the
        // build executor's job, not the option parser's.
        Assert.Equal("z80", options.Target);
    }

    [Fact]
    public void Parses_flags_and_repeated_list_options()
    {
        var options = CommandLineParser.Parse([
            "--target", "nes",
            "--lib-path", "libs/one",
            "--lib-path", "libs/two",
            "--sdk-plugin", "plugin.one",
            "--sdk-plugin", "plugin.two",
            "--world-budget-report",
            "--capacity-report",
            "source.rs",
        ]);

        Assert.Equal("nes", options.Target);
        Assert.Equal(["libs/one", "libs/two"], options.LibraryPaths);
        Assert.Equal(["plugin.one", "plugin.two"], options.Plugins);
        Assert.True(options.WorldBudgetReport);
        Assert.True(options.CapacityReport);
        Assert.Equal("source.rs", options.InputPath);
    }

    [Fact]
    public void Only_the_first_positional_argument_becomes_the_input_path()
    {
        var options = CommandLineParser.Parse(["first.rs", "second.rs"]);

        Assert.Equal("first.rs", options.InputPath);
    }

    [Fact]
    public void No_arguments_produce_a_null_input_path_and_default_options()
    {
        var options = CommandLineParser.Parse([]);

        Assert.Null(options.InputPath);
        Assert.Null(options.OutputPath);
        Assert.Null(options.RuntimeAbiOutputPath);
        Assert.Null(options.SymbolsOutputPath);
        Assert.Null(options.Target);
        Assert.Empty(options.LibraryPaths);
        Assert.Empty(options.Plugins);
        Assert.False(options.WorldBudgetReport);
        Assert.False(options.CapacityReport);
    }
}
