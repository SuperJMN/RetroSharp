namespace RetroSharp.NES.Tests;

using RetroSharp.NES;
using Xunit;

public sealed class NesMusicTests
{
    [Fact]
    public void Rejects_bgm_until_nes_audio_lowering_exists()
    {
        const string source = """
                              void main() {
                                  audio.Init();
                                  music.Play(stage_theme);
                                  audio.Update();
                                  return;
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => NesRomCompiler.CompileSource(source));

        Assert.Equal("Target 'nes' does not support BGM playback yet.", exception.Message);
    }
}
