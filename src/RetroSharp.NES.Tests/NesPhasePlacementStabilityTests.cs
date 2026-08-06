namespace RetroSharp.NES.Tests;

using RetroSharp.NES;
using RetroSharp.Sdk;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Guards the measured placement invariant behind epic #509's C4 checklist item: banked phase
/// placement is a sequential fill in program order, so growth propagates strictly forward. Adding
/// content to a later placement unit leaves every earlier unit on the same physical R6 bank at the
/// same offset, and leaves fixed PRG addresses alone.
/// </summary>
/// <remarks>
/// This is the free half of "growth without reshuffling" and it already holds:
/// <see cref="NesProgramBankPlanner.Plan"/> walks the units in program order and never revisits a
/// placed atom. The test exists so a future planner change — bin-packing, phase-major ordering, or
/// a reserved-headroom scheme — cannot silently lose the property.
/// </remarks>
public sealed class NesPhasePlacementStabilityTests(ITestOutputHelper output)
{
    [Fact]
    public void Growing_the_tail_phase_leaves_the_init_and_frame_phases_exactly_where_they_were()
    {
        var baseline = Build(tailStatements: 0);
        var grown = Build(tailStatements: 12);

        var before = Placement(baseline);
        var after = Placement(grown);
        output.WriteLine($"baseline: {string.Join(" | ", before)}");
        output.WriteLine($"grown:    {string.Join(" | ", after)}");

        Assert.Equal(baseline.Report.SelectedProfile, grown.Report.SelectedProfile);
        Assert.Equal(baseline.Report.PrgRomSize, grown.Report.PrgRomSize);

        foreach (var unitName in new[]
                 {
                     NesRomBuilder.MainInitPlacementUnitName,
                     NesRomBuilder.MainFramePlacementUnitName,
                 })
        {
            var earlier = Single(before, unitName);
            var later = Single(after, unitName);
            Assert.Equal(earlier.Bytes, later.Bytes);
            Assert.Equal(earlier.Banks, later.Banks);
            Assert.Equal(earlier.StartBank, later.StartBank);
            Assert.Equal(earlier.StartOffset, later.StartOffset);
        }

        // The grown unit is the only one that changed, and it still starts where it started.
        var tailBefore = Single(before, NesRomBuilder.MainTailPlacementUnitName);
        var tailAfter = Single(after, NesRomBuilder.MainTailPlacementUnitName);
        Assert.True(tailAfter.Bytes > tailBefore.Bytes, "The tail growth must actually reach the emitted program.");
        Assert.Equal(tailBefore.StartBank, tailAfter.StartBank);
        Assert.Equal(tailBefore.StartOffset, tailAfter.StartOffset);

        // The hot phase keeps its bank, so no gameplay code changed bank because of cold growth.
        Assert.Equal(
            baseline.Report.BankPlacement!.HotPhasePhysicalBank,
            grown.Report.BankPlacement!.HotPhasePhysicalBank);

        // Fixed PRG is addressed by CPU address, not by bank, and cold growth must not disturb it.
        Assert.Equal(baseline.Report.FixedSymbols, grown.Report.FixedSymbols);
    }

    private static NesPhasePlacement Single(IReadOnlyList<NesPhasePlacement> placements, string unitName) =>
        Assert.Single(placements, placement => placement.UnitName == unitName);

    private static IReadOnlyList<NesPhasePlacement> Placement(NesRomBuildResult built) =>
        NesPhasePlacementMap.Read(built.Report) ??
        throw new InvalidOperationException("phase-banked-frame must link a banked program.");

    /// <summary>
    /// Builds the tracked <c>samples/phase-banked-frame</c> candidate with
    /// <paramref name="tailStatements"/> extra statements appended after the frame loop, which the
    /// phase analyzer classifies as cold <c>program:main:tail</c> work.
    /// </summary>
    private static NesRomBuildResult Build(int tailStatements)
    {
        var directory = NesVideoSafeObserver.RepositoryDirectory("samples/phase-banked-frame");
        var main = File.ReadAllText(Path.Combine(directory, "src", "main.rs"));
        const string anchor = "    CompleteLevel(scene);";
        Assert.Contains(anchor, main, StringComparison.Ordinal);
        var growth = string.Concat(Enumerable
            .Range(0, tailStatements)
            .Select(index => $"{Environment.NewLine}    scene.contact = {index % 200 + 1};"));
        var source = File.ReadAllText(Path.Combine(directory, "src", "scene.rs")) +
                     Environment.NewLine +
                     main.Replace(anchor, anchor + growth, StringComparison.Ordinal);
        return RetroSharp.NES.NesRomCompiler.CompileSourceWithReport(
            source,
            directory,
            sdkLibraryImports: [SdkImportResolver.Portable2D]);
    }
}
