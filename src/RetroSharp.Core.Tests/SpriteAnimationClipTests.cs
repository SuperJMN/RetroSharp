namespace RetroSharp.Core.Tests;

using RetroSharp.Core.Sdk;
using RetroSharp.Core.Targeting;
using Xunit;

public sealed class SpriteAnimationClipTests
{
    [Fact]
    public void Clip_data_maps_ticks_to_frames_at_duration_boundaries()
    {
        var clip = new SpriteAnimationClip("run", firstFrame: 1, frameDurations: [6, 6, 6]);

        Assert.Equal([1, 2, 3], clip.FrameIndices);
        Assert.Equal([6, 6, 6], clip.FrameDurations);
        Assert.Equal([0, 6, 12], clip.FrameStartTicks);
        Assert.Equal(18, clip.DurationTicks);

        Assert.Equal(1, clip.FrameAtTick(0));
        Assert.Equal(1, clip.FrameAtTick(5));
        Assert.Equal(2, clip.FrameAtTick(6));
        Assert.Equal(2, clip.FrameAtTick(11));
        Assert.Equal(3, clip.FrameAtTick(12));
        Assert.Equal(3, clip.FrameAtTick(17));
    }

    [Fact]
    public void Default_sprite_metadata_keeps_one_tick_animation_data_for_loaded_frames()
    {
        var metadata = SpriteAssetMetadata.Default(
            id: "player",
            logicalSize: new Size2D(18, 32),
            frameCount: 5,
            paletteSlots: 1);

        var clip = Assert.Single(metadata.AnimationClips);

        Assert.Equal("default", clip.Name);
        Assert.Equal(0, clip.FirstFrame);
        Assert.Equal(5, clip.FrameCount);
        Assert.Equal([0, 1, 2, 3, 4], clip.FrameIndices);
        Assert.Equal([1, 1, 1, 1, 1], clip.FrameDurations);
        Assert.Equal([0, 1, 2, 3, 4], clip.FrameStartTicks);
        Assert.Equal(5, clip.DurationTicks);
        Assert.Equal(4, clip.FrameAtTick(4));
    }
}
