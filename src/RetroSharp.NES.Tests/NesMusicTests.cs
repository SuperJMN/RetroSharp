namespace RetroSharp.NES.Tests;

using System.IO.Compression;
using RetroSharp.NES;
using RetroSharp.Sdk;
using Xunit;

public sealed class NesMusicTests
{
    [Fact]
    public void Compiles_vgz_vgm_input_to_compact_nes_apu_trace()
    {
        var directory = CreateTempDirectory();
        WriteVgmGzip(
            Path.Combine(directory, "stage.nes.vgz"),
            command: 0xB4,
            [0x00, 0x30],
            waitSamples: 735,
            [0x04, 0x7F]);

        var asset = NesMusicAssetCompiler.CompileFromFile("stage_theme", Path.Combine(directory, "stage.vgz"));

        Assert.Equal("stage_theme", asset.Name);
        Assert.NotEmpty(asset.Data);
        Assert.True(ContainsSequence(asset.Data, [0x00, 0x30]), "Compiled NES trace should keep pulse 1 writes.");
        Assert.True(ContainsSequence(asset.Data, [0x04, 0x7F]), "Compiled NES trace should keep pulse 2 writes.");
    }

    [Fact]
    public void Compiles_vgm_bgm_calls_to_nes_apu_runtime()
    {
        var directory = CreateTempDirectory();
        WriteVgmGzip(
            Path.Combine(directory, "stage.nes.vgz"),
            command: 0xB4,
            [0x00, 0x30],
            waitSamples: 735,
            [0x04, 0x7F]);

        const string source = """
                              void main() {
                                  Music.Asset(stage_theme, "stage.vgz");
                                  Audio.Init();
                                  Music.Play(stage_theme);
                                  Audio.Update();
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source, directory);

        Assert.Equal(40976, rom.Length);
        Assert.Equal((byte)'N', rom[0]);
        Assert.Equal((byte)'E', rom[1]);
        Assert.Equal((byte)'S', rom[2]);
        Assert.True(NesTarget.AudioCapabilities.SupportsBgm);
        Assert.False(NesTarget.AudioCapabilities.AllowsBgmNoOp);
        Assert.Contains("vgm", NesTarget.AudioCapabilities.SupportedMusicFormats);
        Assert.True(ContainsSequence(rom, [0xA9, 0x0F, 0x8D, 0x15, 0x40]), "Audio.Init should enable NES pulse/triangle/noise channels through $4015.");
        Assert.True(ContainsSequence(rom, [0x8D, 0x00, 0x40]), "Audio.Update should be able to write pulse 1 register $4000.");
        Assert.True(ContainsSequence(rom, [0x8D, 0x04, 0x40]), "Audio.Update should be able to write pulse 2 register $4004.");
    }

    [Fact]
    public void Music_play_stop_via_library_helpers_are_byte_identical_nes()
    {
        var directory = CreateTempDirectory();
        WriteVgmGzip(
            Path.Combine(directory, "stage.nes.vgz"),
            command: 0xB4,
            [0x00, 0x30],
            waitSamples: 735,
            [0x04, 0x7F]);

        const string direct = """
                              void main() {
                                  music_asset(stage_theme, "stage.vgz");
                                  audio_init();
                                  music_play(stage_theme);
                                  audio_update();
                                  music_stop();
                                  return;
                              }
                              """;
        const string library = """
                               void main() {
                                   Music.Asset(stage_theme, "stage.vgz");
                                   Audio.Init();
                                   Music.Play(stage_theme);
                                   Audio.Update();
                                   Music.Stop();
                                   return;
                               }
                               """;

        var sdkLibrary = SdkLibrarySource.ForTarget(NesTarget.Intrinsics);

        Assert.Contains("class Music", sdkLibrary, StringComparison.Ordinal);
        Assert.Contains("[intrinsic(\"music_play\")]", sdkLibrary, StringComparison.Ordinal);
        Assert.Contains("[intrinsic(\"music_stop\")]", sdkLibrary, StringComparison.Ordinal);
        Assert.Equal(
            NesRomCompiler.CompileSource(direct, directory),
            NesRomCompiler.CompileSource(library, directory));
    }

    private static void WriteVgmGzip(string path, byte command, byte[] firstPayload, int waitSamples, byte[] secondPayload)
    {
        var commands = new List<byte> { command };
        commands.AddRange(firstPayload);
        commands.Add(0x61);
        commands.Add((byte)(waitSamples & 0xFF));
        commands.Add((byte)(waitSamples >> 8));
        commands.Add(command);
        commands.AddRange(secondPayload);
        commands.Add(0x66);

        var bytes = new byte[0xC0 + commands.Count];
        bytes[0] = (byte)'V';
        bytes[1] = (byte)'g';
        bytes[2] = (byte)'m';
        bytes[3] = (byte)' ';
        WriteUInt32(bytes, 0x08, 0x00000161);
        WriteUInt32(bytes, 0x18, (uint)(waitSamples * 2));
        WriteUInt32(bytes, 0x34, 0x8C);
        WriteUInt32(bytes, 0x84, 1_789_773);
        commands.CopyTo(bytes, 0xC0);
        WriteUInt32(bytes, 0x04, (uint)(bytes.Length - 4));

        using var file = File.Create(path);
        using var gzip = new GZipStream(file, CompressionLevel.SmallestSize);
        gzip.Write(bytes);
    }

    private static void WriteUInt32(byte[] bytes, int offset, uint value)
    {
        bytes[offset] = (byte)(value & 0xFF);
        bytes[offset + 1] = (byte)((value >> 8) & 0xFF);
        bytes[offset + 2] = (byte)((value >> 16) & 0xFF);
        bytes[offset + 3] = (byte)(value >> 24);
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

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "retrosharp-nes-music-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
