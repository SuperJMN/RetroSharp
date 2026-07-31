namespace RetroSharp.GameBoy.Tests;

using RetroSharp.GameBoy;
using RetroSharp.Sdk;
using Xunit;

public sealed class GameBoyRunnerJumpScrollCadenceTests
{
    private const int ObservationFrames = 80;
    private const int JumpHeldFrames = 40;

    [Fact]
    public void Shared_runner_jump_does_not_stall_gameplay_during_packed_streaming()
    {
        var build = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
            RunnerSample.CompiledSource(),
            RunnerSample.Directory,
            sdkLibraryImports: [SdkImportResolver.Portable2D]);
        var variables = build.Report.UserVariables.ToDictionary(variable => variable.Name, StringComparer.Ordinal);
        var addresses = new RunnerAddresses(
            variables["player.x"].Address,
            variables["player.y"].Address,
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
            reproduction.PackedReleaseGameplayStalls == 0,
            $"Packed streaming stalled gameplay during the jump: reference={reference.Summary}; jump={reproduction.Summary}.");
        Assert.True(
            reproduction.MaximumGameplayGap <= 1,
            $"Jump scrolling produced a sustained gameplay gap: reference={reference.Summary}; jump={reproduction.Summary}.");
        Assert.True(
            reproduction.MaximumAudioGap <= reference.MaximumAudioGap,
            $"Jump scrolling added an audio gap: reference={reference.Summary}; jump={reproduction.Summary}.");
        Assert.InRange(reproduction.MaximumRequestedVisibleLag, 0, 2);
        Assert.NotEmpty(reproduction.ExtendedColumns);
        Assert.All(reproduction.ExtendedColumns, observation => Assert.Equal(21, observation.PayloadLength));
        Assert.True(
            reproduction.MaximumCommitVramWrites <= 21,
            $"Packed edge commit exceeded its 21-write VBlank budget: {reproduction.Summary}.");
        Assert.True(reproduction.LifecycleDrained, $"Jump lifecycle did not drain: {reproduction.Summary}.");
        Assert.Equal(0, reproduction.ForbiddenVideoWork);
    }

    private static ScenarioResult RunScenario(byte[] rom, RunnerAddresses addresses, bool jump)
    {
        var cpu = new GameBoyTestCpu(rom)
        {
            CycleAccurateLy = true,
            EnforceVblankVramWrites = true,
        };
        cpu.TracedWramBytes.Add(GameBoyRuntimeMemoryLayout.PackedCamera.DiagonalRowPrefetchLatch);
        cpu.TracedWramBytes.Add(GameBoyRuntimeMemoryLayout.PackedCamera.LastCommitVramWrites);
        for (var slot = 0; slot < 2; slot++)
        {
            var metadata = GameBoyPackedCameraRuntime.SlotMetadata(slot);
            cpu.TracedWramBytes.Add((ushort)(metadata + GameBoyPackedCameraRuntime.AxisOffset));
            cpu.TracedWramBytes.Add((ushort)(metadata + GameBoyPackedCameraRuntime.PayloadLengthOffset));
        }
        RunUntilWordEquals(
            cpu,
            GameBoyRuntimeMemoryLayout.PackedCamera.VisibleCameraYLow,
            176,
            maxFrames: 400);
        cpu.RunAdditionalFrames(16);

        var initialPlayerY = Word(cpu, addresses.PlayerY);
        var initialRequestedCameraY = Word(cpu, addresses.CameraY);
        var initialRequests = cpu.Wram(GameBoyRuntimeMemoryLayout.PackedCamera.RequestCount);
        var initialPrepares = cpu.Wram(GameBoyRuntimeMemoryLayout.PackedCamera.PrepareCount);
        var initialResidents = cpu.Wram(GameBoyRuntimeMemoryLayout.PackedCamera.ResidentCount);
        var initialCommits = cpu.Wram(GameBoyRuntimeMemoryLayout.PackedCamera.CommitCount);
        var initialReleases = cpu.Wram(GameBoyRuntimeMemoryLayout.PackedCamera.ReleaseCount);
        var frames = new List<FrameObservation>(ObservationFrames);
        var previousSourceTicks = cpu.SourceWaitCompletions;
        var previousAudioTicks = cpu.AudioUpdateCalls;
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

            cpu.RunAdditionalFrames(1);
            var sourceTicks = cpu.SourceWaitCompletions;
            var audioTicks = cpu.AudioUpdateCalls;
            gameplayGap = sourceTicks == previousSourceTicks ? gameplayGap + 1 : 0;
            audioGap = audioTicks == previousAudioTicks ? audioGap + 1 : 0;
            maximumGameplayGap = Math.Max(maximumGameplayGap, gameplayGap);
            maximumAudioGap = Math.Max(maximumAudioGap, audioGap);
            previousSourceTicks = sourceTicks;
            previousAudioTicks = audioTicks;
            frames.Add(new FrameObservation(
                frame,
                cpu.IoRegister(0xFF43),
                cpu.IoRegister(0xFF42),
                Word(cpu, addresses.CameraX),
                Word(cpu, addresses.CameraY),
                Word(cpu, GameBoyRuntimeMemoryLayout.Camera.XLow),
                Word(cpu, GameBoyRuntimeMemoryLayout.Camera.YLow),
                Word(cpu, GameBoyRuntimeMemoryLayout.PackedCamera.VisibleCameraXLow),
                Word(cpu, GameBoyRuntimeMemoryLayout.PackedCamera.VisibleCameraYLow),
                Word(cpu, addresses.PlayerX),
                Word(cpu, addresses.PlayerY),
                sourceTicks,
                audioTicks,
                cpu.Wram(GameBoyRuntimeMemoryLayout.PackedCamera.RequestCount),
                cpu.Wram(GameBoyRuntimeMemoryLayout.PackedCamera.ResidentCount),
                cpu.Wram(GameBoyRuntimeMemoryLayout.PackedCamera.CommitCount),
                cpu.Wram(GameBoyRuntimeMemoryLayout.PackedCamera.ReleaseCount),
                cpu.Wram(GameBoyRuntimeMemoryLayout.Camera.FineX),
                cpu.Wram(GameBoyRuntimeMemoryLayout.Camera.FineY),
                cpu.Wram(GameBoyRuntimeMemoryLayout.PackedCamera.DiagonalColumnPrefetchLatch),
                cpu.Wram(GameBoyRuntimeMemoryLayout.PackedCamera.DiagonalRowPrefetchLatch)));
        }

        var horizontalStalls = frames
            .Zip(frames.Skip(1), (previous, current) => (previous, current))
            .Count(pair =>
                pair.previous.RequestedCameraX > pair.previous.HardwareScrollX
                && pair.current.HardwareScrollX == pair.previous.HardwareScrollX);
        var maximumRequestedVisibleLag = frames.Max(frame =>
            Math.Max(
                Math.Abs(frame.RequestedCameraX - frame.VisibleCameraX),
                Math.Abs(frame.RequestedCameraY - frame.VisibleCameraY)));
        var packedReleaseGameplayStalls = frames
            .Zip(frames.Skip(1), (previous, current) => (previous, current))
            .Count(pair =>
                pair.current.Releases != pair.previous.Releases
                && pair.current.GameplayTicks == pair.previous.GameplayTicks);
        var requests = Difference(cpu.Wram(GameBoyRuntimeMemoryLayout.PackedCamera.RequestCount), initialRequests);
        var prepares = Difference(cpu.Wram(GameBoyRuntimeMemoryLayout.PackedCamera.PrepareCount), initialPrepares);
        var residents = Difference(cpu.Wram(GameBoyRuntimeMemoryLayout.PackedCamera.ResidentCount), initialResidents);
        var commits = Difference(cpu.Wram(GameBoyRuntimeMemoryLayout.PackedCamera.CommitCount), initialCommits);
        var releases = Difference(cpu.Wram(GameBoyRuntimeMemoryLayout.PackedCamera.ReleaseCount), initialReleases);
        var extendedColumns = Enumerable.Range(0, 2)
            .SelectMany(slot =>
            {
                var metadata = GameBoyPackedCameraRuntime.SlotMetadata(slot);
                var axisAddress = (ushort)(metadata + GameBoyPackedCameraRuntime.AxisOffset);
                var payloadLengthAddress = (ushort)(metadata + GameBoyPackedCameraRuntime.PayloadLengthOffset);
                return cpu.WramByteWrites
                    .Where(write =>
                        write.Address == payloadLengthAddress
                        && write.Value > 19
                        && cpu.WramByteWrites
                            .Where(candidate => candidate.Address == axisAddress && candidate.Cycles <= write.Cycles)
                            .Select(candidate => candidate.Value)
                            .LastOrDefault() == GameBoyPackedCameraRuntime.Column)
                    .Select(write => new ExtendedColumnObservation(
                        write.Value,
                        cpu.WramByteWrites
                            .Where(candidate =>
                                candidate.Address == GameBoyRuntimeMemoryLayout.PackedCamera.DiagonalRowPrefetchLatch
                                && candidate.Cycles <= write.Cycles)
                            .Select(candidate => candidate.Value)
                            .LastOrDefault()))
                    .Where(observation => observation.RowLatch != 0);
            })
            .ToArray();
        var maximumCommitVramWrites = cpu.WramByteWrites
            .Where(write => write.Address == GameBoyRuntimeMemoryLayout.PackedCamera.LastCommitVramWrites)
            .Select(write => (int)write.Value)
            .DefaultIfEmpty()
            .Max();
        var forbiddenVideoWork = new[]
        {
            GameBoyRuntimeMemoryLayout.PackedCamera.BankWorkInCommit,
            GameBoyRuntimeMemoryLayout.PackedCamera.DecodeWorkInCommit,
            GameBoyRuntimeMemoryLayout.PackedCamera.DirectoryWorkInVBlank,
            GameBoyRuntimeMemoryLayout.PackedCamera.DirectoryWorkInCommit,
            GameBoyRuntimeMemoryLayout.PackedCamera.DecodeWorkInVBlank,
        }.Sum(address => cpu.Wram(address));

        return new ScenarioResult(
            initialPlayerY,
            initialRequestedCameraY,
            frames.Min(frame => frame.PlayerY),
            frames.Min(frame => frame.RequestedCameraY),
            horizontalStalls,
            packedReleaseGameplayStalls,
            maximumGameplayGap,
            maximumAudioGap,
            maximumRequestedVisibleLag,
            extendedColumns,
            maximumCommitVramWrites,
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
                        pair.previous.RequestedCameraX > pair.previous.HardwareScrollX
                        && pair.current.HardwareScrollX == pair.previous.HardwareScrollX)
                    .Select(pair =>
                        $"{pair.current.Frame}:hw{pair.current.HardwareScrollX}/req{pair.current.RequestedCameraX}"
                        + $"/log{pair.current.LogicalCameraX}/vis{pair.current.VisibleCameraX}"
                        + $"/fine{pair.current.FineX},{pair.current.FineY}"
                        + $"/latch{pair.current.ColumnLatch},{pair.current.RowLatch}"
                        + $"/life{pair.current.Requests},{pair.current.Residents},{pair.current.Commits},{pair.current.Releases}")));
    }

    private static int Difference(byte current, byte initial) => unchecked((byte)(current - initial));

    private static int Word(GameBoyTestCpu cpu, ushort lowAddress) =>
        cpu.Wram(lowAddress) | cpu.Wram(checked((ushort)(lowAddress + 1))) << 8;

    private static void RunUntilWordEquals(GameBoyTestCpu cpu, ushort lowAddress, int expected, int maxFrames)
    {
        for (var frame = 0; frame < maxFrames; frame++)
        {
            if (Word(cpu, lowAddress) == expected)
            {
                return;
            }

            cpu.RunAdditionalFrames(1);
        }

        Assert.Fail($"Game Boy RAM word ${lowAddress:X4} did not reach {expected} within {maxFrames} frames.");
    }

    private sealed record RunnerAddresses(
        ushort PlayerX,
        ushort PlayerY,
        ushort CameraX,
        ushort CameraY);

    private sealed record ExtendedColumnObservation(int PayloadLength, int RowLatch);

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
        long GameplayTicks,
        long AudioTicks,
        int Requests,
        int Residents,
        int Commits,
        int Releases,
        int FineX,
        int FineY,
        int ColumnLatch,
        int RowLatch);

    private sealed record ScenarioResult(
        int InitialPlayerY,
        int InitialRequestedCameraY,
        int MaximumPlayerY,
        int MaximumRequestedCameraY,
        int HorizontalStalls,
        int PackedReleaseGameplayStalls,
        int MaximumGameplayGap,
        int MaximumAudioGap,
        int MaximumRequestedVisibleLag,
        IReadOnlyList<ExtendedColumnObservation> ExtendedColumns,
        int MaximumCommitVramWrites,
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
            $"stalls={HorizontalStalls}, packedReleaseGameplayStalls={PackedReleaseGameplayStalls}, "
            + $"gameplayGap={MaximumGameplayGap}, audioGap={MaximumAudioGap}, "
            + $"lag={MaximumRequestedVisibleLag}, playerY={InitialPlayerY}->{MaximumPlayerY}, "
            + $"cameraY={InitialRequestedCameraY}->{MaximumRequestedCameraY}, "
            + $"extendedColumns=[{string.Join(",", ExtendedColumns.Select(column => $"{column.PayloadLength}@{column.RowLatch}"))}], "
            + $"commitWrites={MaximumCommitVramWrites}, "
            + $"lifecycle={Requests}/{Prepares}/{Residents}/{Commits}/{Releases}, forbidden={ForbiddenVideoWork}, "
            + $"stallFrames=[{StallFrames}]";
    }
}
