namespace RetroSharp.GameBoy.Tests;

using RetroSharp.Core.Sdk;
using RetroSharp.Core.Sdk.Tiled;
using RetroSharp.Core.Targeting;
using RetroSharp.GameBoy;
using RetroSharp.Parser;
using RetroSharp.Sdk;
using Xunit;
using static RetroSharp.GameBoy.Tests.GameBoyTestSupport;

public partial class GameBoyRomCompilerTests
{
    [Fact]
    public void Logical_palette_declarations_lower_to_game_boy_palette_registers()
    {
        const string source = """
                              void Main() {
                                  Video.Init();
                                  Palette.Background(0, 0, 1, 2, 3);
                                  Palette.Sprite(0, 0, 0, 1, 3);
                                  Palette.Sprite(1, 0, 3, 2, 1);
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.True(ContainsSequence(rom, [0x3E, 0xE4, 0xE0, 0x47]), "Palette.Background should lower slot 0 to BGP.");
        Assert.True(ContainsSequence(rom, [0x3E, 0xD0, 0xE0, 0x48]), "Palette.Sprite slot 0 should lower to OBP0.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x6C, 0xE0, 0x49]), "Palette.Sprite slot 1 should lower to OBP1.");
    }

    [Fact]
    public void Rejects_logical_sprite_palette_slots_outside_game_boy_capabilities()
    {
        const string source = """
                              void Main() {
                                  Video.Init();
                                  Palette.Sprite(2, 0, 1, 2, 3);
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource(source));

        Assert.Equal("Target 'gb' supports sprite palette slots 0..1, but palette slot 2 was requested.", exception.Message);
    }

    [Fact]
    public void GameBoy_runner_uses_lighter_object_palette_for_player_sprite()
    {
        var source = RunnerSample.FlattenedSource();

        Assert.Contains("Palette.Background(0, 0, 1, 2, 3);", source);
        Assert.Contains("Palette.Sprite(0, 0, 0, 1, 3);", source);
        Assert.DoesNotContain("Palette.Set(", source);
        Assert.DoesNotContain("ObjectPalette.Set(", source);
        Assert.DoesNotContain("ObjectPalette.Set(1, 1);", source);
        Assert.DoesNotContain("ObjectPalette.Set(2, 2);", source);
        Assert.DoesNotContain("ObjectPalette.Set(1, 2);", source);
        Assert.DoesNotContain("ObjectPalette.Set(2, 3);", source);

        var rom = GameBoyRomCompiler.CompileSource(RunnerSample.CompiledSource(), RunnerSample.Directory);

        AssertRunnerMbc1Rom(rom);
        Assert.True(ContainsSequence(rom, [0x3E, 0xD0, 0xE0, 0x48]), "Runner should map sprite tones to OBP0 as 0, 0, 1, 3.");
    }

}
