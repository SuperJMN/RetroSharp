namespace RetroSharp.GameBoy.Tests;

using System.Buffers.Binary;
using System.IO.Compression;
using RetroSharp.Core.Sdk;
using RetroSharp.GameBoy;
using RetroSharp.Parser;
using Xunit;

public class GameBoyRomCompilerTests
{
    [Fact]
    public void Compiles_video_api_calls_to_a_game_boy_rom()
    {
        const string source = """
                              void main() {
                                  video_init();
                                  palette_set(0, 0);
                                  palette_set(1, 1);
                                  palette_set(2, 2);
                                  palette_set(3, 3);
                                  tilemap_set(8, 7, 1);
                                  tilemap_set(9, 7, 2);
                                  tilemap_set(10, 7, 1);
                                  tilemap_set(9, 8, 3);
                                  return;
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.Equal(0x00, rom[0x0100]);
        Assert.Equal(0xC3, rom[0x0101]);
        Assert.Equal(0x50, rom[0x0102]);
        Assert.Equal(0x01, rom[0x0103]);
        Assert.Equal("RETROSHARPGB", System.Text.Encoding.ASCII.GetString(rom, 0x0134, 12));
        Assert.Equal(0x00, rom[0x0147]);
        Assert.Equal(0x00, rom[0x0148]);
        Assert.Equal(0x00, rom[0x0149]);
        Assert.Equal(HeaderChecksum(rom), rom[0x014D]);
        Assert.Contains(rom.Skip(0x0150), b => b != 0);
    }

    [Fact]
    public void Compiles_parameterless_user_functions_like_inline_video_blocks()
    {
        const string directSource = """
                                    void main() {
                                        video_init();
                                        palette_set(0, 0);
                                        palette_set(1, 1);
                                        tilemap_set(8, 7, 1);
                                        tilemap_set(9, 7, 2);
                                        video_present();
                                        return;
                                    }
                                    """;

        const string functionSource = """
                                      void setup_palette() {
                                          palette_set(0, 0);
                                          palette_set(1, 1);
                                          return;
                                      }

                                      void draw_mark() {
                                          tilemap_set(8, 7, 1);
                                          tilemap_set(9, 7, 2);
                                          return;
                                      }

                                      void main() {
                                          video_init();
                                          setup_palette();
                                          draw_mark();
                                          video_present();
                                          return;
                                      }
                                      """;

        Assert.Equal(GameBoyRomCompiler.CompileSource(directSource), GameBoyRomCompiler.CompileSource(functionSource));
    }

    [Fact]
    public void GameBoy_drawing_sample_compiles_with_helper_functions()
    {
        var sourcePath = RepositoryFile("samples/gameboy-drawing/drawing.rs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("void draw_face()", source);

        var rom = GameBoyRomCompiler.CompileSource(source, Path.GetDirectoryName(sourcePath));

        Assert.Equal(32768, rom.Length);
    }

    [Fact]
    public void Compiles_runtime_sprite_loop_to_a_game_boy_rom()
    {
        const string source = """
                              void main() {
                                  video_init();
                                  palette_set(0, 0);
                                  palette_set(1, 1);
                                  palette_set(2, 2);
                                  palette_set(3, 3);
                                  i16 x = 8;
                                  while (true) {
                                      video_wait_vblank();
                                      sprite_set(0, x, 88, 6, 0);
                                      x = x + 1;
                                      if (x == 168) {
                                          x = 0;
                                      }
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x3E, 0x97, 0xE0, 0x40]), "ROM should enable LCD, background, 8x16 sprites, and sprite rendering.");
        Assert.True(ContainsSequence(rom, [0xEA, 0x01, 0xFE]), "ROM should write sprite X into OAM.");
        Assert.True(ContainsSequence(rom, [0xFE, 0xA8]), "ROM should compare x with the wrap coordinate.");
        Assert.True(ContainsSequence(rom, [0x18]), "ROM should contain a relative loop jump.");
    }

    [Fact]
    public void Compiles_sprite_asset_draw_to_a_game_boy_metasprite()
    {
        var baseDirectory = WriteSpriteAsset(
            "player.sprite.json",
            SpriteJson(
                Rows(
                    8,
                    16,
                    "01230123",
                    "32103210")));

        const string source = """
                              void main() {
                                  video_init();
                                  sprite_asset(player_run, "player.sprite.json");
                                  sprite_draw(player_run, 72, 80, 0);
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
    public void Sprite_draw_composes_16x32_assets_from_four_hardware_sprites()
    {
        var baseDirectory = WriteSpriteAsset(
            "player.sprite.json",
            SpriteJson(Rows(16, 32)));

        const string source = """
                              void main() {
                                  video_init();
                                  sprite_asset(big_player, "player.sprite.json");
                                  sprite_draw(big_player, 72, 64, 0);
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
    public void Sprite_draw_accepts_attributes_and_flips_logical_metasprites_horizontally()
    {
        var baseDirectory = WriteSpriteAsset(
            "player.sprite.json",
            SpriteJson(Rows(16, 32)));

        const string source = """
                              void main() {
                                  video_init();
                                  sprite_asset(big_player, "player.sprite.json");
                                  i16 flags = 32;
                                  sprite_draw(big_player, 72, 64, 0, flags);
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source, baseDirectory);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xEA, 0x03, 0xFE]), "sprite_draw should write the dynamic flags byte into OAM attributes.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xE6, 0x20, 0xFE, 0x00, 0xCA]), "sprite_draw should test the Game Boy X-flip bit before placing metasprite pieces.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x48, 0xC6, 0x10, 0xEA, 0x01, 0xFE]), "X-flipped first logical piece should move to the mirrored top-right hardware X coordinate.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x48, 0xC6, 0x08, 0xEA, 0x05, 0xFE]), "X-flipped second logical piece should move to the mirrored top-left hardware X coordinate.");
        Assert.True(ContainsSequence(rom, [0xC6, 0x06, 0xEA, 0x02, 0xFE]), "X-flipped first logical piece should keep its own tile and rely on the OAM flip bit.");
        Assert.True(ContainsSequence(rom, [0xC6, 0x08, 0xEA, 0x06, 0xFE]), "X-flipped second logical piece should keep its own tile and rely on the OAM flip bit.");
    }

    [Fact]
    public void Sprite_draw_flips_against_logical_width_before_padding()
    {
        var baseDirectory = WriteSpriteAsset(
            "player.sprite.json",
            SpriteJson(Rows(18, 16)));

        const string source = """
                              void main() {
                                  video_init();
                                  sprite_asset(player, "player.sprite.json");
                                  i16 flags = 32;
                                  sprite_draw(player, 72, 64, 0, flags);
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source, baseDirectory);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x3E, 0x48, 0xC6, 0x12, 0xEA, 0x01, 0xFE]), "The first 8 px piece should move to logical X + 10 when an 18 px sprite is flipped.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x48, 0xC6, 0x0A, 0xEA, 0x05, 0xFE]), "The middle 8 px piece should move to logical X + 2 when an 18 px sprite is flipped.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x48, 0xC6, 0x02, 0xEA, 0x09, 0xFE]), "The padded edge piece should straddle the logical origin instead of adding padded left spacing.");
    }

    [Fact]
    public void Sprite_draw_treats_frame_as_a_logical_frame_index()
    {
        var baseDirectory = WriteSpriteAsset(
            "player.sprite.json",
            SpriteJson(
                Rows(8, 16, "01230123"),
                Rows(8, 16, "32103210")));

        const string source = """
                              void main() {
                                  video_init();
                                  sprite_asset(player_run, "player.sprite.json");
                                  i16 frame = 1;
                                  sprite_draw(player_run, 72, 80, frame);
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source, baseDirectory);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0x47, 0xAF, 0x80, 0x80, 0xC6, 0x06, 0xEA, 0x02, 0xFE]), "sprite_draw should multiply the logical frame by the per-frame tile count.");
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
                              void main() {
                                  video_init();
                                  sprite_asset(player_run, "player-run.gb.png", 16, 16);
                                  i16 frame = 1;
                                  sprite_draw(player_run, 72, 80, frame);
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source, baseDirectory);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x30, 0x60, 0xAA, 0xCC]), "ROM should contain tile data decoded from the PNG sprite sheet with stable palette indexes.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0x47, 0xAF, 0x80, 0x80, 0x80, 0x80, 0xC6, 0x06, 0xEA, 0x02, 0xFE]), "sprite_draw should use the PNG logical frame index.");
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
                              void main() {
                                  video_init();
                                  sprite_asset(mario_run, "mario-run.gb.png", 16, 27);
                                  sprite_draw(mario_run, 72, 77, 0);
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source, baseDirectory);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x3E, 0x4D, 0xC6, 0x20, 0xEA, 0x08, 0xFE]), "sprite_draw should emit a bottom row hardware sprite after padding 27 px to 32 px.");
        Assert.True(ContainsSequence(rom, [0xC6, 0x0C, 0xEA, 0x0E, 0xFE]), "sprite_draw should allocate the fourth 8x16 tile pair for a padded 16x27 logical sprite.");
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
                              void main() {
                                  video_init();
                                  sprite_asset(mario_run, "mario-run.gb.png", 16, 27);
                                  sprite_draw(mario_run, 72, 77, 0);
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source, baseDirectory);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0xA0, 0xC0]), "Grayscale PNG should map black to 3, gray to 2, and white to 1 even when black appears first.");
    }

    [Fact]
    public void Compiles_scroll_set_to_game_boy_scroll_register_writes()
    {
        const string source = """
                              void main() {
                                  video_init();
                                  i16 camera = 0;
                                  while (true) {
                                      video_wait_vblank();
                                      scroll_set(camera, 0);
                                      camera = camera + 1;
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xE0, 0x43]), "ROM should load camera from WRAM and write it to SCX.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x00, 0xE0, 0x42]), "ROM should write the constant Y scroll to SCY.");
    }

    [Fact]
    public void Compiles_camera_runtime_to_world_scroll_state_and_streaming()
    {
        const string source = """
                              void main() {
                                  video_init();
                                  map_column(0, 0, 0, 4, 5);
                                  map_column(1, 0, 0, 4, 5);
                                  map_column(2, 0, 0, 4, 5);
                                  map_column(3, 0, 0, 4, 5);
                                  map_column(4, 0, 0, 4, 5);
                                  map_column(5, 0, 0, 4, 5);
                                  map_column(6, 0, 0, 4, 5);
                                  map_column(7, 0, 0, 4, 5);
                                  map_column(8, 0, 0, 4, 5);
                                  map_column(9, 0, 0, 4, 5);
                                  map_column(10, 0, 0, 4, 5);
                                  map_column(11, 0, 0, 4, 5);
                                  map_column(12, 0, 0, 4, 5);
                                  map_column(13, 0, 0, 4, 5);
                                  map_column(14, 0, 0, 4, 5);
                                  map_column(15, 0, 0, 4, 5);
                                  camera_init(16, 11, 4);
                                  while (true) {
                                      video_wait_vblank();
                                      camera_apply();
                                      camera_move_right();
                                      camera_move_left();
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x3E, 0x00, 0xEA, 0xE0, 0xC0, 0xEA, 0xE1, 0xC0, 0xEA, 0xE2, 0xC0]), "camera_init should initialize the 16-bit world X and fine scroll state.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x14, 0xEA, 0xE4, 0xC0, 0x3E, 0x1F, 0xEA, 0xE5, 0xC0]), "camera_init should seed the circular background streaming edges.");
        Assert.True(ContainsSequence(rom, [0xFA, 0xE0, 0xC0, 0xE0, 0x43, 0x3E, 0x00, 0xE0, 0x42]), "camera_apply should write the current camera X low byte to SCX.");
        Assert.True(ContainsSequence(rom, [0xFA, 0xE0, 0xC0, 0xC6, 0x01, 0xEA, 0xE0, 0xC0]), "camera_move_right should increment the 16-bit camera X low byte.");
        Assert.True(ContainsSequence(rom, [0xFA, 0xE6, 0xC0, 0x5F, 0x16, 0x00]), "camera_move_right should stream from the right source column.");
        Assert.True(ContainsSequence(rom, [0xFA, 0xE5, 0xC0, 0xC6, 0x60, 0x6F, 0x26, 0x99]), "camera_move_left should stream into the left background edge.");
    }

    [Fact]
    public void Camera_set_position_compares_requested_x_before_reusing_camera_steps()
    {
        const string source = """
                              void main() {
                                  video_init();
                                  map_column(0, 0, 0, 4, 5);
                                  map_column(1, 0, 0, 4, 5);
                                  camera_init(2, 11, 4);
                                  i16 requestedX = 1;
                                  camera_set_position(requestedX, 0);
                                  camera_apply();
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0x47, 0xFA, 0xE0, 0xC0, 0xB8, 0xCA]), "camera_set_position should compare current camera X against the requested X and keep a no-movement path.");
        Assert.True(ContainsSequence(rom, [0xFA, 0xE0, 0xC0, 0xC6, 0x01, 0xEA, 0xE0, 0xC0]), "camera_set_position should reuse the right-step camera movement path.");
        Assert.True(ContainsSequence(rom, [0xFA, 0xE0, 0xC0, 0xFE, 0x00, 0xC2]), "camera_set_position should reuse the left-step camera movement path.");
    }

    [Fact]
    public void Compiles_camera_tile_column_at_to_map_width_wrapped_source_column()
    {
        const string source = """
                              void main() {
                                  video_init();
                                  map_column(0, 0, 0, 4, 5);
                                  map_column(1, 0, 0, 4, 5);
                                  map_column(2, 0, 0, 4, 5);
                                  map_column(3, 0, 0, 4, 5);
                                  map_column(4, 0, 0, 4, 5);
                                  map_column(5, 0, 0, 4, 5);
                                  map_column(6, 0, 0, 4, 5);
                                  map_column(7, 0, 0, 4, 5);
                                  map_column(8, 0, 0, 4, 5);
                                  map_column(9, 0, 0, 4, 5);
                                  map_column(10, 0, 0, 4, 5);
                                  map_column(11, 0, 0, 4, 5);
                                  map_column(12, 0, 0, 4, 5);
                                  map_column(13, 0, 0, 4, 5);
                                  map_column(14, 0, 0, 4, 5);
                                  map_column(15, 0, 0, 4, 5);
                                  camera_init(16, 11, 4);
                                  i16 tile = 0;
                                  while (true) {
                                      video_wait_vblank();
                                      tile = map_tile_at(camera_tile_column_at(19), 2);
                                      camera_move_right();
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x3E, 0x13, 0x47, 0xFA, 0xE3, 0xC0, 0x80]), "camera_tile_column_at should add a screen tile column to the camera's source-left column.");
        Assert.True(ContainsSequence(rom, [0xFE, 0x10, 0xDA]), "camera_tile_column_at should branch when the source column is already inside the configured map width.");
        Assert.True(ContainsSequence(rom, [0xD6, 0x10]), "camera_tile_column_at should wrap columns by subtracting the configured map width.");
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
                              void main() {
                                  video_init();
                                  sprite_asset(mario_player, "player-wide.gb.png", 33, 16);
                                  map_column(0, 0, 0, 4, 5);
                                  map_column(1, 0, 0, 4, 5);
                                  map_column(2, 0, 0, 4, 5);
                                  map_column(3, 0, 0, 4, 5);
                                  map_column(4, 0, 0, 4, 5);
                                  map_column(5, 0, 0, 4, 5);
                                  map_column(6, 0, 0, 4, 5);
                                  map_column(7, 0, 0, 4, 5);
                                  map_column(8, 0, 0, 4, 5);
                                  map_column(9, 0, 0, 4, 5);
                                  map_column(10, 0, 0, 4, 5);
                                  map_column(11, 0, 0, 4, 5);
                                  map_column(12, 0, 0, 4, 5);
                                  map_column(13, 0, 0, 3, 5);
                                  map_column(14, 0, 0, 4, 5);
                                  map_column(15, 0, 0, 4, 5);
                                  camera_init(16, 11, 4);
                                  i16 logicalWidth = 0;
                                  i16 footTile = 0;
                                  i16 fail = 0;
                                  while (true) {
                                      video_wait_vblank();
                                      logicalWidth = sprite_width(mario_player);
                                      footTile = camera_span_tile_at(72, sprite_width(mario_player), 2);
                                      fail = camera_span_has_tile(72, sprite_width(mario_player), 2, 3);
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source, baseDirectory);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x3E, 0x21]), "sprite_width should compile to the sprite asset's logical width.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x09, 0x47, 0xFA, 0xE3, 0xC0, 0x80]), "Span collision should check the first tile column covered by screen X.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x0D, 0x47, 0xFA, 0xE3, 0xC0, 0x80]), "Span collision should check the last tile column covered by a 33 px logical sprite.");
        Assert.True(ContainsSequence(rom, [0xFE, 0x03, 0xCA]), "camera_span_has_tile should compare each covered source tile against the requested tile id.");
    }

    [Fact]
    public void Video_wait_vblank_waits_for_the_next_vblank_edge()
    {
        const string source = """
                              void main() {
                                  video_init();
                                  while (true) {
                                      video_wait_vblank();
                                      scroll_set(1, 0);
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0xF0, 0x44, 0xFE, 0x90, 0x30]), "ROM should first wait until the previous VBlank has ended.");
        Assert.True(ContainsSequence(rom, [0xF0, 0x44, 0xFE, 0x90, 0x38]), "ROM should then wait until the next VBlank begins.");
    }

    [Fact]
    public void Compiles_tilemap_fill_column_to_runtime_vram_writes()
    {
        const string source = """
                              void main() {
                                  video_init();
                                  i16 column = 20;
                                  while (true) {
                                      video_wait_vblank();
                                      tilemap_fill_column(column, 13, 2, 4);
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x3E, 0x04, 0x47]), "ROM should preserve the tile id in B.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xC6, 0xA0, 0x6F, 0x26, 0x99, 0x78, 0x77]), "ROM should write row 13 at $99A0 + column.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xC6, 0xC0, 0x6F, 0x26, 0x99, 0x78, 0x77]), "ROM should write row 14 at $99C0 + column.");
    }

    [Fact]
    public void Compiles_long_runtime_loop_with_column_streaming()
    {
        const string source = """
                              void main() {
                                  video_init();
                                  i16 camera = 0;
                                  i16 fine = 0;
                                  i16 streamColumn = 20;
                                  i16 marker = 0;
                                  i16 frame = 0;
                                  while (true) {
                                      video_wait_vblank();
                                      scroll_set(camera, 0);
                                      sprite_set(0, 72, 80, 6 + frame, 0);
                                      sprite_set(1, 80, 80, 8 + frame, 0);
                                      camera = camera + 1;
                                      fine = fine + 1;
                                      frame = frame + 4;
                                      if (fine == 8) {
                                          fine = 0;
                                          tilemap_fill_column(streamColumn, 11, 2, 0);
                                          tilemap_fill_column(streamColumn, 13, 1, 4);
                                          tilemap_fill_column(streamColumn, 14, 1, 5);
                                          marker = marker + 1;
                                          if (marker == 4) {
                                              marker = 0;
                                              tilemap_fill_column(streamColumn, 12, 1, 5);
                                          }
                                          streamColumn = streamColumn + 1;
                                          if (streamColumn == 32) {
                                              streamColumn = 0;
                                          }
                                      }
                                      if (frame == 8) {
                                          frame = 0;
                                      }
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.Contains(rom.Skip(0x0150), b => b == 0xC3);
    }

    [Fact]
    public void Compiles_map_columns_to_rom_data_and_streams_them_to_vram()
    {
        const string source = """
                              void main() {
                                  video_init();
                                  map_column(0, 1, 2, 3, 4);
                                  map_column(1, 5, 6, 7, 8);
                                  i16 targetColumn = 20;
                                  i16 mapColumn = 0;
                                  while (true) {
                                      video_wait_vblank();
                                      map_stream_column(targetColumn, mapColumn, 11, 4);
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x01, 0x05]), "ROM should contain map row 0 data.");
        Assert.True(ContainsSequence(rom, [0x02, 0x06]), "ROM should contain map row 1 data.");
        Assert.True(ContainsSequence(rom, [0x03, 0x07]), "ROM should contain map row 2 data.");
        Assert.True(ContainsSequence(rom, [0x04, 0x08]), "ROM should contain map row 3 data.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x01, 0xC0, 0x5F, 0x16, 0x00, 0x21]), "ROM should load the source map column into DE and a row-table address into HL.");
        Assert.True(ContainsSequence(rom, [0x19, 0x7E, 0x47]), "ROM should read a tile from the map row table and preserve it in B.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xC6, 0x60, 0x6F, 0x26, 0x99, 0x78, 0x77]), "ROM should stream row 11 into the target background column.");
    }

    [Fact]
    public void Compiles_map_tile_lookup_as_a_runtime_expression()
    {
        const string source = """
                              void main() {
                                  video_init();
                                  map_column(0, 1, 0);
                                  map_column(1, 0, 2);
                                  i16 column = 1;
                                  i16 grounded = 0;
                                  if (map_tile_at(column, 1) != 0) {
                                      grounded = 1;
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0x5F, 0x16, 0x00, 0x21]), "ROM should load the runtime source map column and the selected row table address.");
        Assert.True(ContainsSequence(rom, [0x19, 0x7E, 0xFE, 0x00]), "ROM should read the tile id into A and compare it with zero.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x01, 0xEA, 0x01, 0xC0]), "ROM should execute the branch body when the tile is non-zero.");
    }

    [Fact]
    public void Compiles_relational_condition_against_a_constant()
    {
        const string source = """
                              void main() {
                                  video_init();
                                  i16 y = 80;
                                  i16 grounded = 0;
                                  if (y >= 78) {
                                      grounded = 1;
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xFE, 0x4E, 0xDA]), "ROM should compare y with 78 and jump over the branch when y is below the threshold.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x01, 0xEA, 0x01, 0xC0]), "ROM should execute the branch body when the relation is true.");
    }

    [Fact]
    public void Compiles_addition_between_runtime_locals()
    {
        const string source = """
                              void main() {
                                  video_init();
                                  i16 y = 40;
                                  i16 velocity = 3;
                                  y = y + velocity;
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0xFA, 0x01, 0xC0, 0x47, 0xFA, 0x00, 0xC0, 0x80, 0xEA, 0x00, 0xC0]), "ROM should add the two byte-backed locals and store the result.");
    }

    [Fact]
    public void Compiles_button_pressed_as_a_runtime_expression()
    {
        const string source = """
                              void main() {
                                  video_init();
                                  i16 jumped = 0;
                                  if (button_pressed(a) != 0) {
                                      jumped = 1;
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x3E, 0x10, 0xE0, 0x00, 0xF0, 0x00, 0xF0, 0x00, 0xF0, 0x00, 0xF0, 0x00, 0x2F, 0xE6, 0x0F, 0xE6, 0x01, 0xFE, 0x00]), "ROM should settle and read the Game Boy action-button register before returning 1 when A is pressed.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x01, 0xEA, 0x00, 0xC0]), "ROM should execute the branch body when the button is pressed.");
    }

    [Fact]
    public void Compiles_tick_input_api_for_variable_jump()
    {
        const string source = """
                              void main() {
                                  video_init();
                                  i16 grounded = 1;
                                  i16 velocityY = 0;
                                  i16 jumping = 0;
                                  i16 jumpTicks = 0;
                                  while (true) {
                                      video_wait_vblank();
                                      input_poll();
                                      if (button_just_pressed(a) != 0) {
                                          if (grounded != 0) {
                                              velocityY = 252;
                                              jumping = 1;
                                          }
                                      }
                                      if (jumping != 0) {
                                          jumpTicks = button_hold_ticks(a);
                                          if (button_down(a) != 0) {
                                              if (jumpTicks < 12) {
                                                  velocityY = velocityY - 1;
                                              }
                                          }
                                          if (button_just_released(a) != 0) {
                                              jumping = 0;
                                          }
                                      }
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0xFA, 0xF0, 0xC0, 0xEA, 0xF1, 0xC0]), "input_poll should snapshot the previous button mask before reading the current tick.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x10, 0xE0, 0x00, 0xF0, 0x00, 0xF0, 0x00, 0xF0, 0x00, 0xF0, 0x00, 0x2F, 0xE6, 0x0F, 0x47]), "input_poll should read the settled action-button group into the current tick mask.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x20, 0xE0, 0x00, 0xF0, 0x00, 0xF0, 0x00, 0xF0, 0x00, 0xF0, 0x00, 0x2F, 0xE6, 0x0F, 0xCB, 0x37, 0xB0, 0xEA, 0xF0, 0xC0]), "input_poll should read the settled direction-button group and store a combined button mask.");
        Assert.True(ContainsSequence(rom, [0xFA, 0xF2, 0xC0, 0xEA, 0x03, 0xC0]), "button_hold_ticks(a) should read the A-button hold counter into a game variable.");
        Assert.True(ContainsSequence(rom, [0xFA, 0xF0, 0xC0, 0xE6, 0x01, 0xFE, 0x00, 0xCA]), "button_down(a) and button_just_pressed(a) should test the current tick mask.");
        Assert.True(ContainsSequence(rom, [0xFA, 0xF1, 0xC0, 0xE6, 0x01, 0xFE, 0x00, 0xC2]), "button_just_pressed(a) should reject buttons that were already down in the previous tick.");
        Assert.True(ContainsSequence(rom, [0xFA, 0xF0, 0xC0, 0xE6, 0x01, 0xFE, 0x00, 0xC2]), "button_just_released(a) should require the button to be up in the current tick.");
        Assert.True(ContainsSequence(rom, [0xFA, 0xF1, 0xC0, 0xE6, 0x01, 0xFE, 0x00, 0xCA]), "button_just_released(a) should require the button to have been down in the previous tick.");
    }

    [Fact]
    public void Input_poll_settles_joypad_rows_before_latching_buttons()
    {
        const string source = """
                              void main() {
                                  video_init();
                                  while (true) {
                                      video_wait_vblank();
                                      input_poll();
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x3E, 0x10, 0xE0, 0x00, 0xF0, 0x00, 0xF0, 0x00, 0xF0, 0x00, 0xF0, 0x00, 0x2F, 0xE6, 0x0F, 0x47]), "input_poll should discard early action-row reads so DMG hardware has time to settle.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x20, 0xE0, 0x00, 0xF0, 0x00, 0xF0, 0x00, 0xF0, 0x00, 0xF0, 0x00, 0x2F, 0xE6, 0x0F, 0xCB, 0x37]), "input_poll should discard early d-pad-row reads so DMG hardware has time to settle.");
        Assert.True(ContainsSequence(rom, [0xEA, 0xF0, 0xC0, 0x3E, 0x30, 0xE0, 0x00]), "input_poll should deselect both joypad rows after latching the snapshot.");
    }

    [Fact]
    public void GameBoy_runner_drives_scroll_and_run_animation_from_dpad()
    {
        var sourcePath = RepositoryFile("samples/gameboy-runner/runner.rs");
        var source = File.ReadAllText(sourcePath);

        var rightStart = source.IndexOf("if (button_down(right) != 0)", StringComparison.Ordinal);
        Assert.True(rightStart >= 0, "Runner should gate forward movement with the D-pad right button.");

        var leftStart = source.IndexOf("if (button_down(left) != 0)", StringComparison.Ordinal);
        Assert.True(leftStart >= 0, "Runner should gate backward movement with the D-pad left button.");

        var animationStart = source.IndexOf("animTick = animTick + 1;", StringComparison.Ordinal);
        Assert.True(animationStart > rightStart, "Runner should update movement before animation state.");

        Assert.Contains("i16 cameraX = 0;", source);

        var movementBlock = source[rightStart..animationStart];
        Assert.Contains("cameraX = cameraX + 1;", movementBlock);
        Assert.Contains("moving = 1;", movementBlock);
        Assert.Contains("cameraX = cameraX - 1;", movementBlock);
        Assert.Contains("camera_set_position(cameraX, 0);", movementBlock);
        Assert.DoesNotContain("camera_move_right();", source);
        Assert.DoesNotContain("camera_move_left();", source);
        Assert.Contains("if (moving != 0)", source);
        Assert.Contains("animTick = animTick + 1;", source);
        Assert.Contains("frame = 0;", source);
        Assert.Equal(1, CountOccurrences(source, "camera_set_position(cameraX, 0);"));
        Assert.Equal(1, CountOccurrences(source, "animTick = animTick + 1;"));

        var rom = GameBoyRomCompiler.CompileSource(source, Path.GetDirectoryName(sourcePath));
        Assert.Equal(32768, rom.Length);
    }

    [Fact]
    public void GameBoy_runner_uses_one_actor_spritesheet_for_idle_run_and_jump_states()
    {
        var sourcePath = RepositoryFile("samples/gameboy-runner/runner.rs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("""sprite_asset(mario_player, "assets/mario-player.gb.png", 18, 32);""", source);
        Assert.DoesNotContain("sprites_clear();", source);
        Assert.Contains("if (grounded == 0)", source);
        Assert.Contains("displayFrame = 4;", source);
        Assert.Contains("displayFrame = frame + 1;", source);
        Assert.Contains("displayFrame = 0;", source);
        Assert.Contains("displayFlags = 32;", source);
        Assert.Contains("displayFlags = 0;", source);
        Assert.Contains("sprite_draw(mario_player, 72, playerY, displayFrame, displayFlags);", source);
        Assert.Equal(1, CountOccurrences(source, "sprite_draw("));

        var rom = GameBoyRomCompiler.CompileSource(source, Path.GetDirectoryName(sourcePath));
        Assert.Equal(32768, rom.Length);
    }

    [Fact]
    public void GameBoy_runner_uses_lighter_object_palette_for_player_sprite()
    {
        var sourcePath = RepositoryFile("samples/gameboy-runner/runner.rs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("object_palette_set(0, 0);", source);
        Assert.Contains("object_palette_set(1, 0);", source);
        Assert.Contains("object_palette_set(2, 1);", source);
        Assert.Contains("object_palette_set(3, 3);", source);
        Assert.DoesNotContain("object_palette_set(1, 1);", source);
        Assert.DoesNotContain("object_palette_set(2, 2);", source);
        Assert.DoesNotContain("object_palette_set(1, 2);", source);
        Assert.DoesNotContain("object_palette_set(2, 3);", source);

        var rom = GameBoyRomCompiler.CompileSource(source, Path.GetDirectoryName(sourcePath));

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x3E, 0xD0, 0xE0, 0x48]), "Runner should map sprite tones to OBP0 as 0, 0, 1, 3.");
    }

    [Fact]
    public void GameBoy_runner_presents_sprites_immediately_after_vblank()
    {
        var source = File.ReadAllText(RepositoryFile("samples/gameboy-runner/runner.rs"));

        Assert.Contains("i16 playerY = 73;", source);
        Assert.Contains("i16 grounded = 1;", source);

        var vblankStart = source.IndexOf("video_wait_vblank();", StringComparison.Ordinal);
        var inputPoll = source.IndexOf("input_poll();", StringComparison.Ordinal);
        var gravity = source.IndexOf("velocityY = velocityY + 1;", StringComparison.Ordinal);
        var draw = source.IndexOf("sprite_draw(mario_player, 72, playerY, displayFrame, displayFlags);", StringComparison.Ordinal);

        Assert.True(vblankStart >= 0);
        Assert.True(draw > vblankStart, "Runner should draw the active state immediately after entering VBlank.");
        Assert.True(inputPoll > draw, "Runner should finish sprite presentation before input and gameplay updates consume VBlank time.");
        Assert.True(gravity > inputPoll, "Runner should update gameplay after the VBlank presentation block.");
    }

    [Fact]
    public void World_map_generates_initial_visible_tilemap_from_map_columns()
    {
        const string source = """
                              void define_level_columns() {
                                  map_column(0, 0, 0, 4, 5);
                                  map_column(1, 0, 0, 4, 5);
                                  map_column(2, 0, 5, 4, 5);
                                  map_column(3, 0, 0, 4, 5);
                                  map_column(4, 0, 0, 4, 5);
                                  map_column(5, 0, 0, 4, 5);
                                  map_column(6, 0, 0, 4, 5);
                                  map_column(7, 0, 0, 3, 5);
                                  map_column(8, 0, 0, 4, 5);
                                  map_column(9, 0, 0, 4, 5);
                                  map_column(10, 5, 0, 4, 5);
                                  map_column(11, 0, 0, 4, 5);
                                  map_column(12, 0, 0, 4, 5);
                                  map_column(13, 0, 0, 0, 0);
                                  map_column(14, 0, 0, 0, 0);
                                  map_column(15, 0, 0, 0, 0);
                                  return;
                              }

                              void main() {
                                  define_level_columns();
                                  world_map(16, 11, 4);
                                  return;
                              }
                              """;

        var program = CompileVideoProgram(source);
        var worldMap = Assert.IsType<WorldMap2D>(program.WorldMap);

        Assert.Equal(16, worldMap.Width);
        Assert.Equal(4, worldMap.Height);
        Assert.Equal(3, worldMap.TileIdAt(7, 2));
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
                              void define_world() {
                                  world_column(0, 0, 0, 4, 5);
                                  world_column(1, 0, 0, 4, 5);
                                  world_column(2, 0, 5, 4, 5);
                                  world_column(3, 0, 0, 4, 5);
                                  world_column(4, 0, 0, 4, 5);
                                  world_column(5, 0, 0, 4, 5);
                                  world_column(6, 0, 0, 4, 5);
                                  world_column(7, 0, 0, 3, 5);
                                  world_column(8, 0, 0, 4, 5);
                                  world_column(9, 0, 0, 4, 5);
                                  world_column(10, 5, 0, 4, 5);
                                  world_column(11, 0, 0, 4, 5);
                                  world_column(12, 0, 0, 4, 5);
                                  world_column(13, 0, 0, 0, 0);
                                  world_column(14, 0, 0, 0, 0);
                                  world_column(15, 0, 0, 0, 0);
                                  return;
                              }

                              void main() {
                                  define_world();
                                  world_map(16, 11, 4);
                                  camera_init(16, 11, 4);
                                  return;
                              }
                              """;

        var program = CompileVideoProgram(source);
        var worldMap = Assert.IsType<WorldMap2D>(program.WorldMap);

        Assert.Equal(4, program.MapColumnHeight);
        Assert.Equal(3, worldMap.TileIdAt(7, 2));
        Assert.Equal(3, program.MapColumns[7][2]);
        Assert.Equal(0, program.MapColumns[13][2]);
        Assert.Equal(5, program.TileMap[14 * 32]);
        Assert.Equal(3, program.TileMap[13 * 32 + 23]);
        Assert.Equal(32768, GameBoyRomCompiler.CompileSource(source).Length);
    }

    [Fact]
    public void World_map_generates_collision_flags_and_lowers_flag_queries()
    {
        const string source = """
                              void define_world() {
                                  world_column(0, 0, 4);
                                  world_column(1, 3, 5);
                                  world_flags(0, 0, 1);
                                  world_flags(1, 2, 1);
                                  return;
                              }

                              void main() {
                                  define_world();
                                  world_map(2, 11, 2);
                                  camera_init(2, 11, 2);
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

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x00, 0x02]), "ROM should contain world flag row 0 data.");
        Assert.True(ContainsSequence(rom, [0x01, 0x01]), "ROM should contain world flag row 1 data.");
        Assert.True(ContainsSequence(rom, [0xE6, 0x01, 0xFE, 0x00, 0xC2]), "Solid flag queries should mask bit 0 independently.");
        Assert.True(ContainsSequence(rom, [0xE6, 0x02, 0xFE, 0x00, 0xC2]), "Hazard flag queries should mask bit 1 independently.");
    }

    [Fact]
    public void GameBoy_runner_uses_actor_feet_holes_failure_tiles_and_reset_state()
    {
        var sourcePath = RepositoryFile("samples/gameboy-runner/runner.rs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("i16 footTile = 0;", source);
        Assert.Contains("i16 failTile = 0;", source);
        Assert.Contains("i16 resetRequested = 0;", source);

        Assert.Contains("world_column(7, 0, 0, 3, 5);", source);
        Assert.Contains("world_column(13, 0, 0, 0, 0);", source);
        Assert.Contains("world_flags(7, 0, 0, 2, 1);", source);
        Assert.Contains("world_flags(13, 0, 0, 0, 0);", source);
        Assert.DoesNotContain("map_column(", source);
        Assert.Contains("failTile = camera_span_has_flags(72, sprite_width(mario_player), 2, 2);", source);
        Assert.Contains("footTile = camera_span_has_flags(72, sprite_width(mario_player), 2, 1);", source);
        Assert.DoesNotContain("camera_span_has_tile(", source);
        Assert.DoesNotContain("camera_span_tile_at(", source);
        Assert.DoesNotContain("playerLeftFootColumn", source);
        Assert.DoesNotContain("playerCenterFootColumn", source);
        Assert.DoesNotContain("playerRightFootColumn", source);
        Assert.DoesNotContain("map_tile_at(player", source);
        Assert.Contains("if (failTile != 0)", source);
        Assert.DoesNotContain("if (footTile != 3)", source);
        Assert.Contains("if (grounded == 0)", source);
        Assert.Contains("if (playerY >= 116)", source);
        Assert.Contains("if (resetRequested != 0)", source);
        Assert.Contains("playerY = 73;", source);
        Assert.Contains("velocityY = 0;", source);
        Assert.Contains("jumping = 0;", source);

        var rom = GameBoyRomCompiler.CompileSource(source, Path.GetDirectoryName(sourcePath));
        Assert.Equal(32768, rom.Length);
    }

    [Fact]
    public void GameBoy_runner_keeps_visible_map_collision_and_streaming_cursors_in_sync()
    {
        var sourcePath = RepositoryFile("samples/gameboy-runner/runner.rs");
        var source = File.ReadAllText(sourcePath);

        Assert.DoesNotContain("void draw_starting_scene()", source);
        Assert.DoesNotContain("tilemap_fill(", source);
        Assert.DoesNotContain("tilemap_set(", source);
        Assert.Contains("world_map(16, 11, 4);", source);
        Assert.Contains("world_column(13, 0, 0, 0, 0);", source);
        Assert.Contains("world_column(14, 0, 0, 0, 0);", source);
        Assert.Contains("world_column(15, 0, 0, 0, 0);", source);
        Assert.Contains("world_flags(7, 0, 0, 2, 1);", source);
        Assert.Contains("world_flags(13, 0, 0, 0, 0);", source);
        Assert.Contains("world_flags(14, 0, 0, 0, 0);", source);
        Assert.Contains("world_flags(15, 0, 0, 0, 0);", source);
        Assert.DoesNotContain("map_column(", source);

        Assert.Contains("camera_init(16, 11, 4);", source);
        Assert.True(
            source.IndexOf("camera_init(16, 11, 4);", StringComparison.Ordinal) >
            source.IndexOf("world_map(16, 11, 4);", StringComparison.Ordinal));
        Assert.Contains("camera_apply();", source);
        Assert.Contains("footTile = camera_span_has_flags(72, sprite_width(mario_player), 2, 1);", source);
        Assert.Contains("failTile = camera_span_has_flags(72, sprite_width(mario_player), 2, 2);", source);
        Assert.DoesNotContain("camera_span_has_tile(", source);
        Assert.DoesNotContain("camera_span_tile_at(", source);
        Assert.Contains("camera_set_position(cameraX, 0);", source);
        Assert.DoesNotContain("camera_move_right();", source);
        Assert.DoesNotContain("camera_move_left();", source);
        Assert.DoesNotContain("i16 screenLeftColumn = 0;", source);
        Assert.DoesNotContain("i16 rightSourceColumn = 4;", source);
        Assert.DoesNotContain("i16 leftSourceColumn = 15;", source);
        Assert.DoesNotContain("i16 leftStreamColumn = 31;", source);
        Assert.DoesNotContain("map_stream_column(streamColumn, rightSourceColumn, 11, 4);", source);
        Assert.DoesNotContain("map_stream_column(leftStreamColumn, leftSourceColumn, 11, 4);", source);

        var resetStart = source.IndexOf("if (resetRequested != 0)", StringComparison.Ordinal);
        Assert.True(resetStart >= 0);
        var displayStart = source.IndexOf("if (grounded == 0)", resetStart, StringComparison.Ordinal);
        Assert.True(displayStart > resetStart);
        var resetBlock = source[resetStart..displayStart];
        Assert.DoesNotContain("camera = 0;", resetBlock);
        Assert.DoesNotContain("camera_init(", resetBlock);
        Assert.DoesNotContain("streamColumn = 20;", resetBlock);
        Assert.DoesNotContain("screenLeftColumn = 0;", resetBlock);
        Assert.DoesNotContain("rightSourceColumn = 4;", resetBlock);
        Assert.DoesNotContain("leftSourceColumn = 15;", resetBlock);

        var rom = GameBoyRomCompiler.CompileSource(source, Path.GetDirectoryName(sourcePath));
        Assert.Equal(32768, rom.Length);
    }

    [Fact]
    public void GameBoy_runner_applies_reset_before_consuming_jump_input()
    {
        var sourcePath = RepositoryFile("samples/gameboy-runner/runner.rs");
        var source = File.ReadAllText(sourcePath);

        var resetStart = source.IndexOf("if (resetRequested != 0)", StringComparison.Ordinal);
        var jumpStart = source.IndexOf("if (button_just_pressed(a) != 0)", StringComparison.Ordinal);
        var movementStart = source.IndexOf("moving = 0;", resetStart, StringComparison.Ordinal);

        Assert.True(resetStart >= 0);
        Assert.True(jumpStart > resetStart, "Reset should restore safe actor state before jump input is consumed.");
        Assert.True(movementStart > resetStart, "Reset should not discard horizontal movement input for the same frame.");

        var rom = GameBoyRomCompiler.CompileSource(source, Path.GetDirectoryName(sourcePath));
        Assert.Equal(32768, rom.Length);
    }

    [Fact]
    public void GameBoy_runner_keeps_ground_alignment_and_reset_animation_state()
    {
        var sourcePath = RepositoryFile("samples/gameboy-runner/runner.rs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("i16 playerY = 73;", source);
        Assert.Contains("if (playerY >= 74)", source);
        Assert.Equal(3, CountOccurrences(source, "playerY = 73;"));
        Assert.DoesNotContain("playerY = 77;", source);
        Assert.Contains("world_map(16, 11, 4);", source);
        Assert.Contains("world_column(0, 0, 0, 4, 5);", source);
        Assert.Contains("world_column(7, 0, 0, 3, 5);", source);
        Assert.Contains("world_flags(7, 0, 0, 2, 1);", source);
        Assert.DoesNotContain("map_column(", source);
        Assert.DoesNotContain("tilemap_set(7, 13, 3);", source);
        Assert.Contains("failTile = camera_span_has_flags(72, sprite_width(mario_player), 2, 2);", source);
        Assert.Contains("footTile = camera_span_has_flags(72, sprite_width(mario_player), 2, 1);", source);
        Assert.Contains("if (failTile != 0)", source);
        Assert.DoesNotContain("if (footTile != 3)", source);
        Assert.DoesNotContain("if (failTile == 4)", source);

        var resetStart = source.IndexOf("if (resetRequested != 0)", StringComparison.Ordinal);
        Assert.True(resetStart >= 0);
        var jumpStart = source.IndexOf("if (button_just_pressed(a) != 0)", resetStart, StringComparison.Ordinal);
        Assert.True(jumpStart > resetStart);
        var resetBlock = source[resetStart..jumpStart];
        Assert.DoesNotContain("frame = 0;", resetBlock);
        Assert.DoesNotContain("displayFlags = 0;", resetBlock);
        Assert.DoesNotContain("animTick = 0;", resetBlock);
        Assert.DoesNotContain("moving = 0;", resetBlock);

        var rom = GameBoyRomCompiler.CompileSource(source, Path.GetDirectoryName(sourcePath));
        Assert.Equal(32768, rom.Length);
    }

    [Fact]
    public void Compiles_long_if_body_with_map_streaming()
    {
        const string source = """
                              void main() {
                                  video_init();
                                  map_column(0, 0, 0, 3, 5);
                                  map_column(1, 0, 5, 3, 5);
                                  i16 fine = 0;
                                  i16 streamColumn = 20;
                                  i16 mapColumn = 0;
                                  while (true) {
                                      video_wait_vblank();
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

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0xC2]), "ROM should use an absolute conditional JP for long if bodies.");
    }

    private static byte HeaderChecksum(byte[] rom)
    {
        var checksum = 0;
        for (var i = 0x0134; i <= 0x014C; i++)
        {
            checksum = checksum - rom[i] - 1;
        }

        return (byte)checksum;
    }

    private static string WriteSpriteAsset(string fileName, string json)
    {
        var directory = Path.Combine(Path.GetTempPath(), "RetroSharp.GameBoy.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, fileName), json);
        return directory;
    }

    private static string WriteSpritePng(string fileName, int frameWidth, int frameHeight, params string[][] frames)
    {
        var directory = Path.Combine(Path.GetTempPath(), "RetroSharp.GameBoy.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        var width = frameWidth * frames.Length;
        var height = frameHeight;
        var rgba = new byte[width * height * 4];
        var palette = new[]
        {
            (R: (byte)0x00, G: (byte)0x00, B: (byte)0x00, A: (byte)0x00),
            (R: (byte)0xE0, G: (byte)0xF8, B: (byte)0xD0, A: (byte)0xFF),
            (R: (byte)0x88, G: (byte)0xC0, B: (byte)0x70, A: (byte)0xFF),
            (R: (byte)0x34, G: (byte)0x68, B: (byte)0x56, A: (byte)0xFF),
        };

        for (var frameIndex = 0; frameIndex < frames.Length; frameIndex++)
        {
            var frame = frames[frameIndex];
            for (var y = 0; y < frameHeight; y++)
            {
                for (var x = 0; x < frameWidth; x++)
                {
                    var color = palette[frame[y][x] - '0'];
                    var targetX = frameIndex * frameWidth + x;
                    var offset = (y * width + targetX) * 4;
                    rgba[offset] = color.R;
                    rgba[offset + 1] = color.G;
                    rgba[offset + 2] = color.B;
                    rgba[offset + 3] = color.A;
                }
            }
        }

        File.WriteAllBytes(Path.Combine(directory, fileName), EncodeRgbaPng(width, height, rgba));
        return directory;
    }

    private static string WriteGrayscaleSpritePng(string fileName, int frameWidth, int frameHeight, params string[][] frames)
    {
        var directory = Path.Combine(Path.GetTempPath(), "RetroSharp.GameBoy.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        var width = frameWidth * frames.Length;
        var height = frameHeight;
        var rgba = new byte[width * height * 4];
        var palette = new[]
        {
            (R: (byte)0x00, G: (byte)0x00, B: (byte)0x00, A: (byte)0x00),
            (R: (byte)0xFF, G: (byte)0xFF, B: (byte)0xFF, A: (byte)0xFF),
            (R: (byte)0xB8, G: (byte)0xB8, B: (byte)0xB8, A: (byte)0xFF),
            (R: (byte)0x00, G: (byte)0x00, B: (byte)0x00, A: (byte)0xFF),
        };

        for (var frameIndex = 0; frameIndex < frames.Length; frameIndex++)
        {
            var frame = frames[frameIndex];
            for (var y = 0; y < frameHeight; y++)
            {
                for (var x = 0; x < frameWidth; x++)
                {
                    var color = palette[frame[y][x] - '0'];
                    var targetX = frameIndex * frameWidth + x;
                    var offset = (y * width + targetX) * 4;
                    rgba[offset] = color.R;
                    rgba[offset + 1] = color.G;
                    rgba[offset + 2] = color.B;
                    rgba[offset + 3] = color.A;
                }
            }
        }

        File.WriteAllBytes(Path.Combine(directory, fileName), EncodeRgbaPng(width, height, rgba));
        return directory;
    }

    private static string SpriteJson(params string[][] frames)
    {
        var frameJson = string.Join(
            ",",
            frames.Select(frame => "[" + string.Join(",", frame.Select(row => $"\"{row}\"")) + "]"));

        return $$"""
                 {
                   "platforms": {
                     "gb": {
                       "frames": [{{frameJson}}]
                     }
                   }
                 }
                 """;
    }

    private static string[] Rows(int width, int height, params string[] overrides)
    {
        var rows = Enumerable.Repeat(new string('0', width), height).ToArray();
        for (var i = 0; i < overrides.Length; i++)
        {
            rows[i] = overrides[i];
        }

        return rows;
    }

    private static byte[] EncodeRgbaPng(int width, int height, byte[] rgba)
    {
        using var output = new MemoryStream();
        output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr[0..4], width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr[4..8], height);
        ihdr[8] = 8;
        ihdr[9] = 6;
        WritePngChunk(output, "IHDR", ihdr);

        using var raw = new MemoryStream();
        for (var y = 0; y < height; y++)
        {
            raw.WriteByte(0);
            raw.Write(rgba, y * width * 4, width * 4);
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            raw.Position = 0;
            raw.CopyTo(zlib);
        }

        WritePngChunk(output, "IDAT", compressed.ToArray());
        WritePngChunk(output, "IEND", []);
        return output.ToArray();
    }

    private static void WritePngChunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);

        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);

        var crc = Crc32(typeBytes, data);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        output.Write(crcBytes);
    }

    private static uint Crc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in type)
        {
            crc = UpdateCrc32(crc, value);
        }

        foreach (var value in data)
        {
            crc = UpdateCrc32(crc, value);
        }

        return crc ^ 0xFFFFFFFFu;
    }

    private static uint UpdateCrc32(uint crc, byte value)
    {
        crc ^= value;
        for (var i = 0; i < 8; i++)
        {
            crc = (crc & 1) == 0 ? crc >> 1 : 0xEDB88320u ^ (crc >> 1);
        }

        return crc;
    }

    private static bool ContainsSequence(byte[] bytes, byte[] sequence)
    {
        for (var i = 0; i <= bytes.Length - sequence.Length; i++)
        {
            var matches = true;
            for (var j = 0; j < sequence.Length; j++)
            {
                if (bytes[i + j] != sequence[j])
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                return true;
            }
        }

        return false;
    }

    private static string RepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"Could not find repository file '{relativePath}'.");
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static GameBoyVideoProgram CompileVideoProgram(string source)
    {
        var parse = new SomeParser().Parse(source);
        if (parse.IsFailure)
        {
            throw new InvalidOperationException(parse.Error);
        }

        return GameBoyVideoProgram.FromProgram(parse.Value);
    }
}
