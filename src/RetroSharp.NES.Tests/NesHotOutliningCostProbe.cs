namespace RetroSharp.NES.Tests;

using RetroSharp.NES;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Investigation harness for the remaining half of issue #514: what outlining a <c>Hot</c> user
/// function would cost in frame time, and what it would buy in bytes.
/// <para>
/// #524/#526 outlined only <c>Cold</c>/<c>OneShot</c> functions, deliberately leaving every
/// frame-reachable function inline until a cost model existed. This probe builds that cost model
/// from the per-function accounting #521/#523 added (<c>NesRomBuildReport.UserFunctionCalls</c>)
/// rather than re-deriving anything, and pairs it with a measured frame-time budget from
/// <see cref="NesTestCpu"/> via <see cref="NesActiveTickObserver"/>.
/// </para>
/// <para>
/// Everything here is diagnostic. It asserts almost nothing: the numbers it prints are the
/// evidence, and the only assertions are sanity guards that would invalidate the measurement
/// itself. Per the repository's acceptance policy no cycle count, byte count or ROM digest is
/// pinned; a cost that matters becomes an upper-bound budget in production code, not an equality
/// here.
/// </para>
/// <para>
/// The model is an upper bound, not a count, because <c>CallsPerFrame</c> is static. It is not
/// path-sensitive, so it overestimates across mutually exclusive branches, and it does not
/// multiply loop iterations, so it underestimates inside one. Measured against a prototype build,
/// <c>falling-blocks Locate</c> reports 10 static calls per frame and was entered twice. Only a
/// dynamic count settles a specific function; that prototype was measurement scaffolding and its
/// results are recorded in issue #514 rather than kept in the repository, because reproducing them
/// requires compiling each sample twice with the outliner's hot gate removed.
/// </para>
/// </summary>
public sealed class NesHotOutliningCostProbe(ITestOutputHelper output)
{
    private const int WarmupFrames = 40;
    private const int ObservedFrames = 60;

    private static readonly string[][] HeldInputs =
    [
        [],
        ["right"],
        ["right", "a"],
        ["right", "b"],
    ];

    /// <summary>
    /// Per-sample distribution of <c>Hot</c> user functions: calls per frame, duplicated bytes and
    /// the modelled per-frame cost of turning each one into a <c>JSR</c>/<c>RTS</c> pair.
    /// </summary>
    [Fact]
    public void Hot_user_function_cost_survey()
    {
        var totals = new List<string>();
        foreach (var sample in NesSampleProjectBuilds.NesSamples())
        {
            NesRomBuildResult build;
            try
            {
                build = NesSampleProjectBuilds.Build(sample.RelativePath);
            }
            catch (Exception exception)
            {
                output.WriteLine($"## {sample.Id}: BUILD FAILED — {exception.Message}");
                continue;
            }

            var report = build.Report.UserFunctionCalls;
            var hot = report.Functions
                .Where(function => function.Phase is NesUserFunctionPhase.Hot)
                .OrderByDescending(function => NesHotOutliningCost.For(function).UpperBoundCyclesPerFrame)
                .ToArray();

            output.WriteLine(
                $"## {sample.Id} ({build.Report.SelectedProfile}, rom={build.Rom.Length} B, " +
                $"frameLoop={report.HasFrameLoop}, functions={report.Functions.Count}, " +
                $"hot={hot.Length}, totalDuplication={report.DuplicatedBytes} B)");
            if (hot.Length == 0)
            {
                output.WriteLine("   (no hot user functions)");
                continue;
            }

            output.WriteLine(
                "   " +
                $"{"function",-28} {"calls/f",7} {"rep",3} {"copies",6} {"body",6} {"dup",7} " +
                $"{"argB",4} {"ref",3} {"ct",3} {"cyc/f",6} {"floor",6} {"netB",7}");
            foreach (var function in hot)
            {
                var cost = NesHotOutliningCost.For(function);
                output.WriteLine(
                    "   " +
                    $"{function.Name,-28} {function.CallsPerFrame,7} {(function.RepeatsPerFrame ? "yes" : "no"),3} " +
                    $"{function.EmittedCopies,6} {function.EmittedBodyBytes,6} {function.DuplicatedBytes,7} " +
                    $"{function.Arguments.RuntimeBytes,4} {function.Arguments.ReferenceArguments,3} " +
                    $"{function.Arguments.CompileTimeOperands,3} {cost.UpperBoundCyclesPerFrame,6} " +
                    $"{cost.LiteralIssueCyclesPerFrame,6} {cost.NetBytesSaved,7}");
            }

            var sampleCost = hot.Sum(function => NesHotOutliningCost.For(function).UpperBoundCyclesPerFrame);
            var sampleBytes = hot.Sum(function => NesHotOutliningCost.For(function).NetBytesSaved);
            output.WriteLine(
                $"   TOTAL hot outlining: {sampleCost} cycles/frame " +
                $"({sampleCost / (double)NesActiveTickObserver.NtscCyclesPerFrame:P2} of an NTSC frame), " +
                $"net {sampleBytes} B recovered");
            totals.Add($"{sample.Id,-34} {sampleCost,7} cyc/frame  {sampleBytes,7} B  hot={hot.Length}");
        }

        output.WriteLine(string.Empty);
        output.WriteLine("## Summary (sum over hot functions, per sample)");
        foreach (var line in totals.OrderByDescending(line => line))
        {
            output.WriteLine("   " + line);
        }
    }

