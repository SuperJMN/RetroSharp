namespace RetroSharp.NES.Tests;

using RetroSharp.NES;
using RetroSharp.Sdk;
using Xunit;
using Xunit.Abstractions;

public sealed class NesRunnerColumnPrepareMeasurementTests(ITestOutputHelper output)
{
    private const long FrameBudgetCycles = 29_781;
    private const int ObservationFrames = 80;
    private const int JumpHeldFrames = 40;

    [Fact]
    public void Shipping_runner_amortizes_column_prepare_below_one_physical_frame()
    {
        var build = RetroSharp.NES.NesRomCompiler.CompileSourceWithReport(
            RunnerSample.CompiledSource(),
            RunnerSample.Directory,
            sdkLibraryImports: [SdkImportResolver.Portable2D]);
        var variables = build.Report.UserVariables.ToDictionary(variable => variable.Name, StringComparer.Ordinal);
        var addresses = new RunnerAddresses(
            variables["player.y"].Address,
            variables["player.grounded"].Address,
            variables["view.x"].Address);
        var prepareEntry = build.Report.FixedSymbols[NesRomBuilder.WorldPackPrepareEdgeLabel];

        var first = RunMeasurementSet(build.Rom, addresses, prepareEntry);
        var second = RunMeasurementSet(build.Rom, addresses, prepareEntry);

        Assert.Equal(first.GroundedPrepareCycles, second.GroundedPrepareCycles);
        Assert.Equal(first.AirbornePrepareCycles, second.AirbornePrepareCycles);
        Assert.Equal(first.GroundedSuppressions, second.GroundedSuppressions);
        Assert.Equal(first.AirborneSuppressions, second.AirborneSuppressions);
        Assert.Equal(first.GroundedGameplayTicks, second.GroundedGameplayTicks);
        Assert.Equal(first.GroundedAudioTicks, second.GroundedAudioTicks);
        Assert.Equal(first.AirborneGameplayTicks, second.AirborneGameplayTicks);
        Assert.Equal(first.AirborneAudioTicks, second.AirborneAudioTicks);
        Assert.Equal(first.GroundedStableFrames, second.GroundedStableFrames);
        Assert.Equal(first.AirborneStableFrames, second.AirborneStableFrames);
        Assert.NotEmpty(first.GroundedPrepareCycles);
        Assert.NotEmpty(first.AirbornePrepareCycles);
        Assert.All(first.GroundedPrepareCycles, cycles => Assert.InRange(cycles, 1, FrameBudgetCycles - 1));
        Assert.All(first.AirbornePrepareCycles, cycles => Assert.InRange(cycles, 1, FrameBudgetCycles - 1));
        Assert.Equal(0, first.GroundedSuppressions);
        Assert.Equal(0, first.AirborneSuppressions);
        Assert.Equal(ObservationFrames, first.GroundedGameplayTicks);
        Assert.Equal(ObservationFrames, first.GroundedAudioTicks);
        Assert.Equal(ObservationFrames, first.AirborneGameplayTicks);
        Assert.Equal(ObservationFrames, first.AirborneAudioTicks);
        Assert.True(first.GroundedStableFrames > 0);
        Assert.True(first.AirborneStableFrames > 0);

        output.WriteLine(
            $"frameBudget={FrameBudgetCycles}; "
            + $"groundPrepare={Summary(first.GroundedPrepareCycles)}; "
            + $"airPrepare={Summary(first.AirbornePrepareCycles)}; "
            + $"groundSuppressions={first.GroundedSuppressions}; "
            + $"airSuppressions={first.AirborneSuppressions}; "
            + $"stableFrames={first.GroundedStableFrames}/{first.AirborneStableFrames}; "
            + "run1==run2");
    }

