namespace RetroSharp.GameBoy.Tests;

using System.Buffers.Binary;
using System.Text.Json;
using RetroSharp.Core.Sdk;
using RetroSharp.GameBoy;
using Xunit;

public sealed class GameBoyMusicTests
{
    [Fact]
    public void Collects_audio_sdk_operations_before_game_boy_lowering()
    {
        const string source = """
                              void start() {
                                  music.Play(stage_theme);
                              }

                              void main() {
                                  audio.Init();
                                  start();
                                  loop {
                                      video.WaitVBlank();
                                      audio.Update();
                                  }
                              }
                              """;

        var operations = GameBoyRomCompiler.CollectSdkAudioOperations(source);

        Assert.Collection(
            operations,
            operation => Assert.IsType<SdkAudioOperation.InitializeAudio>(operation),
            operation =>
            {
                var play = Assert.IsType<SdkAudioOperation.PlayMusic>(operation);
                Assert.Equal("stage_theme", play.ThemeId);
            },
            operation => Assert.IsType<SdkAudioOperation.UpdateAudio>(operation));
    }

    [Fact]
    public void Compiles_uge_bgm_resource_to_game_boy_audio_runtime()
    {
        var directory = CreateTempDirectory();
        WriteMusicEnvelope(directory, "stage.music.json", "stage.uge");
        HugeFixture.WriteSong(Path.Combine(directory, "stage.uge"));

        const string source = """
                              void main() {
                                  video.Init();
                                  music.Asset(stage_theme, "stage.music.json");
                                  audio.Init();
                                  music.Play(stage_theme);

                                  loop {
                                      video.WaitVBlank();
                                      audio.Update();
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source, directory);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x3E, 0x80, 0xE0, 0x26]), "audio.Init should enable NR52.");
        Assert.True(ContainsSequence(rom, [0x3E, 0xFF, 0xE0, 0x25]), "audio.Init should route all channels to both terminals through NR51.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x77, 0xE0, 0x24]), "audio.Init should set balanced master volume through NR50.");
        Assert.True(ContainsSequence(rom, [0xE0, 0x12]), "audio.Update should write CH1 envelope NR12.");
        Assert.True(ContainsSequence(rom, [0xE0, 0x14]), "audio.Update should trigger CH1 through NR14.");
    }

    [Fact]
    public void Rejects_uge_timer_playback_with_explicit_error()
    {
        var directory = CreateTempDirectory();
        WriteMusicEnvelope(directory, "stage.music.json", "stage.uge");
        HugeFixture.WriteSong(Path.Combine(directory, "stage.uge"), timerPlayback: true);

        const string source = """
                              void main() {
                                  music.Asset(stage_theme, "stage.music.json");
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource(source, directory));
        Assert.Equal("hUGETracker timer-based tempo is not supported by the Game Boy BGM v1 runtime.", exception.Message);
    }

