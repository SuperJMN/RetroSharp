namespace RetroSharp.GameBoy.Tests;

using RetroSharp.Core.Sdk;
using RetroSharp.Core.Targeting;
using RetroSharp.GameBoy;
using Xunit;
using static RetroSharp.GameBoy.Tests.GameBoySdkOperationBoundaryTests;

public sealed class GameBoySdkCameraStreamingLoweringTests
{
    [Fact]
    public void Lowers_stream_map_column_operation_to_game_boy_vram_writes()
    {
        var builder = new GbBuilder();
        var compiler = CreateRuntimeCompiler(
            builder,
            """
            void Main() {
                Video.Init();
                World.Column(0, 1, 2, 3, 4);
                World.Column(1, 5, 6, 7, 8);
                World.Map(2, 11, 4);
            }
            """,
            Directory.GetCurrentDirectory());
        builder.Label(GameBoyRomBuilder.MapRowLabel(0));
        builder.Emit(1, 5);
        builder.Label(GameBoyRomBuilder.MapRowLabel(1));
        builder.Emit(2, 6);
        builder.Label(GameBoyRomBuilder.MapRowLabel(2));
        builder.Emit(3, 7);
        builder.Label(GameBoyRomBuilder.MapRowLabel(3));
        builder.Emit(4, 8);

        compiler.SdkOperationLowerer.Emit(
            new Sdk2DOperation.StreamMapColumn(TargetColumn: 20, SourceColumn: 0, Y: 11, Height: 4));

        var bytes = builder.Build();
        Assert.True(ContainsSequence(bytes, [0x01, 0x05]), "stream operation should keep map row 0 data reachable.");
        Assert.True(ContainsSequence(bytes, [0x3E, 0x00, 0x5F, 0x16, 0x00, 0x21]), "stream operation should load the source map column into DE and a row-table address into HL.");
        Assert.True(ContainsSequence(bytes, [0x3E, 0x14, 0xC6, 0x60, 0x6F, 0x26, 0x99, 0x78, 0x77]), "stream operation should stream row 11 into the target background column.");
    }
    [Fact]
    public void Collects_camera_set_position_with_byte_backed_expressions()
    {
        const string source = """
                              void Main() {
                                  World.Column(0, 0, 4);
                                  World.Column(1, 0, 4);
                                  World.Map(2, 11, 2);
                                  Camera.Init(2, 11, 2);
                                  i16 cameraX = 1;
                                  Camera.SetPosition(cameraX, 0);
                                  Camera.Apply();
                              }
                              """;

        var operations = GameBoyRomCompiler.CollectSdkOperations(source);
        Assert.Collection(
            operations,
            operation =>
            {
                var camera = Assert.IsType<Sdk2DOperation.SetCameraPosition>(operation);

                Assert.Equal(WordLocal("cameraX"), camera.X);
                Assert.Equal(new SdkWordExpression.Constant(0), camera.Y);
                Assert.Equal(ScrollAxes.Horizontal, camera.Axes);
            },
            operation =>
            {
                var apply = Assert.IsType<Sdk2DOperation.ApplyCamera>(operation);
                Assert.Equal(ScrollAxes.Horizontal | ScrollAxes.Vertical, apply.Axes);
            });
        Assert.Equal(32768, GameBoyRomCompiler.CompileSource(source).Length);
    }

    [Fact]
    public void Collects_camera_set_position_constant_above_byte_range_without_truncation()
    {
        const string source = """
                              void Main() {
                                  Camera.SetPosition(256, 0);
                              }
                              """;

        var camera = Assert.IsType<Sdk2DOperation.SetCameraPosition>(
            Assert.Single(GameBoyRomCompiler.CollectSdkOperations(source)));

        Assert.Equal(new SdkWordExpression.Constant(256), camera.X);
        Assert.Equal(ScrollAxes.Horizontal, camera.Axes);
    }

    [Fact]
    public void Collects_camera_set_position_with_vertical_axis_when_y_can_move()
    {
        const string source = """
                              void Main() {
                                  World.Column(0, 0, 4);
                                  World.Column(1, 0, 4);
                                  World.Map(2, 11, 2);
                                  Camera.Init(2, 11, 2);
                                  i16 cameraY = 1;
                                  Camera.SetPosition(0, cameraY);
                                  Camera.Apply();
                              }
                              """;

        var operations = GameBoyRomCompiler.CollectSdkOperations(source);
        Assert.Collection(
            operations,
            operation =>
            {
                var camera = Assert.IsType<Sdk2DOperation.SetCameraPosition>(operation);

                Assert.Equal(new SdkWordExpression.Constant(0), camera.X);
                Assert.Equal(WordLocal("cameraY"), camera.Y);
                Assert.Equal(ScrollAxes.Vertical, camera.Axes);
            },
            operation =>
            {
                var apply = Assert.IsType<Sdk2DOperation.ApplyCamera>(operation);
                Assert.Equal(ScrollAxes.Horizontal | ScrollAxes.Vertical, apply.Axes);
            });
        Assert.Equal(32768, GameBoyRomCompiler.CompileSource(source).Length);
    }

