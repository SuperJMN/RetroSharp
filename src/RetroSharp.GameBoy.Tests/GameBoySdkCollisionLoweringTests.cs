namespace RetroSharp.GameBoy.Tests;

using RetroSharp.Core.Sdk;
using RetroSharp.Core.Targeting;
using RetroSharp.GameBoy;
using Xunit;
using static RetroSharp.GameBoy.Tests.GameBoySdkOperationBoundaryTests;

[Trait("RetroSharp.TestOwnership", "SdkLowering")]
public sealed class GameBoySdkCollisionLoweringTests
{
    [Fact]
    public void Lowers_world_tile_flags_value_operation_through_the_production_seam()
    {
        var builder = new GbBuilder();
        var compiler = CreateRuntimeCompiler(
            builder,
            """
            void Main() {
                World.Column(0, 0, 4);
                World.Flags(0, 1, 0);
                World.Map(1, 11, 2);
            }
            """,
            Directory.GetCurrentDirectory());
        builder.Label(GameBoyRomBuilder.MapFlagDataLabel);
        builder.Emit(1, 0);

        compiler.SdkOperationLowerer.Emit(
            new Sdk2DOperation.ReadWorldTileFlags("default", WorldX: 0, WorldY: 0));

        var bytes = builder.Build();
        Assert.True(ContainsSequence(bytes, [0x3E, 0x00, 0xCB, 0x3F, 0xCB, 0x3F, 0xCB, 0x3F, 0xFE, 0x01]), "world flag lowering should convert world pixels to a bounded tile column.");
        Assert.True(ContainsSequence(bytes, [0x21, 0x50, 0x01, 0x5F, 0x16, 0x00, 0x19, 0x7E]), "world flag lowering should read the selected collision byte from the emitted map flag table.");
    }

    [Fact]
    public void Lowers_camera_aabb_value_operation_through_the_production_seam()
    {
        const string source = """
                              void Main() {
                                  World.Column(0, 0, 4);
                                  World.Flags(0, 1, 0);
                                  World.Map(1, 11, 2);
                                  Camera.Init(1, 11, 2);
                              }
                              """;
        var builder = new GbBuilder();
        var program = GameBoyVideoProgram.FromProgram(ParseLoweredProgram(source));
        var compiler = new GameBoyRuntimeCompiler(builder, program);
        builder.Label(GameBoyRomBuilder.MapFlagDataLabel);
        builder.Emit(1, 0);
        compiler.EmitMain(program.MainBlock);

        compiler.SdkOperationLowerer.Emit(
            new Sdk2DOperation.CameraAabbTiles(
                "default",
                ScreenX: 72,
                WorldY: new SdkWordExpression.Constant(8),
                Width: 8,
                Height: 8,
                Flags: WorldTileFlags.Solid));

        var bytes = builder.Build();
        Assert.True(ContainsSequence(bytes, [0xC6, 0x48, 0xCB, 0x3F, 0xCB, 0x3F, 0xCB, 0x3F]), "camera AABB lowering should project the screen X probe to a tile column.");
        Assert.True(ContainsSequence(bytes, [0xE6, 0x01, 0xFE, 0x00]), "camera AABB lowering should mask the emitted collision result with the requested world flag.");
    }

    [Fact]
    public void Collects_world_tile_flags_query_with_byte_backed_coordinates()
    {
        const string source = """
                              void Main() {
                                  World.Column(0, 0, 4);
                                  World.Flags(0, 0, 1);
                                  World.Map(1, 11, 2);
                                  i16 worldX = 0;
                                  i16 flags = World.TileFlagsAt(worldX, 8);
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
                              void Main() {
                                  World.Column(0, 0, 4);
                                  World.Flags(0, 0, 1);
                                  World.Map(1, 11, 2);
                                  Camera.Init(1, 11, 2);
                                  i16 footY = 16;
                                  i16 hit = Camera.AabbTiles(72, footY, 16, 8, 1);
                              }
                              """;

        var operation = Assert.Single(GameBoyRomCompiler.CollectSdkOperations(source, WriteSpriteAsset()));
        var query = Assert.IsType<Sdk2DOperation.CameraAabbTiles>(operation);

        Assert.Equal("default", query.WorldId);
        Assert.Equal(new SdkByteExpression.Constant(72), query.ScreenX);
        Assert.Equal(WordLocal("footY"), query.WorldY);
        Assert.Equal(0, query.WorldYOffset);
        Assert.Equal(new SdkAabbExtent.Constant(16), query.Width);
        Assert.Equal(8, query.Height);
        Assert.Equal(WorldTileFlags.Solid, query.Flags);
    }

