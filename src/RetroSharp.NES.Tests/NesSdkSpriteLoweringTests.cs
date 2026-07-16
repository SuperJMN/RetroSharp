namespace RetroSharp.NES.Tests;

using RetroSharp.NES;
using Xunit;
using static NesSdkOperationBoundaryTests;

public sealed class NesSdkSpriteLoweringTests
{
    [Fact]
    public void Golden_sprite_draw_emission_is_pinned_nes()
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
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(hero, "hero.nes.json");
                                  while (true) {
                                      Video.WaitVBlank();
                                      Sprite.Draw(hero, 24, 32, 0, false, 2);
                                  }
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source, baseDirectory);

        Assert.Equal("EBCA45A4F744B945E22D73422104BBAF7A5E88A28063CB4314E5F6AEC0C04D71", Fingerprint(rom));
    }

    [Fact]
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

}
