namespace RetroSharp.Cli.Tests;

using Xunit;

/// <summary>
/// Unit tests for <see cref="GbsToGbApuCommandLineParser"/> in isolation. This parser used to be
/// a static local function nested inside <c>CliRunner.Run</c> and could only be exercised by
/// invoking the whole gbs-to-gbapu subcommand end-to-end with real files.
/// </summary>
public sealed class GbsToGbApuCommandLineParserTests
{
    [Fact]
    public void Missing_in_throws()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            GbsToGbApuCommandLineParser.Parse(["--out", "trace.gbapu.json"]));

        Assert.Equal("GBS to GBAPU export requires --in <file.gbs>.", error.Message);
    }

    [Fact]
    public void Missing_out_throws()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            GbsToGbApuCommandLineParser.Parse(["--in", "stage.gbs"]));

        Assert.Equal("GBS to GBAPU export requires --out <file.gbapu.json>.", error.Message);
    }

    [Fact]
    public void Unknown_option_throws()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            GbsToGbApuCommandLineParser.Parse(["--bogus"]));

        Assert.Equal("Unknown gbs-to-gbapu option '--bogus'.", error.Message);
    }

    [Theory]
    [InlineData("--in")]
    [InlineData("--out")]
    [InlineData("-o")]
    [InlineData("--subsong")]
    [InlineData("--seconds")]
    [InlineData("--loop-cycle")]
    [InlineData("--gbsplay")]
    public void Value_option_without_a_value_throws(string option)
    {
        var error = Assert.Throws<ArgumentException>(() =>
            GbsToGbApuCommandLineParser.Parse([option]));

        Assert.Equal($"{option} requires a value.", error.Message);
    }

    [Fact]
    public void Non_positive_subsong_throws()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            GbsToGbApuCommandLineParser.Parse(["--in", "stage.gbs", "--out", "stage.gbapu.json", "--subsong", "0"]));

        Assert.Equal("--subsong requires a positive integer.", error.Message);
    }

    [Fact]
    public void Non_positive_seconds_throws()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            GbsToGbApuCommandLineParser.Parse(["--in", "stage.gbs", "--out", "stage.gbapu.json", "--seconds", "-1"]));

        Assert.Equal("--seconds requires a positive integer.", error.Message);
    }

    [Fact]
    public void Negative_loop_cycle_throws()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            GbsToGbApuCommandLineParser.Parse(["--in", "stage.gbs", "--out", "stage.gbapu.json", "--loop-cycle", "-1"]));

        Assert.Equal("--loop-cycle requires a non-negative integer.", error.Message);
    }

    [Fact]
    public void Defaults_match_the_documented_gbapu_export_defaults()
    {
        var options = GbsToGbApuCommandLineParser.Parse(["--in", "stage.gbs", "--out", "stage.gbapu.json"]);

        Assert.Equal("stage.gbs", options.InputPath);
        Assert.Equal("stage.gbapu.json", options.OutputPath);
        Assert.Equal(1, options.Subsong);
        Assert.Equal(60, options.Seconds);
        Assert.Equal(0, options.LoopCycle);
        Assert.Equal("gbsplay", options.GbsPlayPath);
        Assert.True(options.AutoLoop);
        Assert.False(options.EmitJson);
    }

    [Fact]
    public void No_auto_loop_and_emit_json_flags_are_honored()
    {
        var options = GbsToGbApuCommandLineParser.Parse([
            "--in", "stage.gbs",
            "--out", "stage.gbapu.json",
            "--no-auto-loop",
            "--emit-json",
            "--gbsplay", "tools/gbsplay",
        ]);

        Assert.False(options.AutoLoop);
        Assert.True(options.EmitJson);
        Assert.Equal("tools/gbsplay", options.GbsPlayPath);
    }
}