    [Fact]
    public void Collects_camera_aabb_tiles_query_with_sprite_width_extent()
    {
        const string source = """
                              void Main() {
                                  World.Column(0, 0, 4);
                                  World.Flags(0, 0, 1);
                                  World.Map(1, 11, 2);
                                  Camera.Init(1, 11, 2);
                                  Sprite.Asset(player_run, "player.sprite.json");
                                  i16 footY = 16;
                                  i16 hit = Camera.AabbTiles(72, footY, Sprite.Width(player_run), 8, 1);
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
                              void Main() {
                                  World.Column(0, 0, 4);
                                  World.Flags(0, 0, 1);
                                  World.Map(1, 11, 2);
                                  Camera.Init(1, 11, 2);
                                  i16 footY = 16;
                                  i16 hit = Camera.AabbTiles(72, footY - 8, 16, 8, 1);
                              }
                              """;

        var operation = Assert.Single(GameBoyRomCompiler.CollectSdkOperations(source));
        var query = Assert.IsType<Sdk2DOperation.CameraAabbTiles>(operation);

        Assert.Equal(WordLocal("footY"), query.WorldY);
        Assert.Equal(-8, query.WorldYOffset);
    }

    [Fact]
    public void Collects_camera_aabb_hit_top_query_with_sprite_width_and_search_offset()
    {
        const string source = """
                              void Main() {
                                  World.Column(0, 0, 4);
                                  World.Flags(0, 0, 1);
                                  World.Map(1, 11, 2);
                                  Camera.Init(1, 11, 2);
                                  Sprite.Asset(player_run, "player.sprite.json");
                                  i16 footY = 40;
                                  i16 hitTop = Camera.AabbHitTop(72, footY - 32, Sprite.Width(player_run), 40, 1);
                              }
                              """;

        var operation = Assert.Single(GameBoyRomCompiler.CollectSdkOperations(source, WriteSpriteAsset()));
        var query = Assert.IsType<Sdk2DOperation.CameraAabbHitTop>(operation);

        Assert.Equal("default", query.WorldId);
        Assert.Equal(new SdkByteExpression.Constant(72), query.ScreenX);
        Assert.Equal(WordLocal("footY"), query.WorldY);
        Assert.Equal(-32, query.WorldYOffset);
        Assert.Equal(new SdkAabbExtent.SpriteWidth("player_run"), query.Width);
        Assert.Equal(40, query.Height);
        Assert.Equal(WorldTileFlags.Solid, query.Flags);
    }

    [Fact]
    public void Collects_actor_pool_tile_helpers_as_camera_aabb_operations()
    {
        const string source = """
                              void Main() {
                                  World.Column(0, 0, 4);
                                  World.Flags(0, 0, 1);
                                  World.Map(1, 11, 2);
                                  Camera.Init(1, 11, 2);
                                  Actors.Pool(enemies, 1);
                                  Enemies.Def(Goomba, behavior: Walker, hitboxWidth: 16, hitboxHeight: 8);
                                  enemies[0].active = 1;
                                  enemies[0].kind = Goomba;
                                  enemies[0].x = 24;
                                  enemies[0].xHi = 0;
                                  enemies[0].y = 24;
                                  enemies.TouchTiles(0, 1);
                                  enemies.LandOnTiles(4, 12, 1);
                              }
                              """;

        var operations = GameBoyRomCompiler.CollectSdkOperations(source);

        Assert.Collection(
            operations,
            operation =>
            {
                var query = Assert.IsType<Sdk2DOperation.CameraScreenAabbTiles>(operation);
                Assert.Equal(Local("__enemies_touch_screen_x"), query.ScreenX);
                Assert.Equal(Local("__enemies_touch_screen_y"), query.ScreenY);
                Assert.Equal(0, query.ScreenYOffset);
                Assert.Equal(new SdkAabbExtent.Constant(16), query.Width);
                Assert.Equal(8, query.Height);
            },
            operation =>
            {
                var query = Assert.IsType<Sdk2DOperation.CameraScreenAabbHitTop>(operation);
                Assert.Equal(Local("__enemies_land_screen_x"), query.ScreenX);
                Assert.Equal(Local("__enemies_land_screen_y"), query.ScreenY);
                Assert.Equal(-4, query.ScreenYOffset);
                Assert.Equal(new SdkAabbExtent.Constant(16), query.Width);
                Assert.Equal(12, query.Height);
            });
    }
}
