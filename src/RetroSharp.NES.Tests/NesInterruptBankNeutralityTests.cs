namespace RetroSharp.NES.Tests;

using RetroSharp.NES;
using Xunit;

/// <summary>
/// Guards the banking safety invariant that an interrupt handler must stay fixed-resident and
/// bank-neutral.
/// <para>
/// On MMC3 PRG mode 0 the CPU windows <c>$8000-$9FFF</c> (register R6) and <c>$A000-$BFFF</c>
/// (register R7) are switchable; only <c>$C000-$FFFF</c> is fixed. An NMI can arrive at any
/// instruction boundary, including in the middle of <c>mmc3_select_r6</c> between its
/// <c>STA $8000</c> latch write and its <c>STA $8001</c> bank write. If the handler executed from,
/// read through, or wrote to the switchable window, or left the bank selection or the CPU
/// registers different from how it found them, the interrupted mainline would resume against a
/// mapper and register state it never chose. MMC3 bank registers are write-only, so nothing can
/// detect or repair that after the fact.
/// </para>
/// <para>
/// The measurement is behavioral rather than structural on purpose: the emitted handler body is
/// what has to hold the property, and it is the thing a future change would grow. Adding a music
/// tick, a streaming commit, or an OAM DMA to the NMI - all natural next features, and all of them
/// reachable code that already calls <c>mmc3_select_r6</c> - would break this silently today.
/// <see cref="Mutated_nmi_handler_is_reported_as_bank_hostile"/> is the negative control that keeps
/// this suite from being a test that cannot fail.
/// </para>
/// </summary>
public sealed class NesInterruptBankNeutralityTests
{
    private const int ObservedFrames = 60;

    /// <summary>First CPU address of the MMC3 switchable PRG window (R6 at <c>$8000-$9FFF</c>).</summary>
    private const ushort SwitchableWindowStart = 0x8000;

    /// <summary>
    /// First CPU address of the MMC3 fixed PRG region, which
    /// <c>NesCartridgeLayout.FixedRuntimeCpuBaseAddress</c> pins for every board size.
    /// </summary>
    private const ushort FixedWindowStart = 0xC000;

    /// <summary>
    /// Every NES-capable sample and versioned fixture. Each one is linked once and then observed
    /// under every held input, because linking dominates the cost and the simulation does not.
    /// </summary>
    public static TheoryData<string, string> SamplesAndFixtures()
    {
        var data = new TheoryData<string, string>();
        foreach (var sample in NesSampleProjectBuilds.NesSamplesAndFixtures())
        {
            data.Add(sample.Id, sample.RelativePath);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(SamplesAndFixtures))]
    public void Nes_interrupt_handlers_stay_fixed_resident_and_bank_neutral(string id, string relativePath)
    {
        var rom = NesSampleProjectBuilds.Build(relativePath).Rom;
        if (!UsesSwitchablePrg(rom))
        {
            // Mapper 0 maps all 32 KiB fixed, so there is no switchable window to be neutral about.
            return;
        }

        foreach (var held in NesVideoSafeObserver.HeldInputs)
        {
            var cpu = new NesTestCpu(rom);
            cpu.Held.UnionWith(held);
            cpu.RunFrames(ObservedFrames);

            var hostile = cpu.NmiObservations.Where(IsBankHostile).ToArray();
            Assert.True(
                hostile.Length == 0,
                $"{id} [{string.Join(' ', held)}] took {cpu.NmiObservations.Count} NMI(s); " +
                $"{hostile.Length} were not fixed-resident and bank-neutral:" +
                Environment.NewLine +
                string.Join(
                    Environment.NewLine,
                    hostile.Take(4).Select(observation => "  " + observation)));
        }
    }