    [Fact]
    public void Collects_stream_map_column_before_game_boy_lowering()
    {
        const string source = """
                              void Main() {
                                  World.Column(0, 1, 2, 3, 4);
                                  World.Column(1, 5, 6, 7, 8);
                                  i16 targetColumn = 20;
                                  i16 sourceColumn = 0;
                                  map_stream_column(targetColumn, sourceColumn, 11, 4);
                              }
                              """;

        var operation = Assert.Single(GameBoyRomCompiler.CollectSdkOperations(source));
        var stream = Assert.IsType<Sdk2DOperation.StreamMapColumn>(operation);

        Assert.Equal(Local("targetColumn"), stream.TargetColumn);
        Assert.Equal(Local("sourceColumn"), stream.SourceColumn);
        Assert.Equal(11, stream.Y);
        Assert.Equal(4, stream.Height);
    }

    [Fact]
    public void Compilation_rejects_combined_streaming_that_exceeds_one_frame_budget()
    {
        const string source = """
                              void Main() {
                                  Video.Init();
                                  World.Column(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11);
                                  World.Map(1, 0, 11);
                                  i16 targetColumn = 20;
                                  i16 sourceColumn = 0;
                                  while (true) {
                                      Video.WaitVBlank();
                                      map_stream_column(targetColumn, sourceColumn, 0, 11);
                                      map_stream_column(targetColumn, sourceColumn, 0, 11);
                                  }
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource(source));

        Assert.Equal(
            "Target 'gb' supports 21 background tile writes per frame, but 22 are required for streaming background tiles in one frame.",
            exception.Message);
    }

    [Fact]
    public void Compilation_counts_user_helper_streaming_per_call_site_for_frame_budget()
    {
        const string source = """
                              void stream_once(i16 targetColumn, i16 sourceColumn) {
                                  map_stream_column(targetColumn, sourceColumn, 0, 11);
                              }

                              void Main() {
                                  Video.Init();
                                  World.Column(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11);
                                  World.Map(1, 0, 11);
                                  i16 targetColumn = 20;
                                  i16 sourceColumn = 0;
                                  while (true) {
                                      Video.WaitVBlank();
                                      stream_once(targetColumn, sourceColumn);
                                      stream_once(targetColumn, sourceColumn);
                                  }
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource(source));

        Assert.Equal(
            "Target 'gb' supports 21 background tile writes per frame, but 22 are required for streaming background tiles in one frame.",
            exception.Message);
    }

    [Fact]
    public void Compilation_validates_branch_alternatives_as_exclusive_frame_paths()
    {
        const string source = """
                              void Main() {
                                  Video.Init();
                                  World.Column(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11);
                                  World.Map(1, 0, 11);
                                  i16 targetColumn = 20;
                                  i16 sourceColumn = 0;
                                  while (true) {
                                      Video.WaitVBlank();
                                      if (targetColumn == 20) {
                                          map_stream_column(targetColumn, sourceColumn, 0, 11);
                                      } else {
                                          map_stream_column(targetColumn, sourceColumn, 0, 10);
                                      }
                                  }
                              }
                              """;

        Assert.Equal(32768, GameBoyRomCompiler.CompileSource(source).Length);
    }

    [Fact]
    public void Camera_set_position_accepts_diagonal_movement_on_game_boy_with_staggered_streaming_budget()
    {
        const string source = """
                              void Main() {
                                  World.Column(0, 0, 4);
                                  World.Column(1, 0, 4);
                                  World.Map(2, 11, 2);
                                  Camera.Init(2, 11, 2);
                                  i16 cameraX = 1;
                                  i16 cameraY = 1;
                                  Camera.SetPosition(cameraX, cameraY);
                                  Camera.Apply();
                              }
                              """;

        var operations = GameBoyRomCompiler.CollectSdkOperations(source);
        var camera = Assert.IsType<Sdk2DOperation.SetCameraPosition>(
            Assert.Single(operations.OfType<Sdk2DOperation.SetCameraPosition>()));

        Assert.Equal(WordLocal("cameraX"), camera.X);
        Assert.Equal(WordLocal("cameraY"), camera.Y);
        Assert.Equal(ScrollAxes.Horizontal | ScrollAxes.Vertical, camera.Axes);

        Assert.Equal(32768, GameBoyRomCompiler.CompileSource(source).Length);
    }

    [Fact]
    public void Camera_set_position_validates_arity_at_the_sdk_boundary()
    {
        const string source = """
                              void Main() {
                                  Camera.SetPosition(0);
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CollectSdkOperations(source));

        Assert.Contains("argument 2", exception.Message, StringComparison.Ordinal);
    }
}
