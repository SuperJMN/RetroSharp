namespace RetroSharp.GameBoy.Tests;

using System.IO;
using System.Linq;
using RetroSharp.Core.Sdk;
using RetroSharp.Core.Targeting;
using RetroSharp.GameBoy;
using RetroSharp.Parser;
using RetroSharp.Sdk;
using Xunit;

public sealed class GameBoyRunnerAudioTempoTests
{
    [Fact]
    public void Music_tempo_stays_locked_to_frames_while_the_camera_scrolls()
    {
        // audio.Update() must advance the music exactly once per real frame whether or not the
        // camera is streaming new map columns. Before the deferred-commit fix, a tile-boundary
        // crossing spent a whole extra VBlank inside the streaming path while audio.Update() was
        // still only called once per loop iteration, so the music slowed down during scrolling.
        var runnerDirectory = LocateRunnerDirectory();
        var source = File.ReadAllText(Path.Combine(runnerDirectory, "runner.rs"));
        var rom = GameBoyRomCompiler.CompileSource(source, runnerDirectory);

        const int frames = 1200;

        var idle = new GameBoyTestCpu(rom) { CycleAccurateLy = true };
        idle.RunFrames(frames);

        var walking = new GameBoyTestCpu(rom) { CycleAccurateLy = true };
        walking.Held.Add("right");
        walking.RunFrames(frames);

        var idleTicksPerFrame = idle.AudioUpdateCalls / (double)frames;
        var walkingTicksPerFrame = walking.AudioUpdateCalls / (double)frames;

        // The walking tempo must match the idle tempo (one audio tick per frame) within a small
        // tolerance. The pre-fix runner dropped to ~0.96, far outside this band.
        Assert.True(
            walkingTicksPerFrame >= idleTicksPerFrame - 0.01,
            $"Music slowed while scrolling: idle={idleTicksPerFrame:0.000} ticks/frame, walking={walkingTicksPerFrame:0.000} ticks/frame.");
    }

    [Fact]
    public void Shared_runner_keeps_tracked_game_boy_rom_byte_identical()
    {
        var runnerDirectory = LocateRunnerDirectory();
        var source = File.ReadAllText(Path.Combine(runnerDirectory, "runner.rs"));
        var compiled = GameBoyRomCompiler.CompileSource(source, runnerDirectory);
        var tracked = File.ReadAllBytes(Path.Combine(runnerDirectory, "runner.gb"));

        Assert.Equal(tracked, compiled);
    }

    [Fact]
    public void Runner_dead_zone_camera_stays_still_then_follows_right_input()
    {
        var runnerDirectory = LocateRunnerDirectory();
        var source = File.ReadAllText(Path.Combine(runnerDirectory, "runner.rs"));
        var rom = GameBoyRomCompiler.CompileSource(source, runnerDirectory);

        var cpu = new GameBoyTestCpu(rom) { CycleAccurateLy = true };
        cpu.RunFrames(30);
        var idleScx = cpu.IoRegister(0xFF43);
        var idleScy = cpu.IoRegister(0xFF42);

        cpu.Held.Add("right");
        cpu.RunFrames(70);
        var scxAfterFirstRun = cpu.IoRegister(0xFF43);
        cpu.RunFrames(110);
        var scxAfterSecondRun = cpu.IoRegister(0xFF43);

        Assert.Equal(0, idleScx);
        Assert.True(idleScy > 0, "Runner should use the vertical dead-zone camera path even before horizontal input.");
        Assert.True(scxAfterFirstRun > idleScx, "Runner should start scrolling right after Mario leaves the horizontal dead-zone.");
        Assert.True(scxAfterSecondRun > scxAfterFirstRun, "Runner camera should continue following sustained right input.");
    }

