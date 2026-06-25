namespace RetroSharp.GameBoy.Tests;

using RetroSharp.GameBoy;
using Xunit;

public sealed class GameBoyApuLoopDetectorTests
{
    private const int ClockHz = 4_194_304;
    private const long FrameCycles = 70_224;

    [Fact]
    public void Detects_periodic_loop_and_reports_period_and_start()
    {
        const int period = 80;
        const int frames = 400;
        var events = new List<GameBoyApuTraceEvent>();
        for (var frame = 0; frame < frames; frame++)
        {
            var delta = frame == 0 ? 0 : FrameCycles;
            events.Add(new GameBoyApuTraceEvent(delta, 0xFF13, (byte)(frame % period)));
        }

        var result = GameBoyApuLoopDetector.Detect(events, ClockHz);

        Assert.NotNull(result);
        Assert.Equal(period, result!.PeriodFrames);
        Assert.Equal(0, result.LoopStartCycle);
        Assert.True(result.Similarity >= 0.99, $"expected near-perfect similarity, got {result.Similarity}");
        Assert.Equal(period * FrameCycles, result.LoopEndCycle);
    }

    [Fact]
    public void Returns_null_when_no_loop_is_present()
    {
        var events = new List<GameBoyApuTraceEvent>();
        for (var frame = 0; frame < 400; frame++)
        {
            var delta = frame == 0 ? 0 : FrameCycles;
            events.Add(new GameBoyApuTraceEvent(delta, 0xFF13, (byte)(frame & 0xFF)));
        }

        Assert.Null(GameBoyApuLoopDetector.Detect(events, ClockHz));
    }

    [Fact]
    public void Returns_null_for_captures_shorter_than_two_periods()
    {
        var events = new List<GameBoyApuTraceEvent>
        {
            new(0, 0xFF13, 0x10),
            new(FrameCycles, 0xFF13, 0x20),
        };

        Assert.Null(GameBoyApuLoopDetector.Detect(events, ClockHz));
    }
}
