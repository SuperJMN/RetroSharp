namespace RetroSharp.GameBoy.Tests;

using RetroSharp.Core.Sdk;
using RetroSharp.Core.Targeting;
using RetroSharp.GameBoy;
using Xunit;
using static RetroSharp.GameBoy.Tests.GameBoySdkOperationBoundaryTests;

public sealed class GameBoySdkSpriteLoweringTests
{
    [Fact]
    public void Lowers_draw_logical_sprite_operation_to_game_boy_metasprite_bytes()
    {
        var builder = new GbBuilder();
        var compiler = CreateRuntimeCompiler(
            builder,
            """
            void Main() {
                Video.Init();
                Sprite.Asset(player_run, "player.sprite.json");
            }
            """,
            WriteSpriteAsset());

        compiler.SdkOperationLowerer.Emit(
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
    public void Collects_actor_draw_with_animation_frame_as_sprite_draw_frame()
    {
        const string source = """
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(player_run, "player.sprite.json");
                                  Animation.Clip(walk, 0, 4, 4);
                                  Actors.Pool(enemies, 1);
                                  Enemies.Def(Goomba, sprite: player_run, behavior: Walker, animation: walk);
                                  enemies[0].active = 1;
                                  enemies[0].kind = Goomba;
                                  enemies[0].animTick = 5;
                                  enemies.Draw();
                              }
                              """;

        var operation = Assert.Single(GameBoyRomCompiler.CollectSdkOperations(source, WriteSpriteAsset()));
        var draw = Assert.IsType<Sdk2DOperation.DrawLogicalSprite>(operation);

        Assert.Equal("player_run", draw.SpriteId);
        Assert.Equal(Local("__enemies_draw_frame_Goomba"), draw.Frame);
    }

    [Fact]
    public void Collects_sprite_draw_with_runtime_operands()
    {
        const string source = """
                              void Main() {
                                  i16 y = 80;
                                  i16 frame = 1;
                                  bool flipX = true;
                                  Sprite.Draw(player_run, 72, y, frame, flipX, 1);
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
    public void Collects_sprite_draw_from_compile_time_operand_intrinsic()
    {
        const string source = """
                              void Main() {
                                  i16 y = 80;
                                  i16 frame = 1;
                                  bool flipX = true;
                                  Sprite.Draw(player_run, 72, y, frame, flipX, 1);
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
    }

    [Fact]
    public void Compilation_rejects_sprite_draws_that_exceed_one_frame_hardware_sprite_budget()
    {
        var draws = string.Join(
            Environment.NewLine,
            Enumerable.Range(0, 41).Select(index => $"        Sprite.Draw(player_run, {index % 20}, {(index % 4) * 20}, 0);"));
        var source = """
                     void Main() {
                         Video.Init();
                         Sprite.Asset(player_run, "player.sprite.json");
                         while (true) {
                             Video.WaitVBlank();

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
            Enumerable.Range(0, 11).Select(index => $"        Sprite.Draw(player_run, {index * 8}, 16, 0);"));
        var source = """
                     void Main() {
                         Video.Init();
                         Sprite.Asset(player_run, "player.sprite.json");
                         while (true) {
                             Video.WaitVBlank();

                     """ + draws + """
                         }
                     }
                     """;

        var exception = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource(source, WriteSpriteAsset()));

        Assert.Equal(
            "Target 'gb' supports 10 hardware sprites per scanline, but 11 are required on scanline 16 for drawing logical sprites in one frame.",
            exception.Message);
    }
}
