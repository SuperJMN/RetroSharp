namespace RetroSharp.NES.Tests;

using RetroSharp.NES;
using Xunit;
using static NesSdkOperationBoundaryTests;
using static NesTestAssets;

[Trait("RetroSharp.TestOwnership", "SdkLowering")]
public sealed class NesSdkSpriteLoweringTests
{
    [Fact]
    [Trait("RetroSharp.TestOwnership", "SdkLowering")]
    public void Runtime_struct_array_sprite_fields_share_one_logarithmic_address_materialization()
    {
        var baseDirectory = WriteSpriteAsset(
            "hero.nes.json",
            """
            {
              "platforms": {
                "nes": {
                  "frames": [[
                    "11111111", "11111111", "11111111", "11111111",
                    "11111111", "11111111", "11111111", "11111111"
                  ]]
                }
              }
            }
            """);
        const string source = """
                              struct Actor {
                                  u8 x;
                                  u8 y;
                                  u8 frame;
                                  bool flip;
                                  u8 kind;
                                  u8 active;
                                  u8 state;
                                  u8 timer;
                                  u8 facing;
                                  u8 animTick;
                                  u8 health;
                                  i8 vx;
                                  i8 vy;
                              }

                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(hero, "hero.nes.json");
                                  Actor actors[2];
                                  u8 i = 1;
                                  Sprite.Draw(hero, actors[i].x, actors[i].y, actors[i].frame, actors[i].flip, 0);
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source, baseDirectory);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();
        var repeatedAddMaterialization = new List<byte> { 0xA5, 0x1A, 0x85, 0xE8 };
        for (var count = 1; count < 13; count++)
        {
            repeatedAddMaterialization.AddRange([0x18, 0x65, 0xE8]);
        }

        repeatedAddMaterialization.Add(0xAA);

        Assert.Equal(0, CountOccurrences(prg, repeatedAddMaterialization));
        Assert.Equal(
            1,
            CountOccurrences(prg, [0xA5, 0x1A, 0x85, 0xE8, 0x0A, 0x18, 0x65, 0xE8, 0x0A, 0x0A, 0x18, 0x65, 0xE8, 0xAA]));
    }

    [Fact]
    [Trait("RetroSharp.TestOwnership", "SdkLowering")]
    public void Compiles_logical_sprite_draw_to_nes_oam_and_chr_data()
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
                                      Sprite.Draw(hero, 24, 32, 0, 0, 2);
                                  }
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source, baseDirectory);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();
        var chr = rom.Skip(16 + 32 * 1024).Take(8 * 1024).ToArray();
        var spriteTile = chr.Skip(6 * 16).Take(16).ToArray();

        Assert.Equal(40976, rom.Length);
        Assert.Equal(Enumerable.Repeat((byte)0xFF, 8).Concat(Enumerable.Repeat((byte)0x00, 8)), spriteTile);
        Assert.True(ContainsSequence(prg, [0xA9, 0x20, 0x38, 0xE9, 0x01, 0x8D, 0x00, 0x02]), "sprite_draw should write NES OAM Y as logical y - 1.");
        Assert.True(ContainsSequence(prg, [0xA9, 0x06, 0x8D, 0x01, 0x02]), "sprite_draw should write the first compiled sprite tile to OAM.");
        Assert.True(ContainsSequence(prg, [0xA9, 0x02, 0x8D, 0x02, 0x02]), "sprite_draw should lower portable palette slot 2 to NES OAM attributes.");
        Assert.True(ContainsSequence(prg, [0xA9, 0x18, 0x8D, 0x03, 0x02]), "sprite_draw should write logical x to NES OAM.");
        Assert.True(ContainsSequence(prg, [0xA9, 0x02, 0x8D, 0x14, 0x40]), "sprite_draw should DMA the OAM shadow page after writing logical sprites.");
    }


    [Fact]
    [Trait("RetroSharp.TestOwnership", "SdkLowering")]
    public void Rejects_png_overlay_draw_when_base_palette_slot_has_no_overlay_slot()
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

        const string source = """
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(hero, "hero.png", 8, 8);
                                  while (true) {
                                      Video.WaitVBlank();
                                      Sprite.Draw(hero, 24, 32, 0, false, 3);
                                  }
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => NesRomCompiler.CompileSource(source, baseDirectory));

        Assert.Equal(
            "NES sprite asset 'hero' needs sprite palette slot 4 for an automatic PNG overlay, but target 'nes' supports slots 0..3.",
            exception.Message);
    }


    [Fact]
    [Trait("RetroSharp.TestOwnership", "SdkLowering")]
    public void Rejects_logical_sprite_palette_slots_outside_nes_capabilities()
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
                                  Sprite.Asset(hero, "hero.nes.json");
                                  while (true) {
                                      Sprite.Draw(hero, 24, 32, 0, 0, 4);
                                  }
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => NesRomCompiler.CompileSource(source, baseDirectory));

        Assert.Contains("Target 'nes' supports sprite palette slots 0..3, but slot 4 was requested.", exception.Message);
    }


    [Fact]
    [Trait("RetroSharp.TestOwnership", "SdkLowering")]
    public void Rejects_png_sprite_palette_slots_outside_nes_capabilities_before_palette_derivation()
    {
        var baseDirectory = WriteSpritePng(
            "hero.nes.png",
            8,
            8,
            Rows(8, 8, Enumerable.Repeat("33333333", 8).ToArray()));

        const string source = """
                              void Main() {
                                  Sprite.Asset(hero, "hero.nes.png", 8, 8);
                                  while (true) {
                                      Sprite.Draw(hero, 24, 32, 0, 0, 4);
                                  }
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => NesRomCompiler.CompileSource(source, baseDirectory));

        Assert.Contains("Target 'nes' supports sprite palette slots 0..3, but slot 4 was requested.", exception.Message);
    }


    [Fact]
    [Trait("RetroSharp.TestOwnership", "SdkLowering")]
    public void Rejects_constant_y_sprite_draws_that_exceed_nes_scanline_budget()
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
        var draws = string.Join(
            Environment.NewLine,
            Enumerable.Range(0, 9).Select(index => $"        Sprite.Draw(hero, {index * 8}, 24, 0);"));
        var source = """
                     void Main() {
                         Video.Init();
                         Sprite.Asset(hero, "hero.nes.json");
                         while (true) {
                             Video.WaitVBlank();

                     """ + draws + """
                         }
                     }
                     """;

        var exception = Assert.Throws<InvalidOperationException>(() => NesRomCompiler.CompileSource(source, baseDirectory));

        Assert.Equal(
            "Target 'nes' supports 8 hardware sprites per scanline, but 9 are required on scanline 24 for drawing logical sprites in one frame.",
            exception.Message);
    }


    [Fact]
    [Trait("RetroSharp.TestOwnership", "SdkLowering")]
    public void Rejects_logical_sprite_assets_that_exceed_nes_sprite_count()
    {
        var rows = Enumerable.Repeat(new string('1', 65 * 8), 8);
        var rowJson = string.Join(",", rows.Select(row => $"\"{row}\""));
        var baseDirectory = WriteSpriteAsset(
            "wide.nes.json",
            "{\"platforms\":{\"nes\":{\"frames\":[[" + rowJson + "]]}}}");

        const string source = """
                              void Main() {
                                  Sprite.Asset(wide, "wide.nes.json");
                                  while (true) {
                                      Sprite.Draw(wide, 0, 0, 0);
                                  }
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => NesRomCompiler.CompileSource(source, baseDirectory));

        Assert.Contains("NES sprite asset needs 65 hardware sprites, but the hardware limit is 64.", exception.Message);
    }
}
