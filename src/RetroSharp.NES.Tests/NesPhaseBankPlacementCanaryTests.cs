namespace RetroSharp.NES.Tests;

using RetroSharp.NES;
using RetroSharp.Sdk;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Acceptance evidence for phase-based R6 bank placement, observed on the tracked
/// <c>samples/phase-banked-frame</c> pair. The candidate and the control share
/// <c>src/scene.rs</c>, so the hot frame loop is the same source in both builds and the control
/// is an honest steady-state baseline.
/// </summary>
public sealed class NesPhaseBankPlacementCanaryTests(ITestOutputHelper output)
{
    private const int SteadyStateFrames = 120;
    private const int NtscCyclesPerFrame = 29_780;

    /// <summary>
    /// Banking may not add steady-state cost to the hot frame loop, but a bank transition and a
    /// veneer are real work, so the candidate is allowed a small bounded margin over the control
    /// rather than assumed to be free.
    /// </summary>
    private const int ActiveTickBudgetMargin = 256;

    [Fact]
    public void Candidate_selects_code_banking_and_keeps_the_hot_frame_phase_whole_in_one_r6_bank()
    {
        var candidate = BuildCandidate();
        var control = BuildControl();

        Assert.Equal(NesRomBuilder.CodeBankedProfileName, candidate.Report.SelectedProfile);
        Assert.Equal("nes-mmc3-tvrom-v1", control.Report.SelectedProfile);

        var placement = Assert.IsType<NesProgramBankPlacementReport>(candidate.Report.BankPlacement);
        var init = Phase(placement, NesRomBuilder.MainInitPlacementUnitName);
        var frame = Phase(placement, NesRomBuilder.MainFramePlacementUnitName);
        var tail = Phase(placement, NesRomBuilder.MainTailPlacementUnitName);
        output.WriteLine(
            $"init={init.Bytes} banks=[{string.Join(",", init.PhysicalBanks)}], " +
            $"frame={frame.Bytes} banks=[{string.Join(",", frame.PhysicalBanks)}], " +
            $"tail={tail.Bytes} banks=[{string.Join(",", tail.PhysicalBanks)}], " +
            $"r6Headroom={placement.ProgramR6HeadroomBytes}, fixedHeadroom={candidate.Report.FixedHeadroomBytes}, " +
            $"veneers={candidate.Report.FixedVeneerBytes}, duplicated={placement.DuplicatedSharedBytes}");

        Assert.Equal(NesPrgPlacementPhase.Hot, frame.Phase);
        Assert.Equal(NesPrgPlacementPhase.Cold, init.Phase);
        Assert.Equal(NesPrgPlacementPhase.Cold, tail.Phase);

        var hotBank = Assert.Single(frame.PhysicalBanks);
        Assert.Equal(hotBank, placement.HotPhasePhysicalBank);
        Assert.Equal(NesRomBuilder.MainFramePlacementUnitName, placement.HotPhaseUnitName);
        Assert.Equal(frame.Bytes, placement.HotPhaseBytes);

        // The cold init phase owns whole banks of its own and never shares the hot phase's bank.
        Assert.DoesNotContain(hotBank, init.PhysicalBanks);

        // The canary discriminates: filling banks in raw emission order would have cut the hot
        // phase in two, because the cold phase and the hot phase do not fit one bank together.
        Assert.True(
            init.Bytes + frame.Bytes >
                NesProgramBankPlanner.ProgramBankSize - NesProgramBankPlanner.BankEdgeJumpSize,
            $"init={init.Bytes} + frame={frame.Bytes} must not fit one R6 bank, or this canary proves nothing.");

        Assert.True(placement.ProgramR6HeadroomBytes > 0, "The selected board must retain R6 headroom.");
        Assert.True(candidate.Report.FixedHeadroomBytes > 0, "Fixed PRG must retain headroom before the reset trailer.");

        // V1 places shared SDK helper bodies in fixed PRG and reaches them with bank-neutral JSR,
        // so per-bank duplication could only add bytes.
        Assert.Equal(0, placement.DuplicatedSharedBytes);

        // The frame phase itself is unchanged by banking; only its placement moved.
        Assert.Equal(
            Unit(control.Report, NesRomBuilder.MainFramePlacementUnitName).Size,
            Unit(candidate.Report, NesRomBuilder.MainFramePlacementUnitName).Size);
    }

    [Fact]
    public void Banked_steady_state_frames_never_leave_the_hot_bank_and_stay_within_the_control_budget()
    {
        var candidate = BuildCandidate();
        var control = BuildControl();
        var placement = Assert.IsType<NesProgramBankPlacementReport>(candidate.Report.BankPlacement);
        var hotBank = Assert.Single(Phase(placement, NesRomBuilder.MainFramePlacementUnitName).PhysicalBanks);

        var candidateSteady = ObserveSteadyState(candidate.Rom);
        var controlSteady = ObserveSteadyState(control.Rom);
        output.WriteLine($"candidate: {candidateSteady}");
        output.WriteLine($"control:   {controlSteady}");

        foreach (var steady in new[] { candidateSteady, controlSteady })
        {
            Assert.Equal(SteadyStateFrames, steady.PhysicalFrames);
            Assert.Equal(SteadyStateFrames, steady.LogicalTicks);
            Assert.Equal(1, steady.ResetCount);
            Assert.Equal(0, steady.UnsafePpuWrites);
            Assert.Equal(0, steady.UnsafeOamWrites);
            Assert.True(steady.ApuWrites > 0, "Audio must keep updating in steady state.");
        }

        // Steady-state gameplay executes only out of the hot phase's bank: no bank transition and
        // no veneer are paid per frame by the frame loop itself.
        Assert.Equal([hotBank], candidateSteady.ProgramBanksExecuted);

        Assert.True(
            candidateSteady.PeakTickCycles <= controlSteady.PeakTickCycles + ActiveTickBudgetMargin,
            $"candidate peak={candidateSteady.PeakTickCycles}, control peak={controlSteady.PeakTickCycles}");
        Assert.True(
            candidateSteady.PeakTickCycles < NtscCyclesPerFrame,
            $"candidate peak={candidateSteady.PeakTickCycles} must complete inside one physical frame.");
    }

