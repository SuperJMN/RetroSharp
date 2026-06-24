namespace RetroSharp.NES.Tests;

using RetroSharp.NES;
using Xunit;

public sealed class NesMusicTests
{
    [Fact]
    public void Compiles_bgm_calls_as_noop_until_nes_audio_lowering_exists()
    {
        const string source = """
                              void main() {
                                  music.Asset(stage_theme, "samples/runner/music/free_06_delight.uge");
                                  audio.Init();
                                  music.Play(stage_theme);
                                  audio.Update();
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source, RepoRoot());

        Assert.Equal(40976, rom.Length);
        Assert.Equal((byte)'N', rom[0]);
        Assert.Equal((byte)'E', rom[1]);
        Assert.Equal((byte)'S', rom[2]);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RetroSharp.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
               ?? throw new InvalidOperationException("Could not locate RetroSharp repository root.");
    }
}
