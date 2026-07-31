namespace RetroSharp.GameBoy.Tests;

using RetroSharp.GameBoy;
using Xunit;

public partial class GameBoyRomCompilerTests
{
    [Fact]
    public void Comments_do_not_affect_game_boy_rom_bytes()
    {
        const string withoutComments = """
                                       void Main() {
                                           Video.Init();
                                           Palette.Background(0, 0, 1, 2, 3);
                                       }
                                       """;
        const string withComments = """
                                    // Source-only documentation.
                                    void Main() {
                                        Video.Init(); /* zero-cost comment */
                                        Palette.Background(0, 0, 1, 2, 3);
                                    }
                                    """;

        Assert.Equal(
            GameBoyRomCompiler.CompileSource(withoutComments),
            GameBoyRomCompiler.CompileSource(withComments));
    }

    [Fact]
    public void Portable2D_import_does_not_affect_game_boy_rom_bytes()
    {
        const string implicitSdk = """
                                   void Main() {
                                       Video.WaitVBlank();
                                   }
                                   """;
        const string explicitSdk = """
                                   import RetroSharp.Portable2D;

                                   void Main() {
                                       Video.WaitVBlank();
                                   }
                                   """;

        Assert.Equal(
            GameBoyRomCompiler.CompileSource(implicitSdk),
            GameBoyRomCompiler.CompileSource(explicitSdk));
    }
}
