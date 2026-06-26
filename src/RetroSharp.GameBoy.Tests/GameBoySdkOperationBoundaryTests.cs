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

    [Fact]
    public void Runtime_compiler_consumes_the_collected_sdk_operation_stream()
    {
        var builder = new GbBuilder();
        var program = ProgramWithOverriddenSdkOperations(
            """
            void main() {
                video_init();
                sprite_asset(player_run, "player.sprite.json");
                sprite_draw(player_run, 72, 80, 0);
            }
            """,
            WriteSpriteAsset(),
            [
                new Sdk2DOperation.DrawLogicalSprite(
                    "player_run",
                    X: new SdkByteExpression.Constant(24),
                    Y: new SdkByteExpression.Constant(80),
                    Frame: new SdkByteExpression.Constant(0),
                    FlipX: null,
                    PaletteSlot: 0,
                    StaticTransform: SpriteTransform.None),
            ]);
        var compiler = new GameBoyRuntimeCompiler(builder, program);

        compiler.Emit(program.MainBlock);

        var bytes = builder.Build();
        Assert.True(
            ContainsSequence(bytes, [0x3E, 0x18, 0xC6, 0x08, 0xEA, 0x01, 0xFE]),
            "Runtime emission should use the collected sprite operation operand.");
        Assert.False(
            ContainsSequence(bytes, [0x3E, 0x48, 0xC6, 0x08, 0xEA, 0x01, 0xFE]),
            "Runtime emission should not re-read the sprite operand from the AST call.");
    }

    [Fact]
    public void Lowers_draw_logical_sprite_operation_to_game_boy_metasprite_bytes()
    {
        var builder = new GbBuilder();
        var compiler = CreateRuntimeCompiler(
            builder,
            """
            void main() {
                video_init();
                sprite_asset(player_run, "player.sprite.json");
            }
            """,
            WriteSpriteAsset());

        GameBoySdkOperationLowerer.Emit(
            compiler,
            new Sdk2DOperation.DrawLogicalSprite(
                "player_run",
                X: new SdkByteExpression.Constant(72),
                Y: new SdkByteExpression.Constant(80),
                Frame: new SdkByteExpression.Constant(0),
                FlipX: null,
                PaletteSlot: 0,
                StaticTransform: SpriteTransform.None));

        var bytes = builder.Build();
        Assert.True(ContainsSequence(bytes, [0x3E, 0x50, 0xC6, 0x10, 0xEA, 0x00, 0xFE]), "sprite draw operation should write the logical Y plus the Game Boy sprite offset.");
        Assert.True(ContainsSequence(bytes, [0x3E, 0x48, 0xC6, 0x08, 0xEA, 0x01, 0xFE]), "sprite draw operation should write the logical X plus the Game Boy sprite offset.");
        Assert.True(ContainsSequence(bytes, [0xC6, 0x06, 0xEA, 0x02, 0xFE]), "sprite draw operation should use the first generated tile for the first hardware sprite.");
    }

    [Fact]
    public void Lowers_stream_map_column_operation_to_game_boy_vram_writes()
    {
        var builder = new GbBuilder();
        var compiler = CreateRuntimeCompiler(
            builder,
            """
            void main() {
                video_init();
                map_column(0, 1, 2, 3, 4);
                map_column(1, 5, 6, 7, 8);
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

        GameBoySdkOperationLowerer.Emit(
            compiler,
            new Sdk2DOperation.StreamMapColumn(TargetColumn: 20, SourceColumn: 0, Y: 11, Height: 4));

        var bytes = builder.Build();
        Assert.True(ContainsSequence(bytes, [0x01, 0x05]), "stream operation should keep map row 0 data reachable.");
        Assert.True(ContainsSequence(bytes, [0x3E, 0x00, 0x5F, 0x16, 0x00, 0x21]), "stream operation should load the source map column into DE and a row-table address into HL.");
        Assert.True(ContainsSequence(bytes, [0x3E, 0x14, 0xC6, 0x60, 0x6F, 0x26, 0x99, 0x78, 0x77]), "stream operation should stream row 11 into the target background column.");
    }

    private static GameBoyRuntimeCompiler CreateRuntimeCompiler(GbBuilder builder)
    {
        var program = GameBoyVideoProgram.FromProgram(new SomeParser().Parse("void main() { }").Value);
        return new GameBoyRuntimeCompiler(builder, program);
    }

    private static GameBoyRuntimeCompiler CreateRuntimeCompiler(GbBuilder builder, string source, string baseDirectory)
    {
        var program = GameBoyVideoProgram.FromProgram(new SomeParser().Parse(source).Value, baseDirectory);
        return new GameBoyRuntimeCompiler(builder, program);
    }

    private static GameBoyVideoProgram ProgramWithOverriddenSdkOperations(string source, string baseDirectory, IReadOnlyList<Sdk2DOperation> operations)
    {
        var program = GameBoyVideoProgram.FromProgram(new SomeParser().Parse(source).Value, baseDirectory);
        typeof(GameBoyVideoProgram)
            .GetProperty(nameof(GameBoyVideoProgram.SdkOperations))!
            .SetValue(program, operations);
        return program;
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

        var operations = GameBoyRomCompiler.CollectSdkOperations(source);
        Assert.Collection(
            operations,
            operation =>
            {
                var camera = Assert.IsType<Sdk2DOperation.SetCameraPosition>(operation);

                Assert.Equal(Local("cameraX"), camera.X);
                Assert.Equal(new SdkByteExpression.Constant(0), camera.Y);
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

        var operations = GameBoyRomCompiler.CollectSdkOperations(source);
        Assert.Collection(
            operations,
            operation =>
            {
                var camera = Assert.IsType<Sdk2DOperation.SetCameraPosition>(operation);

                Assert.Equal(new SdkByteExpression.Constant(0), camera.X);
                Assert.Equal(Local("cameraY"), camera.Y);
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

        var operation = Assert.Single(GameBoyRomCompiler.CollectSdkOperations(source, WriteSpriteAsset()));
        var flags = Assert.IsType<Sdk2DOperation.ReadWorldTileFlags>(operation);

        Assert.Equal("default", flags.WorldId);
        Assert.Equal(Local("worldX"), flags.WorldX);
        Assert.Equal(new SdkByteExpression.Constant(8), flags.WorldY);
    }

    [Fact]
    public void Collects_camera_aabb_tiles_query_with_byte_backed_world_y()
    {
        const string source = """
                              void main() {
                                  world_column(0, 0, 4);
                                  world_flags(0, 0, 1);
                                  world_map(1, 11, 2);
                                  camera_init(1, 11, 2);
                                  i16 footY = 16;
                                  i16 hit = camera.AabbTiles(72, footY, 16, 8, 1);
                              }
                              """;

        var operation = Assert.Single(GameBoyRomCompiler.CollectSdkOperations(source, WriteSpriteAsset()));
        var query = Assert.IsType<Sdk2DOperation.CameraAabbTiles>(operation);

        Assert.Equal("default", query.WorldId);
        Assert.Equal(72, query.ScreenX);
        Assert.Equal(Local("footY"), query.WorldY);
        Assert.Equal(0, query.WorldYOffset);
        Assert.Equal(new SdkAabbExtent.Constant(16), query.Width);
        Assert.Equal(8, query.Height);
        Assert.Equal(WorldTileFlags.Solid, query.Flags);
    }

    [Fact]
    public void Collects_camera_aabb_tiles_query_with_sprite_width_extent()
    {
        const string source = """
                              void main() {
                                  world_column(0, 0, 4);
                                  world_flags(0, 0, 1);
                                  world_map(1, 11, 2);
                                  camera_init(1, 11, 2);
                                  sprite_asset(player_run, "player.sprite.json");
                                  i16 footY = 16;
                                  i16 hit = camera.AabbTiles(72, footY, sprite_width(player_run), 8, 1);
                              }
                              """;

        var operation = Assert.Single(GameBoyRomCompiler.CollectSdkOperations(source, WriteSpriteAsset()));
        var query = Assert.IsType<Sdk2DOperation.CameraAabbTiles>(operation);

        Assert.Equal(new SdkAabbExtent.SpriteWidth("player_run"), query.Width);
    }

    [Fact]
    public void Collects_camera_aabb_tiles_query_with_constant_world_y_offset()
    {
        const string source = """
                              void main() {
                                  world_column(0, 0, 4);
                                  world_flags(0, 0, 1);
                                  world_map(1, 11, 2);
                                  camera_init(1, 11, 2);
                                  i16 footY = 16;
                                  i16 hit = camera.AabbTiles(72, footY - 8, 16, 8, 1);
                              }
                              """;

        var operation = Assert.Single(GameBoyRomCompiler.CollectSdkOperations(source));
        var query = Assert.IsType<Sdk2DOperation.CameraAabbTiles>(operation);

        Assert.Equal(Local("footY"), query.WorldY);
        Assert.Equal(-8, query.WorldYOffset);
    }

    [Fact]
    public void Collects_camera_aabb_hit_top_query_with_sprite_width_and_search_offset()
    {
        const string source = """
                              void main() {
                                  world_column(0, 0, 4);
                                  world_flags(0, 0, 1);
                                  world_map(1, 11, 2);
                                  camera_init(1, 11, 2);
                                  sprite_asset(player_run, "player.sprite.json");
                                  i16 footY = 40;
                                  i16 hitTop = camera.AabbHitTop(72, footY - 32, sprite_width(player_run), 40, 1);
                              }
                              """;

        var operation = Assert.Single(GameBoyRomCompiler.CollectSdkOperations(source, WriteSpriteAsset()));
        var query = Assert.IsType<Sdk2DOperation.CameraAabbHitTop>(operation);

        Assert.Equal("default", query.WorldId);
        Assert.Equal(72, query.ScreenX);
        Assert.Equal(Local("footY"), query.WorldY);
        Assert.Equal(-32, query.WorldYOffset);
        Assert.Equal(new SdkAabbExtent.SpriteWidth("player_run"), query.Width);
        Assert.Equal(40, query.Height);
        Assert.Equal(WorldTileFlags.Solid, query.Flags);
    }

    [Fact]
    public void Collects_sprite_draw_with_runtime_operands()
    {
        const string source = """
                              void main() {
                                  i16 y = 80;
                                  i16 frame = 1;
                                  bool flipX = true;
                                  sprite_draw(player_run, 72, y, frame, flipX, 1);
                              }
                              """;

        var operation = Assert.Single(GameBoyRomCompiler.CollectSdkOperations(source));
        var draw = Assert.IsType<Sdk2DOperation.DrawLogicalSprite>(operation);

        Assert.Equal("player_run", draw.SpriteId);
        Assert.Equal(new SdkByteExpression.Constant(72), draw.X);
        Assert.Equal(Local("y"), draw.Y);
        Assert.Equal(Local("frame"), draw.Frame);
        Assert.Equal(Local("flipX"), draw.FlipX);
        Assert.Equal(1, draw.PaletteSlot);
        Assert.Equal(SpriteTransform.None, draw.StaticTransform);
    }

    [Fact]
    public void Collects_byte_operands_as_typed_storage_locations()
    {
        const string source = """
                              struct Actor { u8 x; }

                              void main() {
                                  Actor actor = { x: 24 };
                                  u8 frames[3] = [0, 1, 2];
                                  sprite_draw(player_run, actor.x, frames[2], 0);
                              }
                              """;

        var operation = Assert.Single(GameBoyRomCompiler.CollectSdkOperations(source));
        var draw = Assert.IsType<Sdk2DOperation.DrawLogicalSprite>(operation);

        Assert.Equal(Field(LocalLocation("actor"), "x"), draw.X);
        Assert.Equal(Indexed("frames", 2), draw.Y);
    }

    [Fact]
    public void Collects_stream_map_column_before_game_boy_lowering()
    {
        const string source = """
                              void main() {
                                  map_column(0, 1, 2, 3, 4);
                                  map_column(1, 5, 6, 7, 8);
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
                              void main() {
                                  video_init();
                                  map_column(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12);
                                  i16 targetColumn = 20;
                                  i16 sourceColumn = 0;
                                  while (true) {
                                      video_wait_vblank();
                                      map_stream_column(targetColumn, sourceColumn, 0, 12);
                                      map_stream_column(targetColumn, sourceColumn, 0, 10);
                                  }
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource(source));

        Assert.Equal(
            "Target 'gb' supports 20 background tile writes per frame, but 22 are required for streaming background tiles in one frame.",
            exception.Message);
    }

    [Fact]
    public void Compilation_counts_user_helper_streaming_per_call_site_for_frame_budget()
    {
        const string source = """
                              void stream_once(i16 targetColumn, i16 sourceColumn) {
                                  map_stream_column(targetColumn, sourceColumn, 0, 11);
                              }

                              void main() {
                                  video_init();
                                  map_column(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12);
                                  i16 targetColumn = 20;
                                  i16 sourceColumn = 0;
                                  while (true) {
                                      video_wait_vblank();
                                      stream_once(targetColumn, sourceColumn);
                                      stream_once(targetColumn, sourceColumn);
                                  }
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource(source));

        Assert.Equal(
            "Target 'gb' supports 20 background tile writes per frame, but 22 are required for streaming background tiles in one frame.",
            exception.Message);
    }

    [Fact]
    public void Compilation_validates_branch_alternatives_as_exclusive_frame_paths()
    {
        const string source = """
                              void main() {
                                  video_init();
                                  map_column(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12);
                                  i16 targetColumn = 20;
                                  i16 sourceColumn = 0;
                                  while (true) {
                                      video_wait_vblank();
                                      if (targetColumn == 20) {
                                          map_stream_column(targetColumn, sourceColumn, 0, 12);
                                      } else {
                                          map_stream_column(targetColumn, sourceColumn, 0, 10);
                                      }
                                  }
                              }
                              """;

        Assert.Equal(32768, GameBoyRomCompiler.CompileSource(source).Length);
    }

    [Fact]
    public void Compilation_rejects_sprite_draws_that_exceed_one_frame_hardware_sprite_budget()
    {
        var draws = string.Join(
            Environment.NewLine,
            Enumerable.Range(0, 41).Select(index => $"        sprite_draw(player_run, {index % 20}, {(index % 4) * 20}, 0);"));
        var source = """
                     void main() {
                         video_init();
                         sprite_asset(player_run, "player.sprite.json");
                         while (true) {
                             video_wait_vblank();

                     """ + draws + """
                         }
                     }
                     """;

        var exception = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource(source, WriteSpriteAsset()));

        Assert.Equal(
            "Target 'gb' supports 40 hardware sprites per frame, but 41 are required for drawing logical sprites in one frame.",
            exception.Message);
    }

    [Fact]
    public void Compilation_rejects_constant_y_sprite_draws_that_exceed_scanline_budget()
    {
        var draws = string.Join(
            Environment.NewLine,
            Enumerable.Range(0, 11).Select(index => $"        sprite_draw(player_run, {index * 8}, 16, 0);"));
        var source = """
                     void main() {
                         video_init();
                         sprite_asset(player_run, "player.sprite.json");
                         while (true) {
                             video_wait_vblank();

                     """ + draws + """
                         }
                     }
                     """;

        var exception = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource(source, WriteSpriteAsset()));

        Assert.Equal(
            "Target 'gb' supports 10 hardware sprites per scanline, but 11 are required on scanline 16 for drawing logical sprites in one frame.",
            exception.Message);
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

    private static string WriteSpriteAsset()
    {
        var directory = Path.Combine(Path.GetTempPath(), "retrosharp-gb-sdk-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, "player.sprite.json"),
            """
            {
              "platforms": {
                "gb": {
                  "frames": [
                    [
                      "01230123",
                      "01230123",
                      "01230123",
                      "01230123",
                      "01230123",
                      "01230123",
                      "01230123",
                      "01230123",
                      "01230123",
                      "01230123",
                      "01230123",
                      "01230123",
                      "01230123",
                      "01230123",
                      "01230123",
                      "01230123"
                    ]
                  ]
                }
              }
            }
            """);
        return directory;
    }

    private static bool ContainsSequence(byte[] bytes, byte[] sequence)
    {
        for (var i = 0; i <= bytes.Length - sequence.Length; i++)
        {
            var matched = true;
            for (var j = 0; j < sequence.Length; j++)
            {
                if (bytes[i + j] != sequence[j])
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
            {
                return true;
            }
        }

        return false;
    }

    private static SdkByteExpression.Variable Local(string name)
    {
        return new SdkByteExpression.Variable(LocalLocation(name));
    }

    private static SdkByteExpression.Variable Field(SdkStorageLocation target, string fieldName)
    {
        return new SdkByteExpression.Variable(new SdkStorageLocation.Field(target, fieldName));
    }

    private static SdkByteExpression.Variable Indexed(string baseName, int index)
    {
        return new SdkByteExpression.Variable(new SdkStorageLocation.IndexedElement(baseName, index));
    }

    private static SdkStorageLocation.Local LocalLocation(string name)
    {
        return new SdkStorageLocation.Local(name);
    }
}
