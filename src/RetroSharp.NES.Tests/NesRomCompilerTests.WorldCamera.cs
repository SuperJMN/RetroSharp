namespace RetroSharp.NES.Tests;

using RetroSharp.GameBoy;
using RetroSharp.NES;
using RetroSharp.Parser;
using RetroSharp.Sdk;
using Xunit;
using static RetroSharp.NES.Tests.NesTestAssets;

public partial class NesRomCompilerTests
{
    [Fact]
    public void Compiles_camera_library_helpers_over_nes_intrinsic_like_sdk_operations()
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
                                  return;
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
                                   return;
                               }
                               """;
        Assert.Contains("class RetroSharp_Portable2D_Camera", SdkLibrarySource.ForTarget(NesTarget.Intrinsics), StringComparison.Ordinal);
        Assert.Equal(NesRomCompiler.CompileSource(direct), NesRomCompiler.CompileSource(library));
    }

    [Fact]
    public void Sprite_draw_via_library_helper_is_byte_identical_nes()
    {
        var baseDirectory = WriteSpriteAsset(
            "hero.nes.json",
            """
            {
              "platforms": {
                "nes": {
                  "frames": [
                    [
                      "11111111",
                      "11111111",
                      "11111111",
                      "11111111",
                      "11111111",
                      "11111111",
                      "11111111",
                      "11111111"
                    ]
                  ]
                }
              }
            }
            """);

        const string direct = """
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(hero, "hero.nes.json");
                                  while (true) {
                                      Video.WaitVBlank();
                                      Sprite.Draw(hero, 24, 32, 0, false, 2);
                                  }
                              }
                              """;
        const string library = """
                               void Main() {
                                   Video.Init();
                                   Sprite.Asset(hero, "hero.nes.json");
                                   while (true) {
                                       Video.WaitVBlank();
                                       Sprite.Draw(hero, 24, 32, 0, false, 2);
                                   }
                               }
                               """;

        var sdkLibrary = SdkLibrarySource.ForTarget(NesTarget.Intrinsics);

        Assert.Contains("class RetroSharp_Portable2D_Sprite", sdkLibrary, StringComparison.Ordinal);
        Assert.Contains("[intrinsic(\"sprite_draw\")]", sdkLibrary, StringComparison.Ordinal);
        Assert.Equal(NesRomCompiler.CompileSource(direct, baseDirectory), NesRomCompiler.CompileSource(library, baseDirectory));
    }
    [Fact]
    public void Sprite_draw_library_preserves_capability_and_budget_checks_nes()
    {
        var baseDirectory = WriteSpriteAsset(
            "hero.nes.json",
            """
            {
              "platforms": {
                "nes": {
                  "frames": [
                    [
                      "11111111",
                      "11111111",
                      "11111111",
                      "11111111",
                      "11111111",
                      "11111111",
                      "11111111",
                      "11111111"
                    ]
                  ]
                }
              }
            }
            """);

        const string paletteSource = """
                                     void Main() {
                                         Sprite.Asset(hero, "hero.nes.json");
                                         while (true) {
                                             Sprite.Draw(hero, 24, 32, 0, false, 4);
                                         }
                                     }
                                     """;

        var paletteException = Assert.Throws<InvalidOperationException>(() => NesRomCompiler.CompileSource(paletteSource, baseDirectory));
        Assert.Equal("Target 'nes' supports sprite palette slots 0..3, but slot 4 was requested.", paletteException.Message);

        var draws = string.Join(
            Environment.NewLine,
            Enumerable.Range(0, 9).Select(index => $"        Sprite.Draw(hero, {index * 8}, 24, 0);"));
        var budgetSource = """
                           void Main() {
                               Video.Init();
                               Sprite.Asset(hero, "hero.nes.json");
                               while (true) {
                                   Video.WaitVBlank();

                           """ + draws + """
                               }
                           }
                           """;

        var budgetException = Assert.Throws<InvalidOperationException>(() => NesRomCompiler.CompileSource(budgetSource, baseDirectory));
        Assert.Equal(
            "Target 'nes' supports 8 hardware sprites per scanline, but 9 are required on scanline 24 for drawing logical sprites in one frame.",
            budgetException.Message);
    }

    [Fact]
    public void Sprite_draw_source_package_helper_compiles_nes()
    {
        var baseDirectory = WriteSpriteAsset(
            "hero.nes.json",
            """
            {
              "platforms": {
                "nes": {
                  "frames": [
                    [
                      "11111111",
                      "11111111",
                      "11111111",
                      "11111111",
                      "11111111",
                      "11111111",
                      "11111111",
                      "11111111"
                    ]
                  ]
                }
              }
            }
            """);

        const string source = """
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(hero, "hero.nes.json");
                                  while (true) {
                                      Video.WaitVBlank();
                                      Sprite.Draw(hero, 24, 32, 0);
                                  }
                              }
                              """;

        _ = NesRomCompiler.CompileSource(source, baseDirectory);
    }

    [Fact]
    public void Nes_sdk_library_keeps_world_tile_flags_helper_target_gated()
    {
        // World.TileFlagsAt(...) is gated to Game Boy in source, then removed by
        // target selection before NES duplicate-name and intrinsic resolution.
        var library = SdkLibrarySource.ForTarget(NesTarget.Intrinsics);

        Assert.Contains("class RetroSharp_Portable2D_World", library, StringComparison.Ordinal);
        Assert.Contains("[target(\"gb\")]", library, StringComparison.Ordinal);
        Assert.Contains("[intrinsic(\"world_tile_flags_at\")]", library, StringComparison.Ordinal);
    }

    [Fact]
    public void Nes_sdk_dot_calls_accept_vertical_camera_on_four_screen_target()
    {
        const string source = """
                              void Main() {
                                  Video.Init();
                                  World.Column(0, 1, 2);
                                  World.Column(1, 3, 4);
                                  World.Map(2, 10, 2);
                                  Camera.Init(2, 10, 2);
                                  Camera.SetPosition(4, 1);
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        Assert.Equal(0x08, rom[6] & 0x08);
    }










    [Fact]
    public void Cross_target_camera_sample_compiles_for_game_boy_and_nes()
    {
        var sourcePath = RepositoryFile("samples/cross-target-camera/camera.rs");
        var source = File.ReadAllText(sourcePath);
        var baseDirectory = Path.GetDirectoryName(sourcePath);

        _ = GameBoyRomCompiler.CompileSource(source, baseDirectory);
        _ = NesRomCompiler.CompileSource(source, baseDirectory);
    }

    [Fact]
    public void Compiles_png_sprite_sheet_using_nes_platform_variant()
    {
        var baseDirectory = WriteSpritePng(
            "hero.nes.png",
            8,
            8,
            Rows(8, 8, Enumerable.Repeat("33333333", 8).ToArray()));

        const string source = """
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(hero, "hero.png", 8, 8);
                                  while (true) {
                                      Video.WaitVBlank();
                                      Sprite.Draw(hero, 24, 32, 0, 0, 0);
                                  }
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source, baseDirectory);
        var chr = rom.Skip(16 + 32 * 1024).Take(8 * 1024).ToArray();
        var spriteTile = chr.Skip(6 * 16).Take(16).ToArray();

        Assert.Equal(Enumerable.Repeat((byte)0xFF, 16), spriteTile);
    }

    [Fact]
    public void Colored_png_sprite_sheet_splits_extra_colors_into_optional_overlay_pieces()
    {
        var baseDirectory = WriteSpritePng(
            "hero.nes.png",
            8,
            8,
            [
                (R: (byte)0x00, G: (byte)0x00, B: (byte)0x00, A: (byte)0x00),
                (R: (byte)0xFC, G: (byte)0xBC, B: (byte)0xB0, A: (byte)0xFF),
                (R: (byte)0xD8, G: (byte)0x28, B: (byte)0x00, A: (byte)0xFF),
                (R: (byte)0x00, G: (byte)0x00, B: (byte)0x00, A: (byte)0xFF),
                (R: (byte)0xFF, G: (byte)0xFF, B: (byte)0xFF, A: (byte)0xFF),
            ],
            Rows(
                8,
                8,
                "11111111",
                "11111111",
                "22222222",
                "22222222",
                "33333333",
                "33333333",
                "11114411",
                "11114411"));

        var asset = NesSpriteAssetCompiler.CompileFromFile(
            "hero",
            Path.Combine(baseDirectory, "hero.nes.png"),
            6,
            8,
            8);

        Assert.Equal(2, asset.Pieces.Count);
        Assert.False(asset.Pieces[0].Optional);
        Assert.True(asset.Pieces[1].Optional);
        Assert.Equal(0, asset.Pieces[0].PaletteSlotOffset);
        Assert.Equal(1, asset.Pieces[1].PaletteSlotOffset);
        Assert.Equal(2, asset.TilesPerFrame);

        var baseTile = asset.TileData.Take(16).ToArray();
        var overlayTile = asset.TileData.Skip(16).Take(16).ToArray();
        Assert.Equal(0, TileColor(baseTile, 4, 6));
        Assert.NotEqual(0, TileColor(baseTile, 0, 6));
        Assert.NotEqual(0, TileColor(overlayTile, 4, 6));
        Assert.Equal(0, TileColor(overlayTile, 0, 6));
    }


}
