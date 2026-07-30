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
    public void Compiles_audio_update_library_helper_over_game_boy_intrinsic_like_sdk_operation()
    {
        const string sdkSource = """
                                 void Main() {
                                     Audio.Update();
                                 }
                                 """;

        const string intrinsicSource = """
                                       [target("gb")]
                                       [intrinsic("audio_update")]
                                       extern void gb_audio_update();

                                       class Audio {
                                           static inline void Update() {
                                               gb_audio_update();
                                           }
                                       }

                                       void Main() {
                                           Audio.Update();
                                       }
                                       """;

        Assert.Equal(GameBoyRomCompiler.CompileSource(sdkSource), GameBoyRomCompiler.CompileSource(intrinsicSource));
    }

    [Fact]
    public void Audio_init_via_library_helper_is_byte_identical_gb()
    {
        const string direct = """
                              void Main() {
                                  Audio.Init();
                                  Audio.Update();
                              }
                              """;
        const string library = """
                               void Main() {
                                   Audio.Init();
                                   Audio.Update();
                               }
                               """;

        var sdkLibrary = SdkLibrarySource.ForTarget(GameBoyTarget.Intrinsics);

        Assert.Contains("[intrinsic(\"audio_init\")]", sdkLibrary, StringComparison.Ordinal);
        Assert.Equal(GameBoyRomCompiler.CompileSource(direct), GameBoyRomCompiler.CompileSource(library));
    }

    [Fact]
    public void Injected_game_boy_audio_update_helper_keeps_surface_byte_identical()
    {
        const string source = """
                              void Main() {
                                  Audio.Init();
                                  Audio.Update();
                              }
                              """;
        var explicitLibrarySource = SdkLibrarySource.Merge(
            GameBoyTarget.Intrinsics,
            source,
            SdkLibraryImportMode.ExplicitOnly,
            libraryImportPaths: [SdkImportResolver.Portable2D]);
        var library = SdkLibrarySource.ForTarget(GameBoyTarget.Intrinsics);

        Assert.Contains("class RetroSharp_Portable2D_Audio", library, StringComparison.Ordinal);
        Assert.Equal(GameBoyRomCompiler.CompileSource(explicitLibrarySource), GameBoyRomCompiler.CompileSource(source));
    }

    [Fact]
    public void GameBoy_runner_declares_and_ticks_background_music()
    {
        var baseDirectory = RunnerSample.Directory;
        var source = RunnerSample.FlattenedSource();

        Assert.Contains("""Music.Asset(runner_theme, "assets/music/runner.vgz");""", source);
        Assert.Contains("Audio.Init();", source);
        Assert.Contains("Music.Play(runner_theme);", source);

        var vblankStart = source.IndexOf("Video.WaitVBlank();", StringComparison.Ordinal);
        var audioUpdate = source.IndexOf("Audio.Update();", StringComparison.Ordinal);
        var cameraApply = source.IndexOf("Camera.Apply();", StringComparison.Ordinal);
        var draw = source.IndexOf("Sprite.Draw(mario_player, screenX, screenY, player.displayFrame, player.displayFlipX, 0);", StringComparison.Ordinal);
        Assert.True(vblankStart >= 0);
        Assert.True(audioUpdate > vblankStart, "Runner should tick the music runtime once after VBlank starts.");
        Assert.True(draw < cameraApply, "Runner should write OAM before other VBlank work can run long on real hardware.");
        Assert.True(audioUpdate > cameraApply, "Runner should tick music once per frame after timing-sensitive presentation work.");

        var operations = GameBoyRomCompiler.CollectSdkAudioOperations(RunnerSample.CompiledSource(), baseDirectory);
        Assert.Contains(operations, operation => operation is SdkAudioOperation.InitializeAudio);
        Assert.Contains(operations, operation => operation is SdkAudioOperation.PlayMusic { ThemeId: "runner_theme" });
        Assert.Contains(operations, operation => operation is SdkAudioOperation.UpdateAudio);

        var rom = GameBoyRomCompiler.CompileSource(RunnerSample.CompiledSource(), baseDirectory);
        AssertRunnerMbc1Rom(rom);
        Assert.True(ContainsSequence(rom, [0x3E, 0x80, 0xE0, 0x26]), "Runner BGM should enable NR52.");
        Assert.True(ContainsSequence(rom, [0xE2]), "Runner gbapu playback should write dynamic APU register offsets through LDH (C),A during Audio.Update.");
    }

}
