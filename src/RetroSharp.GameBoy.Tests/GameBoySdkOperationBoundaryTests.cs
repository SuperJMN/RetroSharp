namespace RetroSharp.GameBoy.Tests;

using RetroSharp.Core.Sdk;
using RetroSharp.GameBoy;
using Xunit;

public sealed class GameBoySdkOperationBoundaryTests
{
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
