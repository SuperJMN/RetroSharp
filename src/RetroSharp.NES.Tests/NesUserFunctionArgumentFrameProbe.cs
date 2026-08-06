namespace RetroSharp.NES.Tests;

using RetroSharp.NES;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Investigation harness for #514's computed-argument extension of the cold/one-shot outliner.
/// <para>
/// <see cref="NesUserFunctionOutliner"/> only turns a call site into a <c>JSR</c> when every
/// argument resolves to a compile-time operand, because there is no argument frame yet. This probe
/// sizes what that restriction still costs across every NES sample and validation fixture, so the
/// decision to build an argument-frame ABI is taken against measured duplication rather than an
/// assumed one.
/// </para>
/// <para>
/// Byte figures come from #521/#523's accounting (<see cref="NesUserFunctionCallAccountingReport"/>)
/// rather than from a re-derivation, so the probe measures what the compiler really emitted.
/// <c>TotalSelfBytes - EmittedBodySelfBytes</c> is the per-function share of
/// <see cref="NesUserFunctionCallAccountingReport.DuplicatedBytes"/>, which does not double count
/// nested expansions. The probe asserts nothing about the size of the prize; the printed tables are
/// the evidence.
/// </para>
/// </summary>
public sealed class NesUserFunctionArgumentFrameProbe(ITestOutputHelper output)
{
    /// <summary>
    /// Answers "which functions would an argument frame recover, and how many bytes" for every
    /// shipped NES program, by attributing measured duplication to the gate that actually rejected
    /// each function.
    /// </summary>
    [Fact]
    public void Computed_argument_headroom_across_every_nes_sample_and_fixture()
    {
        var gateBytes = new SortedDictionary<NesUserFunctionOutlineRejection, int>();
        var gateCounts = new SortedDictionary<NesUserFunctionOutlineRejection, int>();
        var phaseBytes = new SortedDictionary<NesUserFunctionPhase, int>();
        var couldServe = 0;
        var totalDuplication = 0;

        foreach (var sample in NesSampleProjectBuilds.NesSamplesAndFixtures())
        {
            NesRomBuildResult build;
            NesUserFunctionOutliner plan;
            try
            {
                build = NesSampleProjectBuilds.Build(sample.RelativePath);
                plan = NesUserFunctionOutliner.Plan(NesSampleProjectBuilds.Program(sample.RelativePath));
            }
            catch (Exception ex)
            {
                output.WriteLine($"{sample.Id}: SKIPPED, {ex.Message}");
                continue;
            }

            var report = build.Report.UserFunctionCalls;
            var duplication = report.Functions.ToDictionary(
                function => function.Name,
                function => function.TotalSelfBytes - function.EmittedBodySelfBytes,
                StringComparer.Ordinal);
            var accounted = plan.Candidates.Count(candidate => duplication.ContainsKey(candidate.Function));

            output.WriteLine(
                $"{sample.Id}  profile={build.Report.SelectedProfile}  " +
                $"functions={report.Functions.Count} candidates={plan.Candidates.Count} " +
                $"accounted={accounted} outlinedBodies={build.Report.OutlinedUserFunctions.Count} " +
                $"duplication={report.DuplicatedBytes}B");
            totalDuplication += report.DuplicatedBytes;

            foreach (var group in plan.Candidates.GroupBy(candidate => candidate.Rejection).OrderBy(group => group.Key))
            {
                var bytes = group.Sum(candidate => duplication.GetValueOrDefault(candidate.Function));
                gateBytes[group.Key] = gateBytes.GetValueOrDefault(group.Key) + bytes;
                gateCounts[group.Key] = gateCounts.GetValueOrDefault(group.Key) + group.Count();
                output.WriteLine($"    gate  {group.Key,-15} functions={group.Count(),3} duplication={bytes,6}B");
            }

            foreach (var group in report.Functions.GroupBy(function => function.Phase).OrderBy(group => group.Key))
            {
                var bytes = group.Sum(function => function.TotalSelfBytes - function.EmittedBodySelfBytes);
                phaseBytes[group.Key] = phaseBytes.GetValueOrDefault(group.Key) + bytes;
                output.WriteLine($"    phase {group.Key,-15} functions={group.Count(),3} duplication={bytes,6}B");
            }

            // The question this probe exists to answer: cold or one-shot, more than one static call
            // site, and therefore a function an argument frame could plausibly reach.
            foreach (var candidate in plan.Candidates
                         .Where(candidate => candidate.Phase is not NesUserFunctionPhase.Hot && candidate.CallSites > 1)
                         .OrderByDescending(candidate => duplication.GetValueOrDefault(candidate.Function)))
            {
                couldServe++;
                output.WriteLine(
                    $"    CANDIDATE {candidate.Function} phase={candidate.Phase} sites={candidate.CallSites} " +
                    $"gate={candidate.Rejection} duplication={duplication.GetValueOrDefault(candidate.Function)}B");
            }
        }

        output.WriteLine(string.Empty);
        output.WriteLine($"TOTAL duplication across every NES program: {totalDuplication} B");
        foreach (var gate in gateBytes)
        {
            output.WriteLine($"  gate  {gate.Key,-15} functions={gateCounts[gate.Key],4} duplication={gate.Value,7}B");
        }

        foreach (var phase in phaseBytes)
        {
            output.WriteLine($"  phase {phase.Key,-15} duplication={phase.Value,7}B");
        }

        output.WriteLine(
            "Cold/one-shot functions with more than one call site, the whole population an argument " +
            $"frame could ever serve: {couldServe}");
    }