    [Theory]
    [InlineData("hscroll-offset.rs", 220, 190, 96)]
    [InlineData("hscroll-full.rs", 2_580, 2_525, 2_240)]
    public void Horizontal_camera_reversal_retains_one_pixel_physical_cadence(
        string sourceFile,
        int totalFrames,
        int traceStart,
        int maximumCameraX)
    {
        var sourcePath = RepositoryFile($"samples/tiled-hscroll/{sourceFile}");
        var directory = Path.GetDirectoryName(sourcePath)
                        ?? throw new InvalidOperationException("Could not locate the horizontal-scroll sample.");
        var build = RetroSharp.NES.NesRomCompiler.CompileSourceWithReport(
            File.ReadAllText(sourcePath),
            directory,
            sdkLibraryImports: [SdkImportResolver.Portable2D]);
        var cameraX = build.Report.UserVariables.Single(variable => variable.Name == "cameraX").Address;
        var cpu = new NesTestCpu(build.Rom);
        var frames = new List<ReversalFrame>();

        for (var frame = 0; frame < totalFrames; frame++)
        {
            cpu.RunFrames(cpu.PhysicalFrames + 1);
            if (frame >= traceStart)
            {
                frames.Add(new ReversalFrame(
                    frame,
                    cpu.ScrollX + ((cpu.PpuControl & 0x01) != 0 ? 256 : 0),
                    Word(cpu, cameraX),
                    CameraWord(
                        cpu,
                        NesRuntimeMemoryLayout.Camera.X,
                        NesRuntimeMemoryLayout.Camera.XHigh),
                    Word(cpu, NesRuntimeMemoryLayout.PackedCamera.VisibleCameraXLow),
                    cpu.Ram(NesRuntimeMemoryLayout.PackedCamera.PrefetchedColumnDirection),
                    cpu.Ram(NesRuntimeMemoryLayout.PackedCamera.PendingAxes),
                    cpu.Ram(NesRuntimeMemoryLayout.PackedCamera.Slot0 + NesPackedCameraRuntime.StateOffset),
                    cpu.Ram(NesRuntimeMemoryLayout.PackedCamera.Slot1 + NesPackedCameraRuntime.StateOffset),
                    cpu.Ram(NesRuntimeMemoryLayout.PackedCamera.RequestCount),
                    cpu.Ram(NesRuntimeMemoryLayout.PackedCamera.ResidentCount),
                    cpu.Ram(NesRuntimeMemoryLayout.PackedCamera.CommitCount)));
            }
        }

        Assert.All(
            frames,
            frame => Assert.InRange(frame.LogicalX, 0, maximumCameraX));
        var cadence = frames.Zip(frames.Skip(1), (previous, current) => (previous, current)).ToArray();
        Assert.All(
            cadence,
            pair => Assert.True(
                Math.Min(
                    (pair.current.HardwareX - pair.previous.HardwareX + 512) % 512,
                    (pair.previous.HardwareX - pair.current.HardwareX + 512) % 512) <= 1,
                $"Physical X changed by more than one pixel: {Trace(frames)}"));
    }

    private static Measurement RunMeasurementSet(
        byte[] rom,
        RunnerAddresses addresses,
        ushort prepareEntry)
    {
        var grounded = RunScenario(rom, addresses, prepareEntry, jump: false);
        var airborne = RunScenario(rom, addresses, prepareEntry, jump: true);
        return new Measurement(
            grounded.PrepareCycles,
            airborne.PrepareCycles,
            grounded.Suppressions,
            airborne.Suppressions,
            grounded.GameplayTicks,
            grounded.AudioTicks,
            airborne.GameplayTicks,
            airborne.AudioTicks,
            grounded.StableFrames,
            airborne.StableFrames);
    }

    private static ScenarioMeasurement RunScenario(
        byte[] rom,
        RunnerAddresses addresses,
        ushort prepareEntry,
        bool jump)
    {
        var cpu = new NesTestCpu(rom);
        RunUntilWordEquals(
            cpu,
            NesRuntimeMemoryLayout.PackedCamera.VisibleCameraYLow,
            80,
            maxFrames: 400);
        ReachElevatedCheckpoint(cpu, addresses);

        var tracker = new PrepareCycleTracker(prepareEntry);
        cpu.OnStep = tracker.Observe;
        var previousGameplayTicks = cpu.Ram(NesRuntimeMemoryLayout.WorldPack.GameplayTickCount);
        var previousAudioTicks = cpu.Ram(NesRuntimeMemoryLayout.WorldPack.AudioTickCount);
        var previousRequestedCameraX = Word(cpu, addresses.CameraX);
        var initialPlayerY = Word(cpu, addresses.PlayerY);
        var gameplayTicks = 0;
        var audioTicks = 0;
        var suppressions = 0;
        var stableFrames = 0;
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
            var currentGameplayTicks = cpu.Ram(NesRuntimeMemoryLayout.WorldPack.GameplayTickCount);
            var currentAudioTicks = cpu.Ram(NesRuntimeMemoryLayout.WorldPack.AudioTickCount);
            gameplayTicks += unchecked((byte)(currentGameplayTicks - previousGameplayTicks));
            audioTicks += unchecked((byte)(currentAudioTicks - previousAudioTicks));
            previousGameplayTicks = currentGameplayTicks;
            previousAudioTicks = currentAudioTicks;
            var requestedCameraX = Word(cpu, addresses.CameraX);
            var playerY = Word(cpu, addresses.PlayerY);
            var stable = requestedCameraX - previousRequestedCameraX == 2
                         && (jump
                             ? frame < JumpHeldFrames
                               && Word(cpu, NesRuntimeMemoryLayout.PackedCamera.VisibleCameraYLow) == 80
                               && playerY < initialPlayerY
                             : playerY == initialPlayerY);
            if (stable)
            {
                stableFrames++;
                if (cpu.Ram(NesRuntimeMemoryLayout.Camera.ScrollApplied)
                    == (byte)NesCameraPublicationState.SuppressedForCurrentTick)
                {
                    suppressions++;
                }
            }
            previousRequestedCameraX = requestedCameraX;
        }

