namespace RetroSharp.GameBoy.Tests;

using RetroSharp.Core.Sdk;
using RetroSharp.Core.Sdk.Tiled;
using RetroSharp.Core.Targeting;
using RetroSharp.GameBoy;
using RetroSharp.Parser;
using RetroSharp.Sdk;
using Xunit;
using static RetroSharp.GameBoy.Tests.GameBoyTestSupport;

public partial class GameBoyRomCompilerTests
{
    [Fact]
    public void Inline_helper_wrapping_camera_set_position_is_byte_identical()
    {
        const string direct = """
                              void Main() {
                                  Video.Init();
                                  World.Column(0, 1, 2);
                                  World.Map(1, 10, 2);
                                  Camera.Init(1, 10, 2);
                                  i16 x = 4;
                                  Camera.SetPosition(x, 0);
                              }
                              """;
        const string wrapped = """
                               inline void csp(i16 px) {
                                   Camera.SetPosition(px, 0);
                               }
                               void Main() {
                                   Video.Init();
                                   World.Column(0, 1, 2);
                                   World.Map(1, 10, 2);
                                   Camera.Init(1, 10, 2);
                                   i16 x = 4;
                                   csp(x);
                               }
                               """;
        Assert.Equal(GameBoyRomCompiler.CompileSource(direct), GameBoyRomCompiler.CompileSource(wrapped));
    }

    [Fact]
    public void Compiles_camera_library_helpers_over_game_boy_intrinsic_like_sdk_operations()
    {
        const string direct = """
                              void Main() {
                                  Video.Init();
                                  World.Column(0, 1, 2);
                                  World.Map(1, 10, 2);
                                  Camera.Init(1, 10, 2);
                                  i16 x = 4;
                                  Camera.SetPosition(x, 0);
                                  Camera.Apply();
                              }
                              """;
        const string library = """
                               void Main() {
                                   Video.Init();
                                   World.Column(0, 1, 2);
                                   World.Map(1, 10, 2);
                                   Camera.Init(1, 10, 2);
                                   i16 x = 4;
                                   Camera.SetPosition(x, 0);
                                   Camera.Apply();
                               }
                               """;
        Assert.Contains("class RetroSharp_Portable2D_Camera", SdkLibrarySource.ForTarget(GameBoyTarget.Intrinsics), StringComparison.Ordinal);
        Assert.Equal(GameBoyRomCompiler.CompileSource(direct), GameBoyRomCompiler.CompileSource(library));
    }

    [Fact]
    public void Sprite_draw_via_library_helper_is_byte_identical_gb()
    {
        var baseDirectory = WriteSpriteJsonAsset(
            "player.sprite.json",
            SpriteJson(Rows(8, 16, "01230123", "32103210")));

        const string direct = """
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(player, "player.sprite.json");
                                  Sprite.Draw(player, 72, 80, 0, false, 1);
                              }
                              """;
        const string library = """
                               void Main() {
                                   Video.Init();
                                   Sprite.Asset(player, "player.sprite.json");
                                   Sprite.Draw(player, 72, 80, 0, false, 1);
                               }
                               """;

        var sdkLibrary = SdkLibrarySource.ForTarget(GameBoyTarget.Intrinsics);

        Assert.Contains("class RetroSharp_Portable2D_Sprite", sdkLibrary, StringComparison.Ordinal);
        Assert.Contains("[intrinsic(\"sprite_draw\")]", sdkLibrary, StringComparison.Ordinal);
        Assert.Equal(GameBoyRomCompiler.CompileSource(direct, baseDirectory), GameBoyRomCompiler.CompileSource(library, baseDirectory));
    }
    [Fact]
    public void Sprite_draw_library_preserves_capability_and_budget_checks_gb()
    {
        var baseDirectory = WriteSpriteJsonAsset(
            "player.sprite.json",
            SpriteJson(Rows(16, 32)));

        const string paletteSource = """
                                     void Main() {
                                         Video.Init();
                                         Sprite.Asset(player, "player.sprite.json");
                                         Sprite.Draw(player, 72, 64, 0, false, 2);
                                     }
                                     """;

        var paletteException = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource(paletteSource, baseDirectory));
        Assert.Equal("Target 'gb' supports sprite palette slots 0..1, but slot 2 was requested.", paletteException.Message);

        var draws = string.Join(
            Environment.NewLine,
            Enumerable.Range(0, 41).Select(index => $"        Sprite.Draw(player, {index % 20}, {(index % 4) * 20}, 0);"));
        var budgetSource = """
                           void Main() {
                               Video.Init();
                               Sprite.Asset(player, "player.sprite.json");
                               while (true) {
                                   Video.WaitVBlank();

                           """ + draws + """
                               }
                           }
                           """;

