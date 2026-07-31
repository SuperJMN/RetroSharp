namespace RetroSharp.NES.Tests;

using Xunit;

public sealed class NesRunnerSmokeTests
{
    [Fact]
    public void Runner_builds_boots_and_keeps_rendering_without_unsafe_writes()
    {
        var projectPath = RepositoryFile("samples/runner/runner.retrosharp.json");
        var romPath = Path.Combine(Path.GetTempPath(), $"retrosharp-runner-{Guid.NewGuid():N}.nes");
        try
        {
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = RetroSharp.Cli.CliRunner.Run(
                ["--target", "nes", "--out", romPath, projectPath],
                stdout,
                stderr);

            Assert.True(exitCode == 0, stdout.ToString() + stderr);
            var cpu = new NesTestCpu(File.ReadAllBytes(romPath));

            for (var frame = 1; frame <= 900 && !HasStartedRendering(cpu); frame++)
            {
                cpu.RunFrames(frame);
            }

            Assert.True(HasStartedRendering(cpu), "Runner did not enable rendering and complete Video.WaitVBlank within 900 frames.");
            var frameBefore = cpu.PhysicalFrames;
            var waitsBefore = cpu.VBlankWaitCompletions;
            var resetsBefore = cpu.ResetCount;

            cpu.RunFrames(frameBefore + 120);

            Assert.True(cpu.VBlankWaitCompletions > waitsBefore, "Runner stopped completing Video.WaitVBlank after boot.");
            Assert.Equal(resetsBefore, cpu.ResetCount);
            var unsafePpuWrites = cpu.PpuWrites
                .Where(write => write.RenderingEnabled && cpu.PpuTiming(write.Cycle, true).Phase != "vblank")
                .ToArray();
            var unsafeOamWrites = cpu.OamWrites
                .Where(write => write.RenderingEnabled && cpu.PpuTiming(write.Cycle, true).Phase != "vblank")
                .ToArray();
            Assert.True(
                unsafePpuWrites.Length == 0,
                string.Join(Environment.NewLine, unsafePpuWrites.Take(8).Select(write =>
                {
                    var timing = cpu.PpuTiming(write.Cycle, true);
                    return $"${write.Register:X4} at {timing.Phase} {timing.Scanline}:{timing.Dot}";
                })));
            Assert.True(
                unsafeOamWrites.Length == 0,
                string.Join(Environment.NewLine, unsafeOamWrites.Take(8).Select(write =>
                {
                    var timing = cpu.PpuTiming(write.Cycle, true);
                    return $"${write.Address:X4} at {timing.Phase} {timing.Scanline}:{timing.Dot}";
                })));
        }
        finally
        {
            File.Delete(romPath);
        }
    }

    private static bool HasStartedRendering(NesTestCpu cpu) =>
        cpu.RenderingEnabled && cpu.VBlankWaitCompletions > 0;

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
}
