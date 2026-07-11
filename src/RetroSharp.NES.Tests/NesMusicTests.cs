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
                              void Main() {
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
        Assert.True(ContainsSequence(rom, [0x91, 0xE8]), "Audio.Update should write the selected NES APU register through an indirect $40xx pointer.");
    }

    [Fact]
    public void Compacts_repeated_nes_frame_counter_writes_in_bgm()
    {
        var directory = CreateTempDirectory();
        WriteVgmGzipFrames(
            Path.Combine(directory, "stage.nes.vgz"),
            [
                [[0x17, 0xFF], [0x00, 0x30]],
                [[0x17, 0xFF]],
                [[0x17, 0xFF], [0x03, 0x08]],
            ]);

        var asset = NesMusicAssetCompiler.CompileFromFile("stage_theme", Path.Combine(directory, "stage.vgz"));

        Assert.Equal(1, CountSequence(asset.Data, [0x17, 0xFF]));
        Assert.True(ContainsSequence(asset.Data, [0x00, 0x30]));
        Assert.True(ContainsSequence(asset.Data, [0x03, 0x08]));
    }


    [Fact]
    public void Compiles_vgz_sfx_calls_into_nes_rom_played_by_shared_apu_writer()
    {
        var directory = CreateTempDirectory();
        WriteVgmGzip(
            Path.Combine(directory, "jump.nes.vgz"),
            command: 0xB4,
            [0x03, 0x08],
            waitSamples: 735,
            [0x00, 0x30]);

        const string source = """
                              void Main() {
                                  Sfx.Asset(jump_sfx, "jump.vgz");
                                  Audio.Init();
                                  Sfx.Play(jump_sfx);
                                  Audio.Update();
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source, directory);

        Assert.Equal(40976, rom.Length);
        Assert.True(ContainsSequence(rom, [0x03, 0x08]), "Compiled NES SFX trace should keep pulse 1 trigger writes.");
        Assert.True(ContainsSequence(rom, [0x91, 0xE8]), "Audio.Update should write SFX pulse 1 registers through the shared indirect $40xx writer.");
        // Sfx.Play only arms the SFX engine (STA $0312 = SfxActive); it must not touch the music tick.
        Assert.True(ContainsSequence(rom, [0x8D, 0x12, 0x03]), "Sfx.Play should set the SfxActive flag at $0312.");
        // The BGM body writer shadows its intended pulse 1 values (STA $0313,Y) so the channel can be
        // restored when the effect ends...
        Assert.True(ContainsSequence(rom, [0x99, 0x13, 0x03]), "BGM should shadow pulse 1 writes to $0313,Y.");
        // ...and on SFX end the full pulse 1 state ($4000-$4003) is restored from the shadow via a
        // loop (LDA $0313,X ; STA $4000,X), not just the sweep, so no duty/volume/frequency residue is
        // left on the BGM's channel.
        Assert.True(ContainsSequence(rom, [0xBD, 0x13, 0x03, 0x9D, 0x00, 0x40]), "SFX end should restore pulse 1 $4000-$4003 from the shadow.");
    }

    [Fact]
    public void Compiles_vgm_sfx_asset_to_flat_pulse1_multi_frame_trace()
    {
        var directory = CreateTempDirectory();
        WriteVgmGzipFrames(
            Path.Combine(directory, "jump.nes.vgz"),
            [
                [[0x15, 0x0F], [0x17, 0xFF], [0x00, 0x82], [0x01, 0xA7], [0x02, 0x7C], [0x03, 0x09], [0x10, 0x00]],
                [[0x17, 0xFF], [0x01, 0xF6], [0x00, 0x5F], [0x10, 0x00]],
            ]);

        var asset = NesSoundEffectAssetCompiler.CompileFromFile("jump_sfx", Path.Combine(directory, "jump.vgz"));

        Assert.Equal("jump_sfx", asset.Name);

        // Flat per-frame trace: frame 0 body, frame 1 body (only changed pulse 1 registers), then the
        // 0xFF end-of-effect marker. The ring-out after the last frame is a runtime linger, not stored
        // empty frames; with no $4015 note-off in the source the linger falls back to its default.
        Assert.Equal(
            [
                0x04, 0x00, 0x82, 0x01, 0xA7, 0x02, 0x7C, 0x03, 0x09,
                0x02, 0x01, 0xF6, 0x00, 0x5F,
                0xFF,
            ],
            asset.Data);
        Assert.Equal(24, asset.LingerFrames);
        Assert.False(ContainsSequence(asset.Data, [0x15, 0x0F]), "SFX must not replay channel-enable writes captured from global APU state.");
        Assert.False(ContainsSequence(asset.Data, [0x17, 0xFF]), "SFX must not replay frame-counter writes captured from global APU state.");
        Assert.False(ContainsSequence(asset.Data, [0x10, 0x00]), "SFX must not replay DMC control writes captured from global APU state.");
    }

    [Fact]
    public void Nes_sfx_trace_encodes_gap_frames_as_empty_bodies()
    {
        var directory = CreateTempDirectory();
        WriteVgmGzipFrames(
            Path.Combine(directory, "blip.nes.vgz"),
            [
                [[0x00, 0x82], [0x03, 0x09]],
                [],
                [],
                [[0x00, 0x40]],
            ]);

        var asset = NesSoundEffectAssetCompiler.CompileFromFile("blip", Path.Combine(directory, "blip.vgz"));

        // Frame 0 body, two empty gap frames, frame 3 body, then 0xFF (ring-out is a runtime linger).
        Assert.Equal(
            [
                0x02, 0x00, 0x82, 0x03, 0x09,
                0x00, 0x00,
                0x01, 0x00, 0x40,
                0xFF,
            ],
            asset.Data);
    }

    [Fact]
    public void Nes_sfx_linger_matches_source_note_off_frame()
    {
        var directory = CreateTempDirectory();
        WriteVgmGzipFrames(
            Path.Combine(directory, "beep.nes.vgz"),
            [
                [[0x15, 0x0F], [0x00, 0x82], [0x03, 0x09]], // frame 0: note-on
                [],                                          // frames 1-3: note rings
                [],
                [],
                [[0x15, 0x00]],                              // frame 4: note-off (clears pulse 1 enable)
            ]);

        var asset = NesSoundEffectAssetCompiler.CompileFromFile("beep", Path.Combine(directory, "beep.vgz"));

        // Last register frame is 0; note-off is at frame 4, so the effect lingers frames 1..3 (= 3).
        Assert.Equal([0x02, 0x00, 0x82, 0x03, 0x09, 0xFF], asset.Data);
        Assert.Equal(3, asset.LingerFrames);
    }

    [Fact]
    public void Compiles_dmc_sample_block_and_dmc_register_writes_to_nes_rom()
    {
        var directory = CreateTempDirectory();
        var sample = Enumerable.Range(0, 17).Select(i => (byte)(0xA0 + i)).ToArray();
        var dataBlock = new byte[2 + sample.Length];
        dataBlock[1] = 0xE0;
        sample.CopyTo(dataBlock, 2);
        WriteVgmGzipWithDataBlock(
            Path.Combine(directory, "dmc.nes.vgz"),
            dataBlockType: 0xC2,
            dataBlock: dataBlock,
            command: 0xB4,
            [0x10, 0x0F],
            [0x12, 0x80],
            [0x13, 0x01],
            [0x15, 0x1F]);

        const string source = """
                              void Main() {
                                  Music.Asset(dmc_theme, "dmc.vgz");
                                  Audio.Init();
                                  Music.Play(dmc_theme);
                                  Audio.Update();
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source, directory);
        var sampleOffset = IndexOfSequence(rom, sample);
        Assert.True(sampleOffset >= 16, "Compiled NES ROM should contain relocated DPCM sample bytes.");
        var sampleAddress = 0x8000 + sampleOffset - 16;
        Assert.Equal(0, (sampleAddress - 0xC000) % 64);
        var relocatedAddressRegister = (byte)((sampleAddress - 0xC000) / 64);

        Assert.True(ContainsSequence(rom, [0x91, 0xE8]), "Audio.Update should write DMC registers through the indirect APU pointer.");
        Assert.True(ContainsSequence(rom, [0x10, 0x0F]), "Compiled NES trace should keep DMC control writes.");
        Assert.True(ContainsSequence(rom, [0x12, relocatedAddressRegister]), "Compiled NES trace should rewrite DMC sample address writes to the relocated PRG sample.");
        Assert.True(ContainsSequence(rom, [0x13, 0x01]), "Compiled NES trace should keep DMC sample length writes.");
    }

    [Fact]
    public void Forced_mmc3_tvrom_link_keeps_dpcm_aligned_in_the_fixed_prg_region()
    {
        var directory = CreateTempDirectory();
        var sample = Enumerable.Range(0, 17).Select(i => (byte)(0xB0 + i)).ToArray();
        var dataBlock = new byte[2 + sample.Length];
        dataBlock[1] = 0xE0;
        sample.CopyTo(dataBlock, 2);
        WriteVgmGzipWithDataBlock(
            Path.Combine(directory, "dmc.nes.vgz"),
            dataBlockType: 0xC2,
            dataBlock: dataBlock,
            command: 0xB4,
            [0x10, 0x0F],
            [0x12, 0x80],
            [0x13, 0x01],
            [0x15, 0x1F]);

        const string source = """
                              void Main() {
                                  Music.Asset(dmc_theme, "dmc.vgz");
                                  Audio.Init();
                                  Music.Play(dmc_theme);
                                  Audio.Update();
                                  return;
                              }
                              """;

        var rom = RetroSharp.NES.NesRomCompiler.CompileSourceForMmc3TvromTests(
            source,
            directory,
            sdkLibraryImports: [SdkImportResolver.Portable2D]);
        var sampleOffset = IndexOfSequence(rom, sample);
        var fixedPhysicalOffset = 16 + 6 * 8 * 1_024;

        Assert.InRange(sampleOffset, fixedPhysicalOffset, 16 + 64 * 1_024 - 7);
        var sampleAddress = 0xC000 + sampleOffset - fixedPhysicalOffset;
        Assert.Equal(0, (sampleAddress - 0xC000) % 64);
        var relocatedAddressRegister = (byte)((sampleAddress - 0xC000) / 64);
        Assert.True(ContainsSequence(rom.Skip(fixedPhysicalOffset).ToArray(), [0x12, relocatedAddressRegister]));
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
                              void Main() {
                                  Music.Asset(stage_theme, "stage.vgz");
                                  Audio.Init();
                                  Music.Play(stage_theme);
                                  Audio.Update();
                                  Music.Stop();
                                  return;
                              }
                              """;
        const string library = """
                               void Main() {
                                   Music.Asset(stage_theme, "stage.vgz");
                                   Audio.Init();
                                   Music.Play(stage_theme);
                                   Audio.Update();
                                   Music.Stop();
                                   return;
                               }
                               """;

        var sdkLibrary = SdkLibrarySource.ForTarget(NesTarget.Intrinsics);

        Assert.Contains("class RetroSharp_Portable2D_Music", sdkLibrary, StringComparison.Ordinal);
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

    private static void WriteVgmGzipFrames(string path, IReadOnlyList<IReadOnlyList<byte[]>> frames)
    {
        var commands = new List<byte>();
        foreach (var frame in frames)
        {
            foreach (var payload in frame)
            {
                commands.Add(0xB4);
                commands.AddRange(payload);
            }

            commands.Add(0x61);
            commands.Add(0xDF);
            commands.Add(0x02);
        }

        commands.Add(0x66);

        var bytes = new byte[0xC0 + commands.Count];
        bytes[0] = (byte)'V';
        bytes[1] = (byte)'g';
        bytes[2] = (byte)'m';
        bytes[3] = (byte)' ';
        WriteUInt32(bytes, 0x08, 0x00000161);
        WriteUInt32(bytes, 0x18, (uint)(735 * frames.Count));
        WriteUInt32(bytes, 0x34, 0x8C);
        WriteUInt32(bytes, 0x84, 1_789_773);
        commands.CopyTo(bytes, 0xC0);
        WriteUInt32(bytes, 0x04, (uint)(bytes.Length - 4));

        using var file = File.Create(path);
        using var gzip = new GZipStream(file, CompressionLevel.SmallestSize);
        gzip.Write(bytes);
    }

    private static void WriteVgmGzipWithDataBlock(
        string path,
        byte dataBlockType,
        byte[] dataBlock,
        byte command,
        params byte[][] commandPayloads)
    {
        var commands = new List<byte>
        {
            0x67,
            0x66,
            dataBlockType,
        };
        commands.AddRange(BitConverter.GetBytes(dataBlock.Length));
        commands.AddRange(dataBlock);
        foreach (var payload in commandPayloads)
        {
            commands.Add(command);
            commands.AddRange(payload);
        }

        commands.Add(0x66);

        var bytes = new byte[0xC0 + commands.Count];
        bytes[0] = (byte)'V';
        bytes[1] = (byte)'g';
        bytes[2] = (byte)'m';
        bytes[3] = (byte)' ';
        WriteUInt32(bytes, 0x08, 0x00000161);
        WriteUInt32(bytes, 0x18, 735);
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
        return IndexOfSequence(bytes, sequence) >= 0;
    }

    private static int CountSequence(IReadOnlyList<byte> bytes, IReadOnlyList<byte> sequence)
    {
        var count = 0;
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
                count++;
            }
        }

        return count;
    }

    private static int IndexOfSequence(IReadOnlyList<byte> bytes, IReadOnlyList<byte> sequence)
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
                return i;
            }
        }

        return -1;
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "retrosharp-nes-music-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
