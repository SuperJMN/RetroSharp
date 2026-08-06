namespace RetroSharp.Cli.Tests;

using Xunit;

/// <summary>
/// Unit tests for <see cref="TargetBuildExecutor"/>'s validation branches, constructed directly
/// from a <see cref="RetroSharpBuildInput"/> instead of through full CLI argument parsing and
/// compilation. Before RetroSharp #548 these branches lived inside a static local function and
/// could only be reached by driving the whole CLI end-to-end.
/// </summary>
public sealed class TargetBuildExecutorTests
{
    [Fact]
    public void Unsupported_target_reports_error_and_exit_code_1()
    {
        var buildInput = CreateBuildInput(target: "z80");
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = TargetBuildExecutor.Execute(buildInput, stdout, stderr);

        Assert.Equal(1, exitCode);
        Assert.Equal(
            "Unknown target 'z80'. Supported targets: nes, gb" + Environment.NewLine,
            stderr.ToString());
        Assert.Empty(stdout.ToString());
    }

    [Fact]
    public void Runtime_abi_out_is_rejected_for_non_nes_targets_before_compiling()
    {
        var buildInput = CreateBuildInput(target: "gb", runtimeAbiOutputPath: "probe.runtime-abi.json");
        var stderr = new StringWriter();

        var exitCode = TargetBuildExecutor.Execute(buildInput, new StringWriter(), stderr);

        Assert.Equal(1, exitCode);
        Assert.Equal(
            "--runtime-abi-out is only supported for target nes." + Environment.NewLine,
            stderr.ToString());
    }

    [Fact]
    public void Capacity_report_is_rejected_for_non_nes_targets_before_compiling()
    {
        var buildInput = CreateBuildInput(target: "gb", capacityReport: true);
        var stderr = new StringWriter();

        var exitCode = TargetBuildExecutor.Execute(buildInput, new StringWriter(), stderr);

        Assert.Equal(1, exitCode);
        Assert.Equal(
            "--capacity-report is only supported for target nes." + Environment.NewLine,
            stderr.ToString());
    }

    private static RetroSharpBuildInput CreateBuildInput(
        string target,
        string? runtimeAbiOutputPath = null,
        bool capacityReport = false)
    {
        return new RetroSharpBuildInput(
            Source: "void Main() { }",
            BaseDirectory: null,
            Target: target,
            OutputPath: null,
            RuntimeAbiOutputPath: runtimeAbiOutputPath,
            SymbolsOutputPath: null,
            LibraryPaths: [],
            LibraryImports: [],
            PrimaryPath: "probe.rs",
            Plugins: [],
            CapacityReport: capacityReport);
    }
}
