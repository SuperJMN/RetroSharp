namespace RetroSharp.GameBoy.Tests;

using RetroSharp.Core.Sdk;
using RetroSharp.GameBoy;
using RetroSharp.Sdk;
using Xunit;
using PackedCameraMemory = RetroSharp.GameBoy.GameBoyRuntimeMemoryLayout.PackedCamera;

public sealed class GameBoyRunnerLandingTests
{
    private const ushort PlayerXLow = 0xC000;
    private const ushort PlayerYLow = 0xC002;
    private const ushort PlayerVelocityY = 0xC004;
    private const ushort PlayerGrounded = 0xC005;
    private const ushort PlayerJumping = 0xC00B;
    private const ushort PlayerVerticalSubpixelLow = 0xC00C;
    private const int FirstPlatformCameraX = 696;
    private const int PlatformTopY = 272;
    private const int PlatformPlayerY = PlatformTopY - 31;
    private const int FloorPlayerY = 273;
    private const int FirstStairWallX = 448;
    private const int PlayerWidth = 18;

    [Fact]
    public void Shared_runner_authors_one_way_platforms_and_only_lands_on_them()
    {
        var mapPath = Path.Combine(RunnerSample.Directory, "assets", "maps", "stage1.tmj");
        var world = GameBoyTiledMapImporter.Load(mapPath, GameBoyVideoProgram.FirstGeneratedBackgroundTile);

        Assert.Equal(WorldTileFlags.Platform, world.WorldFlags[34 * world.Width + 94]);
        Assert.Equal(WorldTileFlags.Platform, world.WorldFlags[35 * world.Width + 106]);
        Assert.Equal(WorldTileFlags.Platform, world.WorldFlags[34 * world.Width + 132]);
        Assert.Equal(WorldTileFlags.Empty, world.WorldFlags[31 * world.Width + 94]);
        Assert.Equal(WorldTileFlags.Empty, world.WorldFlags[31 * world.Width + 96]);

        var operations = GameBoyRomCompiler.CollectSdkOperations(RunnerSample.CompiledSource(), RunnerSample.Directory);
        var landing = Assert.Single(operations.OfType<Sdk2DOperation.CameraAabbHitTop>());
        Assert.Equal(WorldTileFlags.Solid | WorldTileFlags.Platform, landing.Flags);
        Assert.All(
            operations.OfType<Sdk2DOperation.CameraAabbTiles>(),
            operation => Assert.Equal(WorldTileFlags.Solid, operation.Flags));
    }

    [Theory]
    [InlineData(1_056, 272, 4, 272, 132)]
    [InlineData(2_112, 300, 1, 296, 264)]
    public void Packed_camera_preserves_the_wide_source_column_for_collision_queries(
        int cameraX,
        int queryY,
        int flags,
        int expectedTop,
        int expectedSourceColumn)
    {
        var source = $$"""
            void Main() {
                Video.Init();
                World.Load("assets/maps/stage1.tmj");
                Camera.Init(312, 0, 40);
                i16 cameraX = {{cameraX}};
                i16 cameraY = 176;
                u8 frames = 0;
                i16 hitTop = -1;
                while (true) {
                    Video.WaitVBlank();
                    Camera.Apply();
                    Camera.SetPosition(cameraX, cameraY);
                    if (frames < 200) {
                        frames++;
                    }
                    else {
                        hitTop = Camera.AabbHitTop(0, {{queryY}}, 8, 12, {{flags}});
                    }
                }
            }
            """;
        var result = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
            source,
            RunnerSample.Directory,
            sdkLibraryImports: [SdkImportResolver.Portable2D]);
        var cpu = new GameBoyTestCpu(result.Rom) { CycleAccurateLy = true };

        RunUntilWordEquals(cpu, PackedCameraMemory.VisibleCameraXLow, checked((ushort)cameraX), maxFrames: 1_000);
        cpu.RunAdditionalFrames(300);

