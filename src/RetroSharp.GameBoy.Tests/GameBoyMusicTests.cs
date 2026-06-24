namespace RetroSharp.GameBoy.Tests;

using System.Buffers.Binary;
using System.Text.Json;
using RetroSharp.Core.Sdk;
using RetroSharp.GameBoy;
using Xunit;

public sealed class GameBoyMusicTests
{
    [Fact]
    public void Compiles_gbapu_trace_music_asset_to_game_boy_rom()
    {
        var baseDirectory = WriteGbApuTrace(
            "stage.gbapu.json",
            """
            {
              "format": "retrosharp.gbapu.v1",
              "clockHz": 4194304,
              "framesPerSecond": 60,
              "durationCycles": 140448,
              "loopCycle": 0,
              "metadata": {
                "title": "Trace Fixture"
              },
              "events": [
                { "deltaCycles": 0, "address": "FF24", "value": "77" },
                { "deltaCycles": 0, "address": "FF25", "value": "FF" },
                { "deltaCycles": 70224, "address": "FF12", "value": "F0" },
                { "deltaCycles": 0, "address": "FF14", "value": "87" },
                { "deltaCycles": 0, "address": "FF30", "value": "12" }
              ]
            }
            """);

        const string source = """
                              void main() {
                                  video.Init();
                                  music.Asset(stage_theme, "stage.gbapu.json");
                                  audio.Init();
                                  music.Play(stage_theme);
                                  loop {
                                      video.WaitVBlank();
                                      audio.Update();
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source, baseDirectory);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0xE2]), "GBAPU playback should write dynamic high-RAM APU register offsets through LDH (C),A.");
        Assert.True(ContainsSequence(rom, [0x24, 0x77]), "Compiled trace should preserve NR50 writes.");
        Assert.True(ContainsSequence(rom, [0x25, 0xFF]), "Compiled trace should preserve NR51 writes.");
        Assert.True(ContainsSequence(rom, [0x30, 0x12]), "Compiled trace should preserve wave RAM writes.");
    }

    [Fact]
    public void Compiles_gbapu_trace_to_grouped_stream_with_safe_deduplication_and_wave_blocks()
    {
        var directory = CreateTempDirectory();
        var path = Path.Combine(directory, "stage.gbapu.json");
        File.WriteAllText(
            path,
            """
            {
              "format": "retrosharp.gbapu.v1",
              "clockHz": 4194304,
              "framesPerSecond": 60,
              "durationCycles": 70224,
              "loopCycle": 0,
              "events": [
                { "deltaCycles": 0, "address": "FF24", "value": "77" },
                { "deltaCycles": 0, "address": "FF24", "value": "77" },
                { "deltaCycles": 0, "address": "FF14", "value": "80" },
                { "deltaCycles": 0, "address": "FF14", "value": "80" },
                { "deltaCycles": 70224, "address": "FF30", "value": "00" },
                { "deltaCycles": 0, "address": "FF31", "value": "11" },
                { "deltaCycles": 0, "address": "FF32", "value": "22" },
                { "deltaCycles": 0, "address": "FF33", "value": "33" },
                { "deltaCycles": 0, "address": "FF34", "value": "44" },
                { "deltaCycles": 0, "address": "FF35", "value": "55" },
                { "deltaCycles": 0, "address": "FF36", "value": "66" },
                { "deltaCycles": 0, "address": "FF37", "value": "77" },
                { "deltaCycles": 0, "address": "FF38", "value": "88" },
                { "deltaCycles": 0, "address": "FF39", "value": "99" },
                { "deltaCycles": 0, "address": "FF3A", "value": "AA" },
                { "deltaCycles": 0, "address": "FF3B", "value": "BB" },
                { "deltaCycles": 0, "address": "FF3C", "value": "CC" },
                { "deltaCycles": 0, "address": "FF3D", "value": "DD" },
                { "deltaCycles": 0, "address": "FF3E", "value": "EE" },
                { "deltaCycles": 0, "address": "FF3F", "value": "FF" },
                { "deltaCycles": 0, "address": "FF25", "value": "FF" }
              ]
            }
            """);

        var asset = GameBoyMusicAssetCompiler.CompileFromFile("stage_theme", path);

        Assert.Equal(GameBoyMusicAssetKind.ApuTrace, asset.Kind);
        Assert.Equal(
            [
                0x00, 0x00, 0x00,
                0x03, 0x24, 0x77, 0x14, 0x80, 0x14, 0x80, 0x01,
                0x02, 0xFF, 0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x99, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF, 0x25, 0xFF, 0x00,
                0x00,
            ],
            asset.Data);
        Assert.Equal(1, CountRegisterValuePairs(asset.Data, 0x24, 0x77));
        Assert.Equal(2, CountRegisterValuePairs(asset.Data, 0x14, 0x80));
    }

    [Fact]
    public void Compiles_gbapu_loop_offset_to_group_after_loop_cycle_even_when_frame_matches()
    {
        var directory = CreateTempDirectory();
        var path = Path.Combine(directory, "stage.gbapu.json");
        File.WriteAllText(
            path,
            """
            {
              "format": "retrosharp.gbapu.v1",
              "clockHz": 4194304,
              "framesPerSecond": 60,
              "durationCycles": 10,
              "loopCycle": 10,
              "events": [
                { "deltaCycles": 0, "address": "FF24", "value": "77" },
                { "deltaCycles": 10, "address": "FF25", "value": "FF" }
              ]
            }
            """);

        var asset = GameBoyMusicAssetCompiler.CompileFromFile("stage_theme", path);

        Assert.Equal(
            [
                0x00, 0x04, 0x00,
                0x01, 0x24, 0x77, 0x00,
                0x01, 0x25, 0xFF, 0x00,
                0x00,
            ],
            asset.Data);
    }

    [Fact]
    public void Compiles_gbapu_trace_from_music_envelope()
    {
        var baseDirectory = WriteGbApuTrace(
            "music/stage.gbapu.json",
            """
            {
              "format": "retrosharp.gbapu.v1",
              "clockHz": 4194304,
              "framesPerSecond": 60,
              "durationCycles": 70224,
              "loopCycle": 0,
              "events": [
                { "deltaCycles": 0, "address": "FF12", "value": "F0" },
                { "deltaCycles": 0, "address": "FF14", "value": "87" }
              ]
            }
            """);
        File.WriteAllText(
            Path.Combine(baseDirectory, "stage.music.json"),
            """
            {
              "format": "retrosharp.music.v1",
              "platforms": {
                "gb": {
                  "format": "gbapu",
                  "path": "music/stage.gbapu.json"
                }
              }
            }
            """);

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

        var rom = GameBoyRomCompiler.CompileSource(source, baseDirectory);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x12, 0xF0]));
        Assert.True(ContainsSequence(rom, [0x14, 0x87]));
    }

    [Fact]
    public void Gbapu_runtime_continues_zero_wait_event_bursts_in_the_same_frame()
    {
        var baseDirectory = WriteGbApuTrace(
            "stage.gbapu.json",
            """
            {
              "format": "retrosharp.gbapu.v1",
              "clockHz": 4194304,
              "framesPerSecond": 60,
              "durationCycles": 70224,
              "loopCycle": 0,
              "events": [
                { "deltaCycles": 0, "address": "FF12", "value": "F0" },
                { "deltaCycles": 0, "address": "FF13", "value": "05" },
                { "deltaCycles": 0, "address": "FF14", "value": "87" }
              ]
            }
            """);

        const string source = """
                              void main() {
                                  video.Init();
                                  music.Asset(stage_theme, "stage.gbapu.json");
                                  audio.Init();
                                  music.Play(stage_theme);
                                  loop {
                                      video.WaitVBlank();
                                      audio.Update();
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source, baseDirectory);

        Assert.True(
            ContainsSequence(
                rom,
                [
                    0xEA, 0xFC, 0xC0,             // store group wait in MusicTick
                    0x23,                         // advance past wait byte
                    0x7D, 0xEA, 0xFF, 0xC0,       // store current pointer low
                    0x7C, 0xEA, 0x00, 0xC1,       // store current pointer high
                    0xFA, 0xFC, 0xC0,             // reload MusicTick after pointer store clobbers A
                    0xFE, 0x00, 0xCA,             // continue same-frame burst when wait is zero
                ]),
            "GBAPU playback must test the stored wait byte after saving the current pointer; otherwise every zero-wait group costs at least one frame.");
    }

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

    private static string WriteGbApuTrace(string relativePath, string json)
    {
        var directory = CreateTempDirectory();
        var path = Path.Combine(directory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? directory);
        File.WriteAllText(path, json);
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

    private static int CountRegisterValuePairs(IReadOnlyList<byte> bytes, byte register, byte value)
    {
        var count = 0;
        for (var i = 0; i < bytes.Count - 1; i++)
        {
            if (bytes[i] == register && bytes[i + 1] == value)
            {
                count++;
            }
        }

        return count;
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
