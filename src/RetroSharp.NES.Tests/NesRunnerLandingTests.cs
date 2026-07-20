namespace RetroSharp.NES.Tests;

using RetroSharp.Core.Sdk;
using RetroSharp.NES;
using RetroSharp.Sdk;
using Xunit;

public sealed class NesRunnerLandingTests
{
    private const ushort PlayerXLow = 0x0000;
    private const ushort PlayerYLow = 0x0002;
    private const ushort PlayerVelocityY = 0x0004;
    private const ushort PlayerGrounded = 0x0005;
    private const ushort PlayerJumping = 0x000B;
    private const ushort PlayerVerticalSubpixelLow = 0x000C;
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
        var world = NesTiledWorldImporter.Load(mapPath, NesVideoProgram.FirstSpriteTile + 95);

        Assert.Equal(WorldTileFlags.Platform, world.WorldFlags[34 * world.Width + 94]);
        Assert.Equal(WorldTileFlags.Platform, world.WorldFlags[35 * world.Width + 106]);
        Assert.Equal(WorldTileFlags.Platform, world.WorldFlags[34 * world.Width + 132]);

        var operations = NesRomCompiler.CollectSdkOperations(RunnerSample.CompiledSource(), RunnerSample.Directory);
        var landing = Assert.Single(operations.OfType<Sdk2DOperation.CameraAabbHitTop>());
        Assert.Equal(WorldTileFlags.Solid | WorldTileFlags.Platform, landing.Flags);
        Assert.All(
            operations.OfType<Sdk2DOperation.CameraAabbTiles>(),
            operation => Assert.Equal(WorldTileFlags.Solid, operation.Flags));
    }

    [Fact]
    public void Short_jump_below_one_way_platform_does_not_snap_the_player_upward()
    {
        var cpu = RunnerAtFirstPlatform();
        var playerYByTick = RunJump(cpu, heldTicks: 1, observedTicks: 180);

        Assert.True(
            playerYByTick.Min() + 31 > PlatformTopY,
            $"The short jump unexpectedly crossed the platform top: minimum playerY={playerYByTick.Min()}.");
        Assert.DoesNotContain(PlatformPlayerY, playerYByTick);
        Assert.All(playerYByTick.TakeLast(60), playerY => Assert.Equal(FloorPlayerY, playerY));
    }

    [Fact]
    public void Full_jump_crosses_one_way_platform_from_below_then_lands_on_its_top()
    {
        var cpu = RunnerAtFirstPlatform();
        var playerYByTick = RunJump(cpu, heldTicks: 40, observedTicks: 240);

        var apexFrame = playerYByTick.IndexOf(playerYByTick.Min());
        Assert.True(
            playerYByTick[apexFrame] + 31 < PlatformTopY,
            $"The full jump never crossed the platform top: minimum playerY={playerYByTick[apexFrame]}.");
        var landingFrame = playerYByTick.FindIndex(apexFrame, playerY => playerY == PlatformPlayerY);
        Assert.True(
            landingFrame >= 0,
            "The player crossed the platform but never landed on it: "
            + string.Join(',', playerYByTick.Skip(apexFrame).Where(playerY => playerY is >= 220 and <= 280)));
        Assert.All(playerYByTick.Skip(landingFrame), playerY => Assert.Equal(PlatformPlayerY, playerY));
    }

    [Fact]
    public void Walking_off_one_way_platform_releases_grounded_state_and_falls_to_the_floor()
    {
        var cpu = RunnerAtFirstPlatform();
        var jump = RunJump(cpu, heldTicks: 40, observedTicks: 180);
        Assert.Equal(PlatformPlayerY, jump[^1]);

        var playerYByTick = RunHorizontal(cpu, "right", observedTicks: 180);

        Assert.Contains(playerYByTick, playerY => playerY > PlatformPlayerY);
        Assert.True(
            playerYByTick.Max() <= FloorPlayerY,
            $"The player crossed the floor before being reset: maximum playerY={playerYByTick.Max()}.");
        Assert.All(playerYByTick.TakeLast(40), playerY => Assert.Equal(FloorPlayerY, playerY));
    }

    [Fact]
    public void Shared_runner_held_jump_matches_smb3_standing_height_without_crossing_the_floor()
    {
        var rom = NesRomCompiler.CompileSource(RunnerSample.CompiledSource(), RunnerSample.Directory);
        var cpu = new NesTestCpu(rom);
        RunUntilRamWordEquals(cpu, NesRuntimeMemoryLayout.PackedCamera.VisibleCameraYLow, 80, maxFrames: 400);
        AdvanceGameplayTick(cpu);
        AdvanceGameplayTick(cpu);

        var playerYByTick = RunJump(cpu, heldTicks: 40, observedTicks: 240);

        Assert.Equal(202, playerYByTick.Min());
        Assert.True(
            playerYByTick.Max() <= FloorPlayerY,
            $"The player crossed the floor before landing: maximum playerY={playerYByTick.Max()}.");
    }

    [Fact]
    public void Shared_runner_input_route_respawns_once_at_the_authored_start_and_stays_grounded()
    {
        var build = RetroSharp.NES.NesRomCompiler.CompileSourceWithReport(
            RunnerSample.CompiledSource(),
            RunnerSample.Directory,
            sdkLibraryImports: [SdkImportResolver.Portable2D]);
        var oracleProgram = RetroSharp.NES.NesRomCompiler.PrepareVideoProgram(
            RunnerSample.CompiledSource(),
            RunnerSample.Directory,
            SdkLibraryImportMode.ExplicitOnly,
            sdkLibraryRegistry: null,
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            sdkPluginRegistry: null).VideoProgram;
        var playerSprite = oracleProgram.SpriteAssets["mario_player"];
        var playerPaletteSlot = oracleProgram.ResolveSpritePaletteBaseSlot("mario_player", 0);
        var trackedRom = File.ReadAllBytes(Path.Combine(RunnerSample.Directory, "bin", "runner.nes"));
        Assert.Equal(trackedRom, build.Rom);
        var variables = build.Report.UserVariables.ToDictionary(variable => variable.Name, StringComparer.Ordinal);
        var playerX = variables["player.x"].Address;
        var playerY = variables["player.y"].Address;
        var playerVelocityY = variables["player.velocityY"].Address;
        var playerGrounded = variables["player.grounded"].Address;
        var playerDisplayFrame = variables["player.displayFrame"].Address;
        var playerDisplayFlipX = variables["player.displayFlipX"].Address;
        var cameraX = variables["view.x"].Address;
        var cameraY = variables["view.y"].Address;
        var respawnPhase = variables["frame.respawnPhase"].Address;
        var world = NesTiledWorldImporter.Load(
            Path.Combine(RunnerSample.Directory, "assets", "maps", "stage1.tmj"),
            NesVideoProgram.FirstSpriteTile + 95);
        var cpu = new NesTestCpu(trackedRom);
        cpu.TracedRamBytes.Add(playerY);
        cpu.TracedRamBytes.Add(checked((ushort)(playerY + 1)));
        cpu.TracedRamBytes.Add(respawnPhase);
        cpu.TracedRamBytes.Add(NesRuntimeMemoryLayout.WorldPack.GameplayTickCount);

        RunUntilRamWordEquals(cpu, NesRuntimeMemoryLayout.PackedCamera.VisibleCameraYLow, 80, maxFrames: 400);
        AdvanceGameplayTick(cpu);
        AdvanceGameplayTick(cpu);

        for (var frame = 0; frame < 280; frame++)
        {
            cpu.Held.Clear();
            if (frame < 40)
            {
                cpu.Held.Add("a");
            }

            cpu.RunFrames(cpu.PhysicalFrames + 1);
        }

        Assert.DoesNotContain(cpu.RamByteWrites, write => write.Address == respawnPhase && write.Value != 0);
        Assert.Equal(273, RamWord(cpu, playerY));
        Assert.Equal(1, cpu.Ram(playerGrounded));

        var recentRequestedCameras = new Queue<(int X, int Y, int Frame)>();
        var respawnRouteFrame = -1;
        for (var frame = 0; frame < 2_000 && respawnRouteFrame < 0; frame++)
        {
            cpu.Held.Clear();
            cpu.Held.Add("right");
            cpu.Held.Add("b");
            if (frame >= 270 && (frame - 270) % 40 < 22)
            {
                cpu.Held.Add("a");
            }

            cpu.RunFrames(cpu.PhysicalFrames + 1);
            recentRequestedCameras.Enqueue((
                cpu.Ram(NesRuntimeMemoryLayout.Camera.X) | cpu.Ram(NesRuntimeMemoryLayout.Camera.XHigh) << 8,
                cpu.Ram(NesRuntimeMemoryLayout.Camera.Y) | cpu.Ram(NesRuntimeMemoryLayout.Camera.YHigh) << 8,
                frame));
            while (recentRequestedCameras.Count > 4)
            {
                recentRequestedCameras.Dequeue();
            }

            if (cpu.Ram(respawnPhase) != 0)
            {
                respawnRouteFrame = frame;
            }
        }

        var respawnEntryWrite = Assert.Single(
            cpu.RamByteWrites,
            write => write.Address == respawnPhase && write.Value == 1);
        var playerYWrites = cpu.RamByteWrites
            .Where(write => (write.Address == playerY || write.Address == playerY + 1) && write.Cycle < respawnEntryWrite.Cycle)
            .ToArray();
        var highWriteIndex = Array.FindLastIndex(playerYWrites, write => write.Address == playerY + 1);
        Assert.True(
            highWriteIndex > 0 && playerYWrites[highWriteIndex - 1].Address == playerY,
            "The observed player.y byte writes did not end in one adjacent low/high pair.");
        var lowWrite = playerYWrites[highWriteIndex - 1];
        var highWrite = playerYWrites[highWriteIndex];
        var fallY = lowWrite.Value | highWrite.Value << 8;
        Assert.True(
            fallY >= 320,
            $"Respawning began before a complete out-of-world player.y assignment: y={fallY}, lowCycle={lowWrite.Cycle}, highCycle={highWrite.Cycle}, phaseCycle={respawnEntryWrite.Cycle}.");

        cpu.Held.Clear();
        cpu.Held.Add("left");
        cpu.Held.Add("a");
        cpu.Held.Add("b");
        var requestedFrames = new Dictionary<(int X, int Y), int>();
        (int X, int Y)? previousRequestedCamera = null;
        foreach (var requested in recentRequestedCameras)
        {
            var requestedCamera = (requested.X, requested.Y);
            RecordRequestedCameraWalk(
                requestedFrames,
                previousRequestedCamera ?? requestedCamera,
                requestedCamera,
                requested.Frame - respawnRouteFrame);
            previousRequestedCamera = requestedCamera;
        }

        var previousCamera = (X: RamWord(cpu, cameraX), Y: RamWord(cpu, cameraY));
        var frozenVelocityY = cpu.Ram(playerVelocityY);
        var frozenDisplayFrame = cpu.Ram(playerDisplayFrame);
        var frozenDisplayFlipX = cpu.Ram(playerDisplayFlipX) != 0;
        var retainedOam = Enumerable.Range(0, 0x100).Select(index => cpu.Oam((byte)index)).ToArray();
        var oamWritesAtRespawn = cpu.OamWrites.Count;
        var playerOamBytes = playerSprite.Pieces.Count * 4;
        var transitionOam = ExpectedRunnerOam(playerSprite, retainedOam, frozenDisplayFrame, frozenDisplayFlipX, playerPaletteSlot);
        var respawnOamPublished = false;
        var transitionGameplayTicks = 0;
        var transitionAudioTicks = 0;
        var missedGameplayFrames = 0;
        var missedAudioFrames = 0;
        var maximumMissedGameplayFrames = 0;
        var maximumMissedAudioFrames = 0;
        var transitionFrames = 0;
        while (cpu.Ram(respawnPhase) != 0 && transitionFrames < 400)
        {
            var gameplayTicksBefore = cpu.Ram(NesRuntimeMemoryLayout.WorldPack.GameplayTickCount);
            var audioTicksBefore = cpu.Ram(NesRuntimeMemoryLayout.WorldPack.AudioTickCount);
            cpu.RunFrames(cpu.PhysicalFrames + 1);
            var gameplayAdvance = unchecked((byte)(cpu.Ram(NesRuntimeMemoryLayout.WorldPack.GameplayTickCount) - gameplayTicksBefore));
            var audioAdvance = unchecked((byte)(cpu.Ram(NesRuntimeMemoryLayout.WorldPack.AudioTickCount) - audioTicksBefore));
            Assert.InRange(gameplayAdvance, (byte)0, (byte)1);
            transitionGameplayTicks += gameplayAdvance;
            transitionAudioTicks += audioAdvance;
            missedGameplayFrames = gameplayAdvance == 0 ? missedGameplayFrames + 1 : 0;
            missedAudioFrames = audioAdvance == 0 ? missedAudioFrames + 1 : 0;
            maximumMissedGameplayFrames = Math.Max(maximumMissedGameplayFrames, missedGameplayFrames);
            maximumMissedAudioFrames = Math.Max(maximumMissedAudioFrames, missedAudioFrames);

            var sourceCamera = (X: RamWord(cpu, cameraX), Y: RamWord(cpu, cameraY));
            Assert.InRange(Math.Abs(sourceCamera.X - previousCamera.X), 0, 8);
            Assert.InRange(Math.Abs(sourceCamera.Y - previousCamera.Y), 0, 8);
            Assert.False(sourceCamera.X != previousCamera.X && sourceCamera.Y != previousCamera.Y);
            var requestedCamera = (
                X: cpu.Ram(NesRuntimeMemoryLayout.Camera.X) | cpu.Ram(NesRuntimeMemoryLayout.Camera.XHigh) << 8,
                Y: cpu.Ram(NesRuntimeMemoryLayout.Camera.Y) | cpu.Ram(NesRuntimeMemoryLayout.Camera.YHigh) << 8);
            RecordRequestedCameraWalk(requestedFrames, previousRequestedCamera ?? requestedCamera, requestedCamera, transitionFrames);
            previousRequestedCamera = requestedCamera;

            var visibleCamera = (
                X: RamWord(cpu, NesRuntimeMemoryLayout.PackedCamera.VisibleCameraXLow),
                Y: RamWord(cpu, NesRuntimeMemoryLayout.PackedCamera.VisibleCameraYLow));
            Assert.True(
                requestedFrames.TryGetValue(visibleCamera, out var requestedFrame)
                && transitionFrames - requestedFrame <= 2,
                $"Visible camera {visibleCamera} missed the two-frame lifecycle bound on respawn frame {transitionFrames}; target={sourceCamera}; requested={requestedCamera}; requests={string.Join(",", requestedFrames.OrderBy(pair => pair.Value).Select(pair => $"{pair.Key}@{pair.Value}"))}.");
            AssertVisibleBackgroundMatchesWorld(cpu, world, visibleCamera.X, visibleCamera.Y, transitionFrames);

            if (cpu.Ram(respawnPhase) != 0)
            {
                Assert.Equal(0, cpu.Ram(playerGrounded));
                Assert.Equal(frozenVelocityY, cpu.Ram(playerVelocityY));
                Assert.Equal(frozenDisplayFrame, cpu.Ram(playerDisplayFrame));
                Assert.Equal(frozenDisplayFlipX, cpu.Ram(playerDisplayFlipX) != 0);
                respawnOamPublished = AssertOamPublicationSequence(
                    cpu,
                    oamWritesAtRespawn,
                    retainedOam,
                    transitionOam,
                    playerOamBytes,
                    "transition",
                    transitionFrames);

                if (cpu.Ram(respawnPhase) >= 2)
                {
                    cpu.Held.Clear();
                }
            }

            previousCamera = sourceCamera;
            transitionFrames++;
        }

        Assert.InRange(transitionFrames, 1, 400);
        Assert.True(respawnOamPublished, "NES never published the target-correct respawn OAM pose.");
        Assert.True(
            transitionGameplayTicks * 100 >= transitionFrames * 95 && maximumMissedGameplayFrames <= 1,
            $"Respawn gameplay cadence regressed: ticks={transitionGameplayTicks}/{transitionFrames}, maxMiss={maximumMissedGameplayFrames}.");
        Assert.True(
            maximumMissedAudioFrames <= 1 && Math.Abs(transitionAudioTicks - transitionFrames) <= 1,
            $"Respawn audio cadence regressed: audio={transitionAudioTicks}/{transitionFrames}, gameplay={transitionGameplayTicks}, maxAudioMiss={maximumMissedAudioFrames}.");
        var firstRespawn = RunnerState(cpu, playerX, playerY, playerVelocityY, playerGrounded, cameraX, cameraY);
        Assert.True(IsRunnerSpawn(firstRespawn, 80), $"Respawn completed with {firstRespawn}.");
        Assert.True(
            VisibleCameraIsAtSpawn(cpu, 80),
            $"NES visible camera was ({RamWord(cpu, NesRuntimeMemoryLayout.PackedCamera.VisibleCameraXLow)},{RamWord(cpu, NesRuntimeMemoryLayout.PackedCamera.VisibleCameraYLow)}) when respawn phase completed after {transitionFrames} frames.");
        Assert.True(
            AssertOamPublicationSequence(cpu, oamWritesAtRespawn, retainedOam, transitionOam, playerOamBytes, "transition", transitionFrames),
            "NES never completed the target-correct transition metasprite publication.");
        var settledOam = ExpectedRunnerOam(playerSprite, transitionOam, 0, frozenDisplayFlipX, playerPaletteSlot);
        var settledOamPublished = frozenDisplayFrame == 0;
        var oamWritesAtSettling = cpu.OamWrites.Count;
        Assert.Equal(new[] { 1, 2, 3, 4, 0 }, cpu.RamByteWrites
            .Where(write => write.Address == respawnPhase && write.Cycle >= respawnEntryWrite.Cycle)
            .Select(write => (int)write.Value)
            .ToArray());

        cpu.Held.Clear();
        for (var frame = 0; frame < 300; frame++)
        {
            var gameplayTicksBefore = cpu.Ram(NesRuntimeMemoryLayout.WorldPack.GameplayTickCount);
            cpu.RunFrames(cpu.PhysicalFrames + 1);
            var gameplayAdvance = unchecked((byte)(cpu.Ram(NesRuntimeMemoryLayout.WorldPack.GameplayTickCount) - gameplayTicksBefore));
            Assert.InRange(gameplayAdvance, (byte)0, (byte)1);
            var stableState = RunnerState(cpu, playerX, playerY, playerVelocityY, playerGrounded, cameraX, cameraY);
            Assert.True(IsRunnerSpawn(stableState, 80), $"NES spawn changed on idle frame {frame}: {stableState}.");
            Assert.True(VisibleCameraIsAtSpawn(cpu, 80));
            Assert.Equal(0, cpu.Ram(respawnPhase));
            settledOamPublished = AssertOamPublicationSequence(
                cpu,
                oamWritesAtSettling,
                transitionOam,
                settledOam,
                playerOamBytes,
                "settled",
                frame);
            AssertVisibleBackgroundMatchesWorld(cpu, world, 0, 80, frame);
        }
        Assert.True(settledOamPublished, "NES never published the settled respawn OAM frame.");
        Assert.DoesNotContain(
            cpu.OamWrites.Skip(oamWritesAtRespawn),
            write => write.Address >= NesRuntimeMemoryLayout.Sprite.OamShadow + playerOamBytes);
    }

    [Fact]
    public void Shared_runner_uses_smb3_4_4_jump_arcs_for_tap_stand_run_and_p_speed()
    {
        var rom = NesRomCompiler.CompileSource(RunnerSample.CompiledSource(), RunnerSample.Directory);
        var profiles = new[]
        {
            new JumpProfile("tap", RunUpTicks: 0, HeldInputTicks: 1, ExpectedRiseSixteenths: 330),
            new JumpProfile("stand", RunUpTicks: 0, HeldInputTicks: 90, ExpectedRiseSixteenths: 1_131),
            new JumpProfile("run", RunUpTicks: 3, HeldInputTicks: 90, ExpectedRiseSixteenths: 1_361),
            new JumpProfile("p-speed", RunUpTicks: 4, HeldInputTicks: 90, ExpectedRiseSixteenths: 1_607),
        };

        foreach (var profile in profiles)
        {
            var cpu = new NesTestCpu(rom);
            RunUntilRamWordEquals(cpu, NesRuntimeMemoryLayout.PackedCamera.VisibleCameraYLow, 80, maxFrames: 400);
            AdvanceGameplayTick(cpu);
            AdvanceGameplayTick(cpu);
            RunUp(cpu, profile.RunUpTicks);

            var trace = RunJumpTrace(cpu, profile.HeldInputTicks, observedTicks: 180);
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
        var rom = NesRomCompiler.CompileSource(RunnerSample.CompiledSource(), RunnerSample.Directory);
        var probes = new[]
        {
            new GravityProbe(InitialVelocity: -33, IsHeld: true, ExpectedVelocity: -32),
            new GravityProbe(InitialVelocity: -32, IsHeld: true, ExpectedVelocity: -27),
            new GravityProbe(InitialVelocity: -48, IsHeld: false, ExpectedVelocity: -43),
        };

        foreach (var probe in probes)
        {
            var cpu = new NesTestCpu(rom);
            RunUntilRamWordEquals(cpu, NesRuntimeMemoryLayout.PackedCamera.VisibleCameraYLow, 80, maxFrames: 400);
            AdvanceGameplayTick(cpu);
            AdvanceGameplayTick(cpu);
            cpu.Held.Add("a");
            AdvanceGameplayTick(cpu);

            cpu.SetRam(PlayerVelocityY, unchecked((byte)probe.InitialVelocity));
            if (!probe.IsHeld)
            {
                cpu.Held.Remove("a");
            }

            AdvanceGameplayTick(cpu);

            Assert.Equal(probe.ExpectedVelocity, unchecked((sbyte)cpu.Ram(PlayerVelocityY)));
        }
    }

    [Fact]
    public void Fast_fall_crossing_the_floor_lands_instead_of_reaching_the_reset_path()
    {
        var rom = NesRomCompiler.CompileSource(RunnerSample.CompiledSource(), RunnerSample.Directory);
        var cpu = new NesTestCpu(rom);
        RunUntilRamWordEquals(cpu, NesRuntimeMemoryLayout.PackedCamera.VisibleCameraYLow, 80, maxFrames: 400);
        AdvanceGameplayTick(cpu);
        AdvanceGameplayTick(cpu);
        cpu.Held.Clear();

        cpu.SetRam(PlayerYLow, 0x0D);
        cpu.SetRam(PlayerYLow + 1, 0x01);
        cpu.SetRam(PlayerVelocityY, 64);
        cpu.SetRam(PlayerGrounded, 0);
        cpu.SetRam(PlayerJumping, 0);
        cpu.SetRam(PlayerVerticalSubpixelLow, 0);
        cpu.SetRam(PlayerVerticalSubpixelLow + 1, 0);
        AdvanceGameplayTick(cpu);

        Assert.Equal(FloorPlayerY, RamWord(cpu, PlayerYLow));
        Assert.Equal(0, unchecked((sbyte)cpu.Ram(PlayerVelocityY)));
        Assert.Equal(1, cpu.Ram(PlayerGrounded));
    }

    [Fact]
    public void Running_into_the_first_staircase_with_B_never_enters_solid_or_loses_floor_support()
    {
        var rom = NesRomCompiler.CompileSource(RunnerSample.CompiledSource(), RunnerSample.Directory);

        for (var bDelay = 0; bDelay < 9; bDelay++)
        {
            var cpu = new NesTestCpu(rom);
            RunUntilRamWordEquals(cpu, NesRuntimeMemoryLayout.PackedCamera.VisibleCameraYLow, 80, maxFrames: 400);
            AdvanceGameplayTick(cpu);
            AdvanceGameplayTick(cpu);
            cpu.Held.Add("right");
            var trace = new Queue<string>();

            for (var tick = 0; tick < 250; tick++)
            {
                if (tick >= bDelay)
                {
                    cpu.Held.Add("b");
                }

                AdvanceGameplayTick(cpu);
                var playerX = RamWord(cpu, PlayerXLow);
                var playerY = RamWord(cpu, PlayerYLow);
                trace.Enqueue($"{tick}:{playerX}/{playerY}/{unchecked((sbyte)cpu.Ram(PlayerVelocityY))}/{cpu.Ram(PlayerGrounded)}");
                while (trace.Count > 32)
                {
                    trace.Dequeue();
                }

                Assert.True(
                    playerX + PlayerWidth <= FirstStairWallX &&
                    playerY <= FloorPlayerY &&
                    cpu.Ram(PlayerGrounded) == 1,
                    $"Mario entered the first stair or crossed the floor with B delayed {bDelay} ticks: {string.Join(',', trace)}.");
            }

            Assert.Equal(FirstStairWallX - PlayerWidth, RamWord(cpu, PlayerXLow));
        }
    }

    private static NesTestCpu RunnerAtFirstPlatform()
    {
        var source = RunnerSample.CompiledSource();
        var positionedSource = source.Replace(
            "view.y = Camera.VerticalScrollMax();",
            $"view.x = {FirstPlatformCameraX};\n    view.y = Camera.VerticalScrollMax();",
            StringComparison.Ordinal);
        Assert.NotEqual(source, positionedSource);

        var rom = NesRomCompiler.CompileSource(positionedSource, RunnerSample.Directory);
        var cpu = new NesTestCpu(rom);
        RunUntilRamWordEquals(cpu, NesRuntimeMemoryLayout.PackedCamera.VisibleCameraXLow, FirstPlatformCameraX, maxFrames: 800);
        RunUntilRamWordEquals(cpu, NesRuntimeMemoryLayout.PackedCamera.VisibleCameraYLow, 80, maxFrames: 400);
        AdvanceGameplayTick(cpu);
        AdvanceGameplayTick(cpu);
        return cpu;
    }

    private static List<int> RunJump(NesTestCpu cpu, int heldTicks, int observedTicks)
    {
        var playerYByTick = new List<int>(observedTicks);
        for (var tick = 0; tick < observedTicks; tick++)
        {
            if (tick < heldTicks)
            {
                cpu.Held.Add("a");
            }
            else
            {
                cpu.Held.Remove("a");
            }

            AdvanceGameplayTick(cpu);
            playerYByTick.Add(RamWord(cpu, PlayerYLow));
        }

        return playerYByTick;
    }

    private static JumpTrace RunJumpTrace(NesTestCpu cpu, int heldInputTicks, int observedTicks)
    {
        var playerYByTick = new List<int>(observedTicks);
        var verticalSubpixelByTick = new List<int>(observedTicks);
        cpu.Held.Add("a");
        for (var tick = 0; tick < 8 && playerYByTick.Count == 0; tick++)
        {
            AdvanceGameplayTick(cpu);
            var playerY = RamWord(cpu, PlayerYLow);
            var subpixel = RamWord(cpu, PlayerVerticalSubpixelLow);
            if (playerY * 16 + subpixel < FloorPlayerY * 16)
            {
                playerYByTick.Add(playerY);
                verticalSubpixelByTick.Add(subpixel);
            }
        }
        Assert.Single(playerYByTick);

        for (var tick = 1; tick < observedTicks; tick++)
        {
            if (tick >= heldInputTicks)
            {
                cpu.Held.Remove("a");
            }

            AdvanceGameplayTick(cpu);
            playerYByTick.Add(RamWord(cpu, PlayerYLow));
            verticalSubpixelByTick.Add(RamWord(cpu, PlayerVerticalSubpixelLow));
        }

        return new JumpTrace(playerYByTick, verticalSubpixelByTick);
    }

    private static void RunUp(NesTestCpu cpu, int ticks)
    {
        if (ticks == 0)
        {
            return;
        }

        cpu.Held.Add("right");
        cpu.Held.Add("b");
        for (var tick = 0; tick < ticks; tick++)
        {
            AdvanceGameplayTick(cpu);
        }
    }

    private static List<int> RunHorizontal(NesTestCpu cpu, string direction, int observedTicks)
    {
        var playerYByTick = new List<int>(observedTicks);
        cpu.Held.Add(direction);
        for (var tick = 0; tick < observedTicks; tick++)
        {
            AdvanceGameplayTick(cpu);
            playerYByTick.Add(RamWord(cpu, PlayerYLow));
        }

        cpu.Held.Remove(direction);
        return playerYByTick;
    }

    private static void AdvanceGameplayTick(NesTestCpu cpu)
    {
        var previousTick = cpu.Ram(NesRuntimeMemoryLayout.WorldPack.GameplayTickCount);
        for (var frame = 0; frame < 8; frame++)
        {
            cpu.RunFrames(cpu.PhysicalFrames + 1);
            if (cpu.Ram(NesRuntimeMemoryLayout.WorldPack.GameplayTickCount) != previousTick)
            {
                return;
            }
        }

        Assert.Fail("The NES runner did not advance a gameplay tick within eight physical frames.");
    }

    private static void RunUntilRamWordEquals(NesTestCpu cpu, ushort lowAddress, int expected, int maxFrames)
    {
        for (var frame = 0; frame < maxFrames; frame++)
        {
            if (RamWord(cpu, lowAddress) == expected)
            {
                return;
            }

            cpu.RunFrames(cpu.PhysicalFrames + 1);
        }

        Assert.Fail($"RAM word 0x{lowAddress:X4} did not reach {expected} within {maxFrames} frames.");
    }

    private static int RamWord(NesTestCpu cpu, ushort lowAddress) =>
        cpu.Ram(lowAddress) | cpu.Ram((ushort)(lowAddress + 1)) << 8;

    private static RespawnState RunnerState(
        NesTestCpu cpu,
        ushort playerX,
        ushort playerY,
        ushort playerVelocityY,
        ushort playerGrounded,
        ushort cameraX,
        ushort cameraY)
    {
        return new RespawnState(
            RamWord(cpu, playerX),
            RamWord(cpu, playerY),
            unchecked((sbyte)cpu.Ram(playerVelocityY)),
            cpu.Ram(playerGrounded),
            RamWord(cpu, cameraX),
            RamWord(cpu, cameraY));
    }

    private static bool IsRunnerSpawn(RespawnState state, int expectedCameraY) =>
        state == new RespawnState(72, 273, 0, 1, 0, expectedCameraY);

    private static bool VisibleCameraIsAtSpawn(NesTestCpu cpu, int expectedCameraY) =>
        RamWord(cpu, NesRuntimeMemoryLayout.PackedCamera.VisibleCameraXLow) == 0
        && RamWord(cpu, NesRuntimeMemoryLayout.PackedCamera.VisibleCameraYLow) == expectedCameraY;

    private static void RecordRequestedCameraWalk(
        IDictionary<(int X, int Y), int> requestedFrames,
        (int X, int Y) from,
        (int X, int Y) to,
        int frame)
    {
        if (from.X != to.X)
        {
            foreach (var x in Enumerable.Range(Math.Min(from.X, to.X), Math.Abs(to.X - from.X) + 1))
            {
                requestedFrames[(x, from.Y)] = frame;
            }
        }
        if (from.Y != to.Y)
        {
            foreach (var y in Enumerable.Range(Math.Min(from.Y, to.Y), Math.Abs(to.Y - from.Y) + 1))
            {
                requestedFrames[(to.X, y)] = frame;
            }
        }
        if (from == to)
        {
            requestedFrames[to] = frame;
        }
    }

    private static byte[] ExpectedRunnerOam(
        NesCompiledSpriteAsset asset,
        byte[] baseline,
        int animationFrame,
        bool flipX,
        int paletteSlot)
    {
        Assert.InRange(animationFrame, 0, asset.FrameCount - 1);
        var expected = baseline.ToArray();
        for (var sprite = 0; sprite < asset.Pieces.Count; sprite++)
        {
            var piece = asset.Pieces[sprite];
            var offset = sprite * 4;
            expected[offset] = checked((byte)(193 - 1 + piece.YOffset));
            expected[offset + 1] = checked((byte)(asset.FirstTile + animationFrame * asset.TilesPerFrame + piece.TileOffset));
            expected[offset + 2] = checked((byte)(paletteSlot + piece.PaletteSlotOffset + (flipX ? 0x40 : 0)));
            expected[offset + 3] = checked((byte)(72 + (flipX ? asset.LogicalWidth - 8 - piece.XOffset : piece.XOffset)));
        }

        return expected;
    }

    private static bool AssertOamPublicationSequence(
        NesTestCpu cpu,
        int firstOamWrite,
        byte[] retained,
        byte[] expected,
        int byteCount,
        string phase,
        int frame)
    {
        var writes = cpu.OamWrites.Skip(firstOamWrite).ToArray();
        var completePublications = new List<byte[]>();
        for (var start = 0; start < writes.Length; start++)
        {
            if (writes[start].Address != NesRuntimeMemoryLayout.Sprite.OamShadow || start + byteCount > writes.Length)
            {
                continue;
            }

            var publication = writes.AsSpan(start, byteCount);
            if (!publication.ToArray().Select((write, offset) => write.Address == NesRuntimeMemoryLayout.Sprite.OamShadow + offset).All(matches => matches))
            {
                continue;
            }

            completePublications.Add(publication.ToArray().Select(write => write.Value).ToArray());
            start += byteCount - 1;
        }

        var expectedPrefix = expected[..byteCount];
        var retainedPrefix = retained[..byteCount];
        var targetPublished = false;
        var retainedPublications = 0;
        foreach (var publication in completePublications)
        {
            if (publication.SequenceEqual(expectedPrefix))
            {
                targetPublished = true;
                continue;
            }

            Assert.False(targetPublished, $"NES OAM regressed after publishing the {phase} metasprite on frame {frame}.");
            Assert.True(
                publication.SequenceEqual(retainedPrefix),
                $"NES {phase} OAM publication was neither retained nor expected on frame {frame}: publication={Convert.ToHexString(publication)}, retained={Convert.ToHexString(retainedPrefix)}, expected={Convert.ToHexString(expectedPrefix)}.");
            retainedPublications++;
            Assert.InRange(retainedPublications, 0, 1);
        }

        return targetPublished;
    }

    private static void AssertVisibleBackgroundMatchesWorld(
        NesTestCpu cpu,
        NesTiledWorld world,
        int cameraX,
        int cameraY,
        int frame)
    {
        var width = cameraX % 8 == 0 ? 32 : 33;
        var height = cameraY % 8 == 0 ? 30 : 31;
        var startColumn = cameraX / 8;
        var startRow = cameraY / 8;
        for (var screenRow = 0; screenRow < height; screenRow++)
        {
            for (var screenColumn = 0; screenColumn < width; screenColumn++)
            {
                var sourceColumn = (startColumn + screenColumn) % world.Width;
                var sourceRow = (startRow + screenRow) % world.Height;
                var expected = world.WorldTileIds[sourceRow * world.Width + sourceColumn];
                var bufferColumn = (startColumn + screenColumn) % 64;
                var bufferRow = (startRow + screenRow) % 60;
                var nametable = 0x2000 + bufferRow / 30 * 0x800 + bufferColumn / 32 * 0x400;
                var address = checked((ushort)(nametable + bufferRow % 30 * 32 + bufferColumn % 32));
                var actual = cpu.PpuVram(address);
                Assert.True(
                    actual == expected,
                    $"Runner background mismatch on respawn frame {frame} at screen ({screenColumn},{screenRow}), "
                    + $"source ({sourceColumn},{sourceRow}), address ${address:X4}: expected={expected}, actual={actual}.");
            }
        }
    }

    private sealed record JumpTrace(List<int> PlayerY, List<int> VerticalSubpixel);

    private sealed record JumpProfile(
        string Name,
        int RunUpTicks,
        int HeldInputTicks,
        int ExpectedRiseSixteenths);

    private sealed record GravityProbe(int InitialVelocity, bool IsHeld, int ExpectedVelocity);

    private sealed record RespawnState(
        int PlayerX,
        int PlayerY,
        int PlayerVelocityY,
        int PlayerGrounded,
        int CameraX,
        int CameraY);
}
