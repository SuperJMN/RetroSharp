namespace RetroSharp.GameBoy.Tests;

using Xunit;

public sealed class GameBoyRunnerSmokeTests
{
    [Fact]
    public void Runner_builds_boots_and_keeps_rendering_without_unsafe_writes()
    {
        var projectPath = RepositoryFile("samples/runner/runner.retrosharp.json");
        var romPath = Path.Combine(Path.GetTempPath(), $"retrosharp-runner-{Guid.NewGuid():N}.gb");
        try
        {
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = RetroSharp.Cli.CliRunner.Run(
                ["--target", "gb", "--out", romPath, projectPath],
                stdout,
                stderr);

            Assert.True(exitCode == 0, stdout.ToString() + stderr);
            var cpu = new GameBoyTestCpu(File.ReadAllBytes(romPath))
            {
                CycleAccurateLy = true,
                EnforceVblankVramWrites = true,
            };

            for (var frame = 1; frame <= 900 && !HasStartedRendering(cpu); frame++)
            {
                cpu.RunFrames(frame);
            }

            Assert.True(HasStartedRendering(cpu), "Runner did not enable rendering and complete Video.WaitVBlank within 900 frames.");
            var waitsBefore = cpu.VBlankWaitCompletions;
            var resetsBefore = cpu.ResetCount;

            cpu.RunAdditionalFrames(120);

            Assert.True(cpu.VBlankWaitCompletions > waitsBefore, "Runner stopped completing Video.WaitVBlank after boot.");
            Assert.Equal(resetsBefore, cpu.ResetCount);
            Assert.DoesNotContain(cpu.VramWrites, write => write.LcdEnabled && !write.Applied);
            Assert.DoesNotContain(cpu.OamWrites, write => write.LcdEnabled && write.Ly is < 144 or > 153);
        }
        finally
        {
            File.Delete(romPath);
        }
    }

    private static bool HasStartedRendering(GameBoyTestCpu cpu) =>
        (cpu.IoRegister(0xFF40) & 0x80) != 0 && cpu.VBlankWaitCompletions > 0;

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