    /// <summary>
    /// Confirms the computed-argument gate is real rather than merely unmeasured: a cold,
    /// multi-site, stream-free <c>void</c> helper is outlined when its argument folds to a
    /// constant, and falls back to inline expansion when the same argument is computed at runtime.
    /// The delta between the two rows is what an argument frame would recover per such call site.
    /// </summary>
    [Fact]
    public void The_computed_argument_gate_is_what_rejects_an_otherwise_eligible_cold_helper()
    {
        const string helper = """

                              void Bump(u8 amount) {
                                  u8 scratch = amount;
                                  scratch += 1;
                                  scratch += 2;
                                  scratch += 3;
                                  scratch += 4;
                                  scratch += 5;
                                  scratch += 6;
                                  scratch += 7;
                                  return;
                              }
                              """;

        Report(
            """
            void Main() {
                u8 seed = 1;
                Bump(1);
                Bump(1);
                Bump(1);
                return;
            }
            """ + helper,
            "constant argument");
        Report(
            """
            void Main() {
                u8 seed = 1;
                Bump(seed + 1);
                Bump(seed + 2);
                Bump(seed + 3);
                return;
            }
            """ + helper,
            "computed argument");
    }

    private void Report(string source, string label)
    {
        var build = RetroSharp.NES.NesRomCompiler.CompileSourceWithReport(
            source,
            sdkLibraryImports: [RetroSharp.Sdk.SdkImportResolver.Portable2D]);
        var plan = NesUserFunctionOutliner.Plan(RetroSharp.NES.NesRomCompiler.PrepareVideoProgram(
            source,
            null,
            RetroSharp.Sdk.SdkLibraryImportMode.ExplicitOnly,
            null,
            [RetroSharp.Sdk.SdkImportResolver.Portable2D],
            null).VideoProgram);
        var add = build.Report.UserFunctionCalls.Function("Bump");
        var candidate = plan.Candidates.Single(entry => entry.Function == "Bump");
        output.WriteLine(
            $"{label,-20} gate={candidate.Rejection,-15} phase={candidate.Phase} sites={candidate.CallSites} " +
            $"emittedCopies={add?.EmittedCopies} outlinedBodies={build.Report.OutlinedUserFunctions.Count} " +
            $"bodySelfBytes={add?.EmittedBodySelfBytes} " +
            $"duplication={add?.TotalSelfBytes - add?.EmittedBodySelfBytes}B " +
            $"fixedPrg={build.Report.FixedPayloadBytes}B");
    }
}
