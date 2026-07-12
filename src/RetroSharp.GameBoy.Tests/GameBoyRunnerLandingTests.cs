namespace RetroSharp.GameBoy.Tests;

using RetroSharp.GameBoy;
using Xunit;

public sealed class GameBoyRunnerLandingTests
{
    [Fact]
    public void Shared_runner_stays_on_the_authored_stage1_floor_for_300_frames()
    {
        var runnerDirectory = RunnerSample.Directory;
        var rom = GameBoyRomCompiler.CompileSource(RunnerSample.CompiledSource(), runnerDirectory);
        var cpu = new GameBoyTestCpu(rom) { CycleAccurateLy = true };

        RunUntilWordEquals(cpu, 0xC14F, 176, maxFrames: 400);

        for (var frame = 0; frame < 300; frame++)
        {
            cpu.RunAdditionalFrames(1);

            var cameraY = cpu.Wram(0xC14F) | cpu.Wram(0xC150) << 8;
            var playerWorldY = cpu.Oam(0xFE00) - 16 + cameraY;

            Assert.True(
                playerWorldY == 273 && playerWorldY + 31 == 304,
                $"Mario left the authored floor on frame {frame}: playerY={playerWorldY}, footY={playerWorldY + 31}, "
                + $"cameraY={cameraY}, oamY={cpu.Oam(0xFE00)}.");
        }
    }

    [Fact]
    public void Shared_runner_jump_leaves_and_lands_once_on_the_same_stage1_floor()
    {
        var runnerDirectory = RunnerSample.Directory;
        var rom = GameBoyRomCompiler.CompileSource(RunnerSample.CompiledSource(), runnerDirectory);
        var cpu = new GameBoyTestCpu(rom) { CycleAccurateLy = true };

        RunUntilWordEquals(cpu, 0xC14F, 176, maxFrames: 400);
        var floorOamY = cpu.Oam(0xFE00);
        var oamYByFrame = new List<byte>(400);

        for (var frame = 0; frame < 400; frame++)
        {
            if (frame < 20)
            {
                cpu.Held.Add("a");
            }
            else
            {
                cpu.Held.Remove("a");
            }

            cpu.RunAdditionalFrames(1);
            oamYByFrame.Add(cpu.Oam(0xFE00));
        }

        Assert.True(oamYByFrame.Min() < floorOamY, "The scripted A press never left the floor.");

        var leftFloor = false;
        var landingFrames = new List<int>();
        for (var frame = 0; frame < oamYByFrame.Count; frame++)
        {
            if (oamYByFrame[frame] != floorOamY)
            {
                leftFloor = true;
            }
            else if (leftFloor && (frame == 0 || oamYByFrame[frame - 1] != floorOamY))
            {
                landingFrames.Add(frame);
            }
        }

        var landingFrame = Assert.Single(landingFrames);
        Assert.All(oamYByFrame.Skip(landingFrame), y => Assert.Equal(floorOamY, y));
    }

    private static void RunUntilWordEquals(GameBoyTestCpu cpu, ushort lowAddress, ushort expected, int maxFrames)
    {
        for (var frame = 0; frame < maxFrames; frame++)
        {
            var actual = cpu.Wram(lowAddress) | cpu.Wram((ushort)(lowAddress + 1)) << 8;
            if (actual == expected)
            {
                return;
            }

            cpu.RunAdditionalFrames(1);
        }

        Assert.Fail($"WRAM word 0x{lowAddress:X4} did not reach {expected} within {maxFrames} frames.");
    }

}
