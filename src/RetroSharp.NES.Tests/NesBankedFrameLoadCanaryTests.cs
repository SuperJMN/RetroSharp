namespace RetroSharp.NES.Tests;

using RetroSharp.NES;
using RetroSharp.Sdk;
using Xunit;
using Xunit.Abstractions;

public sealed class NesBankedFrameLoadCanaryTests(ITestOutputHelper output)
{
    [Fact]
    public void Representative_fixed_and_code_banked_loads_boot_update_and_remain_video_safe()
    {
        var source = File.ReadAllText(Path.Combine(FixtureDirectory, "src", "main.rs"));
        var fixedBuild = RetroSharp.NES.NesRomCompiler.CompileSourceForMmc3TvromTestsWithReport(
            source,
            FixtureDirectory,
            sdkLibraryImports: [SdkImportResolver.Portable2D]);
        var bankedBuild = RetroSharp.NES.NesRomCompiler.CompileSourceForMmc3TvromCodeBankTestsWithReport(
            source,
            FixtureDirectory,
            sdkLibraryImports: [SdkImportResolver.Portable2D]);

        Assert.Equal("nes-mmc3-tvrom-v1", fixedBuild.Report.SelectedProfile);
        Assert.Equal(NesPhysicalFrameScheduler.CodeBankedProfileName, bankedBuild.Report.SelectedProfile);
        Assert.Single(
            bankedBuild.Report.Segments,
            segment => segment.Owner.StartsWith("program:r6:", StringComparison.Ordinal));

        var fixedObservation = Observe(fixedBuild);
        var bankedObservation = Observe(bankedBuild);
        output.WriteLine($"fixed: {fixedObservation}");
        output.WriteLine($"banked: {bankedObservation}");

        AssertSafe(fixedObservation, "fixed MMC3 control");
        AssertSafe(bankedObservation, "code-banked candidate");
    }

    private static FrameLoadObservation Observe(NesRomBuildResult build)
    {
        var cameraX = Assert.Single(
            build.Report.UserVariables,
            variable => variable.Name == "cameraX").Address;
        var cpu = new NesTestCpu(build.Rom);
        cpu.Held.UnionWith(["right", "b"]);
        cpu.TracedRamBytes.Add(NesRuntimeMemoryLayout.Camera.ScrollApplied);
        var frames = new List<FrameLoadFrame>();

        for (var frame = 0; frame < 240; frame++)
        {
            cpu.RunFrames(cpu.PhysicalFrames + 1);
            frames.Add(new FrameLoadFrame(
                frame,
                Word(cpu, cameraX),
                cpu.ScrollX + ((cpu.PpuControl & 0x01) != 0 ? 256 : 0)));
        }

        var stable = frames
            .Zip(frames.Skip(1), (previous, current) => (previous, current))
            .Where(pair => pair.previous.RequestedX >= 96 && pair.current.RequestedX <= 224)
            .Where(pair => pair.current.RequestedX != pair.previous.RequestedX)
            .ToArray();
        var transitions = stable
            .Select(pair => (pair.current.HardwareX - pair.previous.HardwareX + 512) % 512)
            .ToArray();
        var requestedTransitions = stable
            .Select(pair => pair.current.RequestedX - pair.previous.RequestedX)
            .ToArray();
        var unsafePpuWrites = cpu.PpuWrites.Count(write =>
            write.RenderingEnabled && cpu.PpuTiming(write.Cycle, true).Phase != "vblank");
        var unsafeOamWrites = cpu.OamWrites.Count(write =>
            write.RenderingEnabled && cpu.PpuTiming(write.Cycle, true).Phase != "vblank");
        var legacySuppressions = cpu.RamByteWrites.Count(write =>
            write.Address == NesRuntimeMemoryLayout.Camera.ScrollApplied &&
            write.Value == 0x80);

        return new FrameLoadObservation(
            stable.Length,
            requestedTransitions.Count(delta => delta != 2),
            transitions.Count(delta => delta == 0),
            transitions.Count(delta => delta > 2),
            legacySuppressions,
            cpu.ResetCount,
            unsafePpuWrites,
            unsafeOamWrites,
            cpu.ApuWrites.Count,
            string.Join(",", transitions));
    }

    private static void AssertSafe(FrameLoadObservation observation, string description)
    {
        Assert.InRange(observation.StableTransitions, 60, 65);
        Assert.True(observation.ApuWrites > 0, $"{description} did not update audio.");
        Assert.Equal(1, observation.ResetCount);
        Assert.Equal(0, observation.UnsafePpuWrites);
        Assert.Equal(0, observation.UnsafeOamWrites);
    }

    private static int Word(NesTestCpu cpu, ushort lowAddress) =>
        cpu.Ram(lowAddress) | cpu.Ram(checked((ushort)(lowAddress + 1))) << 8;

    private static string FixtureDirectory => RepositoryDirectory("validation/fixtures/nes-banked-frame-load-v1");

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

    private sealed record FrameLoadFrame(int Frame, int RequestedX, int HardwareX);

    private sealed record FrameLoadObservation(
        int StableTransitions,
        int UnexpectedRequestedTransitions,
        int Holds,
        int CatchUpJumps,
        int LegacySuppressions,
        int ResetCount,
        int UnsafePpuWrites,
        int UnsafeOamWrites,
        int ApuWrites,
        string HardwareTransitions)
    {
        public override string ToString() =>
            $"transitions={StableTransitions}, requestedErrors={UnexpectedRequestedTransitions}, "
            + $"holds={Holds}, jumps={CatchUpJumps}, legacySuppressions={LegacySuppressions}, "
            + $"resets={ResetCount}, unsafePpu={UnsafePpuWrites}, unsafeOam={UnsafeOamWrites}, "
            + $"apu={ApuWrites}, hardware=[{HardwareTransitions}]";
    }
}
