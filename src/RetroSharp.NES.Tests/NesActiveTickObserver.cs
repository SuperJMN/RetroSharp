namespace RetroSharp.NES.Tests;

/// <summary>
/// Measures how much of a physical frame a NES program actually spends doing work, and how much it
/// spends waiting at its frame boundary. It is the counterpart of <see cref="NesVideoSafeObserver"/>:
/// that one measures the 2,273-cycle video-safe window inside VBlank, this one measures the whole
/// <see cref="NtscCyclesPerFrame"/>-cycle frame that surrounds it.
/// <para>
/// What it reports, per <see cref="NesActiveTickMeasurement"/>: peak and median active cycles for a
/// single frame, mean active cycles per frame, and the minimum and mean idle cycles per frame. The
/// peak is the number to compare against the frame budget, because one late frame is a visible
/// stutter; the minimum idle is the headroom a new per-frame cost would eat into.
/// </para>
/// <para>
/// How it works. Every NES frame boundary this compiler emits ends in a tight poll: either the
/// packed <c>FramePending</c>/<c>$2002</c> pair
/// (<c>NesPhysicalFrameScheduler.EmitPackedFrameBoundary</c>) or the plain <c>BIT $2002</c> pair
/// (<c>EmitFrameBoundary</c>). Both are two-instruction loops that touch nothing, so a frame's idle
/// time is exactly the time spent inside such a loop and the complement is the active tick. The
/// detector is structural rather than symbolic so it works on any ROM without a symbol file: a run
/// of instructions confined to at most <see cref="MaximumSpinAddresses"/> distinct program counters
/// inside a <see cref="MaximumSpinSpan"/>-byte window, lasting at least
/// <see cref="MinimumSpinInstructions"/> instructions, is a wait. Real work never stays that narrow
/// for that long.
/// </para>
/// <para>
/// Validated against the three published anchors: <c>falling-blocks</c> 4,014 measured against
/// ~4,018 reported in #506-era work, <c>platformer-landing</c> 13,619 against ~13,580, and
/// <c>samples/runner</c> 90.2% of a frame against ~91.3%. Measure under held input, not idle:
/// <c>samples/runner</c> costs 9,343 cycles idle and 26,861 under held <c>right</c>+<c>b</c>, and
/// an idle-only measurement once hid a shipping corruption bug for a week.
/// </para>
/// <para>
/// What it does not model. It reports where CPU time goes, not whether that time is correct: it
/// says nothing about PPU or OAM safety, and a program that overruns VBlank while doing useful
/// work looks identical to one that does not. It cannot see a program with no frame boundary at
/// all — a one-shot cartridge idles in the terminal loop, which is itself a tight spin, so no wait
/// ever closes and the measurement reports no frames rather than a wrong number. Results are
/// diagnostic: express any limit derived from them as an upper-bound budget, never an equality.
/// </para>
/// </summary>
internal sealed class NesActiveTickObserver
{
    internal const int NtscCyclesPerFrame = 29_780;
    private const int Window = 8;
    private const int MaximumSpinAddresses = 3;
    private const int MaximumSpinSpan = 16;
    private const int MinimumSpinInstructions = 40;

    private readonly ushort[] programCounters = new ushort[Window];
    private readonly long[] cycles = new long[Window];
    private readonly List<(long Start, long End, int Frame)> waits = [];
    private readonly List<long> activeSegments = [];
    private int filled;
    private int next;
    private bool inRun;
    private long runStart;
    private int runInstructions;
    private long lastWaitEnd = -1;

    internal void Observe(NesCpuStep step, int physicalFrame)
    {
        programCounters[next] = step.ProgramCounter;
        cycles[next] = step.Cycle;
        next = (next + 1) % Window;
        filled = Math.Min(filled + 1, Window);

        var qualifies = filled == Window && Qualifies();
        if (qualifies)
        {
            if (!inRun)
            {
                inRun = true;
                runStart = cycles[next];
                runInstructions = Window;
            }
            else
            {
                runInstructions++;
            }

            return;
        }

        if (!inRun)
        {
            return;
        }

        inRun = false;
        if (runInstructions < MinimumSpinInstructions)
        {
            return;
        }

        waits.Add((runStart, step.Cycle, physicalFrame));
        if (lastWaitEnd >= 0 && runStart > lastWaitEnd)
        {
            activeSegments.Add(runStart - lastWaitEnd);
        }

        lastWaitEnd = step.Cycle;
    }

    /// <summary>Active-work segments between consecutive frame waits, in CPU cycles.</summary>
    internal IReadOnlyList<long> ActiveSegments => activeSegments;

    internal NesActiveTickMeasurement Summarize(long observedCycles, int observedFrames)
    {
        var idleByFrame = waits
            .GroupBy(wait => wait.Frame)
            .Select(group => group.Sum(wait => wait.End - wait.Start))
            .ToArray();
        var totalIdle = waits.Sum(wait => wait.End - wait.Start);
        var active = observedCycles - totalIdle;
        return new NesActiveTickMeasurement(
            observedFrames,
            waits.Count,
            activeSegments.Count == 0 ? -1 : activeSegments.Max(),
            activeSegments.Count == 0 ? -1 : Median(activeSegments),
            observedFrames == 0 ? -1 : active / observedFrames,
            idleByFrame.Length == 0 ? -1 : idleByFrame.Min(),
            observedFrames == 0 ? -1 : totalIdle / observedFrames);
    }

    private static long Median(IReadOnlyList<long> values)
    {
        var ordered = values.Order().ToArray();
        return ordered[ordered.Length / 2];
    }

    private bool Qualifies()
    {
        ushort minimum = ushort.MaxValue;
        ushort maximum = 0;
        var distinct = 0;
        for (var index = 0; index < Window; index++)
        {
            var value = programCounters[index];
            minimum = Math.Min(minimum, value);
            maximum = Math.Max(maximum, value);
            var seen = false;
            for (var earlier = 0; earlier < index; earlier++)
            {
                if (programCounters[earlier] == value)
                {
                    seen = true;
                    break;
                }
            }

            if (!seen)
            {
                distinct++;
            }
        }

        return distinct <= MaximumSpinAddresses && maximum - minimum <= MaximumSpinSpan;
    }
}

/// <summary>
/// <paramref name="PeakActiveCycles"/> is the longest stretch of uninterrupted work between two
/// frame waits; <paramref name="MinimumIdleCyclesPerFrame"/> is the frame-time headroom in the
/// tightest observed frame.
/// </summary>
internal sealed record NesActiveTickMeasurement(
    int Frames,
    int Waits,
    long PeakActiveCycles,
    long MedianActiveCycles,
    long ActiveCyclesPerFrame,
    long MinimumIdleCyclesPerFrame,
    long MeanIdleCyclesPerFrame)
{
    internal double PeakFrameFraction => PeakActiveCycles / (double)NesActiveTickObserver.NtscCyclesPerFrame;

    public override string ToString() =>
        $"frames={Frames,3} waits={Waits,4} peakActive={PeakActiveCycles,6} " +
        $"medianActive={MedianActiveCycles,6} activePerFrame={ActiveCyclesPerFrame,6} " +
        $"minIdlePerFrame={MinimumIdleCyclesPerFrame,6} meanIdlePerFrame={MeanIdleCyclesPerFrame,6} " +
        $"peak={PeakFrameFraction:P1}";
}
