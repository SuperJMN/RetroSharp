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
    public void Shared_runner_discovers_the_exact_complete_stage1_pack_and_mbc1_link()
    {
        var runnerDirectory = LocateRunnerDirectory();
        var mapPath = Path.Combine(runnerDirectory, "assets", "maps", "stage1.tmj");
        var packed = GameBoyTiledMapImporter.CompileWorldPack(
            mapPath,
            GameBoyVideoProgram.FirstGeneratedBackgroundTile);
        var result = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
            RunnerSample.CompiledSource(),
            runnerDirectory,
            sdkLibraryImports: [SdkImportResolver.Portable2D]);

        Assert.Equal(312, packed.Pack.Descriptor.HardwareWidth);
        Assert.Equal(40, packed.Pack.Descriptor.HardwareHeight);
        Assert.Equal(60, packed.Pack.Chunks.Count);
        Assert.Equal(770, packed.Pack.Chunks.Sum(chunk => chunk.Directory.VisualStoredBytes));
        Assert.Equal(326, packed.Pack.Chunks.Sum(chunk => chunk.Directory.CollisionStoredBytes));
        Assert.Equal(2_568, packed.SerializedBytes.Length);
        Assert.Equal("gb-simple-mbc1-current", result.Report.SelectedProfile);
        Assert.Equal(131_072, result.Rom.Length);
        Assert.Equal(
            packed.SerializedBytes.Length,
            result.Report.Segments.Where(segment => segment.Owner == "worldpack:default").Sum(segment => segment.Length));
        Assert.DoesNotContain(
            result.Report.Segments,
            segment => segment.Owner.StartsWith("legacy-world-data", StringComparison.Ordinal));
    }

    [Fact]
    public void Music_tempo_stays_locked_to_frames_while_the_camera_scrolls()
    {
        // Audio.Update() must advance the music exactly once per real frame whether or not the
        // camera is streaming new map columns. Before the deferred-commit fix, a tile-boundary
        // crossing spent a whole extra VBlank inside the streaming path while Audio.Update() was
        // still only called once per loop iteration, so the music slowed down during scrolling.
        var runnerDirectory = LocateRunnerDirectory();
        var source = RunnerSample.CompiledSource();
        var rom = GameBoyRomCompiler.CompileSource(source, runnerDirectory);

        const int startupFrames = 300;
        const int measuredFrames = 900;

        var idle = new GameBoyTestCpu(rom) { CycleAccurateLy = true };
        idle.RunFrames(startupFrames);
        var idleTicksAtStart = idle.AudioUpdateCalls;
        idle.RunAdditionalFrames(measuredFrames);

        var walking = new GameBoyTestCpu(rom) { CycleAccurateLy = true };
        walking.Held.Add("right");
        walking.RunFrames(startupFrames);
        var walkingTicksAtStart = walking.AudioUpdateCalls;
        walking.RunAdditionalFrames(measuredFrames);

        var idleTicksPerFrame = (idle.AudioUpdateCalls - idleTicksAtStart) / (double)measuredFrames;
        var walkingTicksPerFrame = (walking.AudioUpdateCalls - walkingTicksAtStart) / (double)measuredFrames;

        Assert.True(
            idleTicksPerFrame >= 0.99,
            $"Music must advance once per real frame while idle: idle={idleTicksPerFrame:0.000} ticks/frame.");
        // The walking tempo must match the idle tempo (one audio tick per frame) within a small
        // tolerance. The pre-fix runner dropped to ~0.96, far outside this band.
        Assert.True(
            walkingTicksPerFrame >= idleTicksPerFrame - 0.01,
            $"Music slowed while scrolling: idle={idleTicksPerFrame:0.000} ticks/frame, walking={walkingTicksPerFrame:0.000} ticks/frame.");
    }

    [Fact]
    public void Shared_runner_keeps_tracked_game_boy_cartridge_shape_and_execution_compatible()
    {
        var runnerDirectory = LocateRunnerDirectory();
        var source = RunnerSample.CompiledSource();
        var compiled = GameBoyRomCompiler.CompileSource(source, runnerDirectory);
        var tracked = File.ReadAllBytes(Path.Combine(runnerDirectory, "bin", "runner.gb"));

        Assert.Equal(tracked.Length, compiled.Length);
        Assert.Equal(tracked[0x147], compiled[0x147]);

        var trackedCpu = new GameBoyTestCpu(tracked);
        var compiledCpu = new GameBoyTestCpu(compiled);
        trackedCpu.RunFrames(120);
        compiledCpu.RunFrames(120);

        Assert.Equal(trackedCpu.Oam(0xFE02), compiledCpu.Oam(0xFE02));
        Assert.Equal(trackedCpu.IoRegister(0xFF43), compiledCpu.IoRegister(0xFF43));
    }

    [Fact]
    public void Runner_dead_zone_camera_stays_still_then_follows_right_input()
    {
        var runnerDirectory = LocateRunnerDirectory();
        var source = RunnerSample.CompiledSource();
        var rom = GameBoyRomCompiler.CompileSource(source, runnerDirectory);

        var cpu = new GameBoyTestCpu(rom) { CycleAccurateLy = true };
        RunUntilWordEquals(cpu, 0xC14F, 176, maxFrames: 400);
        var idleWorldX = cpu.Wram(0xC14D) | cpu.Wram(0xC14E) << 8;
        var idleWorldY = cpu.Wram(0xC14F) | cpu.Wram(0xC150) << 8;

        cpu.Held.Add("right");
        cpu.Held.Add("b");
        // The restored source cadence reaches the authored first-stair stop within the old 9+2
        // physical jump cycles. Sample 3+2 cycles so this remains a dead-zone-follow assertion.
        for (var jump = 0; jump < 3; jump++)
        {
            RunJumpCycle(cpu);
        }

        var worldXAfterFirstRun = cpu.Wram(0xC14D) | cpu.Wram(0xC14E) << 8;
        for (var jump = 0; jump < 2; jump++)
        {
            RunJumpCycle(cpu);
        }

        var worldXAfterSecondRun = cpu.Wram(0xC14D) | cpu.Wram(0xC14E) << 8;
        var playerXAfterSecondRun = cpu.Wram(0xC000) | cpu.Wram(0xC001) << 8;

        Assert.Equal(0, idleWorldX);
        Assert.Equal(176, idleWorldY);
        Assert.True(
            worldXAfterFirstRun > idleWorldX,
            $"Runner should start scrolling right after Mario leaves the horizontal dead-zone: idle={idleWorldX}, first={worldXAfterFirstRun}, "
            + $"requested={cpu.Wram(0xC0E0) | cpu.Wram(0xC0E1) << 8}, sprite=({cpu.Oam(0xFE01)},{cpu.Oam(0xFE00)}).");
        Assert.True(
            worldXAfterSecondRun > worldXAfterFirstRun,
            $"Runner camera should continue following right input through the next ramp jump: first={worldXAfterFirstRun}, second={worldXAfterSecondRun}.");
        Assert.True(
            playerXAfterSecondRun < 430,
            $"Dead-zone checkpoints must precede the authored first-stair stop: player={playerXAfterSecondRun}.");

        static void RunJumpCycle(GameBoyTestCpu cpu)
        {
            cpu.Held.Add("a");
            cpu.RunAdditionalFrames(22);
            cpu.Held.Remove("a");
            cpu.RunAdditionalFrames(18);
        }

    }

    [Fact]
    public void Runner_free_scroll_keeps_visible_game_boy_background_tiles_aligned_with_world_map()
    {
        var runnerDirectory = LocateRunnerDirectory();
        var source = RunnerSample.CompiledSource();
        var program = CompileVideoProgram(source, runnerDirectory);
        var worldTileGrid = Assert.IsType<WorldTileGrid>(program.WorldTileGrid);
        var cpu = new GameBoyTestCpu(GameBoyRomCompiler.CompileSource(source, runnerDirectory))
        {
            CycleAccurateLy = true,
            EnforceVblankVramWrites = true,
        };
        var frame = 0;

        Run(cpu, ref frame, 130);
        AdvanceToAppliedCamera(cpu, ref frame);
        AssertVisibleTilesMatchWorldMap(cpu, worldTileGrid, "idle");

        Run(cpu, ref frame, 18, "right", "b");
        Run(cpu, ref frame, 22, "right", "b", "a");
        Run(cpu, ref frame, 15, "right", "b");
        AdvanceToAppliedCamera(cpu, ref frame);
        AssertVisibleTilesMatchWorldMap(cpu, worldTileGrid, "first platform diagonal scroll");

        Run(cpu, ref frame, 22, "right", "b", "a");
        Run(cpu, ref frame, 15, "right", "b");
        AdvanceToAppliedCamera(cpu, ref frame);
        AssertVisibleTilesMatchWorldMap(cpu, worldTileGrid, "second platform diagonal scroll");

        Run(cpu, ref frame, 22, "right", "b", "a");
        Run(cpu, ref frame, 11, "right", "b");
        AdvanceToAppliedCamera(cpu, ref frame);
        AssertVisibleTilesMatchWorldMap(cpu, worldTileGrid, "third platform diagonal scroll");

        Run(cpu, ref frame, 30, "right", "b", "a");
        AdvanceToAppliedCamera(cpu, ref frame);
        AssertVisibleTilesMatchWorldMap(cpu, worldTileGrid, "upper pyramid diagonal climb");

        Run(cpu, ref frame, 20, "right", "b");
        AdvanceToAppliedCamera(cpu, ref frame);
        AssertVisibleTilesMatchWorldMap(cpu, worldTileGrid, "upper pyramid vertical scroll");

        Run(cpu, ref frame, 30, "right", "b", "a");
        AdvanceToAppliedCamera(cpu, ref frame);
        AssertVisibleTilesMatchWorldMap(cpu, worldTileGrid, "top pyramid diagonal climb");

        Run(cpu, ref frame, 20, "right", "b");
        AdvanceToAppliedCamera(cpu, ref frame);
        AssertVisibleTilesMatchWorldMap(cpu, worldTileGrid, "top pyramid vertical scroll");

        Run(cpu, ref frame, 75, "right", "b");
        AdvanceToAppliedCamera(cpu, ref frame);
        AssertVisibleTilesMatchWorldMap(cpu, worldTileGrid, "right edge high scroll");

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

        static void AdvanceToAppliedCamera(GameBoyTestCpu cpu, ref int frame)
        {
            cpu.RunUntilIoRegisterWrites(0xFF42, 1, maxInstructions: 50_000_000);
            frame = Math.Max(frame, (int)((cpu.Cycles + 70223) / 70224));
        }

        static void AssertVisibleTilesMatchWorldMap(GameBoyTestCpu cpu, WorldTileGrid worldTileGrid, string label)
        {
            var scx = cpu.IoRegister(0xFF43);
            var scy = cpu.IoRegister(0xFF42);
            var cameraX = cpu.Wram(0xC14D) | cpu.Wram(0xC14E) << 8;
            var cameraY = cpu.Wram(0xC14F) | cpu.Wram(0xC150) << 8;
            var firstBufferColumn = scx / 8;
            var firstBufferRow = scy / 8;
            var mismatches = new List<string>();

            for (var screenRow = 0; screenRow < 18; screenRow++)
            {
                var sourceRow = (cameraY + screenRow * 8) / 8 % worldTileGrid.Height;
                var bufferRow = (firstBufferRow + screenRow) % 32;
                for (var screenColumn = 0; screenColumn < 20; screenColumn++)
                {
                    var sourceColumn = (cameraX + screenColumn * 8) / 8 % worldTileGrid.Width;
                    var bufferColumn = (firstBufferColumn + screenColumn) % 32;
                    var expected = (byte)worldTileGrid.TileIdAt(sourceColumn, sourceRow);
                    var actual = cpu.Vram((ushort)(0x9800 + bufferRow * 32 + bufferColumn));
                    if (actual != expected)
                    {
                        mismatches.Add(
                            $"{label}: camera=({cameraX},{cameraY}) scx={scx} scy={scy} screen=({screenColumn},{screenRow}) "
                            + $"state={CameraState(cpu)} "
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

        static string CameraState(GameBoyTestCpu cpu)
        {
            return $"fine=({cpu.Wram(0xC0E2)},{cpu.Wram(0xC0EA)}) "
                + $"screenLeft={cpu.Wram(0xC0E3)} bgLeft={cpu.Wram(0xC0E5)} bgRight={cpu.Wram(0xC0E4)} "
                + $"srcLeft={cpu.Wram(0xC0E7)} srcRight={cpu.Wram(0xC0E6)} "
                + $"topBg={cpu.Wram(0xC0EB)} bottomBg={cpu.Wram(0xC0EC)} "
                + $"topSrc={cpu.Wram(0xC0ED)} bottomSrc={cpu.Wram(0xC0EE)} "
                + $"pending={cpu.Wram(0xC119)}/{cpu.Wram(0xC11A)}/{cpu.Wram(0xC11B)} "
                + $"diagCol={cpu.Wram(0xC11F)}/{cpu.Wram(0xC120)}/{cpu.Wram(0xC121)} "
                + $"diagRow={cpu.Wram(0xC122)}/{cpu.Wram(0xC123)}/{cpu.Wram(0xC124)}";
        }
    }

    [Fact]
    public void Runner_first_packed_row_payload_matches_its_complete_stage1_metadata()
    {
        var runnerDirectory = LocateRunnerDirectory();
        var source = RunnerSample.CompiledSource();
        var program = CompileVideoProgram(source, runnerDirectory);
        var worldTileGrid = Assert.IsType<WorldTileGrid>(program.WorldTileGrid);
        var packedWorld = Assert.IsType<GameBoyTiledWorldPack>(program.PackedWorld);
        var runtime = GameBoyWorldPackRuntimePlan.Create(
            packedWorld.SerializedBytes,
            enablePackedCameraCache: true,
            enableDiagonalVisualCache: true);
        var cpu = new GameBoyTestCpu(GameBoyRomCompiler.CompileSource(source, runnerDirectory));

        cpu.RunFrames(130);

        AssertPayload(0xC170, runtime.Layout.EdgeSlots[0].Start);
        AssertPayload(0xC17A, runtime.Layout.EdgeSlots[1].Start);

        void AssertPayload(ushort metadata, ushort payload)
        {
            Assert.Equal(2, cpu.Wram((ushort)(metadata + 1)));
            var worldRow = cpu.Wram((ushort)(metadata + 3)) | cpu.Wram((ushort)(metadata + 4)) << 8;
            var sourceColumn = cpu.Wram((ushort)(metadata + 7)) | cpu.Wram((ushort)(metadata + 8)) << 8;
            var payloadLength = cpu.Wram((ushort)(metadata + 9));
            var expected = Enumerable.Range(0, payloadLength)
                .Select(offset => (byte)worldTileGrid.TileIdAt((sourceColumn + offset) % worldTileGrid.Width, worldRow))
                .ToArray();
            var actual = Enumerable.Range(0, payloadLength)
                .Select(offset => cpu.Wram((ushort)(payload + offset)))
                .ToArray();

            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void Deferred_camera_commit_streams_columns_into_vram_during_apply()
    {
        // The deferred-commit camera streaming only writes VRAM from Camera.Apply (during the
        // top-of-frame VBlank). A program that scrolls and applies the camera every frame must
        // therefore still mutate the background tilemap as new columns are exposed; a program that
        // applies without moving must leave it untouched.
        var columns = string.Concat(Enumerable.Range(0, 40).Select(column =>
            $"World.Column({column},{column + 1},{column + 1},{column + 1},{column + 1});"));

        var scrolling = CompileCameraProgram(columns, move: true);
        var still = CompileCameraProgram(columns, move: false);

        const int frames = 120;
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
            Assert.InRange(scrollingCpu.Vram(address), (byte)0, (byte)40);
        }

        Assert.True(scrolled, "Scrolling did not stream any new columns into the background tilemap during Camera.Apply.");
    }

    [Fact]
    public void Runner_writes_oam_only_during_vblank_while_scrolling()
    {
        var runnerDirectory = LocateRunnerDirectory();
        var source = RunnerSample.CompiledSource();
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

    [Fact]
    public void Runner_initial_full_stage1_row_commits_write_tilemap_only_during_vblank()
    {
        var runnerDirectory = LocateRunnerDirectory();
        var source = RunnerSample.CompiledSource();
        var rom = GameBoyRomCompiler.CompileSource(source, runnerDirectory);

        var cpu = new GameBoyTestCpu(rom) { CycleAccurateLy = true };
        cpu.RunFrames(130);

        var runtimeTilemapWrites = cpu.VramWrites
            .Where(write => write is { Address: >= 0x9800 and < 0x9C00, LcdEnabled: true })
            .ToArray();
        var unsafeWrites = runtimeTilemapWrites
            .Where(write => write.Ly is < 144 or > 153)
            .Take(12)
            .ToArray();

        Assert.NotEmpty(runtimeTilemapWrites);
        Assert.True(
            unsafeWrites.Length == 0,
            "Runner wrote full-stage background rows after VBlank ended: "
            + string.Join(", ", unsafeWrites.Select(write => $"0x{write.Address:X4}=0x{write.Value:X2} LY={write.Ly} cycles={write.Cycles}")));
    }

    private static byte[] CompileCameraProgram(string columns, bool move)
    {
        var movement = move ? "camera_move_right();" : "";
        var source = $$"""
                       void Main() {
                           Video.Init();
                           {{columns}}
                           World.Map(40, 11, 4);
                           Camera.Init(40, 11, 4);
                           while (true) {
                               Video.WaitVBlank();
                               Camera.Apply();
                               {{movement}}
                           }
                       }
                       """;
        return GameBoyRomCompiler.CompileSource(source);
    }

    private static GameBoyVideoProgram CompileVideoProgram(string source, string? baseDirectory)
    {
        var parse = new SomeParser().Parse(
            SdkLibrarySource.Merge(
                GameBoyTarget.Intrinsics,
                source,
                libraryImportPaths: [SdkImportResolver.Portable2D]));
        if (parse.IsFailure)
        {
            throw new InvalidOperationException(parse.Error);
        }

        var targetProgram = TargetProgramSelector.Select(parse.Value, GameBoyTarget.Intrinsics);
        var actorProgram = ActorFrameworkLowerer.Lower(targetProgram, GameBoyTarget.Capabilities, supportsUpdate: true, supportsDraw: true, baseDirectory);
        var lowered = SdkSourcePackageFacadeLowerer.Lower(actorProgram);
        return GameBoyVideoProgram.FromProgram(lowered, baseDirectory);
    }

    private static void RunUntilWordEquals(GameBoyTestCpu cpu, ushort lowAddress, ushort expected, int maxFrames)
    {
        for (var frame = 0; frame < maxFrames; frame++)
        {
            var actual = cpu.Wram(lowAddress) | cpu.Wram((ushort)(lowAddress + 1)) << 8;
            if (actual == expected)
            {
                return;
            }

            cpu.RunAdditionalFrames(1);
        }

        Assert.Fail($"WRAM word 0x{lowAddress:X4} did not reach {expected} within {maxFrames} frames.");
    }

    private static string LocateRunnerDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "samples", "runner");
            if (File.Exists(Path.Combine(candidate, "runner.retrosharp.json")))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate samples/runner/runner.retrosharp.json from the test output directory.");
    }
}