    [Fact]
    public void Runner_free_scroll_camera_follows_platform_climb_in_two_axes()
    {
        var runnerDirectory = LocateRunnerDirectory();
        var source = File.ReadAllText(Path.Combine(runnerDirectory, "runner.rs"));
        var rom = GameBoyRomCompiler.CompileSource(source, runnerDirectory);

        var cpu = new GameBoyTestCpu(rom) { CycleAccurateLy = true };
        var frame = 0;

        Run(cpu, ref frame, 130);
        var idle = Sample(cpu);

        Run(cpu, ref frame, 18, "right", "b");
        Run(cpu, ref frame, 22, "right", "b", "a");
        Run(cpu, ref frame, 15, "right", "b");
        var firstPlatform = Sample(cpu);

        Run(cpu, ref frame, 22, "right", "b", "a");
        Run(cpu, ref frame, 15, "right", "b");
        var secondPlatform = Sample(cpu);

        Run(cpu, ref frame, 22, "right", "b", "a");
        Run(cpu, ref frame, 11, "right", "b");
        var thirdPlatform = Sample(cpu);

        Assert.True(idle.WorldY >= 190, $"Runner should settle on the lower floor before climbing; observed worldY={idle.WorldY}.");

        Assert.True(firstPlatform.Scx > idle.Scx, $"Expected horizontal scroll on first climb; idle={idle.Scx}, first={firstPlatform.Scx}.");
        Assert.True(firstPlatform.Scy < idle.Scy, $"Expected upward vertical scroll on first climb; idle={idle.Scy}, first={firstPlatform.Scy}.");
        Assert.True(firstPlatform.WorldY <= 165, $"Expected Mario on the first elevated platform; observed worldY={firstPlatform.WorldY}.");

        Assert.True(secondPlatform.Scx > firstPlatform.Scx, $"Expected horizontal scroll to continue on second climb; first={firstPlatform.Scx}, second={secondPlatform.Scx}.");
        Assert.True(secondPlatform.Scy < firstPlatform.Scy, $"Expected camera to keep following upward on second climb; first={firstPlatform.Scy}, second={secondPlatform.Scy}.");
        Assert.True(secondPlatform.WorldY <= 132, $"Expected Mario on the second elevated platform; observed worldY={secondPlatform.WorldY}.");

        Assert.True(thirdPlatform.Scx > secondPlatform.Scx, $"Expected horizontal scroll to continue on third climb; second={secondPlatform.Scx}, third={thirdPlatform.Scx}.");
        Assert.True(thirdPlatform.Scy < secondPlatform.Scy, $"Expected camera to keep following upward on third climb; second={secondPlatform.Scy}, third={thirdPlatform.Scy}.");
        Assert.True(thirdPlatform.WorldY <= 100, $"Expected Mario on the third elevated platform; observed worldY={thirdPlatform.WorldY}.");

        static void Run(GameBoyTestCpu cpu, ref int frame, int frames, params string[] held)
        {
            cpu.Held.Clear();
            foreach (var button in held)
            {
                cpu.Held.Add(button);
            }

            frame += frames;
            cpu.RunFrames(frame);
        }

        static (int Scx, int Scy, int WorldX, int WorldY) Sample(GameBoyTestCpu cpu)
        {
            var scx = cpu.IoRegister(0xFF43);
            var scy = cpu.IoRegister(0xFF42);
            var worldX = scx + cpu.Oam(0xFE01) - 8;
            var worldY = scy + cpu.Oam(0xFE00) - 16;
            return (scx, scy, worldX, worldY);
        }
    }