        Assert.Equal(expectedSourceColumn, cpu.Wram(0xC0E3) | cpu.Wram(0xC143) << 8);
        Assert.Equal(expectedTop, cpu.Wram(0xC005) | cpu.Wram(0xC006) << 8);
    }

    [Fact]
    public void Short_jump_below_one_way_platform_does_not_snap_the_player_upward()
    {
        var cpu = RunnerAtFirstPlatform();
        var trace = RunJump(cpu, heldFrames: 1, observedFrames: 180);
        var playerYByFrame = trace.PlayerY;

        Assert.True(
            playerYByFrame.Min() + 31 > PlatformTopY,
            $"The short jump unexpectedly crossed the platform top: minimum playerY={playerYByFrame.Min()}.");
        Assert.DoesNotContain(PlatformPlayerY, playerYByFrame);
        Assert.All(playerYByFrame.TakeLast(60), playerY => Assert.Equal(FloorPlayerY, playerY));
    }

    [Fact]
    public void Full_jump_crosses_one_way_platform_from_below_then_lands_on_its_top()
    {
        var cpu = RunnerAtFirstPlatform();
        var trace = RunJump(cpu, heldFrames: 40, observedFrames: 240);
        var playerYByFrame = trace.PlayerY;

        Assert.True(
            playerYByFrame.Min() + 31 < PlatformTopY,
            $"The full jump never crossed the platform top: minimum playerY={playerYByFrame.Min()}, "
            + $"minimum velocity={trace.VelocityY.Min()}, maximum hold ticks={trace.HoldTicks.Max()}, "
            + $"trajectory={string.Join(',', playerYByFrame.Take(40).Zip(trace.VelocityY, (y, velocity) => $"{y}/{velocity}"))}.");
        var landingFrame = Enumerable.Range(0, playerYByFrame.Count)
            .FirstOrDefault(
                index => playerYByFrame[index] == PlatformPlayerY && trace.VelocityY[index] == 0,
                -1);
        Assert.True(
            landingFrame >= 0,
            "The player crossed the platform but never landed on it: "
            + string.Join(',', playerYByFrame.Zip(trace.VelocityY, (y, velocity) => (y, velocity))
                .Where(item => item.velocity > 0 && item.y is >= 210 and <= 280)
                .Select(item => $"{item.y}/{item.velocity}")));
        Assert.All(playerYByFrame.Skip(landingFrame), playerY => Assert.Equal(PlatformPlayerY, playerY));
    }

    [Fact]
    public void Walking_off_one_way_platform_releases_grounded_state_and_falls_to_the_floor()
    {
        var cpu = RunnerAtFirstPlatform();
        var jump = RunJump(cpu, heldFrames: 40, observedFrames: 180);
        Assert.Equal(PlatformPlayerY, jump.PlayerY[^1]);

        var playerYByFrame = RunHorizontal(cpu, "right", observedFrames: 400);

        Assert.True(
            playerYByFrame.Any(playerY => playerY > PlatformPlayerY),
            $"The player never fell after walking to world X {cpu.Wram(0xC000) | cpu.Wram(0xC001) << 8}; "
            + $"camera X={cpu.Wram(PackedCameraMemory.VisibleCameraXLow) | cpu.Wram(PackedCameraMemory.VisibleCameraXLow + 1) << 8}; "
            + $"observed Y values={string.Join(',', playerYByFrame.Distinct())}.");
        Assert.True(
            playerYByFrame.Max() <= FloorPlayerY,
            $"The player crossed the floor before being reset: maximum playerY={playerYByFrame.Max()}.");
        Assert.All(playerYByFrame.TakeLast(40), playerY => Assert.Equal(FloorPlayerY, playerY));
    }

    [Fact]
    public void Shared_runner_stays_on_the_authored_stage1_floor_for_300_frames()
    {
        var runnerDirectory = RunnerSample.Directory;
        var rom = GameBoyRomCompiler.CompileSource(RunnerSample.CompiledSource(), runnerDirectory);
        var cpu = new GameBoyTestCpu(rom) { CycleAccurateLy = true };

        RunUntilWordEquals(cpu, 0xC14F, 176, maxFrames: 400);

        for (var frame = 0; frame < 300; frame++)
        {
            cpu.RunAdditionalFrames(1);

            var cameraY = cpu.Wram(0xC14F) | cpu.Wram(0xC150) << 8;
            var playerWorldY = cpu.Oam(0xFE00) - 16 + cameraY;

            Assert.True(
                playerWorldY == 273 && playerWorldY + 31 == 304,
                $"Mario left the authored floor on frame {frame}: playerY={playerWorldY}, footY={playerWorldY + 31}, "
                + $"cameraY={cameraY}, oamY={cpu.Oam(0xFE00)}.");
        }
    }

    [Fact]
    public void Shared_runner_preserves_source_model_walk_cadence_over_physical_frames()
    {
        var result = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
            RunnerSample.CompiledSource(),
            RunnerSample.Directory,
            sdkLibraryImports: [SdkImportResolver.Portable2D]);
        var cpu = new GameBoyTestCpu(result.Rom)
        {
            CycleAccurateLy = true,
            TracedWorldPackCollisionLookupEntry = result.Report.FixedSymbols[GameBoyRomBuilder.WorldPackCollisionLookupLabel],
        };
        RunUntilWordEquals(cpu, PackedCameraMemory.VisibleCameraYLow, 176, maxFrames: 400);
        cpu.RunAdditionalFrames(30);

        var sourceTicksAtStart = cpu.SourceWaitCompletions;
        var audioTicksAtStart = cpu.AudioUpdateCalls;
        var playerXAtStart = cpu.Wram(PlayerXLow) | cpu.Wram(PlayerXLow + 1) << 8;
        var collisionDecodesAtStart = cpu.Wram(GameBoyRuntimeMemoryLayout.Collision.DecodeCountLow)
                                      | cpu.Wram(GameBoyRuntimeMemoryLayout.Collision.DecodeCountHigh) << 8;
        var previousRequests = cpu.Wram(PackedCameraMemory.RequestCount);
        var previousReleases = cpu.Wram(PackedCameraMemory.ReleaseCount);
        var pendingRequests = new Queue<int>();
        var maximumRequestToVisibleFrames = 0;
        var maximumMissedGameplayFrames = 0;
        var maximumMissedAudioFrames = 0;
        var missedGameplayFrames = 0;
        var missedAudioFrames = 0;
        int? firstMovementFrame = null;
        cpu.Held.Add("right");

        for (var frame = 0; frame < 120; frame++)
        {
            var sourceTicksBefore = cpu.SourceWaitCompletions;
            var audioTicksBefore = cpu.AudioUpdateCalls;
            cpu.RunAdditionalFrames(1);

            var currentPlayerX = cpu.Wram(PlayerXLow) | cpu.Wram(PlayerXLow + 1) << 8;
            if (currentPlayerX > playerXAtStart)
            {
                firstMovementFrame ??= frame;
            }

            var currentRequests = cpu.Wram(PackedCameraMemory.RequestCount);
            while (previousRequests != currentRequests)
            {
                pendingRequests.Enqueue(frame);
                previousRequests++;
            }

            var currentReleases = cpu.Wram(PackedCameraMemory.ReleaseCount);
            while (previousReleases != currentReleases)
            {
                Assert.NotEmpty(pendingRequests);
                maximumRequestToVisibleFrames = Math.Max(
                    maximumRequestToVisibleFrames,
                    frame - pendingRequests.Dequeue());
                previousReleases++;
            }

            missedGameplayFrames = cpu.SourceWaitCompletions == sourceTicksBefore
                ? missedGameplayFrames + 1
                : 0;
            missedAudioFrames = cpu.AudioUpdateCalls == audioTicksBefore
                ? missedAudioFrames + 1
                : 0;
            maximumMissedGameplayFrames = Math.Max(maximumMissedGameplayFrames, missedGameplayFrames);
            maximumMissedAudioFrames = Math.Max(maximumMissedAudioFrames, missedAudioFrames);
        }

        var sourceTicks = cpu.SourceWaitCompletions - sourceTicksAtStart;
        var audioTicks = cpu.AudioUpdateCalls - audioTicksAtStart;
        var playerX = cpu.Wram(PlayerXLow) | cpu.Wram(PlayerXLow + 1) << 8;
        var progress = playerX - playerXAtStart;
        var collisionDecodes = cpu.Wram(GameBoyRuntimeMemoryLayout.Collision.DecodeCountLow)
                               | cpu.Wram(GameBoyRuntimeMemoryLayout.Collision.DecodeCountHigh) << 8;
        var newCollisionDecodes = collisionDecodes - collisionDecodesAtStart;
        var memoHitsModulo256 = cpu.Wram(GameBoyRuntimeMemoryLayout.Collision.MemoHitCount);
        Assert.All(
            new[]
            {
                PackedCameraMemory.BankWorkInCommit,
                PackedCameraMemory.DecodeWorkInCommit,
                PackedCameraMemory.DirectoryWorkInCommit,
                PackedCameraMemory.DirectoryWorkInVBlank,
                PackedCameraMemory.DecodeWorkInVBlank,
            },
            address => Assert.Equal(0, cpu.Wram(address)));

        Assert.True(
            sourceTicks >= 114
            && maximumMissedGameplayFrames <= 1
            && firstMovementFrame is <= 1
            && progress >= 142
            && newCollisionDecodes <= 2
            && maximumRequestToVisibleFrames <= 2
            && pendingRequests.All(requestFrame => 119 - requestFrame <= 2),
            $"Runner gameplay cadence regressed: ticks={sourceTicks}/120, maxMissed={maximumMissedGameplayFrames}, progress={progress}px ({playerXAtStart}->{playerX}), "
            + $"firstMovementFrame={firstMovementFrame}, decodes={newCollisionDecodes}, requestToVisible={maximumRequestToVisibleFrames}, "
            + $"memoHitsMod256={memoHitsModulo256}/{cpu.WorldPackCollisionQueries.Count}, "
            + $"queries={string.Join(',', cpu.WorldPackCollisionQueries.Distinct().OrderBy(query => query.HardwareY).ThenBy(query => query.HardwareX))}.");
        Assert.True(
            audioTicks is >= 119 and <= 121 && maximumMissedAudioFrames <= 1,
            $"Runner audio cadence regressed with gameplay load: ticks={audioTicks}/120, maxMissed={maximumMissedAudioFrames}.");
    }

    [Fact]
    public void Shared_runner_jump_leaves_and_lands_once_on_the_same_stage1_floor()
    {
        var runnerDirectory = RunnerSample.Directory;
        var rom = GameBoyRomCompiler.CompileSource(RunnerSample.CompiledSource(), runnerDirectory);
        var cpu = new GameBoyTestCpu(rom) { CycleAccurateLy = true };

        RunUntilWordEquals(cpu, 0xC14F, 176, maxFrames: 400);
        var playerYByFrame = new List<int>(400);

        for (var frame = 0; frame < 400; frame++)
        {
            if (frame < 40)
            {
                cpu.Held.Add("a");
            }
            else
            {
                cpu.Held.Remove("a");
            }

            cpu.RunAdditionalFrames(1);
            playerYByFrame.Add(cpu.Wram(PlayerYLow) | cpu.Wram(PlayerYLow + 1) << 8);
        }

        Assert.Equal(202, playerYByFrame.Min());
        Assert.True(
            playerYByFrame.Max() <= FloorPlayerY,
            $"The player crossed the floor before landing: maximum playerY={playerYByFrame.Max()}.");

        var leftFloor = false;
        var landingFrames = new List<int>();
        for (var frame = 0; frame < playerYByFrame.Count; frame++)
        {
            if (playerYByFrame[frame] != FloorPlayerY)
            {
                leftFloor = true;
            }
            else if (leftFloor && (frame == 0 || playerYByFrame[frame - 1] != FloorPlayerY))
            {
                landingFrames.Add(frame);
            }
        }

        var landingFrame = Assert.Single(landingFrames);
        Assert.All(playerYByFrame.Skip(landingFrame), y => Assert.Equal(FloorPlayerY, y));
    }

    [Fact]
    public void Shared_runner_held_jump_never_crosses_the_floor_between_gameplay_ticks()
    {
        var rom = GameBoyRomCompiler.CompileSource(RunnerSample.CompiledSource(), RunnerSample.Directory);
        var cpu = new GameBoyTestCpu(rom) { CycleAccurateLy = true };
        RunUntilWordEquals(cpu, PackedCameraMemory.VisibleCameraYLow, 176, maxFrames: 400);
        cpu.RunUntilAudioUpdateCalls(cpu.AudioUpdateCalls + 2);

        var trace = RunJump(cpu, heldFrames: 40, observedFrames: 240);

        Assert.Equal(202, trace.PlayerY.Min());
        Assert.True(
            trace.PlayerY.Max() <= FloorPlayerY,
            $"The player crossed the floor before landing: maximum playerY={trace.PlayerY.Max()}.");
    }

    [Fact]
    public void Shared_runner_uses_smb3_4_4_jump_arcs_for_tap_stand_run_and_p_speed()
    {
        var rom = GameBoyRomCompiler.CompileSource(RunnerSample.CompiledSource(), RunnerSample.Directory);
        var profiles = new[]
        {
            new JumpProfile("tap", RunUpTicks: 0, HeldInputTicks: 1, ExpectedRiseSixteenths: 330),
            new JumpProfile("stand", RunUpTicks: 0, HeldInputTicks: 90, ExpectedRiseSixteenths: 1_131),
            new JumpProfile("run", RunUpTicks: 3, HeldInputTicks: 90, ExpectedRiseSixteenths: 1_361),
            new JumpProfile("p-speed", RunUpTicks: 4, HeldInputTicks: 90, ExpectedRiseSixteenths: 1_607),
        };

        foreach (var profile in profiles)
        {
            var cpu = new GameBoyTestCpu(rom) { CycleAccurateLy = true };
            RunUntilWordEquals(cpu, PackedCameraMemory.VisibleCameraYLow, 176, maxFrames: 400);
            cpu.RunUntilAudioUpdateCalls(cpu.AudioUpdateCalls + 2);
            RunUp(cpu, profile.RunUpTicks);

            var trace = RunSmb3Jump(cpu, profile.HeldInputTicks, observedFrames: 180);
            var minimumPositionSixteenths = trace.PlayerY
                .Zip(trace.VerticalSubpixel, (y, subpixel) => y * 16 + subpixel)
                .Min();
            var riseSixteenths = FloorPlayerY * 16 - minimumPositionSixteenths;

            Assert.True(
                riseSixteenths == profile.ExpectedRiseSixteenths,
                $"The {profile.Name} jump rose {riseSixteenths / 16.0:F4}px "
                + $"({riseSixteenths}/16) instead of {profile.ExpectedRiseSixteenths / 16.0:F4}px "
                + $"({profile.ExpectedRiseSixteenths}/16).");
        }
    }

    [Fact]
    public void Shared_runner_selects_jump_gravity_from_hold_state_and_the_minus_two_threshold_without_clamping()
    {
        var rom = GameBoyRomCompiler.CompileSource(RunnerSample.CompiledSource(), RunnerSample.Directory);
        var probes = new[]
        {
            new GravityProbe(InitialVelocity: -33, IsHeld: true, ExpectedVelocity: -32),
            new GravityProbe(InitialVelocity: -32, IsHeld: true, ExpectedVelocity: -27),
            new GravityProbe(InitialVelocity: -48, IsHeld: false, ExpectedVelocity: -43),
        };

        foreach (var probe in probes)
        {
            var cpu = new GameBoyTestCpu(rom) { CycleAccurateLy = true };
            RunUntilWordEquals(cpu, PackedCameraMemory.VisibleCameraYLow, 176, maxFrames: 400);
            cpu.RunUntilAudioUpdateCalls(cpu.AudioUpdateCalls + 2);
            cpu.Held.Add("a");
            AdvanceGameplayTick(cpu);

            cpu.SetWram(PlayerVelocityY, unchecked((byte)probe.InitialVelocity));
            if (!probe.IsHeld)
            {
                cpu.Held.Remove("a");
            }

            AdvanceGameplayTick(cpu);

            Assert.Equal(probe.ExpectedVelocity, unchecked((sbyte)cpu.Wram(PlayerVelocityY)));
        }
    }

    [Fact]
    public void Fast_fall_crossing_the_floor_lands_instead_of_reaching_the_reset_path()
    {
        var rom = GameBoyRomCompiler.CompileSource(RunnerSample.CompiledSource(), RunnerSample.Directory);
        var cpu = new GameBoyTestCpu(rom) { CycleAccurateLy = true };
        RunUntilWordEquals(cpu, PackedCameraMemory.VisibleCameraYLow, 176, maxFrames: 400);
        cpu.RunUntilAudioUpdateCalls(cpu.AudioUpdateCalls + 2);
        cpu.Held.Clear();

        cpu.SetWram(PlayerYLow, 0x0D);
        cpu.SetWram(PlayerYLow + 1, 0x01);
        cpu.SetWram(PlayerVelocityY, 64);
        cpu.SetWram(PlayerGrounded, 0);
        cpu.SetWram(PlayerJumping, 0);
        cpu.SetWram(PlayerVerticalSubpixelLow, 0);
        cpu.SetWram(PlayerVerticalSubpixelLow + 1, 0);
        cpu.RunUntilAudioUpdateCalls(cpu.AudioUpdateCalls + 1);

        Assert.Equal(FloorPlayerY, cpu.Wram(PlayerYLow) | cpu.Wram(PlayerYLow + 1) << 8);
        Assert.Equal(0, unchecked((sbyte)cpu.Wram(PlayerVelocityY)));
        Assert.Equal(1, cpu.Wram(PlayerGrounded));
    }

    [Fact]
    public void Running_into_the_first_staircase_with_B_never_enters_solid_or_loses_floor_support()
    {
        var rom = GameBoyRomCompiler.CompileSource(RunnerSample.CompiledSource(), RunnerSample.Directory);

        for (var bDelay = 0; bDelay < 9; bDelay++)
        {
            var cpu = new GameBoyTestCpu(rom) { CycleAccurateLy = true };
            RunUntilWordEquals(cpu, PackedCameraMemory.VisibleCameraYLow, 176, maxFrames: 400);
            cpu.RunUntilAudioUpdateCalls(cpu.AudioUpdateCalls + 2);
            cpu.Held.Add("right");
            var trace = new Queue<string>();

            for (var tick = 0; tick < 1_000; tick++)
            {
                if (tick >= bDelay)
                {
                    cpu.Held.Add("b");
                }

                cpu.RunUntilAudioUpdateCalls(cpu.AudioUpdateCalls + 1);
                var playerX = cpu.Wram(PlayerXLow) | cpu.Wram(PlayerXLow + 1) << 8;
                var playerY = cpu.Wram(PlayerYLow) | cpu.Wram(PlayerYLow + 1) << 8;
                trace.Enqueue($"{tick}:{playerX}/{playerY}/{unchecked((sbyte)cpu.Wram(PlayerVelocityY))}/{cpu.Wram(PlayerGrounded)}");
                while (trace.Count > 32)
                {
                    trace.Dequeue();
                }

                Assert.True(
                    playerX + PlayerWidth <= FirstStairWallX &&
                    playerY <= FloorPlayerY &&
                    cpu.Wram(PlayerGrounded) == 1,
                    $"Mario entered the first stair or crossed the floor with B delayed {bDelay} ticks: {string.Join(',', trace)}.");
            }

            var finalPlayerX = cpu.Wram(PlayerXLow) | cpu.Wram(PlayerXLow + 1) << 8;
            Assert.Equal(FirstStairWallX - PlayerWidth, finalPlayerX);
        }
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

    private static GameBoyTestCpu RunnerAtFirstPlatform()
    {
        var source = RunnerSample.CompiledSource();
        var positionedSource = source.Replace(
            "view.y = Camera.VerticalScrollMax();",
            $"view.x = {FirstPlatformCameraX};\n    view.y = Camera.VerticalScrollMax();",
            StringComparison.Ordinal);
        Assert.NotEqual(source, positionedSource);

        var rom = GameBoyRomCompiler.CompileSource(positionedSource, RunnerSample.Directory);
        var cpu = new GameBoyTestCpu(rom) { CycleAccurateLy = true };
        RunUntilWordEquals(cpu, PackedCameraMemory.VisibleCameraXLow, FirstPlatformCameraX, maxFrames: 800);
        RunUntilWordEquals(cpu, PackedCameraMemory.VisibleCameraYLow, 176, maxFrames: 400);
        cpu.RunUntilAudioUpdateCalls(cpu.AudioUpdateCalls + 2);
        return cpu;
    }

    private static JumpTrace RunJump(GameBoyTestCpu cpu, int heldFrames, int observedFrames)
    {
        var playerYByFrame = new List<int>(observedFrames);
        var velocityYByFrame = new List<int>(observedFrames);
        var holdTicksByFrame = new List<int>(observedFrames);
        var verticalSubpixelByFrame = new List<int>(observedFrames);
        for (var frame = 0; frame < observedFrames; frame++)
        {
            if (frame < heldFrames)
            {
                cpu.Held.Add("a");
            }
            else
            {
                cpu.Held.Remove("a");
            }

            cpu.RunUntilAudioUpdateCalls(cpu.AudioUpdateCalls + 1);
            playerYByFrame.Add(cpu.Wram(PlayerYLow) | cpu.Wram(PlayerYLow + 1) << 8);
            velocityYByFrame.Add(unchecked((sbyte)cpu.Wram(PlayerVelocityY)));
            holdTicksByFrame.Add(cpu.Wram(0xC0F2));
            verticalSubpixelByFrame.Add(
                cpu.Wram(PlayerVerticalSubpixelLow) | cpu.Wram(PlayerVerticalSubpixelLow + 1) << 8);
        }

        return new JumpTrace(playerYByFrame, velocityYByFrame, holdTicksByFrame, verticalSubpixelByFrame);
    }

    private static JumpTrace RunSmb3Jump(GameBoyTestCpu cpu, int heldInputTicks, int observedFrames)
    {
        var playerYByFrame = new List<int>(observedFrames);
        var velocityYByFrame = new List<int>(observedFrames);
        var holdTicksByFrame = new List<int>(observedFrames);
        var verticalSubpixelByFrame = new List<int>(observedFrames);
        cpu.Held.Add("a");
        for (var tick = 0; tick < 8 && playerYByFrame.Count == 0; tick++)
        {
            AdvanceGameplayTick(cpu);
            var playerY = cpu.Wram(PlayerYLow) | cpu.Wram(PlayerYLow + 1) << 8;
            var subpixel = cpu.Wram(PlayerVerticalSubpixelLow) | cpu.Wram(PlayerVerticalSubpixelLow + 1) << 8;
            if (playerY * 16 + subpixel < FloorPlayerY * 16)
            {
                playerYByFrame.Add(playerY);
                velocityYByFrame.Add(unchecked((sbyte)cpu.Wram(PlayerVelocityY)));
                holdTicksByFrame.Add(cpu.Wram(0xC0F2));
                verticalSubpixelByFrame.Add(subpixel);
            }
        }
        Assert.Single(playerYByFrame);

        for (var frame = 1; frame < observedFrames; frame++)
        {
            if (frame >= heldInputTicks)
            {
                cpu.Held.Remove("a");
            }

            AdvanceGameplayTick(cpu);
            playerYByFrame.Add(cpu.Wram(PlayerYLow) | cpu.Wram(PlayerYLow + 1) << 8);
            velocityYByFrame.Add(unchecked((sbyte)cpu.Wram(PlayerVelocityY)));
            holdTicksByFrame.Add(cpu.Wram(0xC0F2));
            verticalSubpixelByFrame.Add(
                cpu.Wram(PlayerVerticalSubpixelLow) | cpu.Wram(PlayerVerticalSubpixelLow + 1) << 8);
        }

        return new JumpTrace(playerYByFrame, velocityYByFrame, holdTicksByFrame, verticalSubpixelByFrame);
    }

    private static void AdvanceGameplayTick(GameBoyTestCpu cpu)
    {
        var previousTick = cpu.SourceWaitCompletions;
        for (var frame = 0; frame < 8; frame++)
        {
            cpu.RunAdditionalFrames(1);
            if (cpu.SourceWaitCompletions != previousTick)
            {
                return;
            }
        }

        Assert.Fail("The Game Boy runner did not advance a gameplay tick within eight physical frames.");
    }

    private static void RunUp(GameBoyTestCpu cpu, int ticks)
    {
        if (ticks == 0)
        {
            return;
        }

        cpu.Held.Add("right");
        cpu.Held.Add("b");
        for (var tick = 0; tick < ticks; tick++)
        {
            cpu.RunUntilAudioUpdateCalls(cpu.AudioUpdateCalls + 1);
        }
    }

    private static List<int> RunHorizontal(GameBoyTestCpu cpu, string direction, int observedFrames)
    {
        var playerYByFrame = new List<int>(observedFrames);
        cpu.Held.Add(direction);
        for (var frame = 0; frame < observedFrames; frame++)
        {
            cpu.RunUntilAudioUpdateCalls(cpu.AudioUpdateCalls + 1);
            playerYByFrame.Add(cpu.Wram(PlayerYLow) | cpu.Wram(PlayerYLow + 1) << 8);
        }

        cpu.Held.Remove(direction);
        return playerYByFrame;
    }

    private sealed record JumpTrace(
        List<int> PlayerY,
        List<int> VelocityY,
        List<int> HoldTicks,
        List<int> VerticalSubpixel);

    private sealed record JumpProfile(
        string Name,
        int RunUpTicks,
        int HeldInputTicks,
        int ExpectedRiseSixteenths);

    private sealed record GravityProbe(int InitialVelocity, bool IsHeld, int ExpectedVelocity);

}
