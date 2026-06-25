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
    public void Compiles_large_music_resource_to_banked_game_boy_rom_without_source_bank_calls()
    {
        var directory = CreateTempDirectory();
        var musicPath = Path.Combine(directory, "large.gbapu");
        WriteLargeGbApuTrace(musicPath, frameCount: 12000);

        const string source = """
                              void main() {
                                  video.Init();
                                  music.Asset(stage_theme, "large.gbapu");
                                  audio.Init();
                                  music.Play(stage_theme);
                                  loop {
                                      video.WaitVBlank();
                                      audio.Update();
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source, directory);

        Assert.Equal(65536, rom.Length);
        Assert.Equal(0x01, rom[0x0147]);            // MBC1 without RAM
        Assert.Equal(0x01, rom[0x0148]);            // 64 KiB ROM
        Assert.Equal(HeaderChecksum(rom), rom[0x014D]);
        Assert.Equal(0x02, rom[0x4000]);            // GBAPU v2 marker at the start of bank 1
        Assert.True(ContainsSequence(rom, [0xEA, 0x00, 0x20]), "Banked playback should select ROM banks through MBC writes.");
    }

    [Fact]
    public void Rom_only_gbapu_playback_emits_the_source_trace_register_writes_in_order()
    {
        // Control test: a ROM-only (non-banked) program must reproduce the source trace exactly.
        // This validates the SM83 test interpreter against the known-correct non-banked path, so a
        // divergence on the banked path can only mean a banking bug, not an interpreter artifact.
        var directory = CreateTempDirectory();
        var musicPath = Path.Combine(directory, "small.gbapu");
        const int frameCount = 40;
        WriteLargeGbApuTrace(musicPath, frameCount);

        var rom = GameBoyRomCompiler.CompileSource(MinimalPlaybackSource("small.gbapu"), directory);

        Assert.Equal(32768, rom.Length);            // ROM-only cartridge
        Assert.Equal(0x00, rom[0x0147]);            // ROM only

        var cpu = new GameBoyTestCpu(rom);
        var played = cpu.RunUntilRegisterWrites(0xFF14, frameCount, maxInstructions: 5_000_000);

        Assert.Equal(ExpectedTriggerSequence(frameCount), played);
    }

    [Fact]
    public void Banked_gbapu_playback_emits_the_source_trace_across_rom_bank_boundaries()
    {
        // A single music asset large enough to span several MBC1 ROM banks must keep playing the
        // source trace in order as sequential playback crosses 16 KiB bank boundaries. The banked
        // runtime has to track the order-stream bank independently from the bank used to resolve
        // pooled group bodies; conflating them reads later order entries from the wrong bank.
        var directory = CreateTempDirectory();
        var musicPath = Path.Combine(directory, "large.gbapu");
        const int frameCount = 12000;
        WriteLargeGbApuTrace(musicPath, frameCount);

        var rom = GameBoyRomCompiler.CompileSource(MinimalPlaybackSource("large.gbapu"), directory);

        Assert.Equal(65536, rom.Length);            // banked MBC1 cartridge
        Assert.Equal(0x01, rom[0x0147]);            // MBC1

        // 11000 frames cross both the bank 1->2 boundary (~frame 5333) and the bank 2->3 boundary
        // (~frame 10793), exercising repeated transparent bank switches during sequential playback.
        const int checkedFrames = 11000;
        var cpu = new GameBoyTestCpu(rom);
        var played = cpu.RunUntilRegisterWrites(0xFF14, checkedFrames, maxInstructions: 200_000_000);

        Assert.Equal(ExpectedTriggerSequence(checkedFrames), played);
    }

    [Fact]
    public void Multiple_music_themes_can_be_declared_and_switched_at_runtime()
    {
        // Two distinct themes (told apart by the APU register they drive). The program starts theme A,
        // then switches to theme B via music.Play(...). Both must play their own data in order.
        var directory = CreateTempDirectory();
        WriteLargeGbApuTrace(Path.Combine(directory, "theme_a.gbapu"), frameCount: 200, register: 0xFF14);
        WriteLargeGbApuTrace(Path.Combine(directory, "theme_b.gbapu"), frameCount: 200, register: 0xFF19);

        const string source = """
                              void main() {
                                  video.Init();
                                  music.Asset(theme_a, "theme_a.gbapu");
                                  music.Asset(theme_b, "theme_b.gbapu");
                                  audio.Init();
                                  music.Play(theme_a);
                                  u8 ticks = 0;
                                  u8 switched = 0;
                                  loop {
                                      video.WaitVBlank();
                                      audio.Update();
                                      ticks = ticks + 1;
                                      if (ticks == 80) {
                                          if (switched == 0) {
                                              music.Play(theme_b);
                                              switched = 1;
                                          }
                                      }
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source, directory);
        Assert.Equal(32768, rom.Length);            // both small themes still fit a ROM-only cartridge

        var cpu = new GameBoyTestCpu(rom);
        cpu.RunUntilRegisterWrites(0xFF19, count: 50, maxInstructions: 5_000_000);

        var themeA = cpu.ApuWrites.Where(w => w.Register == 0xFF14).Select(w => w.Value).ToList();
        var themeB = cpu.ApuWrites.Where(w => w.Register == 0xFF19).Select(w => w.Value).ToList();

        Assert.Equal(ExpectedTriggerSequence(50), themeA.Take(50).ToList());
        Assert.Equal(ExpectedTriggerSequence(50), themeB.Take(50).ToList());
    }

    [Fact]
    public void Switching_between_two_banked_themes_keeps_each_playing_across_its_own_bank_boundaries()
    {
        // The definitive multi-theme banked case: two large themes each occupy their own MBC1 bank range
        // (theme A in the low banks, theme B above it). The program plays theme A across its own internal
        // bank boundary, switches to theme B, and theme B must then play across *its* boundary from its
        // higher base bank. Each theme drives a different APU register so the two streams stay separable.
        var directory = CreateTempDirectory();
        WriteLargeGbApuTrace(Path.Combine(directory, "theme_a.gbapu"), frameCount: 12000, register: 0xFF14);
        WriteLargeGbApuTrace(Path.Combine(directory, "theme_b.gbapu"), frameCount: 12000, register: 0xFF19);

        // 16-bit frame counter (two u8s) so the switch can happen after theme A has crossed its boundary.
        const string source = """
                              void main() {
                                  video.Init();
                                  music.Asset(theme_a, "theme_a.gbapu");
                                  music.Asset(theme_b, "theme_b.gbapu");
                                  audio.Init();
                                  music.Play(theme_a);
                                  u8 lo = 0;
                                  u8 hi = 0;
                                  u8 switched = 0;
                                  loop {
                                      video.WaitVBlank();
                                      audio.Update();
                                      lo = lo + 1;
                                      if (lo == 0) {
                                          hi = hi + 1;
                                      }
                                      if (hi == 24) {
                                          if (switched == 0) {
                                              music.Play(theme_b);
                                              switched = 1;
                                          }
                                      }
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source, directory);
        Assert.Equal(0x01, rom[0x0147]);            // MBC1
        Assert.True(rom.Length >= 128 * 1024, "Two large themes need several ROM banks each.");

        // Run until theme B has streamed past its own bank 1->2 boundary (~frame 5333).
        var cpu = new GameBoyTestCpu(rom);
        cpu.RunUntilRegisterWrites(0xFF19, count: 7000, maxInstructions: 400_000_000);

        var themeA = cpu.ApuWrites.Where(w => w.Register == 0xFF14).Select(w => w.Value).ToList();
        var themeB = cpu.ApuWrites.Where(w => w.Register == 0xFF19).Select(w => w.Value).ToList();

        // Theme A played in order across its boundary before the switch; theme B does the same afterwards.
        Assert.Equal(ExpectedTriggerSequence(6000), themeA.Take(6000).ToList());
        Assert.Equal(ExpectedTriggerSequence(7000), themeB.Take(7000).ToList());
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
                0x02, 0x20, 0x00, 0x20, 0x00,
                0x03, 0x24, 0x77, 0x14, 0x80, 0x14, 0x80,
                0x02, 0xFF, 0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x99, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF, 0x25, 0xFF,
                0x05, 0x00, 0x01,
                0x0C, 0x00, 0x00,
                0x00, 0x00, 0x00,
            ],
            asset.Data);
        Assert.Equal(1, CountRegisterValuePairs(asset.Data, 0x24, 0x77));
        Assert.Equal(2, CountRegisterValuePairs(asset.Data, 0x14, 0x80));
    }

    [Fact]
    public void Compiles_gbapu_trace_pooling_repeated_group_bodies_once()
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
              "durationCycles": 140448,
              "loopCycle": 0,
              "events": [
                { "deltaCycles": 0, "address": "FF14", "value": "87" },
                { "deltaCycles": 70224, "address": "FF14", "value": "87" }
              ]
            }
            """);

        var asset = GameBoyMusicAssetCompiler.CompileFromFile("stage_theme", path);

        // Both frames produce the same body (a kept trigger write), so the pool stores it once
        // and the two order entries reference the same body offset (0x0005).
        Assert.Equal(
            [
                0x02, 0x08, 0x00, 0x08, 0x00,
                0x01, 0x14, 0x87,
                0x05, 0x00, 0x01,
                0x05, 0x00, 0x01,
                0x00, 0x00, 0x00,
            ],
            asset.Data);
        Assert.Equal(1, CountRegisterValuePairs(asset.Data, 0x14, 0x87));
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
                0x02, 0x0B, 0x00, 0x0E, 0x00,
                0x01, 0x24, 0x77,
                0x01, 0x25, 0xFF,
                0x05, 0x00, 0x00,
                0x08, 0x00, 0x00,
                0x00, 0x00, 0x00,
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
                    0xEA, 0xFC, 0xC0,             // store order-entry wait in MusicTick
                    0x23,                         // advance past wait byte to the next order entry
                    0x7D, 0xEA, 0xFF, 0xC0,       // store current order pointer low
                    0x7C, 0xEA, 0x00, 0xC1,       // store current order pointer high
                ]),
            "GBAPU v2 playback must store the order-entry wait and advance the order pointer before decoding the pooled body.");
        Assert.True(
            ContainsSequence(
                rom,
                [
                    0xFA, 0xFC, 0xC0,             // reload MusicTick after decoding the group body
                    0xFE, 0x00, 0xCA,             // continue same-frame burst when the wait is zero
                ]),
            "GBAPU playback must re-test the stored wait byte; otherwise every zero-wait group costs at least one frame.");
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

    private static void WriteLargeGbApuTrace(string path, int frameCount, ushort register = 0xFF14)
    {
        var events = new List<GameBoyApuTraceEvent>(frameCount);
        for (var frame = 0; frame < frameCount; frame++)
        {
            var delta = frame == 0 ? 0 : 70_224;
            events.Add(new GameBoyApuTraceEvent(delta, register, TriggerValue(frame)));
        }

        var trace = new GameBoyApuTrace(
            4_194_304,
            60,
            DurationCycles: frameCount * 70_224L,
            LoopCycle: 0,
            new GameBoyApuTraceMetadata("Large Trace"),
            events);
        GameBoyApuTraceBinary.Write(path, trace);
    }

    private static byte TriggerValue(int frame) => (byte)(0x80 | (frame & 0x7F));

    private static List<byte> ExpectedTriggerSequence(int frameCount)
    {
        var expected = new List<byte>(frameCount);
        for (var frame = 0; frame < frameCount; frame++)
        {
            expected.Add(TriggerValue(frame));
        }

        return expected;
    }

    private static string MinimalPlaybackSource(string assetFileName) =>
        $$"""
          void main() {
              video.Init();
              music.Asset(stage_theme, "{{assetFileName}}");
              audio.Init();
              music.Play(stage_theme);
              loop {
                  video.WaitVBlank();
                  audio.Update();
              }
          }
          """;

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

    private static byte HeaderChecksum(IReadOnlyList<byte> rom)
    {
        var checksum = 0;
        for (var i = 0x0134; i <= 0x014C; i++)
        {
            checksum = checksum - rom[i] - 1;
        }

        return (byte)checksum;
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