        var budgetException = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource(budgetSource, baseDirectory));
        Assert.Equal(
            "Target 'gb' supports 40 hardware sprites per frame, but 164 are required for drawing logical sprites in one frame.",
            budgetException.Message);
    }

    [Fact]
    public void Sprite_draw_source_package_helper_compiles_gb()
    {
        var baseDirectory = WriteSpriteJsonAsset(
            "player.sprite.json",
            SpriteJson(Rows(8, 16, "01230123", "32103210")));

        const string source = """
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(player, "player.sprite.json");
                                  Sprite.Draw(player, 72, 80, 0);
                              }
                              """;

        _ = GameBoyRomCompiler.CompileSource(source, baseDirectory);
    }

    [Fact]
    public void Collision_aabb_via_compile_time_operand_intrinsic_is_byte_identical_gb()
    {
        const string direct = """
                              void DefineWorld() {
                                  World.Column(0, 0, 4);
                                  World.Column(1, 0, 4);
                                  World.Column(2, 0, 4);
                                  World.Flags(0, 0, 1);
                                  World.Flags(1, 0, 1);
                                  World.Flags(2, 0, 1);
                                  World.Map(3, 11, 2);
                                  Camera.Init(3, 11, 2);
                              }

                              void Main() {
                                  DefineWorld();
                                  i16 footY = 16;
                                  i16 hit = Camera.AabbTiles(72, footY - 8, 16, 16, 1);
                                  i16 hitTop = Camera.AabbHitTop(72, footY - 8, 16, 16, 1);
                              }
                              """;
        const string library = """
                               void DefineWorld() {
                                   World.Column(0, 0, 4);
                                   World.Column(1, 0, 4);
                                   World.Column(2, 0, 4);
                                   World.Flags(0, 0, 1);
                                   World.Flags(1, 0, 1);
                                   World.Flags(2, 0, 1);
                                   World.Map(3, 11, 2);
                                   Camera.Init(3, 11, 2);
                               }

                               void Main() {
                                   DefineWorld();
                                   i16 footY = 16;
                                   i16 hit = Camera.AabbTiles(72, footY - 8, 16, 16, 1);
                                   i16 hitTop = Camera.AabbHitTop(72, footY - 8, 16, 16, 1);
                               }
                               """;

        var sdkLibrary = SdkLibrarySource.ForTarget(GameBoyTarget.Intrinsics);

        Assert.Contains("[intrinsic(\"camera_aabb_tiles\")]", sdkLibrary, StringComparison.Ordinal);
        Assert.Contains("[intrinsic(\"camera_aabb_hit_top\")]", sdkLibrary, StringComparison.Ordinal);
        Assert.Equal(GameBoyRomCompiler.CompileSource(direct), GameBoyRomCompiler.CompileSource(library));
    }

    [Fact]
    public void Screen_collision_aabb_via_compile_time_operand_intrinsic_is_byte_identical_gb()
    {
        const string direct = """
                              void DefineWorld() {
                                  World.Column(0, 0, 4);
                                  World.Column(1, 0, 4);
                                  World.Column(2, 0, 4);
                                  World.Flags(0, 0, 1);
                                  World.Flags(1, 0, 1);
                                  World.Flags(2, 0, 1);
                                  World.Map(3, 11, 2);
                                  Camera.Init(3, 11, 2);
                              }

                              void Main() {
                                  DefineWorld();
                                  i16 screenX = 40;
                                  i16 screenY = 16;
                                  i16 hit = Camera.ScreenAabbTiles(screenX, screenY, 16, 16, 1);
                                  i16 hitTop = Camera.ScreenAabbHitTop(screenX, screenY, 16, 16, 1);
                              }
                              """;
        const string library = """
                               void DefineWorld() {
                                   World.Column(0, 0, 4);
                                   World.Column(1, 0, 4);
                                   World.Column(2, 0, 4);
                                   World.Flags(0, 0, 1);
                                   World.Flags(1, 0, 1);
                                   World.Flags(2, 0, 1);
                                   World.Map(3, 11, 2);
                                   Camera.Init(3, 11, 2);
                               }

                               void Main() {
                                   DefineWorld();
                                   i16 screenX = 40;
                                   i16 screenY = 16;
                                   i16 hit = Camera.ScreenAabbTiles(screenX, screenY, 16, 16, 1);
                                   i16 hitTop = Camera.ScreenAabbHitTop(screenX, screenY, 16, 16, 1);
                               }
                               """;

        var sdkLibrary = SdkLibrarySource.ForTarget(GameBoyTarget.Intrinsics);

        Assert.Contains("[intrinsic(\"camera_screen_aabb_tiles\")]", sdkLibrary, StringComparison.Ordinal);
        Assert.Contains("[intrinsic(\"camera_screen_aabb_hit_top\")]", sdkLibrary, StringComparison.Ordinal);
        Assert.Equal(GameBoyRomCompiler.CompileSource(direct), GameBoyRomCompiler.CompileSource(library));
    }

    [Fact]
    public void Collision_capability_checks_preserved()
    {
        const string source = """
                              void Main() {
                                  World.Column(0, 0, 4);
                                  World.Flags(0, 0, 1);
                                  World.Map(1, 11, 2);
                                  Camera.Init(1, 11, 2);
                                  i16 footY = 16;
                                  i16 hit = Camera.AabbTiles(150, footY, 16, 8, 1);
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource(source));

        Assert.Equal("camera AABB screen span must fit within target 'gb' visible width 160.", exception.Message);
    }

    [Fact]
    public void Hud_set_tile_resource_compiles_to_game_boy_window_tilemap()
    {
        const string source = """
                              void Main() {
                                  Video.Init();
                                  Hud.SetTile(window, 1, 0, 5);
                                  return;
                              }
                              """;

        Assert.Empty(GameBoyRomCompiler.CollectSdkOperations(source));

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.True(ContainsSequence(rom, [0x3E, 0x00, 0xE0, 0x4A, 0x3E, 0x07, 0xE0, 0x4B]), "Window HUD should position WY=0 and WX=7.");
        Assert.True(ContainsSequence(rom, [0x21, 0x00, 0x9C]), "Window HUD should copy a separate tilemap to $9C00.");
        Assert.True(ContainsSequence(rom, [0x3E, 0xF7, 0xE0, 0x40]), "Window HUD should enable the LCD window layer without disabling existing LCD features.");
    }

    [Fact]
    public void Rejects_split_scroll_hud_mode_through_capability_check()
    {
        const string source = """
                              void Main() {
                                  Video.Init();
                                  Hud.SetTile(split_scroll, 0, 0, 1);
                                  return;
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource(source));

        Assert.Equal(
            "Target 'gb' does not support SplitScroll HUD. Use Window HUD, SpriteHud, or disable HUD for this target.",
            exception.Message);
    }

    [Fact]
    public void Compiles_runtime_sprite_loop_to_a_game_boy_rom()
    {
        const string source = """
                              void Main() {
                                  Video.Init();
                                  Palette.Set(0, 0);
                                  Palette.Set(1, 1);
                                  Palette.Set(2, 2);
                                  Palette.Set(3, 3);
                                  i16 x = 8;
                                  while (true) {
                                      Video.WaitVBlank();
                                      sprite_set(0, x, 88, 6, 0);
                                      x = x + 1;
                                      if (x == 168) {
                                          x = 0;
                                      }
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.True(ContainsSequence(rom, [0x3E, 0x97, 0xE0, 0x40]), "ROM should enable LCD, background, 8x16 sprites, and sprite rendering.");
        Assert.True(ContainsSequence(rom, [0xEA, 0x01, 0xFE]), "ROM should write sprite X into OAM.");
        Assert.True(ContainsSequence(rom, [0xFE, 0xA8]), "ROM should compare x with the wrap coordinate.");
        Assert.True(ContainsSequence(rom, [0x18]), "ROM should contain a relative loop jump.");
    }

    [Fact]
    public void Inline_helper_wrapping_sprite_draw_and_camera_apply_is_byte_identical()
    {
        var baseDirectory = WriteSpriteJsonAsset(
            "player.sprite.json",
            SpriteJson(
                Rows(
                    8,
                    16,
                    "01230123",
                    "32103210")));

        const string direct = """
                              void Main() {
                                  Video.Init();
                                  World.Column(0, 1, 2);
                                  World.Map(1, 10, 2);
                                  Camera.Init(1, 10, 2);
                                  Sprite.Asset(player_run, "player.sprite.json");
                                  i16 sy = 80;
                                  Sprite.Draw(player_run, 72, sy, 0);
                                  Camera.Apply();
                              }
                              """;
        const string wrapped = """
                               inline void sd(i16 y) {
                                   Sprite.Draw(player_run, 72, y, 0);
                               }
                               inline void ca() {
                                   Camera.Apply();
                               }
                               void Main() {
                                   Video.Init();
                                   World.Column(0, 1, 2);
                                   World.Map(1, 10, 2);
                                   Camera.Init(1, 10, 2);
                                   Sprite.Asset(player_run, "player.sprite.json");
                                   i16 sy = 80;
                                   sd(sy);
                                   ca();
                               }
                               """;
        Assert.Equal(GameBoyRomCompiler.CompileSource(direct, baseDirectory), GameBoyRomCompiler.CompileSource(wrapped, baseDirectory));
    }

    [Fact]
    public void Compiles_png_sprite_sheet_to_a_game_boy_metasprite()
    {
        var baseDirectory = WriteSpritePng(
            "player-run.gb.png",
            16,
            16,
            Rows(
                16,
                16,
                "0231000000000000",
                "3210321000000000"),
            Rows(
                16,
                16,
                "3210321000000000",
                "0123012300000000"));

        const string source = """
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(player_run, "player-run.gb.png", 16, 16);
                                  i16 frame = 1;
                                  Sprite.Draw(player_run, 72, 80, frame);
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source, baseDirectory);

        Assert.True(ContainsSequence(rom, [0x30, 0x60, 0xAA, 0xCC]), "ROM should contain tile data decoded from the PNG sprite sheet with stable palette indexes.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0x47, 0xAF, 0x80, 0x80, 0x80, 0x80, 0xC6, 0x06, 0xEA, 0x02, 0xC6]), "sprite_draw should use the PNG logical frame index in shadow OAM.");
    }

    [Fact]
    public void Compiles_png_sprite_sheet_using_game_boy_platform_variant()
    {
        var baseDirectory = WriteSpritePng(
            "player-run.gb.png",
            8,
            16,
            Rows(8, 16, "11111111"));

        const string source = """
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(player_run, "player-run.png", 8, 16);
                                  Sprite.Draw(player_run, 72, 80, 0);
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source, baseDirectory);
        var program = CompileVideoProgram(source, baseDirectory);
        var asset = program.SpriteAssets["player_run"];

        Assert.Equal(8, asset.LogicalWidth);
        Assert.Equal(16, asset.LogicalHeight);
        Assert.Equal(1, asset.FrameCount);
    }

    [Fact]
    public void Compiles_png_sprite_sheet_with_non_hardware_height_by_padding()
    {
        var baseDirectory = WriteSpritePng(
            "mario-run.gb.png",
            16,
            27,
            Rows(16, 27, "0231000000000000"));

        const string source = """
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(mario_run, "mario-run.gb.png", 16, 27);
                                  Sprite.Draw(mario_run, 72, 77, 0);
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source, baseDirectory);

        Assert.True(ContainsSequence(rom, [0x3E, 0x4D, 0xC6, 0x20, 0xEA, 0x08, 0xC6]), "sprite_draw should emit a bottom row shadow OAM sprite after padding 27 px to 32 px.");
        Assert.True(ContainsSequence(rom, [0xC6, 0x0C, 0xEA, 0x0E, 0xC6]), "sprite_draw should allocate the fourth 8x16 tile pair for a padded 16x27 logical sprite in shadow OAM.");
    }

    [Fact]
    public void Compiles_grayscale_png_sprite_sheet_with_stable_light_to_dark_mapping()
    {
        var baseDirectory = WriteGrayscaleSpritePng(
            "mario-run.gb.png",
            16,
            27,
            Rows(16, 27, "3210000000000000"));

        const string source = """
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(mario_run, "mario-run.gb.png", 16, 27);
                                  Sprite.Draw(mario_run, 72, 77, 0);
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source, baseDirectory);

        Assert.True(ContainsSequence(rom, [0xA0, 0xC0]), "Grayscale PNG should map black to 3, gray to 2, and white to 1 even when black appears first.");
    }

    [Fact]
    public void Compiles_scroll_set_to_game_boy_scroll_register_writes()
    {
        const string source = """
                              void Main() {
                                  Video.Init();
                                  i16 camera = 0;
                                  while (true) {
                                      Video.WaitVBlank();
                                      scroll_set(camera, 0);
                                      camera = camera + 1;
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xE0, 0x43]), "ROM should load camera from WRAM and write it to SCX.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x00, 0xE0, 0x42]), "ROM should write the constant Y scroll to SCY.");
    }

    [Fact]
    public void Compiles_camera_span_tile_helpers_across_sprite_logical_width()
    {
        var baseDirectory = WriteSpritePng(
            "player-wide.gb.png",
            33,
            16,
            Rows(33, 16, new string('1', 33)));

        const string source = """
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(mario_player, "player-wide.gb.png", 33, 16);
                                  World.Column(0, 0, 0, 4, 5);
                                  World.Column(1, 0, 0, 4, 5);
                                  World.Column(2, 0, 0, 4, 5);
                                  World.Column(3, 0, 0, 4, 5);
                                  World.Column(4, 0, 0, 4, 5);
                                  World.Column(5, 0, 0, 4, 5);
                                  World.Column(6, 0, 0, 4, 5);
                                  World.Column(7, 0, 0, 4, 5);
                                  World.Column(8, 0, 0, 4, 5);
                                  World.Column(9, 0, 0, 4, 5);
                                  World.Column(10, 0, 0, 4, 5);
                                  World.Column(11, 0, 0, 4, 5);
                                  World.Column(12, 0, 0, 4, 5);
                                  World.Column(13, 0, 0, 3, 5);
                                  World.Column(14, 0, 0, 4, 5);
                                  World.Column(15, 0, 0, 4, 5);
                                  World.Map(16, 11, 4);
                                  Camera.Init(16, 11, 4);
                                  i16 logicalWidth = 0;
                                  i16 footTile = 0;
                                  i16 fail = 0;
                                  while (true) {
                                      Video.WaitVBlank();
                                      logicalWidth = Sprite.Width(mario_player);
                                      footTile = camera_span_tile_at(72, Sprite.Width(mario_player), 2);
                                      fail = camera_span_has_tile(72, Sprite.Width(mario_player), 2, 3);
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source, baseDirectory);

        Assert.True(ContainsSequence(rom, [0x3E, 0x21]), "sprite_width should compile to the sprite asset's logical width.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x09, 0x47, 0xFA, 0xE3, 0xC0, 0x80]), "Span collision should check the first tile column covered by screen X.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x0D, 0x47, 0xFA, 0xE3, 0xC0, 0x80]), "Span collision should check the last tile column covered by a 33 px logical sprite.");
        Assert.True(ContainsSequence(rom, [0xFE, 0x03, 0xCA]), "camera_span_has_tile should compare each covered source tile against the requested tile id.");
    }

    [Fact]
    public void Direct_legacy_camera_builtins_are_rejected()
    {
        const string source = """
                              void Main() {
                                  World.Column(0, 1, 2);
                                  World.Map(1, 10, 2);
                                  Camera.Init(1, 10, 2);
                                  camera_set_position(4, 0);
                                  camera_apply();
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource(source));
        Assert.Contains("camera_set_position", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Direct_legacy_sprite_draw_builtin_is_rejected()
    {
        var baseDirectory = WriteSpriteJsonAsset(
            "player.sprite.json",
            SpriteJson(Rows(8, 16, "01230123", "32103210")));

        const string source = """
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(player, "player.sprite.json");
                                  sprite_draw(player, 72, 80, 0);
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource(source, baseDirectory));
        Assert.Contains("sprite_draw", exception.Message, StringComparison.Ordinal);
    }
    [Fact]
    public void Compiles_camera_state_receiver_helper_like_flat_horizontal_input()
    {
        const string flatSource = """
                                  type Pixel = i16;

                                  void Main() {
                                      Video.Init();
                                      World.Column(0, 1, 2);
                                      World.Flags(0, 0, 1);
                                      World.Map(1, 10, 2);
                                      Camera.Init(1, 10, 2);
                                      Input.Poll();
                                      Pixel cameraX;
                                      Pixel moving;
                                      bool displayFlipX;
                                      moving = 0;
                                      if (Input.IsDown(Button.Right) != 0) {
                                          moving = 1;
                                          displayFlipX = false;
                                          cameraX += 1;
                                      }
                                      if (Input.IsDown(Button.Left) != 0) {
                                          moving = 1;
                                          displayFlipX = true;
                                          cameraX -= 1;
                                      }
                                      if (moving != 0) {
                                          Camera.SetPosition(cameraX, 0);
                                      }
                                  }
                                  """;

        const string receiverSource = """
                                      type Pixel = i16;

                                      struct CameraState {
                                          Pixel x;
                                          Pixel moving;
                                      }

                                      struct PlayerState {
                                          bool displayFlipX;
                                      }

                                      inline void HandleHorizontalInput(this CameraState view, PlayerState player) {
                                          view.moving = 0;
                                          if (Input.IsDown(Button.Right) != 0) {
                                              view.moving = 1;
                                              player.displayFlipX = false;
                                              view.x += 1;
                                          }
                                          if (Input.IsDown(Button.Left) != 0) {
                                              view.moving = 1;
                                              player.displayFlipX = true;
                                              view.x -= 1;
                                          }
                                          if (view.moving != 0) {
                                              Camera.SetPosition(view.x, 0);
                                          }
                                      }

                                      void Main() {
                                          Video.Init();
                                          World.Column(0, 1, 2);
                                          World.Flags(0, 0, 1);
                                          World.Map(1, 10, 2);
                                          Camera.Init(1, 10, 2);
                                          Input.Poll();
                                          CameraState view;
                                          PlayerState player;
                                          view.HandleHorizontalInput(player);
                                      }
                                      """;

        Assert.Equal(GameBoyRomCompiler.CompileSource(flatSource), GameBoyRomCompiler.CompileSource(receiverSource));
    }
    [Fact]
    public void World_map_generates_initial_visible_tilemap_from_map_columns()
    {
        const string source = """
                              void define_level_columns() {
                                  World.Column(0, 0, 0, 4, 5);
                                  World.Column(1, 0, 0, 4, 5);
                                  World.Column(2, 0, 5, 4, 5);
                                  World.Column(3, 0, 0, 4, 5);
                                  World.Column(4, 0, 0, 4, 5);
                                  World.Column(5, 0, 0, 4, 5);
                                  World.Column(6, 0, 0, 4, 5);
                                  World.Column(7, 0, 0, 3, 5);
                                  World.Column(8, 0, 0, 4, 5);
                                  World.Column(9, 0, 0, 4, 5);
                                  World.Column(10, 5, 0, 4, 5);
                                  World.Column(11, 0, 0, 4, 5);
                                  World.Column(12, 0, 0, 4, 5);
                                  World.Column(13, 0, 0, 0, 0);
                                  World.Column(14, 0, 0, 0, 0);
                                  World.Column(15, 0, 0, 0, 0);
                                  return;
                              }

                              void Main() {
                                  define_level_columns();
                                  World.Map(16, 11, 4);
                                  return;
                              }
                              """;

        var program = CompileVideoProgram(source);
        var worldMap = Assert.IsType<WorldMap2D>(program.WorldMap);
        var worldTileGrid = Assert.IsType<WorldTileGrid>(program.WorldTileGrid);

        Assert.Equal(16, worldMap.Width);
        Assert.Equal(4, worldMap.Height);
        Assert.Equal(3, worldTileGrid.TileIdAt(7, 2));
        Assert.Equal(WorldTileFlags.Empty, worldMap.FlagsAt(7, 2));
        Assert.Equal(4, program.TileMap[13 * 32]);
        Assert.Equal(5, program.TileMap[14 * 32]);
        Assert.Equal(5, program.TileMap[12 * 32 + 2]);
        Assert.Equal(5, program.TileMap[11 * 32 + 10]);
        Assert.Equal(3, program.TileMap[13 * 32 + 7]);
        Assert.Equal(0, program.TileMap[13 * 32 + 13]);
        Assert.Equal(0, program.TileMap[14 * 32 + 13]);
        Assert.Equal(5, program.TileMap[12 * 32 + 18]);
        Assert.Equal(3, program.TileMap[13 * 32 + 23]);
        Assert.Equal(0, program.TileMap[13 * 32 + 29]);
    }

    [Fact]
    public void World_map_generates_streaming_columns_from_world_columns()
    {
        const string source = """
                              void DefineWorld() {
                                  World.Column(0, 0, 0, 4, 5);
                                  World.Column(1, 0, 0, 4, 5);
                                  World.Column(2, 0, 5, 4, 5);
                                  World.Column(3, 0, 0, 4, 5);
                                  World.Column(4, 0, 0, 4, 5);
                                  World.Column(5, 0, 0, 4, 5);
                                  World.Column(6, 0, 0, 4, 5);
                                  World.Column(7, 0, 0, 3, 5);
                                  World.Column(8, 0, 0, 4, 5);
                                  World.Column(9, 0, 0, 4, 5);
                                  World.Column(10, 5, 0, 4, 5);
                                  World.Column(11, 0, 0, 4, 5);
                                  World.Column(12, 0, 0, 4, 5);
                                  World.Column(13, 0, 0, 0, 0);
                                  World.Column(14, 0, 0, 0, 0);
                                  World.Column(15, 0, 0, 0, 0);
                                  return;
                              }

                              void Main() {
                                  DefineWorld();
                                  World.Map(16, 11, 4);
                                  Camera.Init(16, 11, 4);
                                  return;
                              }
                              """;

        var program = CompileVideoProgram(source);
        var worldMap = Assert.IsType<WorldMap2D>(program.WorldMap);
        var worldTileGrid = Assert.IsType<WorldTileGrid>(program.WorldTileGrid);

        Assert.Equal(4, program.MapColumnHeight);
        Assert.Equal(3, worldTileGrid.TileIdAt(7, 2));
        Assert.Equal(3, program.MapColumns[7][2]);
        Assert.Equal(0, program.MapColumns[13][2]);
        Assert.Equal(5, program.TileMap[14 * 32]);
        Assert.Equal(3, program.TileMap[13 * 32 + 23]);
        _ = GameBoyRomCompiler.CompileSource(source);
    }

    [Fact]
    public void World_load_imports_tiled_json_map_layers()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RetroSharp.GameBoy.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        WriteTiledTilesheetPng(directory, "runner.png", 8, 8, 1, 2, 3, 1, 2, 3);
        File.WriteAllText(
            Path.Combine(directory, "runner.tsj"),
            """
            {
              "type": "tileset",
              "version": "1.10",
              "tiledversion": "1.12.2",
              "name": "runner",
              "tilewidth": 8,
              "tileheight": 8,
              "spacing": 0,
              "margin": 0,
              "tilecount": 6,
              "columns": 6,
              "image": "runner.png",
              "imagewidth": 48,
              "imageheight": 8
            }
            """);
        File.WriteAllText(
            Path.Combine(directory, "level.tmj"),
            """
            {
              "type": "map",
              "version": "1.10",
              "tiledversion": "1.10.2",
              "orientation": "orthogonal",
              "renderorder": "right-down",
              "width": 3,
              "height": 4,
              "tilewidth": 8,
              "tileheight": 8,
              "infinite": false,
              "properties": [
                { "name": "retrosharpStreamY", "type": "int", "value": 5 },
                { "name": "retrosharpWorldY", "type": "int", "value": 2 },
                { "name": "retrosharpWorldHeight", "type": "int", "value": 2 }
              ],
              "layers": [
                {
                  "id": 1,
                  "name": "background",
                  "type": "tilelayer",
                  "width": 3,
                  "height": 4,
                  "visible": true,
                  "opacity": 1,
                  "x": 0,
                  "y": 0,
                  "data": [2, 0, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0]
                },
                {
                  "id": 2,
                  "name": "world",
                  "type": "tilelayer",
                  "width": 3,
                  "height": 4,
                  "visible": true,
                  "opacity": 1,
                  "x": 0,
                  "y": 0,
                  "data": [0, 0, 0, 0, 0, 0, 5, 0, 6, 4, 5, 0]
                },
                {
                  "id": 3,
                  "name": "collision",
                  "type": "tilelayer",
                  "width": 3,
                  "height": 4,
                  "visible": true,
                  "opacity": 1,
                  "x": 0,
                  "y": 0,
                  "data": [0, 0, 0, 0, 0, 0, 1, 0, 4, 2, 1, 0]
                }
              ],
              "tilesets": [
                { "firstgid": 1, "source": "runner.tsj" }
              ]
            }
            """);

        const string source = """
                              void Main() {
                                  World.Load("level.tmj");
                                  Camera.Init(3, 5, 2);
                                  return;
                              }
                              """;

        var program = CompileVideoProgram(source, directory);
        var worldMap = Assert.IsType<WorldMap2D>(program.WorldMap);
        var worldTileGrid = Assert.IsType<WorldTileGrid>(program.WorldTileGrid);

        Assert.Equal(3, worldMap.Width);
        Assert.Equal(2, worldMap.Height);
        Assert.Equal(6, worldTileGrid.TileIdAt(0, 0));
        Assert.Equal(7, worldTileGrid.TileIdAt(2, 0));
        Assert.Equal(8, worldTileGrid.TileIdAt(0, 1));
        Assert.Equal(WorldTileFlags.Solid, worldMap.FlagsAt(0, 0));
        Assert.Equal(WorldTileFlags.Platform, worldMap.FlagsAt(2, 0));
        Assert.Equal(WorldTileFlags.Hazard, worldMap.FlagsAt(0, 1));
        Assert.Equal(6, program.TileMap[3 * 32]);
        Assert.Equal(7, program.TileMap[3 * 32 + 2]);
        Assert.Equal(6, program.TileMap[3 * 32 + 3]);
        Assert.Equal(6, program.TileMap[5 * 32]);
        Assert.Equal(7, program.TileMap[5 * 32 + 2]);
        Assert.Equal(6, program.TileMap[5 * 32 + 3]);
        Assert.Equal(8, program.TileMap[6 * 32]);
        _ = GameBoyRomCompiler.CompileSource(source, directory);
    }

    [Fact]
    public void World_load_composes_tiled_background_under_empty_world_tiles_with_world_y_alignment()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RetroSharp.GameBoy.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        WriteTiledTilesheetPng(directory, "runner.png", 8, 8, 1, 2, 3);
        File.WriteAllText(
            Path.Combine(directory, "runner.tsj"),
            """
            {
              "type": "tileset",
              "version": "1.10",
              "tiledversion": "1.12.2",
              "name": "runner",
              "tilewidth": 8,
              "tileheight": 8,
              "spacing": 0,
              "margin": 0,
              "tilecount": 3,
              "columns": 3,
              "image": "runner.png",
              "imagewidth": 24,
              "imageheight": 8
            }
            """);
        File.WriteAllText(
            Path.Combine(directory, "level.tmj"),
            """
            {
              "type": "map",
              "version": "1.10",
              "tiledversion": "1.10.2",
              "orientation": "orthogonal",
              "renderorder": "right-down",
              "width": 3,
              "height": 6,
              "tilewidth": 8,
              "tileheight": 8,
              "infinite": false,
              "properties": [
                { "name": "retrosharpStreamY", "type": "int", "value": 2 },
                { "name": "retrosharpWorldY", "type": "int", "value": 3 },
                { "name": "retrosharpWorldHeight", "type": "int", "value": 2 }
              ],
              "layers": [
                {
                  "id": 1,
                  "name": "background",
                  "type": "tilelayer",
                  "width": 3,
                  "height": 6,
                  "visible": true,
                  "opacity": 1,
                  "x": 0,
                  "y": 0,
                  "data": [0, 0, 0, 0, 0, 0, 1, 2, 3, 3, 3, 3, 0, 0, 0, 0, 0, 0]
                },
                {
                  "id": 2,
                  "name": "world",
                  "type": "tilelayer",
                  "width": 3,
                  "height": 6,
                  "visible": true,
                  "opacity": 1,
                  "x": 0,
                  "y": 0,
                  "data": [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 0, 0, 0, 0, 0]
                },
                {
                  "id": 3,
                  "name": "collision",
                  "type": "tilelayer",
                  "width": 3,
                  "height": 6,
                  "visible": true,
                  "opacity": 1,
                  "x": 0,
                  "y": 0,
                  "data": [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0]
                }
              ],
              "tilesets": [
                { "firstgid": 1, "source": "runner.tsj" }
              ]
            }
            """);

        const string source = """
                              void Main() {
                                  World.Load("level.tmj");
                                  Camera.Init(3, 2, 2);
                                  return;
                              }
                              """;

        var program = CompileVideoProgram(source, directory);
        var worldMap = Assert.IsType<WorldMap2D>(program.WorldMap);
        var worldTileGrid = Assert.IsType<WorldTileGrid>(program.WorldTileGrid);

        Assert.Equal(8, worldTileGrid.TileIdAt(0, 0));
        Assert.Equal(8, worldTileGrid.TileIdAt(1, 0));
        Assert.Equal(8, worldTileGrid.TileIdAt(2, 0));
        Assert.Equal(WorldTileFlags.Empty, worldMap.FlagsAt(0, 0));
        Assert.Equal(7, worldTileGrid.TileIdAt(0, 1));
        Assert.Equal(WorldTileFlags.Solid, worldMap.FlagsAt(0, 1));
        Assert.Equal(0, worldTileGrid.TileIdAt(1, 1));
        Assert.Equal(WorldTileFlags.Empty, worldMap.FlagsAt(1, 1));
        Assert.Equal(6, program.TileMap[1 * 32]);
        Assert.Equal(7, program.TileMap[1 * 32 + 1]);
        Assert.Equal(8, program.TileMap[1 * 32 + 2]);
        Assert.Equal(8, program.TileMap[2 * 32]);
        Assert.Equal(8, program.TileMap[2 * 32 + 1]);
        Assert.Equal(8, program.TileMap[2 * 32 + 2]);
        Assert.Equal(7, program.TileMap[3 * 32]);
        Assert.Equal(0, program.TileMap[3 * 32 + 1]);
        Assert.Equal(7, program.MapColumns[0][1]);
        Assert.Equal(0, program.MapColumns[1][1]);

        Assert.Equal(2, program.BackgroundStreamHeight);
        for (var column = 0; column < 3; column++)
        {
            for (var row = 0; row < program.BackgroundStreamHeight; row++)
            {
                Assert.Equal(program.TileMap[row * 32 + column], program.BackgroundColumns[column][row]);
            }
        }
    }


    [Fact]
    public void World_load_imports_tiled_external_tilesets_images_and_object_collisions()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RetroSharp.GameBoy.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        WriteTiledTilesheetPng(directory, "tiles.png", 16, 16, 0, 2, 3);
        File.WriteAllText(
            Path.Combine(directory, "level.tsx"),
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <tileset version="1.10" tiledversion="1.12.2" name="Level" tilewidth="16" tileheight="16" tilecount="3" columns="3">
             <image source="tiles.png" width="48" height="16"/>
             <tile id="1">
              <objectgroup draworder="index" id="2">
               <object id="1" x="0" y="0" width="16" height="16"/>
              </objectgroup>
             </tile>
            </tileset>
            """);
        File.WriteAllText(
            Path.Combine(directory, "level.tmj"),
            """
            {
              "type": "map",
              "version": "1.10",
              "tiledversion": "1.12.2",
              "orientation": "orthogonal",
              "renderorder": "right-down",
              "width": 3,
              "height": 3,
              "tilewidth": 16,
              "tileheight": 16,
              "infinite": false,
              "properties": [
                { "name": "retrosharpStreamY", "type": "int", "value": 4 },
                { "name": "retrosharpWorldY", "type": "int", "value": 1 },
                { "name": "retrosharpWorldHeight", "type": "int", "value": 2 }
              ],
              "layers": [
                {
                  "id": 1,
                  "name": "background",
                  "type": "tilelayer",
                  "width": 3,
                  "height": 3,
                  "visible": true,
                  "opacity": 1,
                  "x": 0,
                  "y": 0,
                  "data": [1, 2, 3, 0, 0, 0, 0, 0, 0]
                },
                {
                  "id": 2,
                  "name": "world",
                  "type": "tilelayer",
                  "width": 3,
                  "height": 3,
                  "visible": true,
                  "opacity": 1,
                  "x": 0,
                  "y": 0,
                  "data": [0, 0, 0, 2, 3, 0, 0, 0, 0]
                }
              ],
              "tilesets": [
                { "firstgid": 1, "source": "level.tsx" }
              ]
            }
            """);

        const string source = """
                              void Main() {
                                  World.Load("level.tmj");
                                  Camera.Init(6, 4, 4);
                                  return;
                              }
                              """;

        var program = CompileVideoProgram(source, directory);
        var worldMap = Assert.IsType<WorldMap2D>(program.WorldMap);
        var worldTileGrid = Assert.IsType<WorldTileGrid>(program.WorldTileGrid);

        Assert.Equal(6, worldMap.Width);
        Assert.Equal(4, worldMap.Height);
        Assert.Equal(6, worldTileGrid.TileIdAt(0, 0));
        Assert.Equal(6, worldTileGrid.TileIdAt(1, 0));
        Assert.Equal(7, worldTileGrid.TileIdAt(2, 0));
        Assert.Equal(7, worldTileGrid.TileIdAt(3, 0));
        Assert.Equal(6, worldTileGrid.TileIdAt(0, 1));
        Assert.Equal(6, worldTileGrid.TileIdAt(1, 1));
        Assert.Equal(WorldTileFlags.Solid, worldMap.FlagsAt(0, 0));
        Assert.Equal(WorldTileFlags.Solid, worldMap.FlagsAt(1, 0));
        Assert.Equal(WorldTileFlags.Solid, worldMap.FlagsAt(0, 1));
        Assert.Equal(WorldTileFlags.Solid, worldMap.FlagsAt(1, 1));
        Assert.Equal(WorldTileFlags.Empty, worldMap.FlagsAt(2, 0));
        Assert.Equal(0, program.TileMap[0]);
        Assert.Equal(0, program.TileMap[1]);
        Assert.Equal(6, program.TileMap[2 * 32 + 2]);
        Assert.Equal(6, program.TileMap[2 * 32 + 3]);
        Assert.Equal(7, program.TileMap[2 * 32 + 4]);
        Assert.Equal(7, program.TileMap[2 * 32 + 5]);
        Assert.Equal(6, program.TileMap[4 * 32]);
        Assert.Equal(6, program.TileMap[4 * 32 + 1]);
        Assert.Equal(7, program.TileMap[4 * 32 + 2]);
        Assert.Equal(7, program.TileMap[4 * 32 + 3]);
        Assert.Equal(6, program.TileMap[5 * 32]);
        Assert.Equal(6, program.TileMap[5 * 32 + 1]);
        _ = GameBoyRomCompiler.CompileSource(source, directory);
    }

    [Fact]
    public void World_pack_matches_raw_game_boy_import_for_a_shifted_composed_slice()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RetroSharp.GameBoy.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        WriteTiledTilesheetPng(directory, "tiles.png", 8, 8, 1, 3);
        File.WriteAllText(
            Path.Combine(directory, "level.tsx"),
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <tileset version="1.10" tiledversion="1.12.2" name="Level" tilewidth="8" tileheight="8" tilecount="2" columns="2">
             <image source="tiles.png" width="16" height="8"/>
            </tileset>
            """);
        File.WriteAllText(
            Path.Combine(directory, "level.tmj"),
            """
            {
              "type": "map",
              "orientation": "orthogonal",
              "infinite": false,
              "width": 3,
              "height": 3,
              "tilewidth": 8,
              "tileheight": 8,
              "properties": [
                { "name": "retrosharpStreamY", "type": "int", "value": 0 },
                { "name": "retrosharpWorldY", "type": "int", "value": 1 },
                { "name": "retrosharpWorldHeight", "type": "int", "value": 2 }
              ],
              "tilesets": [
                { "firstgid": 1, "source": "level.tsx" }
              ],
              "layers": [
                { "type": "tilelayer", "name": "background", "width": 3, "height": 3, "data": [2, 2, 2, 1, 1, 1, 1, 1, 1] },
                { "type": "tilelayer", "name": "world", "width": 3, "height": 3, "data": [0, 0, 0, 2, 0, 2, 0, 2, 0] },
                { "type": "tilelayer", "name": "collision", "width": 3, "height": 3, "data": [0, 0, 0, 1, 0, 2, 4, 0, 1] }
              ]
            }
            """);
        var path = Path.Combine(directory, "level.tmj");
        var firstGeneratedTile = GameBoyVideoProgram.FirstGeneratedBackgroundTile;
        var raw = GameBoyTiledMapImporter.Load(path, firstGeneratedTile);

        var compiled = GameBoyTiledMapImporter.CompileWorldPack(path, firstGeneratedTile);
        var decoded = WorldPackSerializer.Deserialize(compiled.SerializedBytes);
        var decodedTiles = decoded.ToWorldTileGrid(cell => cell.Span[0]);

        Assert.Equal(3, decoded.Descriptor.HardwareWidth);
        Assert.Equal(2, decoded.Descriptor.HardwareHeight);
        Assert.NotEqual(raw.WorldTileIds[0], raw.WorldTileIds[1]);
        Assert.Equal(raw.GeneratedTileData, compiled.GeneratedTileData);
        for (var index = 0; index < raw.WorldTileIds.Length; index++)
        {
            var x = index % raw.Width;
            var y = index / raw.Width;
            Assert.Equal(raw.WorldTileIds[index], decodedTiles.TileIdAt(x, y));
            Assert.Equal(raw.WorldFlags[index], decoded.CollisionAt(x, y));
        }
    }

    [Fact]
    public void World_load_uses_game_boy_tileset_png_variant_when_present()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RetroSharp.GameBoy.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        WriteTiledTilesheetPng(directory, "tiles.png", 8, 8, 3);
        WriteTiledTilesheetPng(directory, "tiles.gb.png", 8, 8, 0);
        File.WriteAllText(
            Path.Combine(directory, "level.tsx"),
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <tileset version="1.10" tiledversion="1.12.2" name="Level" tilewidth="8" tileheight="8" tilecount="1" columns="1">
             <image source="tiles.png" width="8" height="8"/>
            </tileset>
            """);
        File.WriteAllText(
            Path.Combine(directory, "level.tmj"),
            """
            {
              "type": "map",
              "version": "1.10",
              "tiledversion": "1.12.2",
              "orientation": "orthogonal",
              "renderorder": "right-down",
              "width": 1,
              "height": 1,
              "tilewidth": 8,
              "tileheight": 8,
              "infinite": false,
              "properties": [
                { "name": "retrosharpStreamY", "type": "int", "value": 0 },
                { "name": "retrosharpWorldY", "type": "int", "value": 0 },
                { "name": "retrosharpWorldHeight", "type": "int", "value": 1 }
              ],
              "layers": [
                {
                  "id": 1,
                  "name": "world",
                  "type": "tilelayer",
                  "width": 1,
                  "height": 1,
                  "visible": true,
                  "opacity": 1,
                  "x": 0,
                  "y": 0,
                  "data": [1]
                }
              ],
              "tilesets": [
                { "firstgid": 1, "source": "level.tsx" }
              ]
            }
            """);

        const string source = """
                              void Main() {
                                  World.Load("level.tmj");
                                  Camera.Init(1, 0, 1);
                                  return;
                              }
                              """;

        var program = CompileVideoProgram(source, directory);
        var worldMap = Assert.IsType<WorldMap2D>(program.WorldMap);
        var worldTileGrid = Assert.IsType<WorldTileGrid>(program.WorldTileGrid);
        var packed = GameBoyTiledMapImporter.CompileWorldPack(
            Path.Combine(directory, "level.tmj"),
            GameBoyVideoProgram.FirstGeneratedBackgroundTile);
        var packedTileGrid = WorldPackSerializer.Deserialize(packed.SerializedBytes).ToWorldTileGrid(cell => cell.Span[0]);

        Assert.Equal(0, worldTileGrid.TileIdAt(0, 0));
        Assert.Equal(0, packedTileGrid.TileIdAt(0, 0));
        Assert.Empty(packed.GeneratedTileData);
        Assert.Equal(0, program.TileMap[0]);
    }

    [Fact]
    public void World_load_reserves_generated_background_tiles_before_sprite_assets()
    {
        var directory = WriteSpriteJsonAsset(
            "player.sprite.json",
            SpriteJson(Rows(8, 16, "01230123")));
        WriteTiledTilesheetPng(directory, "tiles.png", 16, 16, 3);
        File.WriteAllText(
            Path.Combine(directory, "level.tsx"),
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <tileset version="1.10" tiledversion="1.12.2" name="Level" tilewidth="16" tileheight="16" tilecount="1" columns="1">
             <image source="tiles.png" width="16" height="16"/>
            </tileset>
            """);
        File.WriteAllText(
            Path.Combine(directory, "level.tmj"),
            """
            {
              "type": "map",
              "version": "1.10",
              "tiledversion": "1.12.2",
              "orientation": "orthogonal",
              "renderorder": "right-down",
              "width": 1,
              "height": 1,
              "tilewidth": 16,
              "tileheight": 16,
              "infinite": false,
              "properties": [
                { "name": "retrosharpStreamY", "type": "int", "value": 4 },
                { "name": "retrosharpWorldY", "type": "int", "value": 0 },
                { "name": "retrosharpWorldHeight", "type": "int", "value": 1 }
              ],
              "layers": [
                {
                  "id": 1,
                  "name": "world",
                  "type": "tilelayer",
                  "width": 1,
                  "height": 1,
                  "visible": true,
                  "opacity": 1,
                  "x": 0,
                  "y": 0,
                  "data": [1]
                }
              ],
              "tilesets": [
                { "firstgid": 1, "source": "level.tsx" }
              ]
            }
            """);

        const string source = """
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(player, "player.sprite.json");
                                  World.Load("level.tmj");
                                  Sprite.Draw(player, 8, 8, 0);
                                  return;
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source, directory);

        Assert.True(ContainsSequence(rom, [0xC6, 0x08, 0xEA, 0x02, 0xC6]), "Generated background tiles should leave the first 8x16 shadow OAM sprite tile on an even tile after them.");
    }

    [Fact]
    public void World_map_generates_collision_flags_and_lowers_flag_queries()
    {
        const string source = """
                              void DefineWorld() {
                                  World.Column(0, 0, 4);
                                  World.Column(1, 3, 5);
                                  World.Flags(0, 0, 1);
                                  World.Flags(1, 2, 1);
                                  return;
                              }

                              void Main() {
                                  DefineWorld();
                                  World.Map(2, 11, 2);
                                  Camera.Init(2, 11, 2);
                                  i16 column = 1;
                                  i16 hazard = 0;
                                  i16 solid = 0;
                                  if (map_flags_at(column, 0) != 0) {
                                      hazard = 1;
                                  }
                                  if (camera_span_has_flags(0, 8, 1, 1) != 0) {
                                      solid = 1;
                                  }
                                  if (camera_span_has_flags(8, 8, 0, 2) != 0) {
                                      hazard = 2;
                                  }
                                  return;
                              }
                              """;

        var program = CompileVideoProgram(source);
        var worldMap = Assert.IsType<WorldMap2D>(program.WorldMap);

        Assert.Equal(WorldTileFlags.Solid, worldMap.FlagsAt(0, 1));
        Assert.Equal(WorldTileFlags.Hazard, worldMap.FlagsAt(1, 0));
        Assert.Equal(WorldTileFlags.Solid, worldMap.FlagsAt(1, 1));

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.True(ContainsSequence(rom, [0x00, 0x02]), "ROM should contain world flag row 0 data.");
        Assert.True(ContainsSequence(rom, [0x01, 0x01]), "ROM should contain world flag row 1 data.");
        Assert.True(ContainsSequence(rom, [0xE6, 0x01, 0xFE, 0x00, 0xC2]), "Solid flag queries should mask bit 0 independently.");
        Assert.True(ContainsSequence(rom, [0xE6, 0x02, 0xFE, 0x00, 0xC2]), "Hazard flag queries should mask bit 1 independently.");
    }
    [Fact]
    public void Compiles_long_if_body_with_map_streaming()
    {
        const string source = """
                              void Main() {
                                  Video.Init();
                                  World.Column(0, 0, 0, 3, 5);
                                  World.Column(1, 0, 5, 3, 5);
                                  World.Map(2, 11, 4);
                                  i16 fine = 0;
                                  i16 streamColumn = 20;
                                  i16 mapColumn = 0;
                                  while (true) {
                                      Video.WaitVBlank();
                                      fine = fine + 1;
                                      if (fine == 8) {
                                          fine = 0;
                                          map_stream_column(streamColumn, mapColumn, 11, 4);
                                          streamColumn = streamColumn + 1;
                                          if (streamColumn == 32) {
                                              streamColumn = 0;
                                          }
                                          mapColumn = mapColumn + 1;
                                          if (mapColumn == 2) {
                                              mapColumn = 0;
                                          }
                                      }
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.True(ContainsSequence(rom, [0xC2]), "ROM should use an absolute conditional JP for long if bodies.");
    }

}
