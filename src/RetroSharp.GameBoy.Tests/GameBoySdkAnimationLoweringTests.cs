namespace RetroSharp.GameBoy.Tests;

using Xunit;
using static RetroSharp.GameBoy.Tests.GameBoySdkOperationBoundaryTests;

[Trait("RetroSharp.TestOwnership", "SdkLowering")]
public sealed class GameBoySdkAnimationLoweringTests
{
    [Fact]
    public void Animation_frame_maps_constant_ticks_through_looping_clip_data()
    {
        const string source = """
                              void Main() {
                                  Video.Init();
                                  Animation.Clip(run, 1, 6, 6, 6);
                                  sprite_set(0, 72, 80, Animation.Frame(run, 0), 0);
                                  sprite_set(1, 80, 80, Animation.Frame(run, 5), 0);
                                  sprite_set(2, 88, 80, Animation.Frame(run, 6), 0);
                                  sprite_set(3, 96, 80, Animation.Frame(run, 17), 0);
                                  sprite_set(4, 104, 80, Animation.Frame(run, 18), 0);
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x3E, 0x01, 0x6F, 0x26, 0x00, 0x7D, 0xEA, 0x02, 0xFE]), "tick 0 should select frame 1 through the complete zero-extended I16 intrinsic return.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x01, 0x6F, 0x26, 0x00, 0x7D, 0xEA, 0x06, 0xFE]), "tick 5 should still select frame 1 through the complete zero-extended I16 intrinsic return.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x02, 0x6F, 0x26, 0x00, 0x7D, 0xEA, 0x0A, 0xFE]), "tick 6 should select frame 2 through the complete zero-extended I16 intrinsic return.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x03, 0x6F, 0x26, 0x00, 0x7D, 0xEA, 0x0E, 0xFE]), "tick 17 should select frame 3 through the complete zero-extended I16 intrinsic return.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x01, 0x6F, 0x26, 0x00, 0x7D, 0xEA, 0x12, 0xFE]), "tick 18 should loop to frame 1 through the complete zero-extended I16 intrinsic return.");
    }

    [Fact]
    public void Animation_frame_lowers_dynamic_ticks_with_predictable_modulo_and_boundary_checks()
    {
        const string source = """
                              void Main() {
                                  Video.Init();
                                  Animation.Clip(run, 1, 6, 6, 6);
                                  i16 tick = 18;
                                  sprite_set(0, 72, 80, Animation.Frame(run, tick), 0);
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0xFE, 0x12, 0xDA]), "animation_frame should compare the tick against total clip duration before modulo subtraction.");
        Assert.True(ContainsSequence(rom, [0xD6, 0x12]), "animation_frame should subtract total clip duration while the tick is outside the clip.");
        Assert.True(ContainsSequence(rom, [0xFE, 0x06, 0xDA]), "animation_frame should test the first frame boundary.");
        Assert.True(ContainsSequence(rom, [0xFE, 0x0C, 0xDA]), "animation_frame should test the second frame boundary.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x01]), "animation_frame should be able to return frame 1.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x02]), "animation_frame should be able to return frame 2.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x03]), "animation_frame should be able to return frame 3.");
    }
}
