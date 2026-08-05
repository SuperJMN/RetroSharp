namespace RetroSharp.NES;

/// <summary>One indivisible emitted atom offered to the bank planner.</summary>
internal readonly record struct NesProgramBankAtom(int SourceOffset, int Length);

/// <summary>
/// One placement unit offered to the bank planner. The planner sees a unit only as a phase plus a
/// sequence of indivisible atoms; it never inspects instructions, labels, or relocations.
/// </summary>
internal sealed record NesProgramBankUnit(
    string Name,
    NesPrgPlacementPhase Phase,
    IReadOnlyList<NesProgramBankAtom> Atoms);

/// <summary>Where one atom lands: which R6 program bank, at which offset, at which linked length.</summary>
internal sealed record NesProgramBankAtomSlot(
    int UnitIndex,
    int AtomIndex,
    int BankIndex,
    int Offset,
    int Length);

/// <summary>The R6 program banks one phase unit occupies, in placement order.</summary>
internal sealed record NesProgramPhaseBankAssignment(
    string UnitName,
    NesPrgPlacementPhase Phase,
    IReadOnlyList<int> BankIndexes,
    int Bytes);

internal sealed record NesProgramBankPlan(
    IReadOnlyList<NesProgramBankAtomSlot> Atoms,
    IReadOnlyList<int> UsedBytesByBank,
    int RequiredBanks,
    IReadOnlyList<NesProgramPhaseBankAssignment> Phases,
    int? HotBankIndex,
    string? HotUnitName,
    int HotUnitBytes,
    int DuplicatedSharedBytes);

internal sealed class NesHotPhaseBankCapacityException(string unitName, int unitBytes, int bankCapacity)
    : InvalidOperationException(
        $"NES banked program hot phase unit '{unitName}' links to {unitBytes} bytes, but a hot phase must stay " +
        $"whole inside one 8 KiB R6 bank, which holds at most {bankCapacity} bytes in this position.")
{
    internal string UnitName { get; } = unitName;

    internal int UnitBytes { get; } = unitBytes;

    internal int BankCapacity { get; } = bankCapacity;
}

/// <summary>
/// Assigns the movable program's phase placement units to R6 program banks.
/// </summary>
/// <remarks>
/// This is the single owner of banked program placement policy. Callers hand it the phase-classified
/// units in program order and read back a finished plan; bin-packing, atom indivisibility, bank-edge
/// reserve, hot-phase wholeness, and the capacity diagnostics all live here.
/// <para>
/// The policy is: emit units in program order so the linker's linear bank-to-bank fallthrough chain
/// stays valid, but treat a <see cref="NesPrgPlacementPhase.Hot"/> unit as one indivisible group. A
/// hot unit that would straddle a bank cut starts a fresh bank instead, so steady-state frame code
/// never pays a bank transition or veneer. Cold and one-shot phases keep filling banks in phase and
/// source order.
/// </para>
/// <para>
/// V1 duplicates nothing. Shared SDK helper bodies are fixed-resident and reached by ordinary
/// bank-neutral <c>JSR</c>, so copying them into a phase bank could only add bytes; the plan reports
/// that as zero duplicated bytes rather than hiding the decision.
/// </para>
/// </remarks>
internal static class NesProgramBankPlanner
{
    internal const int ProgramBankSize = 8 * 1_024;

    /// <summary>Every non-final bank ends in an absolute jump to the next bank's entry veneer.</summary>
    internal const int BankEdgeJumpSize = 3;

