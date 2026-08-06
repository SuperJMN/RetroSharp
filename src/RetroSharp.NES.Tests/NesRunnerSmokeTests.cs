namespace RetroSharp.NES.Tests;

using Xunit;

public sealed class NesRunnerSmokeTests
{
    /// <summary>
    /// The idle case only exercises the sprite publication. Holding a direction makes the camera
    /// stream, which adds the packed background column commit to the same VBlank; that joint case
    /// is where the runner previously spilled onto rendered scanlines, so it is held here. The
    /// running jump adds the peak sprite count on top of a streaming camera, the scene reported
    /// in issue #477.
    /// </summary>
    [Theory]
    [InlineData]
    [InlineData("right")]
    [InlineData("right", "a")]
    [InlineData("right", "b")]
    [InlineData("left")]
    public void Runner_builds_boots_and_keeps_rendering_without_unsafe_writes(params string[] held)
    {
        var projectPath = RepositoryFile("samples/runner/runner.retrosharp.json");
        var romPath = Path.Combine(AppContext.BaseDirectory, $"retrosharp-runner-{Guid.NewGuid():N}.nes");
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
            cpu.Held.UnionWith(held);

            for (var frame = 1; frame <= 900 && !HasStartedRendering(cpu); frame++)
            {
                cpu.RunFrames(frame);
            }

            Assert.True(HasStartedRendering(cpu), "Runner did not enable rendering and complete Video.WaitVBlank within 900 frames.");
            var frameBefore = cpu.PhysicalFrames;
            var waitsBefore = cpu.VBlankWaitCompletions;
            var resetsBefore = cpu.ResetCount;
            var ppuWritesBefore = cpu.PpuWrites.Count;
            var oamWritesBefore = cpu.OamWrites.Count;

            cpu.RunFrames(frameBefore + 120);

            Assert.True(cpu.VBlankWaitCompletions > waitsBefore, "Runner stopped completing Video.WaitVBlank after boot.");
            Assert.Equal(resetsBefore, cpu.ResetCount);
            var unsafePpuWrites = cpu.PpuWrites
                .Skip(ppuWritesBefore)
                .Where(write => write.RenderingEnabled && cpu.PpuTiming(write.Cycle, true).Phase != "vblank")
                .ToArray();
            var unsafeOamWrites = cpu.OamWrites
                .Skip(oamWritesBefore)
                .Where(write => write.RenderingEnabled && cpu.PpuTiming(write.Cycle, true).Phase != "vblank")
                .ToArray();
            Assert.True(
                unsafePpuWrites.Length == 0,
                $"Holding [{string.Join(",", held)}] produced {unsafePpuWrites.Length} PPU writes outside VBlank: " +
                string.Join(Environment.NewLine, unsafePpuWrites.Take(8).Select(write =>
                {
                    var timing = cpu.PpuTiming(write.Cycle, true);
                    return $"${write.Register:X4} at {timing.Phase} {timing.Scanline}:{timing.Dot}";
                })));
            Assert.True(
                unsafeOamWrites.Length == 0,
                $"Holding [{string.Join(",", held)}] produced {unsafeOamWrites.Length} OAM writes outside VBlank: " +
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