        cpu.OnStep = null;
        tracker.AssertComplete();
        return new ScenarioMeasurement(
            tracker.Cycles.ToArray(),
            suppressions,
            gameplayTicks,
            audioTicks,
            stableFrames);
    }

    private static string Summary(IReadOnlyList<long> values) =>
        $"n={values.Count},mean={values.Average():F1},max={values.Max()}";

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

    private static int Word(NesTestCpu cpu, ushort lowAddress) =>
        cpu.Ram(lowAddress) | cpu.Ram(checked((ushort)(lowAddress + 1))) << 8;

    private static int CameraWord(NesTestCpu cpu, byte lowAddress, ushort highAddress) =>
        cpu.Ram(lowAddress) | cpu.Ram(highAddress) << 8;

    private static string RepositoryFile(string relativePath)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        return Path.Combine(root, relativePath);
    }

    private static string Trace(IEnumerable<ReversalFrame> frames) =>
        string.Join(
            "; ",
            frames.Select(frame =>
                $"{frame.Frame}:hw{frame.HardwareX}/req{frame.RequestedX}/log{frame.LogicalX}"
                + $"/vis{frame.VisibleX}/latch{frame.ColumnLatch:X2}/pending{frame.PendingAxes}"
                + $"/slots{frame.Slot0State},{frame.Slot1State}"
                + $"/life{frame.Requests},{frame.Residents},{frame.Commits}"));

    private sealed class PrepareCycleTracker(ushort entry)
    {
        private ActiveCall? active;

        internal List<long> Cycles { get; } = [];

        internal void Observe(NesCpuStep step)
        {
            if (active is { } call)
            {
                if (step.StackPointer == unchecked((byte)(call.EntryStackPointer + 2)))
                {
                    Cycles.Add(step.Cycle - call.StartCycle);
                    active = null;
                }
                return;
            }

            if (step.ProgramCounter == entry)
            {
                active = new ActiveCall(step.Cycle, step.StackPointer);
            }
        }

        internal void AssertComplete() => Assert.Null(active);

        private sealed record ActiveCall(long StartCycle, byte EntryStackPointer);
    }

    private sealed record RunnerAddresses(ushort PlayerY, ushort PlayerGrounded, ushort CameraX);

    private sealed record ScenarioMeasurement(
        IReadOnlyList<long> PrepareCycles,
        int Suppressions,
        int GameplayTicks,
        int AudioTicks,
        int StableFrames);

    private sealed record Measurement(
        IReadOnlyList<long> GroundedPrepareCycles,
        IReadOnlyList<long> AirbornePrepareCycles,
        int GroundedSuppressions,
        int AirborneSuppressions,
        int GroundedGameplayTicks,
        int GroundedAudioTicks,
        int AirborneGameplayTicks,
        int AirborneAudioTicks,
        int GroundedStableFrames,
        int AirborneStableFrames);

    private sealed record ReversalFrame(
        int Frame,
        int HardwareX,
        int RequestedX,
        int LogicalX,
        int VisibleX,
        int ColumnLatch,
        int PendingAxes,
        int Slot0State,
        int Slot1State,
        int Requests,
        int Residents,
        int Commits);
}
