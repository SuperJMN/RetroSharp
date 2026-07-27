namespace RetroSharp.NES.Tests;

using RetroSharp.NES;
using RetroSharp.Sdk;
using Xunit;

public sealed class NesRunnerJumpScrollCadenceTests
{
    private const int ObservationFrames = 80;
    private const int JumpHeldFrames = 40;

    [Fact]
    public void Shared_runner_jump_adds_no_horizontal_scroll_stall()
    {
        var build = RetroSharp.NES.NesRomCompiler.CompileSourceWithReport(
            RunnerSample.CompiledSource(),
            RunnerSample.Directory,
            sdkLibraryImports: [SdkImportResolver.Portable2D]);
        var variables = build.Report.UserVariables.ToDictionary(variable => variable.Name, StringComparer.Ordinal);
        var addresses = new RunnerAddresses(
            variables["player.x"].Address,
            variables["player.y"].Address,
            variables["player.grounded"].Address,
            variables["view.x"].Address,
            variables["view.y"].Address);

        var reference = RunScenario(build.Rom, addresses, jump: false);
        var reproduction = RunScenario(build.Rom, addresses, jump: true);

        Assert.True(
            reproduction.MaximumPlayerY < reproduction.InitialPlayerY
            && reproduction.MaximumRequestedCameraY != reproduction.InitialRequestedCameraY,
            $"The jump did not activate vertical camera follow: {reproduction.Summary}.");
        Assert.True(
            reproduction.HorizontalStalls <= reference.HorizontalStalls,
            $"Jump scrolling added horizontal stalls: reference={reference.Summary}; jump={reproduction.Summary}.");
        Assert.True(
            reproduction.MaximumGameplayGap <= reference.MaximumGameplayGap,
            $"Jump scrolling added a gameplay gap: reference={reference.Summary}; jump={reproduction.Summary}.");
        Assert.True(
            reproduction.MaximumAudioGap <= reference.MaximumAudioGap,
            $"Jump scrolling added an audio gap: reference={reference.Summary}; jump={reproduction.Summary}.");
        Assert.InRange(reproduction.MaximumRequestedVisibleLag, 0, 3);
        Assert.InRange(reproduction.MaximumBacklogRun, 0, 1);
        Assert.True(reproduction.LifecycleDrained, $"Jump lifecycle did not drain: {reproduction.Summary}.");
        Assert.Equal(0, reproduction.ForbiddenVideoWork);
    }