    [Fact]
    public void Compiles_uge_bgm_with_wave_noise_and_row_volume_effect()
    {
        var directory = CreateTempDirectory();
        WriteMusicEnvelope(directory, "stage.music.json", "stage.uge");
        HugeFixture.WriteSong(Path.Combine(directory, "stage.uge"), includeWave: true, includeNoise: true, includeVolumeEffect: true);

        const string source = """
                              void main() {
                                  video.Init();
                                  music.Asset(stage_theme, "stage.music.json");
                                  audio.Init();
                                  music.Play(stage_theme);

                                  loop {
                                      video.WaitVBlank();
                                      audio.Update();
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source, directory);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0xE0, 0x1A]), "audio.Update should enable CH3 through NR30.");
        Assert.True(ContainsSequence(rom, [0xE0, 0x30]), "audio.Update should write wave RAM.");
        Assert.True(ContainsSequence(rom, [0xE0, 0x21]), "audio.Update should write CH4 envelope NR42.");
        Assert.True(ContainsSequence(rom, [0xE0, 0x23]), "audio.Update should trigger CH4 through NR44.");
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "retrosharp-music-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void WriteMusicEnvelope(string directory, string name, string ugePath)
    {
        var json = JsonSerializer.Serialize(new
        {
            format = "retrosharp.music.v1",
            platforms = new
            {
                gb = new
                {
                    format = "uge",
                    path = ugePath,
                },
            },
        });

        File.WriteAllText(Path.Combine(directory, name), json);
    }

    private static bool ContainsSequence(IReadOnlyList<byte> bytes, IReadOnlyList<byte> sequence)
    {
        for (var i = 0; i <= bytes.Count - sequence.Count; i++)
        {
            var found = true;
            for (var j = 0; j < sequence.Count; j++)
            {
                if (bytes[i + j] != sequence[j])
                {
                    found = false;
                    break;
                }
            }

            if (found)
            {
                return true;
            }
        }

        return false;
    }

    private static class HugeFixture
    {
        public static void WriteSong(
            string path,
            bool timerPlayback = false,
            bool includeWave = false,
            bool includeNoise = false,
            bool includeVolumeEffect = false)
        {
            using var stream = File.Create(path);
            using var writer = new BinaryWriter(stream);

            writer.Write((uint)6);
            WriteShortString(writer, "RetroSharp Test");
            WriteShortString(writer, "RetroSharp");
            WriteShortString(writer, string.Empty);

            for (var i = 0; i < 15; i++) WriteDutyInstrument(writer, i == 0);
            for (var i = 0; i < 15; i++) WriteWaveInstrument(writer, includeWave && i == 0);
            for (var i = 0; i < 15; i++) WriteNoiseInstrument(writer, includeNoise && i == 0);

            for (var wave = 0; wave < 16; wave++)
            {
                for (var i = 0; i < 32; i++) writer.Write((byte)(wave == 0 ? i & 0x0F : 0));
            }

            writer.Write((uint)4);                  // ticks per row
            writer.Write(timerPlayback);            // timer based tempo enabled
            writer.Write((uint)0);                  // timer divider
            writer.Write((uint)4);                  // patterns

            WritePattern(writer, 0, note: 24, instrument: 1, effectCode: includeVolumeEffect ? 12 : 0, effectParameter: includeVolumeEffect ? 5 : 0);
            WritePattern(writer, 1, note: 90, instrument: 0);
            WritePattern(writer, 2, note: 36, instrument: includeWave ? 1 : 0);
            WritePattern(writer, 3, note: 44, instrument: includeNoise ? 1 : 0);

            WriteOrder(writer, [0]);
            WriteOrder(writer, []);
            WriteOrder(writer, includeWave ? [2] : []);
            WriteOrder(writer, includeNoise ? [3] : []);

            for (var i = 0; i < 16; i++) WriteString(writer, string.Empty);
        }

        private static void WriteDutyInstrument(BinaryWriter writer, bool audible)
        {
            writer.Write((uint)0);
            WriteShortString(writer, audible ? "Lead" : string.Empty);
            writer.Write((uint)0);
            writer.Write(false);
            writer.Write((byte)(audible ? 15 : 0));
            writer.Write((uint)1);
            writer.Write((byte)0);
            writer.Write((uint)0);
            writer.Write((uint)0);
            writer.Write((uint)0);
            writer.Write((byte)0x80);
            writer.Write((uint)0);
            writer.Write((uint)0);
            writer.Write((uint)0);
            writer.Write(false);
            WriteSubpatternRows(writer);
        }

        private static void WriteWaveInstrument(BinaryWriter writer, bool audible)
        {
            writer.Write((uint)1);
            WriteShortString(writer, audible ? "Wave" : string.Empty);
            writer.Write((uint)0);
            writer.Write(false);
            writer.Write((byte)0);
            writer.Write((uint)0);
            writer.Write((byte)0);
            writer.Write((uint)0);
            writer.Write((uint)0);
            writer.Write((uint)0);
            writer.Write((byte)0);
            writer.Write((uint)(audible ? 1 : 0));
            writer.Write((uint)0);
            writer.Write((uint)0);
            writer.Write(false);
            WriteSubpatternRows(writer);
        }

        private static void WriteNoiseInstrument(BinaryWriter writer, bool audible)
        {
            writer.Write((uint)2);
            WriteShortString(writer, audible ? "Noise" : string.Empty);
            writer.Write((uint)0);
            writer.Write(false);
            writer.Write((byte)(audible ? 15 : 0));
            writer.Write((uint)1);
            writer.Write((byte)0);
            writer.Write((uint)0);
            writer.Write((uint)0);
            writer.Write((uint)0);
            writer.Write((byte)0);
            writer.Write((uint)0);
            writer.Write((uint)0);
            writer.Write((uint)0);
            writer.Write(false);
            WriteSubpatternRows(writer);
        }

        private static void WriteSubpatternRows(BinaryWriter writer)
        {
            for (var i = 0; i < 64; i++)
            {
                writer.Write((uint)90);
                writer.Write((uint)0);
                writer.Write((uint)0);
                writer.Write((uint)0);
                writer.Write((byte)0);
            }
        }

        private static void WritePattern(
            BinaryWriter writer,
            int index,
            int note,
            int instrument,
            int effectCode = 0,
            int effectParameter = 0)
        {
            writer.Write((uint)index);
            for (var row = 0; row < 64; row++)
            {
                writer.Write((uint)(row == 0 ? note : 90));
                writer.Write((uint)(row == 0 ? instrument : 0));
                writer.Write((uint)0);
                writer.Write((uint)(row == 0 ? effectCode : 0));
                writer.Write((byte)(row == 0 ? effectParameter : 0));
            }
        }

        private static void WriteOrder(BinaryWriter writer, IReadOnlyCollection<int> orders)
        {
            writer.Write((uint)(orders.Count + 1));
            foreach (var order in orders)
            {
                writer.Write((uint)order);
            }

            writer.Write((uint)0);
        }

        private static void WriteShortString(BinaryWriter writer, string value)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(value);
            writer.Write((byte)bytes.Length);
            writer.Write(bytes);
            writer.Write(new byte[255 - bytes.Length]);
        }

        private static void WriteString(BinaryWriter writer, string value)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(value);
            writer.Write((uint)bytes.Length);
            writer.Write(bytes);
            writer.Write((byte)0);
        }
    }
}
