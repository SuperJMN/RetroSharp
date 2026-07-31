namespace RetroSharp.NES.Tests;

using RetroSharp.NES;
using RetroSharp.Sdk;
using Xunit;

public sealed class NesRunnerJumpScrollCadenceTests
{
    [Fact]
    public void Shared_runner_grounded_scroll_stays_continuous_in_both_directions()
    {
        var build = RetroSharp.NES.NesRomCompiler.CompileSourceWithReport(
            RunnerSample.CompiledSource(),
            RunnerSample.Directory,
            sdkLibraryImports: [SdkImportResolver.Portable2D]);
        var variables = build.Report.UserVariables.ToDictionary(variable => variable.Name, StringComparer.Ordinal);
        var addresses = new RunnerAddresses(
            variables["player.x"].Address,
            variables["player.grounded"].Address,
            variables["view.x"].Address);
        var cpu = new NesTestCpu(build.Rom);
        RunUntilWordEquals(
            cpu,
            NesRuntimeMemoryLayout.PackedCamera.VisibleCameraYLow,
            80,
            maxFrames: 400);

        cpu.Held.Add("right");
        cpu.Held.Add("b");
        var right = ObserveGroundedScrollUntil(
            cpu,
            addresses,
            () => Word(cpu, addresses.PlayerX) >= 430,
            maxFrames: 400);
        Assert.True(Word(cpu, addresses.PlayerX) >= 430, "The runner did not reach the grounded reversal checkpoint.");

        cpu.Held.Clear();
        cpu.RunFrames(cpu.PhysicalFrames + 1);
        cpu.Held.Add("left");
        cpu.Held.Add("b");
        var left = ObserveGroundedScrollUntil(
            cpu,
            addresses,
            () => Word(cpu, addresses.PlayerX) <= 300,
            maxFrames: 240);

        AssertSmoothCadence("right", right);
        AssertSmoothCadence("left", left);
    }

    [Fact]
    public void Shared_runner_first_wall_jump_keeps_one_tick_per_physical_frame()
    {
        var build = RetroSharp.NES.NesRomCompiler.CompileSourceWithReport(
            RunnerSample.CompiledSource(),
            RunnerSample.Directory,
            sdkLibraryImports: [SdkImportResolver.Portable2D]);
        var variables = build.Report.UserVariables.ToDictionary(variable => variable.Name, StringComparer.Ordinal);
        var addresses = new RunnerAddresses(
            variables["player.x"].Address,
            variables["player.grounded"].Address,
            variables["view.x"].Address);
        var cpu = new NesTestCpu(build.Rom);
        RunUntilWordEquals(
            cpu,
            NesRuntimeMemoryLayout.PackedCamera.VisibleCameraYLow,
            80,
            maxFrames: 400);

        cpu.Held.Add("right");
        cpu.Held.Add("b");
        var reachedFirstWall = false;
        for (var frame = 0; frame < 400; frame++)
        {
            cpu.RunFrames(cpu.PhysicalFrames + 1);
            if (Word(cpu, addresses.PlayerX) >= 430
                && cpu.Ram(addresses.PlayerGrounded) != 0)
            {
                reachedFirstWall = true;
                break;
            }
        }

        Assert.True(reachedFirstWall, "The NES runner did not reach the first jump wall.");

        cpu.Held.Clear();
        cpu.RunFrames(cpu.PhysicalFrames + 1);
        cpu.Held.Add("right");
        cpu.Held.Add("b");
        cpu.Held.Add("a");
        var frames = new List<FirstWallCadenceObservation>();
        var previousHardwareScrollX = HardwareScrollX(cpu);
        var previousRequestedCameraX = Word(cpu, addresses.CameraX);
        var targetRequestedCameraX = previousRequestedCameraX + 30;
        var previousGameplayTicks = cpu.Ram(NesRuntimeMemoryLayout.WorldPack.GameplayTickCount);
        var previousAudioTicks = cpu.Ram(NesRuntimeMemoryLayout.WorldPack.AudioTickCount);
        var inputReleased = false;
        var settledFrames = 0;
        for (var frame = 0; frame < 200; frame++)
        {
            cpu.RunFrames(cpu.PhysicalFrames + 1);
            var hardwareScrollX = HardwareScrollX(cpu);
            var requestedCameraX = Word(cpu, addresses.CameraX);
            var gameplayTicks = cpu.Ram(NesRuntimeMemoryLayout.WorldPack.GameplayTickCount);
            var audioTicks = cpu.Ram(NesRuntimeMemoryLayout.WorldPack.AudioTickCount);
            frames.Add(new FirstWallCadenceObservation(
                frame,
                requestedCameraX,
                (requestedCameraX - previousRequestedCameraX + 512) % 512,
                hardwareScrollX,
                (hardwareScrollX - previousHardwareScrollX + 512) % 512,
                cpu.Ram(addresses.PlayerGrounded) != 0,
                unchecked((byte)(gameplayTicks - previousGameplayTicks)),
                unchecked((byte)(audioTicks - previousAudioTicks))));

            if (!inputReleased && requestedCameraX >= targetRequestedCameraX)
            {
                cpu.Held.Clear();
                inputReleased = true;
            }

            settledFrames = inputReleased
                            && requestedCameraX == previousRequestedCameraX
                            && hardwareScrollX == requestedCameraX
                ? settledFrames + 1
                : 0;

            previousHardwareScrollX = hardwareScrollX;
            previousRequestedCameraX = requestedCameraX;
            previousGameplayTicks = gameplayTicks;
            previousAudioTicks = audioTicks;
            if (settledFrames >= 2)
            {
                break;
            }
        }
        var trace = string.Join(
            ";",
            frames.Select(frame =>
                $"{frame.Frame}:hw{frame.HardwareScrollX}/req{frame.RequestedCameraX}"
                + $"/delta{frame.HardwareScrollDelta},{frame.RequestedCameraDelta}"
                + $"/ground{(frame.Grounded ? 1 : 0)}"
                + $"/ticks{frame.GameplayTickDelta},{frame.AudioTickDelta}"));
        Assert.NotEmpty(frames);
        Assert.Contains(frames, frame => frame.RequestedCameraX >= targetRequestedCameraX);
        Assert.All(
            frames,
            frame => Assert.True(
                frame.GameplayTickDelta == 1 && frame.AudioTickDelta == 1,
                $"The runner skipped a physical gameplay/audio tick: {trace}."));
        var visibleFreezeRun = 0;
        var maximumVisibleFreezeRun = 0;
        foreach (var frame in frames)
        {
            visibleFreezeRun = !frame.Grounded
                               && frame.RequestedCameraDelta > 0
                               && frame.HardwareScrollDelta == 0
                ? visibleFreezeRun + 1
                : 0;
            maximumVisibleFreezeRun = Math.Max(maximumVisibleFreezeRun, visibleFreezeRun);
        }

        Assert.True(
            maximumVisibleFreezeRun == 0,
            $"The airborne camera visibly froze while the requested camera kept advancing: {trace}.");
        Assert.Equal(frames[^1].RequestedCameraX, frames[^1].HardwareScrollX);
    }

