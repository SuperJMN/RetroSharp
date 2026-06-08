namespace RetroSharp.GameBoy.Tests;

using RetroSharp.Core.Sdk;
using RetroSharp.GameBoy;
using Xunit;

public sealed class GameBoySdkOperationBoundaryTests
{
    [Fact]
    public void Lowers_wait_frame_operation_to_existing_game_boy_vblank_routine()
    {
        var builder = new GbBuilder();

        GameBoySdkOperationLowerer.Emit(builder, new Sdk2DOperation.WaitFrame());

        Assert.Equal(
            [0xF0, 0x44, 0xFE, 0x90, 0x30, 0xFA, 0xF0, 0x44, 0xFE, 0x90, 0x38, 0xFA],
            builder.Build());
    }

    [Fact]
    public void Collects_portable_sdk_operations_before_game_boy_lowering()
    {
        const string source = """
                              void tick() {
                                  input_poll();
                                  scroll_set(0, 0);
                                  return;
                              }

                              void main() {
                                  while (true) {
                                      video_wait_vblank();
                                      tick();
                                      sprite_set(0, 8, 16, 6, 0);
                                  }
                              }
                              """;

        var operations = GameBoyRomCompiler.CollectSdkOperations(source);

        Assert.Collection(
            operations,
            operation => Assert.IsType<Sdk2DOperation.WaitFrame>(operation),
            operation => Assert.IsType<Sdk2DOperation.PollInput>(operation));

        Assert.Equal(32768, GameBoyRomCompiler.CompileSource(source).Length);
    }
}
