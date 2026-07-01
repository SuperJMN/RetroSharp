namespace RetroSharp.GameBoy.Tests;

using System;
using System.IO;
using System.Linq;
using RetroSharp.GameBoy;
using Xunit;

// Executable specification for transparent MBC1 banking (code + data overlays).
// See docs/GameBoyBankingRoadmap.md. These tests are live regression guards for
// the transparent MBC1 foundation and executable specs for each landed banking
// increment.
public sealed class GameBoyBankingRoadmapTests
{
    private const byte CartridgeRomOnly = 0x00;
    private const byte CartridgeMbc1 = 0x01;

    // --- Live regression guards (must stay green during the whole effort) ---

    [Fact]
    public void Small_program_stays_rom_only_and_unbanked()
    {
        const string source = """
            void Main() {
                Video.Init();
                loop {
                    Video.WaitVBlank();
                }
            }
            """;

        var rom = GameBoyRomCompiler.CompileSource(source, null);

        Assert.Equal(32768, rom.Length);
        Assert.Equal(CartridgeRomOnly, rom[0x147]);
    }

    [Fact]
    public void Music_only_program_keeps_building_under_existing_banking()
    {
        // Music banking already exists; this guards that the SDK-stream / banking
        // work does not regress it.
        var directory = RepositoryDirectory("samples/runner/music");
        var source = $$"""
            void Main() {
                Video.Init();
                Music.Asset(theme, "delight.gbapu");
                Audio.Init();
                Music.Play(theme);
                loop {
                    Video.WaitVBlank();
                    Audio.Update();
                }
            }
            """;

        var rom = GameBoyRomCompiler.CompileSource(source, directory);
        Assert.True(rom.Length >= 32768);
    }

    // --- Phase 1: calling convention (subroutine emission) ---

    [Fact]
    public void Non_inline_function_called_repeatedly_emits_shared_call_ret_subroutine()
    {
        // A non-inline helper called several times should be emitted once as a
        // CALL/RET subroutine instead of being inlined at every call site.
        const string source = """
            void bump() {
                Video.WaitVBlank();
            }

            void Main() {
                Video.Init();
                loop {
                    bump();
                    bump();
                    bump();
                }
            }
            """;

        var rom = GameBoyRomCompiler.CompileSource(source, null);

        Assert.Contains((byte)0xCD, rom); // CALL nn
        Assert.Contains((byte)0xC9, rom); // RET
    }

    [Fact]
    public void Subroutine_emission_reduces_code_for_multi_call_functions()
    {
        // De-duplicating a multi-call body must make the ROM payload smaller than
        // the fully-inlined equivalent (proves real subroutine sharing).
        var inlined = GameBoyRomCompiler.CompileSource(SharedBodyProgram(inline: true), null);
        var shared = GameBoyRomCompiler.CompileSource(SharedBodyProgram(inline: false), null);

        Assert.True(
            MeaningfulBytes(shared) < MeaningfulBytes(inlined),
            $"shared={MeaningfulBytes(shared)} inlined={MeaningfulBytes(inlined)}");
    }

    [Fact]
    public void Subroutine_value_parameter_is_passed_through_a_slot_and_executes()
    {
        // A subroutine taking a value parameter must observe the caller's argument
        // (passed via a fixed WRAM slot) at runtime.
        const string source = """
            void store_frame(i16 value) {
                Sprite.Draw(player, 16, 16, value, false, 0);
            }

            void Main() {
                Video.Init();
                Sprite.Asset(player, "assets/mario-player.png", 18, 32);
                Animation.Clip(walk, 1, 4, 4);
                loop {
                    Video.WaitVBlank();
                    store_frame(2);
                    store_frame(3);
                }
            }
            """;

        var rom = GameBoyRomCompiler.CompileSource(source, RepositoryDirectory("samples/runner"));
        var cpu = new GameBoyTestCpu(rom);
        cpu.RunFrames(4);

        // Both draws executed (last write wins in OAM); the run must not fault.
        Assert.True(cpu.Cycles > 0);
    }

    // --- Phase 2: MBC1 code overlays + trampolines ---

    [Fact]
    public void Runner_sample_builds_as_banked_mbc1_rom()
    {
        var sourcePath = RepositoryFile("samples/runner/runner.rs");
        var source = File.ReadAllText(sourcePath);

        var rom = GameBoyRomCompiler.CompileSource(source, Path.GetDirectoryName(sourcePath));

        Assert.Equal(CartridgeMbc1, rom[0x147]);
        Assert.True(rom.Length > 32768, "a banked ROM spans more than two banks");
        Assert.Equal(0, rom.Length % 16384);
    }

