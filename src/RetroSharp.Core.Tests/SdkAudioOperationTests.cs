namespace RetroSharp.Core.Tests;

using RetroSharp.Core.Sdk;
using RetroSharp.Core.Targeting;
using Xunit;

public sealed class SdkAudioOperationTests
{
    [Fact]
    public void Audio_operations_keep_semantic_sdk_data()
    {
        SdkAudioOperation[] operations =
        [
            new SdkAudioOperation.InitializeAudio(),
            new SdkAudioOperation.PlayMusic("stage_theme"),
            new SdkAudioOperation.PlaySoundEffect("jump_sfx"),
            new SdkAudioOperation.UpdateAudio(),
            new SdkAudioOperation.StopMusic(),
        ];

        Assert.IsType<SdkAudioOperation.InitializeAudio>(operations[0]);
        var play = Assert.IsType<SdkAudioOperation.PlayMusic>(operations[1]);
        Assert.Equal("stage_theme", play.ThemeId);
        var sfx = Assert.IsType<SdkAudioOperation.PlaySoundEffect>(operations[2]);
        Assert.Equal("jump_sfx", sfx.SoundId);
        Assert.IsType<SdkAudioOperation.UpdateAudio>(operations[3]);
        Assert.IsType<SdkAudioOperation.StopMusic>(operations[4]);
    }

    [Fact]
    public void Validator_accepts_game_boy_bgm_operations()
    {
        var capabilities = new TargetAudioCapabilities(
            Name: "gb",
            SupportsBgm: true,
            SupportedMusicFormats: ["uge"],
            SupportsSfx: true,
            SupportedSfxFormats: ["vgm"]);

        SdkAudioOperationValidator.Validate(capabilities, new SdkAudioOperation.InitializeAudio());
        SdkAudioOperationValidator.Validate(capabilities, new SdkAudioOperation.PlayMusic("stage_theme"));
        SdkAudioOperationValidator.Validate(capabilities, new SdkAudioOperation.PlaySoundEffect("jump_sfx"));
        SdkAudioOperationValidator.Validate(capabilities, new SdkAudioOperation.UpdateAudio());
        SdkAudioOperationValidator.Validate(capabilities, new SdkAudioOperation.StopMusic());
    }

    [Fact]
    public void Validator_rejects_bgm_on_targets_without_audio_lowering()
    {
        var capabilities = new TargetAudioCapabilities(
            Name: "nes",
            SupportsBgm: false,
            SupportedMusicFormats: []);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            SdkAudioOperationValidator.Validate(capabilities, new SdkAudioOperation.PlayMusic("stage_theme")));

        Assert.Equal("Target 'nes' does not support BGM playback yet.", exception.Message);
    }

    [Fact]
    public void Validator_rejects_sfx_on_targets_without_sfx_lowering()
    {
        var capabilities = new TargetAudioCapabilities(
            Name: "gb",
            SupportsBgm: true,
            SupportedMusicFormats: ["vgm"],
            SupportsSfx: false,
            SupportedSfxFormats: []);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            SdkAudioOperationValidator.Validate(capabilities, new SdkAudioOperation.PlaySoundEffect("jump_sfx")));

        Assert.Equal("Target 'gb' does not support SFX playback yet.", exception.Message);
    }
}
