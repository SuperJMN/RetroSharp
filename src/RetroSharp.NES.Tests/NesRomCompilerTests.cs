namespace RetroSharp.NES.Tests;

using RetroSharp.NES;
using Xunit;

public class NesRomCompilerTests
{
    [Fact]
    public void Compiles_video_api_calls_to_an_ines_rom()
    {
        const string source = """
                              void main() {
                                  video_init();
                                  palette_set(0, 15);
                                  palette_set(1, 39);
                                  palette_set(2, 22);
                                  palette_set(3, 48);
                                  tilemap_set(14, 12, 1);
                                  tilemap_set(15, 12, 2);
                                  tilemap_set(16, 12, 1);
                                  tilemap_set(15, 13, 3);
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);

        Assert.Equal(24592, rom.Length);
        Assert.Equal((byte)'N', rom[0]);
        Assert.Equal((byte)'E', rom[1]);
        Assert.Equal((byte)'S', rom[2]);
        Assert.Equal(0x1A, rom[3]);
        Assert.Equal(1, rom[4]);
        Assert.Equal(1, rom[5]);

        var prg = rom.Skip(16).Take(16 * 1024).ToArray();
        var chr = rom.Skip(16 + 16 * 1024).Take(8 * 1024).ToArray();

        Assert.Equal(0x00, prg[^4]);
        Assert.Equal(0xC0, prg[^3]);
        Assert.Contains(chr, b => b != 0);
    }

    [Fact]
    public void Compiles_parameterless_user_functions_like_inline_video_blocks()
    {
        const string directSource = """
                                    void main() {
                                        video_init();
                                        palette_set(0, 15);
                                        palette_set(1, 39);
                                        tilemap_set(14, 12, 1);
                                        tilemap_set(15, 12, 2);
                                        video_present();
                                        return;
                                    }
                                    """;

        const string functionSource = """
                                      void setup_palette() {
                                          palette_set(0, 15);
                                          palette_set(1, 39);
                                          return;
                                      }

                                      void draw_mark() {
                                          tilemap_set(14, 12, 1);
                                          tilemap_set(15, 12, 2);
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

        Assert.Equal(NesRomCompiler.CompileSource(directSource), NesRomCompiler.CompileSource(functionSource));
    }

    [Fact]
    public void Nes_drawing_sample_compiles_with_helper_functions()
    {
        var source = File.ReadAllText(RepositoryFile("samples/nes-drawing/drawing.rs"));

        Assert.Contains("void draw_face()", source);

        var rom = NesRomCompiler.CompileSource(source);

        Assert.Equal(24592, rom.Length);
    }

    [Fact]
    public void Compiles_tick_input_helpers_to_nes_controller_state()
    {
        const string source = """
                              void main() {
                                  video_init();
                                  i16 down = 0;
                                  i16 pressed = 0;
                                  i16 released = 0;
                                  i16 held = 0;
                                  while (true) {
                                      video_wait_vblank();
                                      input_poll();
                                      down = button_down(a);
                                      pressed = button_just_pressed(a);
                                      released = button_just_released(a);
                                      held = button_hold_ticks(a);
                                  }
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(16 * 1024).ToArray();

        Assert.Equal(24592, rom.Length);
        Assert.True(ContainsSequence(prg, [0xA5, 0xF0, 0x85, 0xF1]), "input_poll should snapshot previous controller state before reading the current tick.");
        Assert.True(ContainsSequence(prg, [0xA9, 0x01, 0x8D, 0x16, 0x40, 0xA9, 0x00, 0x8D, 0x16, 0x40]), "input_poll should strobe NES controller port $4016.");
        Assert.True(ContainsSequence(prg, [0xAD, 0x16, 0x40, 0x29, 0x01]), "input_poll should read serial button bits from NES controller port $4016.");
        Assert.True(ContainsSequence(prg, [0xA5, 0xF0, 0x29, 0x01]), "button_down(a) should read the current tick mask.");
        Assert.True(ContainsSequence(prg, [0xA5, 0xF1, 0x29, 0x01]), "edge helpers should read the previous tick mask.");
        Assert.True(ContainsSequence(prg, [0xA5, 0xF2]), "button_hold_ticks(a) should read the A-button hold counter.");
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
                              void main() {
                                  video_init();
                                  sprite_asset(hero, "hero.nes.json");
                                  while (true) {
                                      video_wait_vblank();
                                      sprite_draw(hero, 24, 32, 0, 0, 2);
                                  }
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source, baseDirectory);
        var prg = rom.Skip(16).Take(16 * 1024).ToArray();
        var chr = rom.Skip(16 + 16 * 1024).Take(8 * 1024).ToArray();
        var spriteTile = chr.Skip(6 * 16).Take(16).ToArray();

        Assert.Equal(24592, rom.Length);
        Assert.Equal(Enumerable.Repeat((byte)0xFF, 8).Concat(Enumerable.Repeat((byte)0x00, 8)), spriteTile);
        Assert.True(ContainsSequence(prg, [0xA9, 0x20, 0x38, 0xE9, 0x01, 0x8D, 0x00, 0x02]), "sprite_draw should write NES OAM Y as logical y - 1.");
        Assert.True(ContainsSequence(prg, [0xA9, 0x06, 0x8D, 0x01, 0x02]), "sprite_draw should write the first compiled sprite tile to OAM.");
        Assert.True(ContainsSequence(prg, [0xA9, 0x02, 0x8D, 0x02, 0x02]), "sprite_draw should lower portable palette slot 2 to NES OAM attributes.");
        Assert.True(ContainsSequence(prg, [0xA9, 0x18, 0x8D, 0x03, 0x02]), "sprite_draw should write logical x to NES OAM.");
        Assert.True(ContainsSequence(prg, [0xA9, 0x02, 0x8D, 0x14, 0x40]), "sprite_draw should DMA the OAM shadow page after writing logical sprites.");
    }

    [Fact]
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
                              void main() {
                                  sprite_asset(hero, "hero.nes.json");
                                  while (true) {
                                      sprite_draw(hero, 24, 32, 0, 0, 4);
                                  }
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => NesRomCompiler.CompileSource(source, baseDirectory));

        Assert.Contains("Target 'nes' supports sprite palette slots 0..3, but slot 4 was requested.", exception.Message);
    }

    [Fact]
    public void Rejects_logical_sprite_assets_that_exceed_nes_sprite_count()
    {
        var rows = Enumerable.Repeat(new string('1', 65 * 8), 8);
        var rowJson = string.Join(",", rows.Select(row => $"\"{row}\""));
        var baseDirectory = WriteSpriteAsset(
            "wide.nes.json",
            "{\"platforms\":{\"nes\":{\"frames\":[[" + rowJson + "]]}}}");

        const string source = """
                              void main() {
                                  sprite_asset(wide, "wide.nes.json");
                                  while (true) {
                                      sprite_draw(wide, 0, 0, 0);
                                  }
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => NesRomCompiler.CompileSource(source, baseDirectory));

        Assert.Contains("NES sprite asset needs 65 hardware sprites, but the hardware limit is 64.", exception.Message);
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

    private static string WriteSpriteAsset(string fileName, string contents)
    {
        var directory = Path.Combine(Path.GetTempPath(), "RetroSharp.NES.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, fileName), contents);
        return directory;
    }

    private static bool ContainsSequence(IReadOnlyList<byte> bytes, IReadOnlyList<byte> sequence)
    {
        for (var i = 0; i <= bytes.Count - sequence.Count; i++)
        {
            var match = true;
            for (var j = 0; j < sequence.Count; j++)
            {
                if (bytes[i + j] != sequence[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return true;
            }
        }

        return false;
    }
}