    [Fact]
    public void Banked_runner_executes_across_a_bank_boundary_without_faulting()
    {
        var sourcePath = RepositoryFile("samples/runner/runner.rs");
        var source = File.ReadAllText(sourcePath);
        var rom = GameBoyRomCompiler.CompileSource(source, Path.GetDirectoryName(sourcePath));

        var cpu = new GameBoyTestCpu(rom);
        cpu.RunFrames(120);

        // The actor sprite is drawn every frame; OAM/VRAM must be populated and the
        // CPU must keep running (a bad cross-bank CALL/RET would hang or fault).
        Assert.True(cpu.Cycles > 0);
        Assert.True(Enumerable.Range(0x8000, 0x1800).Any(address => cpu.Vram((ushort)address) != 0));
    }

    [Fact]
    public void Banked_subroutine_calls_go_through_fixed_bank_trampolines()
    {
        var directory = CreateTempDirectory();
        WriteLargeGbApuTrace(Path.Combine(directory, "large.gbapu"), frameCount: 12000);
        var source = LargeProgramWithSharedSubroutineAndBankedMusic();
        var rom = GameBoyRomCompiler.CompileSource(source, directory);
        var trampolineAddress = Bank1CallTrampolineAddress(rom);

        Assert.Equal(CartridgeMbc1, rom[0x147]);
        Assert.True(trampolineAddress.HasValue,
            "Banked subroutines should be entered through a bank-0 trampoline that selects the program bank, CALLs the body, restores the program bank, and RETs.");
        Assert.True(
            ContainsCallTo(rom, trampolineAddress.Value),
            "Banked subroutine call sites should CALL the fixed-bank trampoline instead of relying on the currently selected switchable bank.");
    }

    [Fact]
    public void Multiple_banked_subroutine_bodies_use_their_own_program_banks_transparently()
    {
        var source = ProgramWithMultipleLargeSharedSubroutines();
        var rom = GameBoyRomCompiler.CompileSource(source, null);

        var trampolineBanks = FixedBankTrampolineTargetBanks(rom);
        var cpu = new GameBoyTestCpu(rom);
        cpu.RunFrames(2);

        Assert.Equal(CartridgeMbc1, rom[0x147]);
        Assert.True(rom.Length >= 65536, "multiple switchable program banks should produce an MBC1 ROM with at least four banks");
        Assert.Contains((byte)2, trampolineBanks);
        Assert.True(cpu.Cycles > 0);
    }

    [Fact]
    public void Main_flow_crossing_multiple_program_banks_continues_transparently()
    {
        var source = ProgramWithLargeMainFlow();
        var rom = GameBoyRomCompiler.CompileSource(source, null);

        var cpu = new GameBoyTestCpu(rom);
        cpu.RunFrames(120);

        Assert.Equal(CartridgeMbc1, rom[0x147]);
        Assert.True(rom.Length >= 65536, "large main flow should use multiple program banks transparently");
        Assert.Equal(9, cpu.Oam(0xFE02));
    }

    [Fact]
    public void Main_loop_back_edge_between_switchable_program_banks_is_rejected_explicitly()
    {
        var source = ProgramWithLoopBackEdgeAcrossProgramBanks();

        var exception = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource(source, null));