    [Fact]
    public void Runner_free_scroll_keeps_visible_game_boy_background_tiles_aligned_with_world_map()
    {
        var runnerDirectory = LocateRunnerDirectory();
        var source = File.ReadAllText(Path.Combine(runnerDirectory, "runner.rs"));
        var program = CompileVideoProgram(source, runnerDirectory);
        var worldMap = Assert.IsType<WorldMap2D>(program.WorldMap);
        var cpu = new GameBoyTestCpu(GameBoyRomCompiler.CompileSource(source, runnerDirectory))
        {
            CycleAccurateLy = true,
        };
        var frame = 0;

        Run(cpu, ref frame, 130);
        AssertVisibleTilesMatchWorldMap(cpu, worldMap, "idle");

        Run(cpu, ref frame, 18, "right", "b");
        Run(cpu, ref frame, 22, "right", "b", "a");
        Run(cpu, ref frame, 15, "right", "b");
        AssertVisibleTilesMatchWorldMap(cpu, worldMap, "first platform diagonal scroll");

        Run(cpu, ref frame, 22, "right", "b", "a");
        Run(cpu, ref frame, 15, "right", "b");
        AssertVisibleTilesMatchWorldMap(cpu, worldMap, "second platform diagonal scroll");

        Run(cpu, ref frame, 22, "right", "b", "a");
        Run(cpu, ref frame, 11, "right", "b");
        AssertVisibleTilesMatchWorldMap(cpu, worldMap, "third platform diagonal scroll");

        Run(cpu, ref frame, 75, "right", "b");
        AssertVisibleTilesMatchWorldMap(cpu, worldMap, "right edge high scroll");

        static void Run(GameBoyTestCpu cpu, ref int frame, int frames, params string[] held)
        {
            cpu.Held.Clear();
            foreach (var button in held)
            {
                cpu.Held.Add(button);
            }

            frame += frames;
            cpu.RunFrames(frame);
        }

        static void AssertVisibleTilesMatchWorldMap(GameBoyTestCpu cpu, WorldMap2D worldMap, string label)
        {
            var scx = cpu.IoRegister(0xFF43);
            var scy = cpu.IoRegister(0xFF42);
            var firstColumn = scx / 8;
            var firstRow = scy / 8;
            var mismatches = new List<string>();

            // Include the partially visible right/bottom edge tiles, not just the 20x18 full cells.
            for (var screenRow = 0; screenRow <= 18; screenRow++)
            {
                var sourceRow = (firstRow + screenRow) % worldMap.Height;
                var bufferRow = (firstRow + screenRow) % 32;
                for (var screenColumn = 0; screenColumn <= 20; screenColumn++)
                {
                    var sourceColumn = (firstColumn + screenColumn) % worldMap.Width;
                    var bufferColumn = (firstColumn + screenColumn) % 32;
                    var expected = (byte)worldMap.TileIdAt(sourceColumn, sourceRow);
                    var actual = cpu.Vram((ushort)(0x9800 + bufferRow * 32 + bufferColumn));
                    if (actual != expected)
                    {
                        mismatches.Add(
                            $"{label}: scx={scx} scy={scy} screen=({screenColumn},{screenRow}) "
                            + $"buffer=({bufferColumn},{bufferRow}) source=({sourceColumn},{sourceRow}) "
                            + $"expected=0x{expected:X2} actual=0x{actual:X2}");
                        if (mismatches.Count >= 12)
                        {
                            break;
                        }
                    }
                }

                if (mismatches.Count >= 12)
                {
                    break;
                }
            }

            Assert.True(mismatches.Count == 0, string.Join(Environment.NewLine, mismatches));
        }
    }