    private static ScenarioResult RunScenario(byte[] rom, RunnerAddresses addresses, bool jump)
    {
        var cpu = new NesTestCpu(rom);
        RunUntilWordEquals(
            cpu,
            NesRuntimeMemoryLayout.PackedCamera.VisibleCameraYLow,
            80,
            maxFrames: 400);
        ReachElevatedCheckpoint(cpu, addresses);

        var initialPlayerY = Word(cpu, addresses.PlayerY);
        var initialRequestedCameraY = Word(cpu, addresses.CameraY);
        var initialRequests = cpu.Ram(NesRuntimeMemoryLayout.PackedCamera.RequestCount);
        var initialPrepares = cpu.Ram(NesRuntimeMemoryLayout.PackedCamera.PrepareCount);
        var initialResidents = cpu.Ram(NesRuntimeMemoryLayout.PackedCamera.ResidentCount);
        var initialCommits = cpu.Ram(NesRuntimeMemoryLayout.PackedCamera.CommitCount);
        var initialReleases = cpu.Ram(NesRuntimeMemoryLayout.PackedCamera.ReleaseCount);
        var frames = new List<FrameObservation>(ObservationFrames);
        var previousGameplayTicks = cpu.Ram(NesRuntimeMemoryLayout.WorldPack.GameplayTickCount);
        var previousAudioTicks = cpu.Ram(NesRuntimeMemoryLayout.WorldPack.AudioTickCount);
        var gameplayGap = 0;
        var audioGap = 0;
        var maximumGameplayGap = 0;
        var maximumAudioGap = 0;
        cpu.Held.Add("right");
        cpu.Held.Add("b");

        for (var frame = 0; frame < ObservationFrames; frame++)
        {
            if (jump && frame < JumpHeldFrames)
            {
                cpu.Held.Add("a");
            }
            else
            {
                cpu.Held.Remove("a");
            }

            cpu.RunFrames(cpu.PhysicalFrames + 1);
            var gameplayTicks = cpu.Ram(NesRuntimeMemoryLayout.WorldPack.GameplayTickCount);
            var audioTicks = cpu.Ram(NesRuntimeMemoryLayout.WorldPack.AudioTickCount);
            gameplayGap = gameplayTicks == previousGameplayTicks ? gameplayGap + 1 : 0;
            audioGap = audioTicks == previousAudioTicks ? audioGap + 1 : 0;
            maximumGameplayGap = Math.Max(maximumGameplayGap, gameplayGap);
            maximumAudioGap = Math.Max(maximumAudioGap, audioGap);
            previousGameplayTicks = gameplayTicks;
            previousAudioTicks = audioTicks;
            frames.Add(new FrameObservation(
                frame,
                cpu.ScrollX + ((cpu.PpuControl & 0x01) != 0 ? 256 : 0),
                cpu.ScrollY + ((cpu.PpuControl & 0x02) != 0 ? 240 : 0),
                Word(cpu, addresses.CameraX),
                Word(cpu, addresses.CameraY),
                CameraWord(cpu, NesRuntimeMemoryLayout.Camera.X, NesRuntimeMemoryLayout.Camera.XHigh),
                CameraWord(cpu, NesRuntimeMemoryLayout.Camera.Y, NesRuntimeMemoryLayout.Camera.YHigh),
                Word(cpu, NesRuntimeMemoryLayout.PackedCamera.VisibleCameraXLow),
                Word(cpu, NesRuntimeMemoryLayout.PackedCamera.VisibleCameraYLow),
                Word(cpu, addresses.PlayerX),
                Word(cpu, addresses.PlayerY),
                gameplayTicks,
                audioTicks,
                cpu.Ram(NesRuntimeMemoryLayout.PackedCamera.RequestCount),
                cpu.Ram(NesRuntimeMemoryLayout.PackedCamera.ResidentCount),
                cpu.Ram(NesRuntimeMemoryLayout.PackedCamera.CommitCount),
                cpu.Ram(NesRuntimeMemoryLayout.PackedCamera.ReleaseCount),
                cpu.Ram(NesRuntimeMemoryLayout.Camera.X) & 0x07,
                cpu.Ram(NesRuntimeMemoryLayout.PackedCamera.PrefetchedColumnDirection),
                cpu.Ram(NesRuntimeMemoryLayout.PackedCamera.PendingAxes),
                cpu.Ram(NesRuntimeMemoryLayout.Camera.ScrollApplied)));
        }

        var horizontalStalls = frames
            .Zip(frames.Skip(1), (previous, current) => (previous, current))
            .Count(pair =>
                pair.current.ScrollApplied == (byte)NesCameraPublicationState.SuppressedForCurrentTick
                &&
                pair.current.RequestedCameraX > pair.previous.RequestedCameraX
                && pair.current.RequestedCameraX > pair.current.LogicalCameraX
                && pair.current.LogicalCameraX == pair.previous.LogicalCameraX);
        var stableFrames = frames
            .Where(frame => frame.ScrollApplied == (byte)NesCameraPublicationState.SuppressedForCurrentTick)
            .ToArray();
        var maximumRequestedVisibleLag = stableFrames.Max(frame =>
            Math.Max(
                Math.Abs(frame.RequestedCameraX - frame.VisibleCameraX),
                Math.Abs(frame.RequestedCameraY - frame.VisibleCameraY)));
        var backlogRun = 0;
        var maximumBacklogRun = 0;
        foreach (var frame in stableFrames)
        {
            var lag = Math.Max(
                Math.Abs(frame.RequestedCameraX - frame.VisibleCameraX),
                Math.Abs(frame.RequestedCameraY - frame.VisibleCameraY));
            backlogRun = lag > 2 ? backlogRun + 1 : 0;
            maximumBacklogRun = Math.Max(maximumBacklogRun, backlogRun);
        }
        var requests = Difference(cpu.Ram(NesRuntimeMemoryLayout.PackedCamera.RequestCount), initialRequests);
        var prepares = Difference(cpu.Ram(NesRuntimeMemoryLayout.PackedCamera.PrepareCount), initialPrepares);
        var residents = Difference(cpu.Ram(NesRuntimeMemoryLayout.PackedCamera.ResidentCount), initialResidents);
        var commits = Difference(cpu.Ram(NesRuntimeMemoryLayout.PackedCamera.CommitCount), initialCommits);
        var releases = Difference(cpu.Ram(NesRuntimeMemoryLayout.PackedCamera.ReleaseCount), initialReleases);
        var forbiddenVideoWork = new[]
        {
            NesRuntimeMemoryLayout.PackedCamera.BankWorkInCommit,
            NesRuntimeMemoryLayout.PackedCamera.DirectoryWorkInCommit,
            NesRuntimeMemoryLayout.PackedCamera.DecodeWorkInCommit,
        }.Sum(address => cpu.Ram(address));

        return new ScenarioResult(
            initialPlayerY,
            initialRequestedCameraY,
            frames.Min(frame => frame.PlayerY),
            frames.Min(frame => frame.RequestedCameraY),
            horizontalStalls,
            maximumGameplayGap,
            maximumAudioGap,
            maximumRequestedVisibleLag,
            maximumBacklogRun,
            requests == prepares && prepares == residents && residents == commits && commits == releases,
            forbiddenVideoWork,
            requests,
            prepares,
            residents,
            commits,
            releases,
            string.Join(
                ";",
                frames.Zip(frames.Skip(1), (previous, current) => (previous, current))
                    .Where(pair =>
                        pair.current.ScrollApplied == (byte)NesCameraPublicationState.SuppressedForCurrentTick
                        &&
                        pair.current.RequestedCameraX > pair.previous.RequestedCameraX
                        && pair.current.RequestedCameraX > pair.current.LogicalCameraX
                        && pair.current.LogicalCameraX == pair.previous.LogicalCameraX)
                    .Select(pair =>
                        $"{pair.current.Frame}:hw{pair.current.HardwareScrollX}/req{pair.current.RequestedCameraX}"
                        + $"/log{pair.current.LogicalCameraX}/vis{pair.current.VisibleCameraX}"
                        + $"/fine{pair.current.FineX}/latch{pair.current.ColumnLatch}"
                        + $"/pending{pair.current.PendingAxes}/apply{pair.current.ScrollApplied}"
                        + $"/life{pair.current.Requests},{pair.current.Residents},{pair.current.Commits},{pair.current.Releases}")));
    }