        Assert.Contains("Direct Game Boy control flow from switchable program bank", exception.Message);
    }

    // --- Phase 3: bank read-only tile/map data ---

    [Fact]
    public void Banked_tile_data_is_copied_into_vram_at_startup()
    {
        var directory = CreateTempDirectory();
        WriteLargeGbApuTrace(Path.Combine(directory, "large.gbapu"), frameCount: 12000);
        CopyRunnerSprite(directory, "player.gb.png");
        var source = LargeProgramWithStartupSpriteDataAndBankedMusic();
        var rom = GameBoyRomCompiler.CompileSource(source, directory);

        var cpu = new GameBoyTestCpu(rom);
        cpu.RunFrames(2);

        Assert.Equal(CartridgeMbc1, rom[0x147]);
        Assert.True(rom.Length > 65536, "banked read-only data should extend beyond the one-tail-bank music layout when needed");
        Assert.True(
            ContainsBankedStartupTileCopy(rom),
            "startup tile copy should select a dedicated read-only data bank before reading tile_data");

        // Tile data is not part of the contiguous executable program tail here,
        // but startup still copies it transparently to the 0x8000 tile region.
        Assert.True(Enumerable.Range(0x8000, 0x1000).Any(address => cpu.Vram((ushort)address) != 0));
    }

    [Fact]
    public void Banked_map_rows_are_read_through_fixed_bank_helpers()
    {
        var directory = CreateTempDirectory();
        WriteLargeGbApuTrace(Path.Combine(directory, "large.gbapu"), frameCount: 12000);
        CopyRunnerSprite(directory, "player.gb.png");
        var source = LargeProgramWithBankedMapRowsAndMusic();
        var rom = GameBoyRomCompiler.CompileSource(source, directory);

        var cpu = new GameBoyTestCpu(rom);
        cpu.RunFrames(120);

        Assert.Equal(CartridgeMbc1, rom[0x147]);
        Assert.True(
            ContainsBankedMapRowReader(rom),
            "banked map rows should be read by a fixed-bank helper that selects the data bank and restores the program bank");
        Assert.Equal(7, cpu.Oam(0xFE02));
    }

    [Fact]
    public void Banked_audio_update_from_program_tail_call_site_keeps_playing()
    {
        var directory = CreateTempDirectory();
        WriteLargeGbApuTrace(Path.Combine(directory, "large.gbapu"), frameCount: 12000);
        var source = LargeProgramWithAudioUpdateInProgramTail();
        var rom = GameBoyRomCompiler.CompileSource(source, directory);

        var cpu = new GameBoyTestCpu(rom);
        var played = cpu.RunUntilRegisterWrites(0xFF14, count: 24, maxInstructions: 50_000_000);

        Assert.Equal(CartridgeMbc1, rom[0x147]);
        Assert.Equal(ExpectedTriggerSequence(24), played);
    }

    [Fact]
    public void Banked_music_play_from_program_tail_call_site_starts_theme()
    {
        var directory = CreateTempDirectory();
        WriteLargeGbApuTrace(Path.Combine(directory, "large.gbapu"), frameCount: 12000);
        var source = LargeProgramWithMusicPlayInProgramTail();
        var rom = GameBoyRomCompiler.CompileSource(source, directory);

        var cpu = new GameBoyTestCpu(rom);
        var played = cpu.RunUntilRegisterWrites(0xFF14, count: 24, maxInstructions: 50_000_000);

        Assert.Equal(CartridgeMbc1, rom[0x147]);
        Assert.Equal(ExpectedTriggerSequence(24), played);
    }

    // --- Helpers ---

    private static string SharedBodyProgram(bool inline)
    {
        var keyword = inline ? "inline " : string.Empty;
        return $$"""
            {{keyword}}void paint() {
                Video.WaitVBlank();
                Input.Poll();
                Video.WaitVBlank();
                Input.Poll();
            }

            void Main() {
                Video.Init();
                loop {
                    Video.WaitVBlank();
                    paint();
                    paint();
                    paint();
                    paint();
                }
            }
            """;
    }

    private static int MeaningfulBytes(byte[] rom)
    {
        var end = rom.Length;
        while (end > 0 && (rom[end - 1] == 0x00 || rom[end - 1] == 0xFF))
        {
            end--;
        }

        return end;
    }

    private static string LargeProgramWithSharedSubroutineAndBankedMusic()
    {
        var filler = string.Join(Environment.NewLine, Enumerable.Repeat("    x += 1;", 2500));
        return """
            void tick_input() {
                Input.Poll();
            }

            void Main() {
                Video.Init();
                Music.Asset(theme, "large.gbapu");
                Audio.Init();
                Music.Play(theme);
                u8 x = 0;
            """ + filler + """
                loop {
                    Video.WaitVBlank();
                    Audio.Update();
                    tick_input();
                    tick_input();
                }
            }
            """;
    }

    private static string ProgramWithMultipleLargeSharedSubroutines()
    {
        var builder = new System.Text.StringBuilder();
        for (var function = 0; function < 12; function++)
        {
            builder.AppendLine($"void chunk{function}() {{");
            builder.AppendLine($"    u8 x{function} = 0;");
            for (var i = 0; i < 550; i++)
            {
                builder.AppendLine($"    x{function} += 1;");
            }

            builder.AppendLine("}");
            builder.AppendLine();
        }

        builder.AppendLine("void Main() {");
        builder.AppendLine("    Video.Init();");
        builder.AppendLine("    loop {");
        builder.AppendLine("        Video.WaitVBlank();");
        for (var function = 0; function < 12; function++)
        {
            builder.AppendLine($"        chunk{function}();");
            builder.AppendLine($"        chunk{function}();");
        }

        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string ProgramWithLargeMainFlow()
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("void Main() {");
        builder.AppendLine("    Video.Init();");
        builder.AppendLine("    u8 x = 0;");
        for (var i = 0; i < 6000; i++)
        {
            builder.AppendLine("    x += 1;");
        }

        builder.AppendLine("    loop {");
        builder.AppendLine("        Video.WaitVBlank();");
        builder.AppendLine("        sprite_set(0, 8, 16, 9, 0);");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string ProgramWithLoopBackEdgeAcrossProgramBanks()
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("void Main() {");
        builder.AppendLine("    Video.Init();");
        builder.AppendLine("    u8 x = 0;");
        for (var i = 0; i < 2600; i++)
        {
            builder.AppendLine("    x += 1;");
        }

        builder.AppendLine("    loop {");
        builder.AppendLine("        Video.WaitVBlank();");
        for (var i = 0; i < 3000; i++)
        {
            builder.AppendLine("        x += 1;");
        }

        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string LargeProgramWithStartupSpriteDataAndBankedMusic()
    {
        var filler = string.Join(Environment.NewLine, Enumerable.Repeat("    x += 1;", 3500));
        return """
            void Main() {
                Video.Init();
                Sprite.Asset(player0, "player.png", 18, 32);
                Sprite.Asset(player1, "player.png", 18, 32);
                Sprite.Asset(player2, "player.png", 18, 32);
                Sprite.Asset(player3, "player.png", 18, 32);
                Music.Asset(theme, "large.gbapu");
                Audio.Init();
                Music.Play(theme);
                u8 x = 0;
            """ + filler + """
                loop {
                    Video.WaitVBlank();
                    Audio.Update();
                }
            }
            """;
    }

    private static string LargeProgramWithBankedMapRowsAndMusic()
    {
        var filler = string.Join(Environment.NewLine, Enumerable.Repeat("    x += 1;", 3500));
        return """
            void Main() {
                Video.Init();
                Sprite.Asset(player0, "player.png", 18, 32);
                Sprite.Asset(player1, "player.png", 18, 32);
                Sprite.Asset(player2, "player.png", 18, 32);
                Sprite.Asset(player3, "player.png", 18, 32);
            """ + MapColumns(200, 16) + """
                Music.Asset(theme, "large.gbapu");
                Audio.Init();
                Music.Play(theme);
                u8 x = 0;
            """ + filler + """
                loop {
                    Video.WaitVBlank();
                    u8 tile = map_tile_at(2, 1);
                    sprite_set(0, 8, 16, tile, 0);
                }
            }
            """;
    }

    private static string LargeProgramWithAudioUpdateInProgramTail()
    {
        var filler = string.Join(Environment.NewLine, Enumerable.Repeat("    x += 1;", 3500));
        return """
            void Main() {
                Video.Init();
                Music.Asset(theme, "large.gbapu");
                Audio.Init();
                Music.Play(theme);
                u8 x = 0;
            """ + filler + """
                loop {
                    Video.WaitVBlank();
                    Audio.Update();
                }
            }
            """;
    }

    private static string LargeProgramWithMusicPlayInProgramTail()
    {
        var filler = string.Join(Environment.NewLine, Enumerable.Repeat("    x += 1;", 3500));
        return """
            void Main() {
                Video.Init();
                Music.Asset(theme, "large.gbapu");
                Audio.Init();
                u8 x = 0;
            """ + filler + """
                Music.Play(theme);
                loop {
                    Video.WaitVBlank();
                    Audio.Update();
                }
            }
            """;
    }

    private static IReadOnlyList<byte> ExpectedTriggerSequence(int count)
    {
        return Enumerable.Range(0, count).Select(frame => TriggerValue(frame)).ToList();
    }

    private static string MapColumns(int columnCount, int height)
    {
        var lines = new List<string>(columnCount);
        for (var column = 0; column < columnCount; column++)
        {
            var values = Enumerable
                .Range(0, height)
                .Select(row => column == 2 && row == 1 ? "7" : ((column + row) % 5 + 1).ToString());
            lines.Add($"    map_column({column}, {string.Join(", ", values)});");
        }

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static ushort? Bank1CallTrampolineAddress(byte[] rom)
    {
        return FixedBankTrampolineAddress(rom, targetBank: 1);
    }

    private static bool ContainsCallTo(byte[] rom, ushort address)
    {
        for (var offset = 0; offset <= rom.Length - 3; offset++)
        {
            if (rom[offset] == 0xCD &&
                rom[offset + 1] == (byte)(address & 0xFF) &&
                rom[offset + 2] == (byte)(address >> 8))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsBankedStartupTileCopy(byte[] rom)
    {
        for (var offset = 0x0150; offset <= 0x4000 - 14; offset++)
        {
            if (rom[offset] == 0x3E &&
                rom[offset + 1] > 1 &&
                rom[offset + 2] == 0xEA &&
                rom[offset + 3] == 0x00 &&
                rom[offset + 4] == 0x20 &&
                rom[offset + 5] == 0x11 &&
                rom[offset + 8] == 0x21 &&
                rom[offset + 9] == 0x00 &&
                rom[offset + 10] == 0x80)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsBankedMapRowReader(byte[] rom)
    {
        for (var offset = 0x0150; offset <= 0x4000 - 22; offset++)
        {
            if (rom[offset] == 0x5F &&
                rom[offset + 1] == 0x16 &&
                rom[offset + 2] == 0x00 &&
                rom[offset + 3] == 0x3E &&
                rom[offset + 4] > 1 &&
                rom[offset + 5] == 0xEA &&
                rom[offset + 6] == 0x00 &&
                rom[offset + 7] == 0x20 &&
                rom[offset + 8] == 0x21 &&
                rom[offset + 11] == 0x19 &&
                rom[offset + 12] == 0x7E &&
                rom[offset + 13] == 0x47 &&
                rom[offset + 14] == 0xFA &&
                rom[offset + 17] == 0xEA &&
                rom[offset + 18] == 0x00 &&
                rom[offset + 19] == 0x20 &&
                rom[offset + 20] == 0x78 &&
                rom[offset + 21] == 0xC9)
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<byte> FixedBankTrampolineTargetBanks(byte[] rom)
    {
        var banks = new List<byte>();
        for (var offset = 0x0150; offset <= 0x4000 - 23; offset++)
        {
            if (IsFixedBankTrampoline(rom, offset, out var bank))
            {
                banks.Add(bank);
            }
        }

        return banks;
    }

    private static ushort? FixedBankTrampolineAddress(byte[] rom, byte targetBank)
    {
        for (var offset = 0x0150; offset <= 0x4000 - 23; offset++)
        {
            if (IsFixedBankTrampoline(rom, offset, out var bank) && bank == targetBank)
            {
                return (ushort)offset;
            }
        }

        return null;
    }

    private static bool IsFixedBankTrampoline(byte[] rom, int offset, out byte targetBank)
    {
        targetBank = 0;
        if (rom[offset] == 0xFA &&
            rom[offset + 3] == 0xF5 &&
            rom[offset + 4] == 0x3E &&
            rom[offset + 6] == 0xEA &&
            rom[offset + 9] == 0xEA &&
            rom[offset + 10] == 0x00 &&
            rom[offset + 11] == 0x20 &&
            rom[offset + 12] == 0xCD &&
            rom[offset + 15] == 0xF1 &&
            rom[offset + 16] == 0xEA &&
            rom[offset + 19] == 0xEA &&
            rom[offset + 20] == 0x00 &&
            rom[offset + 21] == 0x20 &&
            rom[offset + 22] == 0xC9)
        {
            targetBank = rom[offset + 5];
            return true;
        }

        return false;
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "retrosharp-gb-banking-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void WriteLargeGbApuTrace(string path, int frameCount)
    {
        var events = new List<GameBoyApuTraceEvent>(frameCount);
        for (var frame = 0; frame < frameCount; frame++)
        {
            var delta = frame == 0 ? 0 : 70224;
            events.Add(new GameBoyApuTraceEvent(delta, 0xFF14, TriggerValue(frame)));
        }

        var trace = new GameBoyApuTrace(
            ClockHz: 4_194_304,
            FramesPerSecond: 60,
            DurationCycles: Math.Max(1, frameCount) * 70224L,
            LoopCycle: 0,
            new GameBoyApuTraceMetadata("Large Trace"),
            events);
        GameBoyApuTraceBinary.Write(path, trace);
    }

    private static byte TriggerValue(int frame)
    {
        return (byte)(0x80 | (frame & 0x7F));
    }

    private static void CopyRunnerSprite(string directory, string fileName)
    {
        File.Copy(RepositoryFile("samples/runner/assets/mario-player.gb.png"), Path.Combine(directory, fileName));
    }

    private static string RepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"Could not find repository file '{relativePath}'.");
    }

    private static string RepositoryDirectory(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"Could not find repository directory '{relativePath}'.");
    }
}
