namespace RetroSharp.GameBoy.Tests;

using RetroSharp.Core.Sdk;
using RetroSharp.Core.Targeting;
using RetroSharp.GameBoy;
using RetroSharp.Parser;
using Xunit;

public sealed class GameBoySdkOperationBoundaryTests
{
    [Fact]
    public void Lowers_wait_frame_operation_to_existing_game_boy_vblank_routine()
    {
        var builder = new GbBuilder();
        var compiler = CreateRuntimeCompiler(builder);

        GameBoySdkOperationLowerer.Emit(compiler, new Sdk2DOperation.WaitFrame());

        Assert.Equal(
            [0xF0, 0x44, 0xFE, 0x90, 0x30, 0xFA, 0xF0, 0x44, 0xFE, 0x90, 0x38, 0xFA],
            builder.Build());
    }

    [Fact]
    public void Lowers_poll_input_operation_to_deterministic_game_boy_bytes()
    {
        var viaOperation = new GbBuilder();
        GameBoySdkOperationLowerer.Emit(CreateRuntimeCompiler(viaOperation), new Sdk2DOperation.PollInput());

        var viaDirect = new GbBuilder();
        CreateRuntimeCompiler(viaDirect).EmitPollInput();

        Assert.NotEmpty(viaOperation.Build());
        Assert.Equal(viaDirect.Build(), viaOperation.Build());
    }

    private static GameBoyRuntimeCompiler CreateRuntimeCompiler(GbBuilder builder)
    {
        var program = GameBoyVideoProgram.FromProgram(new SomeParser().Parse("void main() { }").Value);
        return new GameBoyRuntimeCompiler(builder, program);
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
    public void Collects_camera_set_position_with_vertical_axis_when_y_can_move()
    {
        const string source = """
                              void main() {
                                  map_column(0, 0, 4);
                                  map_column(1, 0, 4);
                                  camera_init(2, 11, 2);
                                  i16 cameraY = 1;
                                  camera_set_position(0, cameraY);
                                  camera_apply();
                              }
                              """;

        var operation = Assert.Single(GameBoyRomCompiler.CollectSdkOperations(source));
        var camera = Assert.IsType<Sdk2DOperation.SetCameraPosition>(operation);

        Assert.Equal(new SdkByteExpression.Constant(0), camera.X);
        Assert.Equal(new SdkByteExpression.Variable("cameraY"), camera.Y);
        Assert.Equal(ScrollAxes.Vertical, camera.Axes);
        Assert.Equal(32768, GameBoyRomCompiler.CompileSource(source).Length);
    }

    [Fact]
    public void Collects_world_tile_flags_query_with_byte_backed_coordinates()
    {
        const string source = """
                              void main() {
                                  world_column(0, 0, 4);
                                  world_flags(0, 0, 1);
                                  world_map(1, 11, 2);
                                  i16 worldX = 0;
                                  i16 flags = world_tile_flags_at(worldX, 8);
                              }
                              """;

        var operation = Assert.Single(GameBoyRomCompiler.CollectSdkOperations(source));
        var flags = Assert.IsType<Sdk2DOperation.ReadWorldTileFlags>(operation);

        Assert.Equal("default", flags.WorldId);
        Assert.Equal(new SdkByteExpression.Variable("worldX"), flags.WorldX);
        Assert.Equal(new SdkByteExpression.Constant(8), flags.WorldY);
    }

    [Fact]
    public void Camera_set_position_rejects_diagonal_movement_that_exceeds_game_boy_budget()
    {
        const string source = """
                              void main() {
                                  map_column(0, 0, 4);
                                  map_column(1, 0, 4);
                                  camera_init(2, 11, 2);
                                  i16 cameraX = 1;
                                  i16 cameraY = 1;
                                  camera_set_position(cameraX, cameraY);
                                  camera_apply();
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource(source));

        Assert.Equal(
            "Target 'gb' supports 20 background tile writes per frame, but 38 are required for moving the camera diagonally (18 column tiles + 20 row tiles).",
            exception.Message);
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