    internal static NesProgramBankPlan Plan(IReadOnlyList<NesProgramBankUnit> units)
    {
        ArgumentNullException.ThrowIfNull(units);

        var atomCount = units.Sum(unit => unit.Atoms.Count);
        if (atomCount == 0)
        {
            return new NesProgramBankPlan(
                [],
                [],
                RequiredBanks: 0,
                Phases: [],
                HotBankIndex: null,
                HotUnitName: null,
                HotUnitBytes: 0,
                DuplicatedSharedBytes: 0);
        }

        var slots = new List<NesProgramBankAtomSlot>(atomCount);
        var usedBytesByBank = new List<int>();
        var phases = new List<NesProgramPhaseBankAssignment>(units.Count);
        var bankIndex = 0;
        var offset = 0;
        var placedAtoms = 0;
        int? hotBankIndex = null;
        string? hotUnitName = null;
        var hotUnitBytes = 0;

        for (var unitIndex = 0; unitIndex < units.Count; unitIndex++)
        {
            var unit = units[unitIndex];
            if (unit.Atoms.Count == 0)
            {
                phases.Add(new NesProgramPhaseBankAssignment(unit.Name, unit.Phase, [], Bytes: 0));
                continue;
            }

            if (unit.Phase is NesPrgPlacementPhase.Hot)
            {
                var unitBytes = unit.Atoms.Sum(atom => atom.Length);
                var endsProgram = placedAtoms + unit.Atoms.Count == atomCount;
                if (!UnitFitsFrom(unit, startOffset: 0, endsProgram))
                {
                    throw new NesHotPhaseBankCapacityException(unit.Name, unitBytes, BankCapacity(endsProgram));
                }

                if (offset > 0 && !UnitFitsFrom(unit, offset, endsProgram))
                {
                    CloseBank(usedBytesByBank, ref bankIndex, ref offset);
                }

                hotBankIndex = bankIndex;
                hotUnitName = unit.Name;
                hotUnitBytes = unitBytes;
            }

            var unitBanks = new List<int>();
            var unitLinkedBytes = 0;
            for (var atomIndex = 0; atomIndex < unit.Atoms.Count; atomIndex++)
            {
                var atom = unit.Atoms[atomIndex];
                var capacity = BankCapacity(endsProgram: placedAtoms == atomCount - 1);
                if (atom.Length > capacity)
                {
                    throw new InvalidOperationException(
                        $"NES banked program atom in unit '{unit.Name}' at source offset {atom.SourceOffset} is " +
                        $"{atom.Length} bytes; an indivisible atom may use at most {capacity} bytes in this position.");
                }

                if (offset + atom.Length > capacity)
                {
                    CloseBank(usedBytesByBank, ref bankIndex, ref offset);
                }

                if (unitBanks.Count == 0 || unitBanks[^1] != bankIndex)
                {
                    unitBanks.Add(bankIndex);
                }

                slots.Add(new NesProgramBankAtomSlot(unitIndex, atomIndex, bankIndex, offset, atom.Length));
                offset += atom.Length;
                unitLinkedBytes += atom.Length;
                placedAtoms++;
            }

            phases.Add(new NesProgramPhaseBankAssignment(unit.Name, unit.Phase, unitBanks, unitLinkedBytes));
        }

        usedBytesByBank.Add(offset);
        return new NesProgramBankPlan(
            slots,
            usedBytesByBank,
            bankIndex + 1,
            phases,
            hotBankIndex,
            hotUnitName,
            hotUnitBytes,
            DuplicatedSharedBytes: 0);
    }

    private static int BankCapacity(bool endsProgram) =>
        endsProgram ? ProgramBankSize : ProgramBankSize - BankEdgeJumpSize;

    /// <summary>
    /// Replays the atom-granular fill for one unit so the hot-phase decision uses exactly the
    /// capacities the placement loop will enforce, never an optimistic unit-level approximation.
    /// </summary>
    private static bool UnitFitsFrom(NesProgramBankUnit unit, int startOffset, bool endsProgram)
    {
        var offset = startOffset;
        for (var atomIndex = 0; atomIndex < unit.Atoms.Count; atomIndex++)
        {
            offset += unit.Atoms[atomIndex].Length;
            if (offset > BankCapacity(endsProgram && atomIndex == unit.Atoms.Count - 1))
            {
                return false;
            }
        }

        return true;
    }

    private static void CloseBank(List<int> usedBytesByBank, ref int bankIndex, ref int offset)
    {
        usedBytesByBank.Add(offset + BankEdgeJumpSize);
        bankIndex++;
        offset = 0;
    }
}
