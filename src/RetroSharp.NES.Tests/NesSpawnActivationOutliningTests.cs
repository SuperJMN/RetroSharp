namespace RetroSharp.NES.Tests;

using RetroSharp.Core.Sdk;
using RetroSharp.NES;
using RetroSharp.Sdk;
using Xunit;
using Xunit.Abstractions;

public sealed class NesSpawnActivationOutliningTests(ITestOutputHelper output)
{
    [Fact]
    public void Authored_spawn_content_grows_fixed_spawn_body_without_growing_the_hot_phase()
    {
        var one = Build("src/one.rs");
        var eight = Build("src/eight.rs");
        var oneHot = HotPhaseBytes(one.Report);
        var eightHot = HotPhaseBytes(eight.Report);

        output.WriteLine(
            $"one: hot={oneHot}, fixed={one.Report.FixedPayloadBytes}, program={one.Report.ProgramR6Bytes}");
        output.WriteLine(
            $"eight: hot={eightHot}, fixed={eight.Report.FixedPayloadBytes}, program={eight.Report.ProgramR6Bytes}");

        Assert.Equal(oneHot, eightHot);
        Assert.True(
            eight.Report.FixedPayloadBytes > one.Report.FixedPayloadBytes,
            "The extra authored spawn records should be paid by the fixed outlined body/tables.");
    }

    [Fact]
    public void Spawn_activation_is_a_fixed_resident_hot_outlined_body_called_directly_from_the_hot_bank()
    {
        var result = Build("src/eight.rs");
        var placement = Assert.IsType<NesProgramBankPlacementReport>(result.Report.BankPlacement);
        var outlined = Assert.Single(
            result.Report.OutlinedUserFunctions,
            function => function.Function == "__enemies_spawn_0_call0");

        output.WriteLine(
            $"outlined={outlined.Function} address=${outlined.CpuAddress:X4}, phase={outlined.Phase}, callSites={outlined.CallSites}");

        Assert.Equal(NesUserFunctionPhase.Hot, outlined.Phase);
        Assert.Equal(1, outlined.CallSites);
        Assert.True(outlined.OverridesInlineHint);
        Assert.InRange(outlined.CpuAddress, 0xC000, 0xFF7F);

        var hotBank = Assert.IsType<int>(placement.HotPhasePhysicalBank);
        var hotBankStart = 16 + hotBank * NesProgramBankPlanner.ProgramBankSize;
        var hotBankBytes = new ReadOnlySpan<byte>(
            result.Rom,
            hotBankStart,
            NesProgramBankPlanner.ProgramBankSize);
        var directCall = new byte[] { 0x20, (byte)outlined.CpuAddress, (byte)(outlined.CpuAddress >> 8) };

        Assert.Equal(1, CountSequence(hotBankBytes, directCall));
    }

    [Fact]
    public void Video_safe_report_counts_outlined_spawn_activation_before_camera_apply()
    {
        var cases = new[] { 1, 8, 16 }
            .Select(count =>
            {
                var result = Build($"src/prefix-{count}.rs");
                var videoSafe = Assert.Single(
                    result.Report.CpuWork.Windows,
                    window => window.Id == SdkCpuWorkWindowIds.VideoSafe);
                var observation = NesVideoSafeObserver.Observe(result.Rom, []);
                var observedCycles = videoSafe.Capacity - observation.MinimumVBlankSlack;
                output.WriteLine(
                    $"spawns={count}: reportUpper={videoSafe.KnownUpper}, observed={observedCycles}, " +
                    $"status={videoSafe.Status}, unsafePpu={observation.UnsafePpuWrites}");
                return (count, Reported: videoSafe.KnownUpper ?? videoSafe.KnownLower, observedCycles, videoSafe.Status);
            })
            .ToArray();

        Assert.True(cases[1].Reported > cases[0].Reported);
        Assert.True(cases[2].Reported > cases[1].Reported);
        Assert.All(cases, item => Assert.True(
            item.Reported >= item.observedCycles,
            $"spawns={item.count} reported {item.Reported} but observed {item.observedCycles}."));
        Assert.All(cases, item => Assert.NotEqual(SdkCpuWorkStatuses.Fits, item.Status));
    }

    [Fact]
    public void User_authored_nes_spawn_activation_marker_is_rejected()
    {
        const string source = """
                              import RetroSharp.Portable2D;

                              [__rs_nes_fixed_spawn_activation] void UserHotFunction()
                              {
                              }

                              void Main()
                              {
                                  Video.Init();
                                  while (true)
                                  {
                                      Video.WaitVBlank();
                                      UserHotFunction();
                                  }
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() =>
            RetroSharp.NES.NesRomCompiler.CompileSourceWithReport(
                source,
                sdkLibraryImports: [SdkImportResolver.Portable2D]));

        Assert.Equal(
            "NES reserved compiler attribute '[__rs_nes_fixed_spawn_activation]' cannot be used on user function 'UserHotFunction'.",
            exception.Message);
    }

    private static NesRomBuildResult Build(string relativeSource)
    {
        var directory = FixtureDirectory;
        return RetroSharp.NES.NesRomCompiler.CompileSourceForMmc3TvromCodeBankTestsWithReport(
            File.ReadAllText(Path.Combine(directory, relativeSource)),
            directory,
            sdkLibraryImports: [SdkImportResolver.Portable2D]);
    }

    private static int HotPhaseBytes(NesRomBuildReport report)
    {
        var placement = Assert.IsType<NesProgramBankPlacementReport>(report.BankPlacement);
        Assert.Equal(NesRomBuilder.MainFramePlacementUnitName, placement.HotPhaseUnitName);
        return placement.HotPhaseBytes;
    }

    private static int CountSequence(ReadOnlySpan<byte> bytes, ReadOnlySpan<byte> pattern)
    {
        var count = 0;
        for (var index = 0; index <= bytes.Length - pattern.Length; index++)
        {
            if (bytes[index..(index + pattern.Length)].SequenceEqual(pattern))
            {
                count++;
            }
        }

        return count;
    }

    private static string FixtureDirectory => RepositoryDirectory("validation/fixtures/nes-spawn-outline-v1");

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
}