    [Fact]
    public void Deferred_camera_commit_streams_columns_into_vram_during_apply()
    {
        // The deferred-commit camera streaming only writes VRAM from camera.Apply (during the
        // top-of-frame VBlank). A program that scrolls and applies the camera every frame must
        // therefore still mutate the background tilemap as new columns are exposed; a program that
        // applies without moving must leave it untouched.
        const string columns = "map_column(0,1,1,1,1);map_column(1,2,2,2,2);map_column(2,3,3,3,3);"
            + "map_column(3,4,4,4,4);map_column(4,5,5,5,5);map_column(5,6,6,6,6);map_column(6,7,7,7,7);"
            + "map_column(7,8,8,8,8);map_column(8,9,9,9,9);map_column(9,10,10,10,10);map_column(10,11,11,11,11);"
            + "map_column(11,12,12,12,12);map_column(12,13,13,13,13);map_column(13,14,14,14,14);"
            + "map_column(14,15,15,15,15);map_column(15,16,16,16,16);map_column(16,17,17,17,17);"
            + "map_column(17,18,18,18,18);map_column(18,19,19,19,19);map_column(19,20,20,20,20);"
            + "map_column(20,21,21,21,21);map_column(21,22,22,22,22);map_column(22,23,23,23,23);"
            + "map_column(23,24,24,24,24);";

        var scrolling = CompileCameraProgram(columns, move: true);
        var still = CompileCameraProgram(columns, move: false);

        const int frames = 40;
        var scrollingCpu = new GameBoyTestCpu(scrolling) { CycleAccurateLy = true };
        scrollingCpu.RunFrames(frames);
        var stillCpu = new GameBoyTestCpu(still) { CycleAccurateLy = true };
        stillCpu.RunFrames(frames);

        // The streamed world band lives at GB rows 11..14 (camera_init y=11, height=4): 0x9800 + 11*32.
        const ushort bandStart = 0x9800 + (11 * 32);
        const ushort bandEnd = 0x9800 + (15 * 32);

        var scrolled = false;
        for (var address = bandStart; address < bandEnd; address++)
        {
            if (scrollingCpu.Vram(address) != stillCpu.Vram(address))
            {
                scrolled = true;
            }

            // Every streamed tile must be a valid source tile id (1..24), never garbage.
            Assert.InRange(scrollingCpu.Vram(address), (byte)0, (byte)24);
        }

        Assert.True(scrolled, "Scrolling did not stream any new columns into the background tilemap during camera.Apply.");
    }

    [Fact]
    public void Runner_writes_oam_only_during_vblank_while_scrolling()
    {
        var runnerDirectory = LocateRunnerDirectory();
        var source = File.ReadAllText(Path.Combine(runnerDirectory, "runner.rs"));
        var rom = GameBoyRomCompiler.CompileSource(source, runnerDirectory);

        var cpu = new GameBoyTestCpu(rom) { CycleAccurateLy = true };
        cpu.Held.Add("right");
        cpu.Held.Add("b");
        cpu.RunFrames(1200);

        var lcdEnabledWrites = cpu.OamWrites.Where(write => write.LcdEnabled).ToArray();
        var unsafeWrites = lcdEnabledWrites
            .Where(write => write.Ly is < 144 or > 153)
            .Take(8)
            .ToArray();

        Assert.NotEmpty(lcdEnabledWrites);
        Assert.True(
            unsafeWrites.Length == 0,
            "Runner wrote OAM after VBlank ended: "
            + string.Join(", ", unsafeWrites.Select(write => $"0x{write.Address:X4}=0x{write.Value:X2} LY={write.Ly} cycles={write.Cycles}")));
    }

    private static byte[] CompileCameraProgram(string columns, bool move)
    {
        var movement = move ? "camera_move_right();" : "";
        var source = $$"""
                       void main() {
                           video_init();
                           {{columns}}
                           camera_init(24, 11, 4);
                           while (true) {
                               video_wait_vblank();
                               camera_apply();
                               {{movement}}
                           }
                       }
                       """;
        return GameBoyRomCompiler.CompileSource(source);
    }

    private static GameBoyVideoProgram CompileVideoProgram(string source, string? baseDirectory)
    {
        var parse = new SomeParser().Parse(source);
        if (parse.IsFailure)
        {
            throw new InvalidOperationException(parse.Error);
        }

        var lowered = ActorFrameworkLowerer.Lower(parse.Value, GameBoyTarget.Capabilities, supportsUpdate: true, supportsDraw: true, baseDirectory);
        return GameBoyVideoProgram.FromProgram(lowered, baseDirectory);
    }

    private static string LocateRunnerDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "samples", "runner");
            if (File.Exists(Path.Combine(candidate, "runner.rs")))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate samples/runner/runner.rs from the test output directory.");
    }
}
