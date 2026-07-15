namespace RetroSharp.NES.Tests;

using RetroSharp.Core.Sdk;
using RetroSharp.NES;
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
        RunUntilRamWordEquals(cpu, NesPackedCameraRuntime.VisibleCameraYLow, 80, maxFrames: 400);
        AdvanceGameplayTick(cpu);
        AdvanceGameplayTick(cpu);

        var playerYByTick = RunJump(cpu, heldTicks: 40, observedTicks: 240);

        Assert.Equal(202, playerYByTick.Min());
        Assert.True(
            playerYByTick.Max() <= FloorPlayerY,
            $"The player crossed the floor before landing: maximum playerY={playerYByTick.Max()}.");
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
            RunUntilRamWordEquals(cpu, NesPackedCameraRuntime.VisibleCameraYLow, 80, maxFrames: 400);
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
            RunUntilRamWordEquals(cpu, NesPackedCameraRuntime.VisibleCameraYLow, 80, maxFrames: 400);
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
        RunUntilRamWordEquals(cpu, NesPackedCameraRuntime.VisibleCameraYLow, 80, maxFrames: 400);
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
            RunUntilRamWordEquals(cpu, NesPackedCameraRuntime.VisibleCameraYLow, 80, maxFrames: 400);
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
        RunUntilRamWordEquals(cpu, NesPackedCameraRuntime.VisibleCameraXLow, FirstPlatformCameraX, maxFrames: 800);
        RunUntilRamWordEquals(cpu, NesPackedCameraRuntime.VisibleCameraYLow, 80, maxFrames: 400);
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
        var previousTick = cpu.Ram(NesWorldPackRuntimeAbi.GameplayTickCount);
        for (var frame = 0; frame < 8; frame++)
        {
            cpu.RunFrames(cpu.PhysicalFrames + 1);
            if (cpu.Ram(NesWorldPackRuntimeAbi.GameplayTickCount) != previousTick)
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

    private sealed record JumpTrace(List<int> PlayerY, List<int> VerticalSubpixel);

    private sealed record JumpProfile(
        string Name,
        int RunUpTicks,
        int HeldInputTicks,
        int ExpectedRiseSixteenths);

    private sealed record GravityProbe(int InitialVelocity, bool IsHeld, int ExpectedVelocity);
}
