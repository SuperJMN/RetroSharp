namespace RetroSharp.GameBoy.Tests;

using System.IO;
using System.Linq;
using RetroSharp.GameBoy;
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
