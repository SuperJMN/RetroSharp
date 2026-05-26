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
}
