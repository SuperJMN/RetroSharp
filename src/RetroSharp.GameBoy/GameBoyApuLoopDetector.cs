namespace RetroSharp.GameBoy;

public sealed record GameBoyApuLoopResult(long LoopStartCycle, long LoopEndCycle, double Similarity, int PeriodFrames);

// Detects the musical loop of an APU register trace by autocorrelating per-frame write
// signatures. GBS rips contain no explicit loop point, so the exporter would otherwise have
// to store an arbitrary capture window. Storing exactly intro + one loop body is the single
// largest size lever for these assets.
//
// The loop a register trace exposes is never bit-exact: drivers with free-running modulators
// (software volume envelopes, vibrato LFOs) leave a slightly different register phase each
// pass, so detection is similarity-based, not equality-based.
public static class GameBoyApuLoopDetector
{
    private const long DmgClockHz = 4_194_304;
    private const long DmgFrameCycles = 70_224;
    private const double MinSimilarity = 0.90;
    private const int MinPeriodFrames = 60;
    private const int MaxCompareWindow = 1_200;

    public static GameBoyApuLoopResult? Detect(IReadOnlyList<GameBoyApuTraceEvent> events, int clockHz)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (events.Count == 0)
        {
            return null;
        }

        var framePeriod = DmgFrameCycles * clockHz / DmgClockHz;
        if (framePeriod <= 0)
        {
            framePeriod = DmgFrameCycles;
        }

        var signatures = BuildFrameSignatures(events, framePeriod, out var frameCount);
        if (frameCount < MinPeriodFrames * 2)
        {
            return null;
        }

        var period = FindPeriod(signatures, frameCount);
        if (period <= 0)
        {
            return null;
        }

        var (loopStartFrame, similarity) = FindLoopStart(signatures, frameCount, period);
        if (similarity < MinSimilarity)
        {
            return null;
        }

        var loopStartCycle = (long)loopStartFrame * framePeriod;
        var loopEndCycle = (long)(loopStartFrame + period) * framePeriod;
        return new GameBoyApuLoopResult(loopStartCycle, loopEndCycle, similarity, period);
    }

    private static ulong[] BuildFrameSignatures(IReadOnlyList<GameBoyApuTraceEvent> events, long framePeriod, out int frameCount)
    {
        var totalCycles = 0L;
        var lastFrame = 0;
        var perFrame = new Dictionary<int, ulong>();
        foreach (var traceEvent in events)
        {
            totalCycles = checked(totalCycles + traceEvent.DeltaCycles);
            var frame = (int)((totalCycles + framePeriod / 2) / framePeriod);
            lastFrame = frame;

            // Order-independent rolling hash of the (address, value) writes in this frame.
            var contribution = unchecked(((ulong)traceEvent.Address << 8 | traceEvent.Value) * 0x9E3779B97F4A7C15UL);
            perFrame[frame] = perFrame.TryGetValue(frame, out var existing) ? existing ^ contribution : contribution;
        }

        frameCount = lastFrame + 1;
        var signatures = new ulong[frameCount];
        foreach (var (frame, hash) in perFrame)
        {
            signatures[frame] = hash;
        }

        return signatures;
    }

    private static int FindPeriod(ulong[] signatures, int frameCount)
    {
        var bestPeriod = 0;
        var bestScore = 0.0;
        var maxPeriod = frameCount / 2;
        for (var period = MinPeriodFrames; period <= maxPeriod; period++)
        {
            var window = Math.Min(period, frameCount - 2 * period);
            if (window < MinPeriodFrames)
            {
                continue;
            }

            window = Math.Min(window, MaxCompareWindow);
            var tailStart = frameCount - window;
            var matches = 0;
            for (var k = 0; k < window; k++)
            {
                if (signatures[tailStart + k] == signatures[tailStart + k - period])
                {
                    matches++;
                }
            }

            var score = (double)matches / window;
            if (score > bestScore)
            {
                bestScore = score;
                bestPeriod = period;
            }
        }

        return bestScore >= MinSimilarity ? bestPeriod : 0;
    }

    private static (int LoopStartFrame, double Similarity) FindLoopStart(ulong[] signatures, int frameCount, int period)
    {
        var maxStart = frameCount - 2 * period;
        var bestStart = 0;
        var bestScore = 0.0;
        for (var start = 0; start <= maxStart; start++)
        {
            var window = Math.Min(period, frameCount - start - period);
            if (window < MinPeriodFrames)
            {
                break;
            }

            window = Math.Min(window, MaxCompareWindow);
            var matches = 0;
            for (var k = 0; k < window; k++)
            {
                if (signatures[start + k] == signatures[start + k + period])
                {
                    matches++;
                }
            }

            var score = (double)matches / window;
            if (score >= MinSimilarity)
            {
                return (start, score);
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestStart = start;
            }
        }

        return (bestStart, bestScore);
    }
}
