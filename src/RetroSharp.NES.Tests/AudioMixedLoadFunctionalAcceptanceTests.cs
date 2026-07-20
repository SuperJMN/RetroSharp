namespace RetroSharp.NES.Tests;

using RetroSharp.NES;
using Xunit;

public sealed class AudioMixedLoadFunctionalAcceptanceTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(8)]
    [InlineData(10)]
    [InlineData(12)]
    [InlineData(14)]
    [InlineData(16)]
    [InlineData(18)]
    [InlineData(20)]
    public void Exact_tracked_runner_services_gameplay_and_audio_on_every_physical_frame_during_airborne_load(int startDelay)
    {
        var rom = File.ReadAllBytes(RepositoryFile("samples/runner/bin/runner.nes"));

        var cpu = new NesTestCpu(rom);
        cpu.RunFrames(500 + startDelay);
        var expectedResetCount = cpu.ResetCount;

        for (var frame = 1; frame <= 120; frame++)
        {
            var gameplayBefore = cpu.Ram(NesRuntimeMemoryLayout.WorldPack.GameplayTickCount);
            var audioBefore = cpu.Ram(NesRuntimeMemoryLayout.WorldPack.AudioTickCount);
            cpu.Held.Clear();
            if (frame <= 40)
            {
                cpu.Held.Add("a");
            }

            cpu.RunFrames(500 + startDelay + frame);
            Assert.True(
                cpu.ResetCount == expectedResetCount,
                $"phase={startDelay} observationFrame={frame}: reset count changed from {expectedResetCount} to {cpu.ResetCount}.");
            Assert.True(
                (byte)(cpu.Ram(NesRuntimeMemoryLayout.WorldPack.GameplayTickCount) - gameplayBefore) == 1,
                $"phase={startDelay} observationFrame={frame}: gameplay delta was "
                + $"{(byte)(cpu.Ram(NesRuntimeMemoryLayout.WorldPack.GameplayTickCount) - gameplayBefore)}, expected 1.");
            Assert.True(
                (byte)(cpu.Ram(NesRuntimeMemoryLayout.WorldPack.AudioTickCount) - audioBefore) == 1,
                $"phase={startDelay} observationFrame={frame}: audio delta was "
                + $"{(byte)(cpu.Ram(NesRuntimeMemoryLayout.WorldPack.AudioTickCount) - audioBefore)}, expected 1.");
        }
    }

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
