namespace RetroSharp.NES.Tests;

using RetroSharp.NES;
using Xunit;

public sealed class NesProgramBankPlannerTests
{
    private const int BankSize = NesProgramBankPlanner.ProgramBankSize;
    private const int EdgeJump = NesProgramBankPlanner.BankEdgeJumpSize;

    [Fact]
    public void Hot_phase_starts_a_fresh_bank_instead_of_straddling_a_bank_cut()
    {
        var plan = NesProgramBankPlanner.Plan(
        [
            Unit("program:main:init", NesPrgPlacementPhase.Cold, 5_000),
            Unit("program:main:frame", NesPrgPlacementPhase.Hot, 4_000),
            Unit("program:main:tail", NesPrgPlacementPhase.Cold, 32),
        ]);

        Assert.Equal(2, plan.RequiredBanks);
        Assert.Equal([0], Banks(plan, "program:main:init"));
        Assert.Equal([1], Banks(plan, "program:main:frame"));
        Assert.Equal([1], Banks(plan, "program:main:tail"));
        Assert.Equal(0, HotOffset(plan));
        Assert.Equal(1, plan.HotBankIndex);
        Assert.Equal(4_000, plan.HotUnitBytes);

        // The cold phase keeps only what it emitted plus its bank-edge jump; the rest of the
        // first bank is deliberately unused so the hot phase stays whole.
        Assert.Equal([5_000 + EdgeJump, 4_032], plan.UsedBytesByBank);
    }

    [Fact]
    public void Hot_phase_stays_in_place_when_it_already_fits_the_open_bank()
    {
        var plan = NesProgramBankPlanner.Plan(
        [
            Unit("program:main:init", NesPrgPlacementPhase.Cold, 1_000),
            Unit("program:main:frame", NesPrgPlacementPhase.Hot, 4_000),
            Unit("program:main:tail", NesPrgPlacementPhase.Cold, 32),
        ]);

        Assert.Equal(1, plan.RequiredBanks);
        Assert.Equal([0], Banks(plan, "program:main:frame"));
        Assert.Equal(1_000, HotOffset(plan));
        Assert.Equal([5_032], plan.UsedBytesByBank);
    }

    [Fact]
    public void Cold_phases_keep_filling_banks_in_source_order()
    {
        var plan = NesProgramBankPlanner.Plan(
        [
            Unit("program:main:init", NesPrgPlacementPhase.OneShot, BankSize - EdgeJump, atomCount: 8),
            Unit("program:main:more", NesPrgPlacementPhase.Cold, 64),
        ]);

        Assert.Equal(2, plan.RequiredBanks);
        Assert.Equal([0], Banks(plan, "program:main:init"));
        Assert.Equal([1], Banks(plan, "program:main:more"));
        Assert.Null(plan.HotBankIndex);
    }

    [Fact]
    public void Hot_phase_larger_than_one_bank_is_rejected_by_name_and_size()
    {
        var exception = Assert.Throws<NesHotPhaseBankCapacityException>(() => NesProgramBankPlanner.Plan(
        [
            Unit("program:main:frame", NesPrgPlacementPhase.Hot, BankSize + 16, atomCount: 8),
            Unit("program:main:tail", NesPrgPlacementPhase.Cold, 32),
        ]));

        Assert.Equal("program:main:frame", exception.UnitName);
        Assert.Equal(BankSize + 16, exception.UnitBytes);
        Assert.Equal(BankSize - EdgeJump, exception.BankCapacity);
        Assert.Contains("program:main:frame", exception.Message, StringComparison.Ordinal);
        Assert.Contains((BankSize + 16).ToString(), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_hot_phase_that_ends_the_program_may_use_the_whole_final_bank()
    {
        var plan = NesProgramBankPlanner.Plan(
        [
            Unit("program:main:init", NesPrgPlacementPhase.Cold, 128),
            Unit("program:main:frame", NesPrgPlacementPhase.Hot, BankSize, atomCount: 8),
        ]);

        Assert.Equal(2, plan.RequiredBanks);
        Assert.Equal([1], Banks(plan, "program:main:frame"));
        Assert.Equal([128 + EdgeJump, BankSize], plan.UsedBytesByBank);
    }

    [Fact]
    public void An_indivisible_atom_larger_than_a_bank_is_rejected_with_its_source_offset()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => NesProgramBankPlanner.Plan(
        [
            new NesProgramBankUnit(
                "program:main:init",
                NesPrgPlacementPhase.Cold,
                [new NesProgramBankAtom(0, BankSize + 1), new NesProgramBankAtom(BankSize + 1, 1)]),
        ]));

        Assert.Equal(
            $"NES banked program atom in unit 'program:main:init' at source offset 0 is {BankSize + 1} bytes; " +
            $"an indivisible atom may use at most {BankSize - EdgeJump} bytes in this position.",
            exception.Message);
    }

    [Fact]
    public void An_empty_program_needs_no_bank()
    {
        var plan = NesProgramBankPlanner.Plan([]);

        Assert.Equal(0, plan.RequiredBanks);
        Assert.Empty(plan.Atoms);
        Assert.Null(plan.HotBankIndex);
    }

    [Fact]
    public void V1_never_duplicates_shared_bodies_because_shared_helpers_are_fixed_resident()
    {
        var plan = NesProgramBankPlanner.Plan(
        [
            Unit("program:main:init", NesPrgPlacementPhase.Cold, 5_000),
            Unit("program:main:frame", NesPrgPlacementPhase.Hot, 4_000),
        ]);

        Assert.Equal(0, plan.DuplicatedSharedBytes);
    }

    private static NesProgramBankUnit Unit(
        string name,
        NesPrgPlacementPhase phase,
        int bytes,
        int atomCount = 4)
    {
        var atoms = new List<NesProgramBankAtom>(atomCount);
        var offset = 0;
        for (var index = 0; index < atomCount; index++)
        {
            var length = index == atomCount - 1
                ? bytes - offset
                : bytes / atomCount;
            atoms.Add(new NesProgramBankAtom(offset, length));
            offset += length;
        }

        return new NesProgramBankUnit(name, phase, atoms);
    }

    private static IReadOnlyList<int> Banks(NesProgramBankPlan plan, string unitName) =>
        plan.Phases.Single(phase => phase.UnitName == unitName).BankIndexes;

    private static int HotOffset(NesProgramBankPlan plan)
    {
        var hotUnitIndex = plan.Phases
            .Select((phase, index) => (phase, index))
            .Single(item => item.phase.Phase is NesPrgPlacementPhase.Hot)
            .index;
        return plan.Atoms.First(atom => atom.UnitIndex == hotUnitIndex).Offset;
    }
}
