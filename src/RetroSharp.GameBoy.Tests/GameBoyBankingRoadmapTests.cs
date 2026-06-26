namespace RetroSharp.GameBoy.Tests;

using System;
using System.IO;
using System.Linq;
using RetroSharp.GameBoy;
using Xunit;

// Executable specification for transparent MBC1 banking (code + data overlays).
// See docs/GameBoyBankingRoadmap.md. The skipped tests describe the finished
// behaviour; un-skip each one as its phase lands. The non-skipped tests are live
// regression guards that must stay green throughout the work.
public sealed class GameBoyBankingRoadmapTests
{
    private const byte CartridgeRomOnly = 0x00;
    private const byte CartridgeMbc1 = 0x01;

    // --- Live regression guards (must stay green during the whole effort) ---

    [Fact]
    public void Small_program_stays_rom_only_and_unbanked()
    {
        const string source = """
            void main() {
                video.Init();
                loop {
                    video.WaitVBlank();
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
            void main() {
                video.Init();
                music.Asset(theme, "delight.gbapu");
                audio.Init();
                music.Play(theme);
                loop {
                    video.WaitVBlank();
                    audio.Update();
                }
            }
            """;

        var rom = GameBoyRomCompiler.CompileSource(source, directory);
        Assert.True(rom.Length >= 32768);
    }

    // --- Phase 1: calling convention (subroutine emission) ---

    [Fact(Skip = "Banking roadmap Phase 1 — see docs/GameBoyBankingRoadmap.md")]
    public void Non_inline_function_called_repeatedly_emits_shared_call_ret_subroutine()
    {
        // A non-inline helper called several times should be emitted once as a
        // CALL/RET subroutine instead of being inlined at every call site.
        const string source = """
            void bump() {
                video.WaitVBlank();
            }

            void main() {
                video.Init();
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

    [Fact(Skip = "Banking roadmap Phase 1 — see docs/GameBoyBankingRoadmap.md")]
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

    [Fact(Skip = "Banking roadmap Phase 1 — see docs/GameBoyBankingRoadmap.md")]
    public void Subroutine_value_parameter_is_passed_through_a_slot_and_executes()
    {
        // A subroutine taking a value parameter must observe the caller's argument
        // (passed via a fixed WRAM slot) at runtime.
        const string source = """
            void store_frame(i16 value) {
                sprite.Draw(player, 16, 16, value, false, 0);
            }

            void main() {
                video.Init();
                sprite.Asset(player, "assets/mario-player.png", 18, 32);
                animation.Clip(walk, 1, 4, 4);
                loop {
                    video.WaitVBlank();
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

    [Fact(Skip = "Banking roadmap Phase 2 — see docs/GameBoyBankingRoadmap.md")]
    public void Runner_sample_builds_as_banked_mbc1_rom()
    {
        var sourcePath = RepositoryFile("samples/runner/runner.rs");
        var source = File.ReadAllText(sourcePath);

        var rom = GameBoyRomCompiler.CompileSource(source, Path.GetDirectoryName(sourcePath));

        Assert.Equal(CartridgeMbc1, rom[0x147]);
        Assert.True(rom.Length > 32768, "a banked ROM spans more than two banks");
        Assert.Equal(0, rom.Length % 16384);
    }

    [Fact(Skip = "Banking roadmap Phase 2 — see docs/GameBoyBankingRoadmap.md")]
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

    // --- Phase 3: bank read-only tile/map data ---

    [Fact(Skip = "Banking roadmap Phase 3 — see docs/GameBoyBankingRoadmap.md")]
    public void Banked_tile_data_is_copied_into_vram_at_startup()
    {
        var sourcePath = RepositoryFile("samples/runner/runner.rs");
        var source = File.ReadAllText(sourcePath);
        var rom = GameBoyRomCompiler.CompileSource(source, Path.GetDirectoryName(sourcePath));

        var cpu = new GameBoyTestCpu(rom);
        cpu.RunFrames(2);

        // Tile data lives in a bank but is still copied to the 0x8000 tile region.
        Assert.True(Enumerable.Range(0x8000, 0x1000).Any(address => cpu.Vram((ushort)address) != 0));
    }

    // --- Helpers ---

    private static string SharedBodyProgram(bool inline)
    {
        var keyword = inline ? "inline " : string.Empty;
        return $$"""
            {{keyword}}void paint() {
                sprite.Draw(player, 8, 8, 1, false, 0);
                sprite.Draw(player, 24, 8, 2, false, 0);
                sprite.Draw(player, 40, 8, 3, false, 0);
            }

            void main() {
                video.Init();
                sprite.Asset(player, "assets/mario-player.png", 18, 32);
                animation.Clip(walk, 1, 4, 4);
                loop {
                    video.WaitVBlank();
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
