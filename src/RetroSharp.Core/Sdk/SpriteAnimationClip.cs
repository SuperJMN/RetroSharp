namespace RetroSharp.Core.Sdk;

public sealed record SpriteAnimationClip
{
    public SpriteAnimationClip(string Name, int FirstFrame, int FrameCount)
        : this(Name, FirstFrame, Enumerable.Repeat(1, FrameCount).ToArray())
    {
    }

    public SpriteAnimationClip(string name, int firstFrame, IReadOnlyList<int> frameDurations)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Animation clip name must not be empty.", nameof(name));
        }

        if (firstFrame < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(firstFrame), firstFrame, "First frame must be zero or greater.");
        }

        if (frameDurations.Count == 0)
        {
            throw new ArgumentException("Animation clip must contain at least one frame duration.", nameof(frameDurations));
        }

        if (frameDurations.Any(duration => duration <= 0))
        {
            throw new ArgumentException("Animation frame durations must be greater than zero.", nameof(frameDurations));
        }

        Name = name;
        FirstFrame = firstFrame;
        FrameDurations = frameDurations.ToArray();
        FrameIndices = Enumerable.Range(firstFrame, FrameDurations.Count).ToArray();
        FrameStartTicks = BuildFrameStartTicks(FrameDurations);
        DurationTicks = FrameDurations.Sum();
    }

    public string Name { get; }

    public int FirstFrame { get; }

    public int FrameCount => FrameDurations.Count;

    public IReadOnlyList<int> FrameIndices { get; }

    public IReadOnlyList<int> FrameDurations { get; }

    public IReadOnlyList<int> FrameStartTicks { get; }

    public int DurationTicks { get; }

    public int FrameAtTick(int tick)
    {
        if (tick < 0 || tick >= DurationTicks)
        {
            throw new ArgumentOutOfRangeException(nameof(tick), tick, $"Tick must be between 0 and {DurationTicks - 1}.");
        }

        for (var i = FrameStartTicks.Count - 1; i >= 0; i--)
        {
            if (tick >= FrameStartTicks[i])
            {
                return FrameIndices[i];
            }
        }

        throw new InvalidOperationException("Animation clip frame table is inconsistent.");
    }

    private static int[] BuildFrameStartTicks(IReadOnlyList<int> frameDurations)
    {
        var frameStartTicks = new int[frameDurations.Count];
        var tick = 0;
        for (var i = 0; i < frameDurations.Count; i++)
        {
            frameStartTicks[i] = tick;
            tick += frameDurations[i];
        }

        return frameStartTicks;
    }
}
