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
    public void Logical_palette_declarations_map_tones_to_nes_grayscale_palette_slots()
    {
        const string source = """
                              void Main() {
                                  Video.Init();
                                  Palette.Background(2, 0, 1, 2, 3);
                                  Palette.Sprite(3, 0, 0, 1, 3);
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        Assert.True(ContainsSequence(rom, [0x30, 0x10, 0x00, 0x0F]), "Palette.Background should map logical light-to-dark tones to NES grayscale colors.");
        Assert.True(ContainsSequence(rom, [0x30, 0x30, 0x10, 0x0F]), "Palette.Sprite should map the runner's logical sprite tones to NES grayscale colors.");
    }

    [Fact]
    public void Rejects_logical_palette_sprite_slots_outside_nes_capabilities()
    {
        const string source = """
                              void Main() {
                                  Video.Init();
                                  Palette.Sprite(4, 15, 17, 34, 51);
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => NesRomCompiler.CompileSource(source));

        Assert.Equal("Target 'nes' supports sprite palette slots 0..3, but palette slot 4 was requested.", exception.Message);
    }

    [Fact]
    public void Colored_png_sprite_sheet_applies_derived_nes_sprite_palette_to_draw_slot()
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
            ],
            Rows(
                8,
                8,
                "11112222",
                "11112222",
                "11112222",
                "11112222",
                "33333333",
                "33333333",
                "33333333",
                "33333333"));

        const string source = """
                              void Main() {
                                  Video.Init();
                                  Palette.Background(0, 0, 1, 2, 3);
                                  Palette.Sprite(0, 0, 0, 1, 3);
                                  Sprite.Asset(hero, "hero.png", 8, 8);
                                  while (true) {
                                      Video.WaitVBlank();
                                      Sprite.Draw(hero, 24, 32, 0, 0, 0);
                                  }
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source, baseDirectory);

        Assert.True(
            ContainsSequence(rom, [0x30, 0x36, 0x16, 0x0F]),
            "colored NES PNG sprite assets should drive the sprite palette slot without overwriting the universal background color.");
    }

    [Fact]
    public void Colored_png_sprite_sheets_with_distinct_palettes_can_share_a_logical_slot_when_nes_has_free_physical_slots()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), "RetroSharp.NES.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(baseDirectory);

        WriteSpritePngFile(
            baseDirectory,
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
        WriteSpritePngFile(
            baseDirectory,
            "enemy.nes.png",
            8,
            8,
            [
                (R: (byte)0x00, G: (byte)0x00, B: (byte)0x00, A: (byte)0x00),
                (R: (byte)0x00, G: (byte)0xEB, B: (byte)0xDB, A: (byte)0xFF),
                (R: (byte)0x4F, G: (byte)0xDF, B: (byte)0x4B, A: (byte)0xFF),
                (R: (byte)0x00, G: (byte)0x00, B: (byte)0x00, A: (byte)0xFF),
            ],
            Rows(
                8,
                8,
                "11112222",
                "11112222",
                "11112222",
                "11112222",
                "33333333",
                "33333333",
                "33333333",
                "33333333"));

        const string source = """
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(hero, "hero.png", 8, 8);
                                  Sprite.Asset(enemy, "enemy.png", 8, 8);
                                  while (true) {
                                      Video.WaitVBlank();
                                      Sprite.Draw(hero, 24, 32, 0, false, 0);
                                      Sprite.Draw(enemy, 40, 32, 0, false, 0);
                                  }
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source, baseDirectory);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.True(ContainsSequence(prg, [0xA9, 0x00, 0x8D, 0x02, 0x02]), "hero base pieces should keep the requested logical slot 0.");
        Assert.True(ContainsSequence(prg, [0xA9, 0x01, 0x8D, 0x06, 0x02]), "hero overlay pieces should use the next physical sprite palette slot.");
        Assert.True(ContainsSequence(prg, [0xA9, 0x02, 0x8D, 0x0A, 0x02]), "enemy pieces should be remapped to a free NES physical sprite palette slot.");
    }





}