    private static int HardwareScrollX(NesTestCpu cpu) =>
        cpu.ScrollX + ((cpu.PpuControl & 0x01) != 0 ? 256 : 0);

    private static IReadOnlyList<DirectionalCadenceObservation> ObserveGroundedScrollUntil(
        NesTestCpu cpu,
        RunnerAddresses addresses,
        Func<bool> stop,
        int maxFrames)
    {
        var observations = new List<DirectionalCadenceObservation>();
        var previousHardwareScrollX = HardwareScrollX(cpu);
        var previousRequestedCameraX = Word(cpu, addresses.CameraX);
        for (var frame = 0; frame < maxFrames && !stop(); frame++)
        {
            cpu.RunFrames(cpu.PhysicalFrames + 1);
            var hardwareScrollX = HardwareScrollX(cpu);
            var requestedCameraX = Word(cpu, addresses.CameraX);
            var requestedDelta = SignedDelta(requestedCameraX, previousRequestedCameraX);
            if (cpu.Ram(addresses.PlayerGrounded) != 0 && requestedDelta != 0)
            {
                observations.Add(new DirectionalCadenceObservation(
                    frame,
                    requestedCameraX,
                    requestedDelta,
                    hardwareScrollX,
                    SignedDelta(hardwareScrollX, previousHardwareScrollX)));
            }

            previousHardwareScrollX = hardwareScrollX;
            previousRequestedCameraX = requestedCameraX;
        }

        return observations;
    }

    private static void AssertSmoothCadence(string direction, IReadOnlyList<DirectionalCadenceObservation> observations)
    {
        Assert.NotEmpty(observations);
        Assert.True(
            observations.All(frame =>
                frame.HardwareScrollDelta != 0
                && Math.Sign(frame.HardwareScrollDelta) == Math.Sign(frame.RequestedCameraDelta)
                && Math.Abs(frame.HardwareScrollDelta) <= Math.Abs(frame.RequestedCameraDelta) + 1
                && Math.Abs(SignedDelta(frame.HardwareScrollX, frame.RequestedCameraX)) <= 1),
            $"Grounded {direction} scroll visibly froze, reversed, jumped, or accumulated lag: "
            + string.Join(
                ";",
                observations
                    .Where(frame =>
                        frame.HardwareScrollDelta == 0
                        || Math.Sign(frame.HardwareScrollDelta) != Math.Sign(frame.RequestedCameraDelta)
                        || Math.Abs(frame.HardwareScrollDelta) > Math.Abs(frame.RequestedCameraDelta) + 1
                        || Math.Abs(SignedDelta(frame.HardwareScrollX, frame.RequestedCameraX)) > 1)
                    .Select(frame =>
                        $"{frame.Frame}:hw{frame.HardwareScrollX}/req{frame.RequestedCameraX}"
                        + $"/delta{frame.HardwareScrollDelta},{frame.RequestedCameraDelta}")));
    }

    private static int SignedDelta(int current, int previous)
    {
        var delta = (current - previous + 512) % 512;
        return delta > 256 ? delta - 512 : delta;
    }

    private static int Word(NesTestCpu cpu, ushort lowAddress) =>
        cpu.Ram(lowAddress) | cpu.Ram(checked((ushort)(lowAddress + 1))) << 8;

    private static void RunUntilWordEquals(NesTestCpu cpu, ushort lowAddress, int expected, int maxFrames)
    {
        for (var frame = 0; frame < maxFrames; frame++)
        {
            if (Word(cpu, lowAddress) == expected)
            {
                return;
            }

            cpu.RunFrames(cpu.PhysicalFrames + 1);
        }

        Assert.Fail($"NES RAM word ${lowAddress:X4} did not reach {expected} within {maxFrames} frames.");
    }

    private sealed record RunnerAddresses(
        ushort PlayerX,
        ushort PlayerGrounded,
        ushort CameraX);

    private sealed record FirstWallCadenceObservation(
        int Frame,
        int RequestedCameraX,
        int RequestedCameraDelta,
        int HardwareScrollX,
        int HardwareScrollDelta,
        bool Grounded,
        int GameplayTickDelta,
        int AudioTickDelta);

    private sealed record DirectionalCadenceObservation(
        int Frame,
        int RequestedCameraX,
        int RequestedCameraDelta,
        int HardwareScrollX,
        int HardwareScrollDelta);
}
