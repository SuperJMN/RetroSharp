namespace RetroSharp.NES.Tests;

using RetroSharp.NES;

/// <summary>
/// Where each banked placement unit physically landed: its R6 bank list plus the exact bank and
/// byte offset of its first byte.
/// </summary>
/// <remarks>
/// <see cref="NesProgramBankPlacementReport"/> reports the bank list and the linked size of every
/// phase but not the offset inside a shared bank, so "did this phase move?" cannot be answered
/// from it alone. This replays the planner's sequential fill from the reported bank lists and the
/// per-bank used byte counts in <see cref="NesRomBuildReport.Segments"/>. The arithmetic is
/// determined because <see cref="NesProgramBankPlanner"/> packs units in program order and closes
/// a bank with a <see cref="NesProgramBankPlanner.BankEdgeJumpSize"/> byte jump.
/// </remarks>
internal sealed record NesPhasePlacement(
    string UnitName,
    NesPrgPlacementPhase Phase,
    int Bytes,
    IReadOnlyList<int> Banks,
    int StartBank,
    int StartOffset)
{
    public override string ToString() =>
        $"{UnitName,-20} {Phase,-7} bytes={Bytes,5} banks=[{string.Join(",", Banks),-9}] " +
        $"start=bank{StartBank}+{StartOffset}";
}

internal static class NesPhasePlacementMap
{
    /// <summary>Per-bank used byte counts of the linked banked program, in physical order.</summary>
    internal static IReadOnlyList<(int Bank, int Used)> ProgramBankUse(NesRomBuildReport report) => report.Segments
        .Where(segment => segment.Owner.StartsWith("program:r6:", StringComparison.Ordinal))
        .OrderBy(segment => segment.PhysicalStart)
        .Select(segment => (segment.PhysicalBank, segment.Length))
        .ToArray();

    /// <summary>
    /// The placement of every phase, or <see langword="null"/> when the build did not link a
    /// banked program at all.
    /// </summary>
    internal static IReadOnlyList<NesPhasePlacement>? Read(NesRomBuildReport report)
    {
        if (report.BankPlacement is not { } placement)
        {
            return null;
        }

        var bankUse = ProgramBankUse(report);
        var used = bankUse.ToDictionary(entry => entry.Bank, entry => entry.Used);
        var result = new List<NesPhasePlacement>(placement.Phases.Count);
        var cursorBank = bankUse.Count == 0 ? 0 : bankUse[0].Bank;
        var cursorOffset = 0;
        foreach (var phase in placement.Phases)
        {
            if (phase.PhysicalBanks.Count == 0)
            {
                result.Add(new NesPhasePlacement(phase.UnitName, phase.Phase, phase.Bytes, [], cursorBank, cursorOffset));
                continue;
            }

            if (cursorBank != phase.PhysicalBanks[0])
            {
                cursorBank = phase.PhysicalBanks[0];
                cursorOffset = 0;
            }

            var startBank = cursorBank;
            var startOffset = cursorOffset;
            var remaining = phase.Bytes;
            for (var index = 0; index + 1 < phase.PhysicalBanks.Count; index++)
            {
                remaining -= used[phase.PhysicalBanks[index]] - NesProgramBankPlanner.BankEdgeJumpSize - cursorOffset;
                cursorBank = phase.PhysicalBanks[index + 1];
                cursorOffset = 0;
            }

            cursorOffset += remaining;
            result.Add(new NesPhasePlacement(
                phase.UnitName,
                phase.Phase,
                phase.Bytes,
                phase.PhysicalBanks,
                startBank,
                startOffset));
        }

        return result;
    }
}