    private static NesRomBuildResult BuildCandidate() => Build("src/main.rs");

    private static NesRomBuildResult BuildControl() => Build("src/control.rs");

    private static NesRomBuildResult Build(string mainSource)
    {
        var directory = SampleDirectory;
        var source = File.ReadAllText(Path.Combine(directory, "src", "scene.rs")) +
                     Environment.NewLine +
                     File.ReadAllText(Path.Combine(directory, mainSource));
        return RetroSharp.NES.NesRomCompiler.CompileSourceWithReport(
            source,
            directory,
            sdkLibraryImports: [SdkImportResolver.Portable2D]);
    }

    private static NesProgramPhaseBankPlacement Phase(NesProgramBankPlacementReport placement, string unitName) =>
        Assert.Single(placement.Phases, phase => phase.UnitName == unitName);

    private static NesPrgPlacementUnit Unit(NesRomBuildReport report, string unitName) =>
        Assert.Single(report.PlacementUnits, unit => unit.Name == unitName);

    private static SteadyState ObserveSteadyState(byte[] rom)
    {
        var cpu = new NesTestCpu(rom);
        cpu.Held.UnionWith(["right", "b"]);
        cpu.TracedRamBytes.Add(NesRuntimeMemoryLayout.Camera.ScrollApplied);
        cpu.RunFrames(40);

        var startFrame = cpu.PhysicalFrames;
        var startTicks = cpu.VBlankWaitCompletions;
        var startNmi = cpu.NmiCompletionCycles.Count;
        var startWrite = cpu.RamByteWrites.Count;
        var startApu = cpu.ApuWrites.Count;
        var startPpuWrite = cpu.PpuWrites.Count;
        var startOamWrite = cpu.OamWrites.Count;
        var programBanks = new SortedSet<int>();
        cpu.OnStep = step =>
        {
            if (step.ProgramCounter is >= 0x8000 and <= 0x9FFF)
            {
                programBanks.Add(cpu.CurrentR6Bank);
            }
        };
        cpu.RunFrames(startFrame + SteadyStateFrames);
        cpu.OnStep = null;

        var nmiCycles = cpu.NmiCompletionCycles.Skip(startNmi).ToArray();
        var publications = cpu.RamByteWrites
            .Skip(startWrite)
            .Where(write =>
                write.Address == NesRuntimeMemoryLayout.Camera.ScrollApplied &&
                write.Value == (byte)NesCameraPublicationState.Ready)
            .ToArray();
        var tickCycles = nmiCycles
            .Zip(nmiCycles.Skip(1), (start, end) => (Start: start, End: end))
            .Select(window => publications
                .Where(write => write.Cycle >= window.Start && write.Cycle < window.End)
                .Select(write => write.Cycle - window.Start)
                .DefaultIfEmpty()
                .Max())
            .Where(cycles => cycles > 0)
            .ToArray();

        return new SteadyState(
            cpu.PhysicalFrames - startFrame,
            cpu.VBlankWaitCompletions - startTicks,
            tickCycles.Length == 0 ? -1 : (int)tickCycles.Max(),
            cpu.ResetCount,
            cpu.PpuWrites.Skip(startPpuWrite).Count(write =>
                write.RenderingEnabled && cpu.PpuTiming(write.Cycle, true).Phase != "vblank"),
            cpu.OamWrites.Skip(startOamWrite).Count(write =>
                write.RenderingEnabled && cpu.PpuTiming(write.Cycle, true).Phase != "vblank"),
            cpu.ApuWrites.Count - startApu,
            programBanks.ToArray());
    }

    private static string SampleDirectory => RepositoryDirectory("samples/phase-banked-frame");

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

    private sealed record SteadyState(
        int PhysicalFrames,
        long LogicalTicks,
        int PeakTickCycles,
        int ResetCount,
        int UnsafePpuWrites,
        int UnsafeOamWrites,
        int ApuWrites,
        IReadOnlyList<int> ProgramBanksExecuted)
    {
        public override string ToString() =>
            $"frames={PhysicalFrames}, ticks={LogicalTicks}, peakTick={PeakTickCycles}, resets={ResetCount}, " +
            $"unsafePpu={UnsafePpuWrites}, unsafeOam={UnsafeOamWrites}, apu={ApuWrites}, " +
            $"programBanks=[{string.Join(",", ProgramBanksExecuted)}]";
    }
}
