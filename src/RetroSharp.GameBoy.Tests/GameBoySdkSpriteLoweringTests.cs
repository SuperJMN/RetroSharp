namespace RetroSharp.GameBoy.Tests;

using RetroSharp.Core.Sdk;
using RetroSharp.Core.Targeting;
using RetroSharp.GameBoy;
using Xunit;
using static RetroSharp.GameBoy.Tests.GameBoySdkOperationBoundaryTests;
using static RetroSharp.GameBoy.Tests.GameBoyTestSupport;

[Trait("RetroSharp.TestOwnership", "SdkLowering")]
public sealed class GameBoySdkSpriteLoweringTests
{
    [Fact]
    [Trait("RetroSharp.TestOwnership", "SdkLowering")]
    public void Golden_sprite_draw_emission_is_pinned_gb()
    {
        var baseDirectory = WriteSpriteJsonAsset(
            "player.sprite.json",
            SpriteJson(
                Rows(
                    8,
                    16,
                    "01230123",
                    "32103210")));

        const string source = """
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(player_run, "player.sprite.json");
                                  Sprite.Draw(player_run, 72, 80, 0, false, 1);
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source, baseDirectory);

        Assert.Equal("5D42DDFDB36FD0FCE746A9960C0379D6F9DE6D747735D7498BB11261380D407C", Fingerprint(rom));
    }

    [Fact]
    [Trait("RetroSharp.TestOwnership", "SdkLowering")]
    public void Compiles_sprite_asset_draw_to_a_game_boy_metasprite()
    {
        var baseDirectory = WriteSpriteJsonAsset(
            "player.sprite.json",
            SpriteJson(
                Rows(
                    8,
                    16,
                    "01230123",
                    "32103210")));

        const string source = """
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(player_run, "player.sprite.json");
                                  Sprite.Draw(player_run, 72, 80, 0);
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source, baseDirectory);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x55, 0x33, 0xAA, 0xCC]), "ROM should contain tile data loaded from the editable sprite asset.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x50, 0xC6, 0x10, 0xEA, 0x00, 0xFE]), "sprite_draw should write the logical Y plus the Game Boy sprite offset.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x48, 0xC6, 0x08, 0xEA, 0x01, 0xFE]), "sprite_draw should write the logical X plus the Game Boy sprite offset.");
        Assert.True(ContainsSequence(rom, [0xC6, 0x06, 0xEA, 0x02, 0xFE]), "sprite_draw should use the first generated tile for the first hardware sprite.");
    }

    [Fact]
    [Trait("RetroSharp.TestOwnership", "SdkLowering")]
    public void Sprite_draw_composes_16x32_assets_from_four_hardware_sprites()
    {
        var baseDirectory = WriteSpriteJsonAsset("player.sprite.json", SpriteJson(Rows(16, 32)));
        const string source = """
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(big_player, "player.sprite.json");
                                  Sprite.Draw(big_player, 72, 64, 0);
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source, baseDirectory);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x3E, 0x40, 0xC6, 0x10, 0xEA, 0x00, 0xFE]), "Top-left piece should use the logical Y coordinate.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x48, 0xC6, 0x10, 0xEA, 0x05, 0xFE]), "Top-right piece should add the 8 px X offset.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x40, 0xC6, 0x20, 0xEA, 0x08, 0xFE]), "Bottom-left piece should add the 16 px Y offset.");
        Assert.True(ContainsSequence(rom, [0xC6, 0x0C, 0xEA, 0x0E, 0xFE]), "Bottom-right piece should use the fourth generated 8x16 tile pair.");
    }

    [Fact]
    [Trait("RetroSharp.TestOwnership", "SdkLowering")]
    public void Sprite_draw_accepts_logical_flip_x_and_flips_logical_metasprites_horizontally()
    {
        var baseDirectory = WriteSpriteJsonAsset("player.sprite.json", SpriteJson(Rows(16, 32)));
        const string source = """
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(big_player, "player.sprite.json");
                                  bool flipX = true;
                                  Sprite.Draw(big_player, 72, 64, 0, flipX);
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source, baseDirectory);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x3E, 0x20]), "sprite_draw should lower logical flipX to the Game Boy OAM X-flip bit.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xFE, 0x00, 0xCA]), "sprite_draw should test the logical flipX boolean before placing metasprite pieces.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x48, 0xC6, 0x10, 0xEA, 0x01, 0xFE]), "X-flipped first logical piece should move to the mirrored top-right hardware X coordinate.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x48, 0xC6, 0x08, 0xEA, 0x05, 0xFE]), "X-flipped second logical piece should move to the mirrored top-left hardware X coordinate.");
        Assert.True(ContainsSequence(rom, [0xC6, 0x06, 0xEA, 0x02, 0xFE]), "X-flipped first logical piece should keep its own tile and rely on the OAM flip bit.");
        Assert.True(ContainsSequence(rom, [0xC6, 0x08, 0xEA, 0x06, 0xFE]), "X-flipped second logical piece should keep its own tile and rely on the OAM flip bit.");
    }

    [Fact]
    [Trait("RetroSharp.TestOwnership", "SdkLowering")]
    public void Sprite_draw_accepts_logical_palette_slot_and_lowers_to_game_boy_object_palette_bit()
    {
        var baseDirectory = WriteSpriteJsonAsset("player.sprite.json", SpriteJson(Rows(16, 32)));
        const string source = """
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(player, "player.sprite.json");
                                  Sprite.Draw(player, 72, 64, 0, false, 1);
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source, baseDirectory);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x3E, 0x10, 0xEA, 0x03, 0xFE]), "palette slot 1 should lower to the Game Boy OBP1 OAM attribute bit.");
    }

    [Fact]
    [Trait("RetroSharp.TestOwnership", "SdkLowering")]
    public void Sprite_draw_combines_logical_flip_x_and_palette_slot_in_oam_attributes()
    {
        var baseDirectory = WriteSpriteJsonAsset("player.sprite.json", SpriteJson(Rows(16, 32)));
        const string source = """
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(player, "player.sprite.json");
                                  Sprite.Draw(player, 72, 64, 0, true, 1);
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source, baseDirectory);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x3E, 0x30, 0xEA, 0x03, 0xFE]), "flipX and palette slot 1 should combine into OAM attributes without exposing raw flags in source.");
    }

    [Fact]
    [Trait("RetroSharp.TestOwnership", "SdkLowering")]
    public void Sprite_draw_rejects_palette_slots_outside_game_boy_capabilities()
    {
        var baseDirectory = WriteSpriteJsonAsset("player.sprite.json", SpriteJson(Rows(16, 32)));
        const string source = """
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(player, "player.sprite.json");
                                  Sprite.Draw(player, 72, 64, 0, false, 2);
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource(source, baseDirectory));

        Assert.Equal("Target 'gb' supports sprite palette slots 0..1, but slot 2 was requested.", exception.Message);
    }

    [Fact]
    [Trait("RetroSharp.TestOwnership", "SdkLowering")]
    public void Sprite_draw_flips_against_logical_width_before_padding()
    {
        var baseDirectory = WriteSpriteJsonAsset("player.sprite.json", SpriteJson(Rows(18, 16)));
        const string source = """
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(player, "player.sprite.json");
                                  bool flipX = true;
                                  Sprite.Draw(player, 72, 64, 0, flipX);
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source, baseDirectory);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x3E, 0x48, 0xC6, 0x12, 0xEA, 0x01, 0xFE]), "The first 8 px piece should move to logical X + 10 when an 18 px sprite is flipped.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x48, 0xC6, 0x0A, 0xEA, 0x05, 0xFE]), "The middle 8 px piece should move to logical X + 2 when an 18 px sprite is flipped.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x48, 0xC6, 0x02, 0xEA, 0x09, 0xFE]), "The padded edge piece should straddle the logical origin instead of adding padded left spacing.");
    }

    [Fact]
    [Trait("RetroSharp.TestOwnership", "SdkLowering")]
    public void Sprite_draw_rejects_raw_oam_attribute_constants_in_portable_flip_argument()
    {
        var baseDirectory = WriteSpriteJsonAsset("player.sprite.json", SpriteJson(Rows(16, 32)));
        const string source = """
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(player, "player.sprite.json");
                                  Sprite.Draw(player, 72, 64, 0, 32);
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource(source, baseDirectory));

        Assert.Equal("sprite_draw argument 5 is portable flipX and must be 0, 1, true, false, or a local bool-like value. Use sprite_set for raw Game Boy OAM attributes.", exception.Message);
    }

    [Fact]
    [Trait("RetroSharp.TestOwnership", "SdkLowering")]
    public void Sprite_draw_treats_frame_as_a_logical_frame_index()
    {
        var baseDirectory = WriteSpriteJsonAsset(
            "player.sprite.json",
            SpriteJson(
                Rows(8, 16, "01230123"),
                Rows(8, 16, "32103210")));
        const string source = """
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(player_run, "player.sprite.json");
                                  i16 frame = 1;
                                  Sprite.Draw(player_run, 72, 80, frame);
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source, baseDirectory);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0x47, 0xAF, 0x80, 0x80, 0xC6, 0x06, 0xEA, 0x02, 0xFE]), "sprite_draw should multiply the logical frame by the per-frame tile count.");
    }

    [Fact]
    [Trait("RetroSharp.TestOwnership", "SdkLowering")]
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
    [Trait("RetroSharp.TestOwnership", "SdkLowering")]
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
    [Trait("RetroSharp.TestOwnership", "SdkLowering")]
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
    [Trait("RetroSharp.TestOwnership", "SdkLowering")]
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
    [Trait("RetroSharp.TestOwnership", "SdkLowering")]
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
    [Trait("RetroSharp.TestOwnership", "SdkLowering")]
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
