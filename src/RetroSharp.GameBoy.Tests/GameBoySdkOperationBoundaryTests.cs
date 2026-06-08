namespace RetroSharp.GameBoy.Tests;

using RetroSharp.Core.Sdk;
using RetroSharp.Core.Targeting;
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

    [Fact]
    public void Collects_camera_set_position_with_byte_backed_expressions()
    {
        const string source = """
                              void main() {
                                  map_column(0, 0, 4);
                                  map_column(1, 0, 4);
                                  camera_init(2, 11, 2);
                                  i16 cameraX = 1;
                                  camera_set_position(cameraX, 0);
                                  camera_apply();
                              }
                              """;

        var operation = Assert.Single(GameBoyRomCompiler.CollectSdkOperations(source));
        var camera = Assert.IsType<Sdk2DOperation.SetCameraPosition>(operation);

        Assert.Equal(new SdkByteExpression.Variable("cameraX"), camera.X);
        Assert.Equal(new SdkByteExpression.Constant(0), camera.Y);
        Assert.Equal(ScrollAxes.Horizontal, camera.Axes);
        Assert.Equal(32768, GameBoyRomCompiler.CompileSource(source).Length);
    }

    [Fact]
    public void Camera_set_position_validates_arity_at_the_sdk_boundary()
    {
        const string source = """
                              void main() {
                                  camera_set_position(0);
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CollectSdkOperations(source));

        Assert.Equal("camera_set_position expects 2 arguments, got 1.", exception.Message);
    }
}