    /// <summary>
    /// Keeps the theory above from passing vacuously. The worst case for this invariant is an NMI
    /// taken while the mainline is executing out of a switched R6 bank, and this asserts that the
    /// banked canary actually produces that case under every held input rather than always being
    /// interrupted inside the fixed frame-wait loop.
    /// </summary>
    [Fact]
    public void Banked_canary_takes_nmis_while_the_mainline_runs_from_a_switched_bank()
    {
        var rom = NesSampleProjectBuilds
            .Build("samples/phase-banked-frame/phase-banked-frame.retrosharp.json")
            .Rom;
        Assert.True(UsesSwitchablePrg(rom), "phase-banked-frame must link onto a banked board.");

        foreach (var held in NesVideoSafeObserver.HeldInputs)
        {
            var cpu = new NesTestCpu(rom);
            cpu.Held.UnionWith(held);
            cpu.RunFrames(ObservedFrames);

            var interruptedInSwitchableWindow = cpu.NmiObservations
                .Count(observation => observation.InterruptedProgramCounter
                    is >= SwitchableWindowStart and < FixedWindowStart);
            Assert.True(
                interruptedInSwitchableWindow > 0,
                $"[{string.Join(' ', held)}] took {cpu.NmiObservations.Count} NMI(s) but none " +
                "interrupted mainline code in the switchable PRG window, so the observation says " +
                "nothing about bank neutrality.");
        }
    }

    /// <summary>
    /// Negative control. A handler that dirties the MMC3 bank-select latch, touches the switchable
    /// window and clobbers X is patched over the real one in a linked ROM, and every signal the
    /// guard relies on has to report it. The switchable-window access counter is the same signal
    /// that a handler body placed outside <c>$C000-$FFFF</c> would trip through its own instruction
    /// fetches, so proving it fires on a data access proves the residence half too.
    /// </summary>
    [Fact]
    public void Mutated_nmi_handler_is_reported_as_bank_hostile()
    {
        var rom = NesSampleProjectBuilds
            .Build("samples/phase-banked-frame/phase-banked-frame.retrosharp.json")
            .Rom;
        var prgLength = rom[4] * 16 * 1_024;
        var nmiVector = Word(rom, 16 + prgLength - 6);
        var irqVector = Word(rom, 16 + prgLength - 2);

        // Still signals the frame so the mainline keeps running, but leaves the bank-select latch
        // armed for a different register, writes through the switchable window and clobbers X.
        // The latch must not be 6: gameplay leaves it there after every mmc3_select_r6, so a
        // control that re-selects 6 would be invisible - which is itself part of the hazard, since
        // a handler that re-arms the latch while the mainline sits between its STA $8000 and
        // STA $8001 redirects that mainline's bank write to another register.
        byte[] mutant =
        [
            0x48,                                                       // PHA
            0xA2, 0xFF,                                                 // LDX #$FF
            0xA9, 0x07,                                                 // LDA #7
            0x8D, 0x00, 0x80,                                           // STA $8000
            0xA9, 0x01,                                                 // LDA #1
            0x8D,
            unchecked((byte)NesRuntimeMemoryLayout.PackedCamera.FramePending),
            (byte)(NesRuntimeMemoryLayout.PackedCamera.FramePending >> 8),
            0x68,                                                       // PLA
            0x40,                                                       // RTI
        ];
        Assert.True(
            mutant.Length <= irqVector - nmiVector,
            $"the control handler needs {mutant.Length} bytes but the emitted NMI handler occupies " +
            $"only {irqVector - nmiVector}.");

        var fixedRegionStart = prgLength - 16 * 1_024;
        mutant.CopyTo(rom.AsSpan(16 + fixedRegionStart + (nmiVector - FixedWindowStart)));

        var cpu = new NesTestCpu(rom);
        cpu.Held.Add("right");
        try
        {
            cpu.RunFrames(ObservedFrames);
        }
        catch (InvalidOperationException)
        {
            // A bank-hostile handler is expected to destabilise the run; the recorded NMIs are the
            // evidence, and they are collected before anything downstream can fail.
        }

        Assert.NotEmpty(cpu.NmiObservations);
        var first = cpu.NmiObservations[0];
        Assert.NotEqual(first.MapperBefore, first.MapperAfter);
        Assert.True(first.SwitchableWindowAccesses > 0, first.ToString());
        Assert.True(IsBankHostile(first), first.ToString());
        Assert.Contains(
            cpu.NmiObservations,
            observation => observation.RegistersBefore != observation.RegistersAfter);
    }

    private static bool IsBankHostile(NesNmiObservation observation) =>
        observation.MapperBefore != observation.MapperAfter ||
        observation.RegistersBefore != observation.RegistersAfter ||
        observation.SwitchableWindowAccesses > 0 ||
        observation.LowestHandlerAddress < FixedWindowStart;

    private static bool UsesSwitchablePrg(byte[] rom) => ((rom[6] >> 4) | (rom[7] & 0xF0)) != 0;

    private static ushort Word(byte[] rom, int offset) => (ushort)(rom[offset] | rom[offset + 1] << 8);
}