    private static int Difference(byte current, byte initial) => unchecked((byte)(current - initial));

    private static void ReachElevatedCheckpoint(NesTestCpu cpu, RunnerAddresses addresses)
    {
        cpu.Held.Add("right");
        cpu.Held.Add("b");
        for (var frame = 0; frame < 2_000; frame++)
        {
            if (frame % 120 < JumpHeldFrames)
            {
                cpu.Held.Add("a");
            }
            else
            {
                cpu.Held.Remove("a");
            }

            cpu.RunFrames(cpu.PhysicalFrames + 1);
            if (Word(cpu, addresses.PlayerY) == 209 && cpu.Ram(addresses.PlayerGrounded) != 0)
            {
                cpu.Held.Clear();
                return;
            }
        }

        Assert.Fail("The NES runner did not reach the deterministic elevated jump checkpoint.");
    }

    private static int CameraWord(NesTestCpu cpu, byte lowAddress, ushort highAddress) =>
        cpu.Ram(lowAddress) | cpu.Ram(highAddress) << 8;

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
        ushort PlayerY,
        ushort PlayerGrounded,
        ushort CameraX,
        ushort CameraY);

    private sealed record FrameObservation(
        int Frame,
        int HardwareScrollX,
        int HardwareScrollY,
        int RequestedCameraX,
        int RequestedCameraY,
        int LogicalCameraX,
        int LogicalCameraY,
        int VisibleCameraX,
        int VisibleCameraY,
        int PlayerX,
        int PlayerY,
        int GameplayTicks,
        int AudioTicks,
        int Requests,
        int Residents,
        int Commits,
        int Releases,
        int FineX,
        int ColumnLatch,
        int PendingAxes,
        int ScrollApplied);

    private sealed record ScenarioResult(
        int InitialPlayerY,
        int InitialRequestedCameraY,
        int MaximumPlayerY,
        int MaximumRequestedCameraY,
        int HorizontalStalls,
        int MaximumGameplayGap,
        int MaximumAudioGap,
        int MaximumRequestedVisibleLag,
        int MaximumBacklogRun,
        bool LifecycleDrained,
        int ForbiddenVideoWork,
        int Requests,
        int Prepares,
        int Residents,
        int Commits,
        int Releases,
        string StallFrames)
    {
        public string Summary =>
            $"stalls={HorizontalStalls}, gameplayGap={MaximumGameplayGap}, audioGap={MaximumAudioGap}, "
            + $"lag={MaximumRequestedVisibleLag}, backlogRun={MaximumBacklogRun}, playerY={InitialPlayerY}->{MaximumPlayerY}, "
            + $"cameraY={InitialRequestedCameraY}->{MaximumRequestedCameraY}, "
            + $"lifecycle={Requests}/{Prepares}/{Residents}/{Commits}/{Releases}, forbidden={ForbiddenVideoWork}, "
            + $"stallFrames=[{StallFrames}]";
    }
}