    /// <summary>
    /// Measured frame-time headroom of every NES sample under held input, so the modelled cost above
    /// can be judged against what each sample actually has left.
    /// </summary>
    [Fact]
    public void Frame_time_headroom_survey()
    {
        foreach (var sample in NesSampleProjectBuilds.NesSamples())
        {
            byte[] rom;
            try
            {
                rom = NesSampleProjectBuilds.Build(sample.RelativePath).Rom;
            }
            catch (Exception exception)
            {
                output.WriteLine($"{sample.Id,-34} BUILD FAILED — {exception.Message}");
                continue;
            }

            foreach (var held in HeldInputs)
            {
                string measurement;
                try
                {
                    measurement = Measure(rom, held).ToString();
                }
                catch (Exception exception)
                {
                    measurement = $"NOT MEASURED — {exception.GetType().Name}: {exception.Message}";
                }

                output.WriteLine($"{sample.Id,-34} held=[{string.Join(",", held),-7}] {measurement}");
            }
        }
    }

    /// <summary>
    /// Where the mandatory per-frame video-safe work sits relative to user code. If a frame's PPU
    /// and OAM traffic forms a single uninterrupted burst, no user statement can be interleaved
    /// into it, which is what decides whether hot outlining can spend the VBlank budget at all.
    /// </summary>
    [Fact]
    public void Video_safe_burst_shape_survey()
    {
        foreach (var sample in NesSampleProjectBuilds.NesSamples())
        {
            byte[] rom;
            try
            {
                rom = NesSampleProjectBuilds.Build(sample.RelativePath).Rom;
            }
            catch (Exception exception)
            {
                output.WriteLine($"{sample.Id,-34} BUILD FAILED — {exception.Message}");
                continue;
            }

            foreach (var held in new[] { Array.Empty<string>(), ["right", "b"] })
            {
                string observation;
                try
                {
                    observation = NesVideoSafeObserver.Observe(rom, held).ToString();
                }
                catch (Exception exception)
                {
                    observation = $"NOT MEASURED — {exception.GetType().Name}: {exception.Message}";
                }

                output.WriteLine($"{sample.Id,-34} held=[{string.Join(",", held),-7}] {observation}");
            }
        }
    }

    internal static NesActiveTickMeasurement Measure(byte[] rom, IReadOnlyCollection<string> held)
    {
        var cpu = new NesTestCpu(rom);
        cpu.Held.UnionWith(held);
        cpu.RunFrames(WarmupFrames);

        var observer = new NesActiveTickObserver();
        var startCycles = cpu.Cycles;
        var startFrame = cpu.PhysicalFrames;
        cpu.OnStep = step => observer.Observe(step, cpu.PhysicalFrames);
        cpu.RunFrames(startFrame + ObservedFrames);
        cpu.OnStep = null;

        return observer.Summarize(cpu.Cycles - startCycles, cpu.PhysicalFrames - startFrame);
    }
}

/// <summary>
/// The 6502 cost of replacing one inline expansion with a call, using the anchors issue #514
/// recorded: <c>JSR</c> 6 cycles / 3 bytes, <c>RTS</c> 6 cycles / 1 byte, and one
/// <c>STA abs</c> at the site plus one <c>LDA abs</c> in the body per runtime argument byte,
/// 4 cycles / 3 bytes each.
/// </summary>
internal sealed record NesHotOutliningCost(
    long UpperBoundCyclesPerFrame,
    long LiteralIssueCyclesPerFrame,
    long NetBytesSaved)
{
    private const int JsrCycles = 6;
    private const int RtsCycles = 6;
    private const int AbsoluteAccessCycles = 4;
    private const int JsrBytes = 3;
    private const int RtsBytes = 1;
    private const int AbsoluteAccessBytes = 3;

    internal static NesHotOutliningCost For(NesUserFunctionAccounting function)
    {
        ArgumentNullException.ThrowIfNull(function);

        // A reference argument has no ABI yet. Charging it as a 16-bit pointer is the pessimistic
        // reading; monomorphising on the referenced storage would charge nothing.
        var argumentBytes = function.Arguments.RuntimeBytes + (function.Arguments.ReferenceArguments * 2);

        // Both JSR and RTS execute on every call, and both the caller's store and the body's load
        // execute on every call, so the per-frame cost scales with calls per frame.
        var perCall = JsrCycles + RtsCycles + (2 * AbsoluteAccessCycles * argumentBytes);

        // #514's table lists RTS and the body's LDA "once", which is a byte count. Reading it as a
        // cycle count gives this floor; it understates the real cost by roughly a factor of two.
        var literal = (function.CallsPerFrame * (long)(JsrCycles + (AbsoluteAccessCycles * argumentBytes)))
                      + RtsCycles + (AbsoluteAccessCycles * argumentBytes);

        var callSites = function.EmittedCopies;
        var addedBytes = (callSites * (long)(JsrBytes + (AbsoluteAccessBytes * argumentBytes)))
                         + RtsBytes + (AbsoluteAccessBytes * argumentBytes);
        return new NesHotOutliningCost(
            function.CallsPerFrame * (long)perCall,
            literal,
            function.DuplicatedBytes - addedBytes);
    }
}
