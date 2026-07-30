namespace RetroSharp.GameBoy.Tests;

using RetroSharp.Core.Sdk;
using RetroSharp.Core.Sdk.Tiled;
using RetroSharp.Core.Targeting;
using RetroSharp.GameBoy;
using RetroSharp.Parser;
using RetroSharp.Sdk;
using Xunit;
using static RetroSharp.GameBoy.Tests.GameBoyTestSupport;

public partial class GameBoyRomCompilerTests
{
    [Fact]
    public void Comments_do_not_affect_game_boy_rom_bytes()
    {
        const string withoutComments = """
                                       void Main() {
                                           Video.Init();
                                           Palette.Background(0, 0, 1, 2, 3);
                                       }
                                       """;
        const string withComments = """
                                    // Source-only documentation.
                                    void Main() {
                                        Video.Init(); /* zero-cost comment */
                                        Palette.Background(0, 0, 1, 2, 3);
                                    }
                                    """;

        Assert.Equal(
            GameBoyRomCompiler.CompileSource(withoutComments),
            GameBoyRomCompiler.CompileSource(withComments));
    }

    [Fact]
    public void Portable2D_import_does_not_affect_game_boy_rom_bytes()
    {
        const string implicitSdk = """
                                   void Main() {
                                       Video.WaitVBlank();
                                   }
                                   """;
        const string explicitSdk = """
                                   import RetroSharp.Portable2D;

                                   void Main() {
                                       Video.WaitVBlank();
                                   }
                                   """;

        Assert.Equal(
            GameBoyRomCompiler.CompileSource(implicitSdk),
            GameBoyRomCompiler.CompileSource(explicitSdk));
    }

    [Fact]
    public void GameBoy_runner_uses_constant_groups_and_lightweight_player_state()
    {
        var source = RunnerSample.FlattenedSource();

        Assert.Contains("static class Level", source);
        Assert.Contains("Width = 312", source);
        Assert.Contains("Height = 40", source);
        Assert.Contains("StreamHeight = 40", source);
        Assert.DoesNotContain("SignedVelocityWrap", source);
        Assert.Contains("PixelWidth = 2496", source);
        Assert.Contains("static class Player", source);
        Assert.Contains("StartX = 72", source);
        Assert.Contains("class PlayerState", source);
        Assert.Contains("void Land(Pixel targetY)", source);
        Assert.Contains("inline void ApplyGravity()", source);
        Assert.Contains("PlayerState player;", source);
        Assert.Contains("player.Land(Player.StartY);", source);
        Assert.Contains("frame.AdvanceRespawn(player, view);", source);
        Assert.Contains("player.ApplyGravity();", source);
        Assert.Contains("""World.Load("assets/maps/stage1.tmj");""", source);
        Assert.Contains("LoadWorld();", source);
        Assert.Contains("Sprite.Draw(mario_player, screenX, screenY", source);
        Assert.DoesNotContain("const WorldWidth", source);
        Assert.DoesNotContain("const PlayerScreenX", source);
        Assert.DoesNotContain("Pixel playerY =", source);
        Assert.DoesNotContain("Pixel velocityY =", source);
    }

    [Fact]
    public void GameBoy_runner_extracts_frame_loop_into_named_inline_helpers()
    {
        var source = RunnerSample.FlattenedSource();

        Assert.Contains("class CameraState", source);
        Assert.Contains("class FrameState", source);
        Assert.Contains("inline void PresentFrame(PlayerState player, CameraState view)", source);
        Assert.Contains("inline void HandleJumpInput(u8 horizontalSpeed)", source);
        Assert.Contains("inline void HandleHorizontalInput(PlayerState player, Pixel footWorldY)", source);
        Assert.Contains("inline void ResolveLanding(PlayerState player, Pixel screenX, Pixel previousFootWorldY, Pixel footWorldY)", source);
        Assert.Contains("inline void ResolveFall(PlayerState player, CameraState view)", source);
        Assert.Contains("inline pure bool IsRespawning()", source);
        Assert.Contains("void AdvanceRespawn(PlayerState player, CameraState view)", source);
        Assert.Contains("inline void UpdateRunAnimation(CameraState view)", source);
        Assert.Contains("PresentFrame(player, view);", source);
        Assert.Contains("if (frame.IsRespawning())", source);
        Assert.DoesNotContain("view.CaptureScreen(player);", source);
        Assert.Contains("frame.ResolveLanding(player, screenX, previousFootWorldY, footWorldY);", source);
        Assert.Contains("frame.ResolveFall(player, view);", source);
        Assert.Contains("frame.AdvanceRespawn(player, view);", source);
        Assert.Contains("player.HandleJumpInput(view.speed);", source);
        Assert.Contains("i16 movementFootWorldY = player.y + Player.FootOffset;", source);
        Assert.Contains("view.HandleHorizontalInput(player, movementFootWorldY);", source);
        Assert.Contains("player.UpdateRunAnimation(view);", source);
        Assert.DoesNotContain("Pixel cameraX = 0;", source);
        Assert.DoesNotContain("Pixel moving = 0;", source);
        Assert.DoesNotContain("Pixel resetRequested = 0;", source);
    }

    [Fact]
    public void GameBoy_runner_keeps_layout_readable_and_gives_hit_feedback()
    {
        var source = RunnerSample.FlattenedSource();

        Assert.DoesNotContain("view.screenX", source);
        Assert.Contains("Player.FootOffset", source);
        Assert.Contains("CollisionProbe.LandingSearchHeight", source);
        Assert.Contains("CollisionFlag.Landable", source);
        Assert.DoesNotContain("EnemyState", source);
        Assert.DoesNotContain("hitFlashTicks", source);

        var program = CompileVideoProgram(RunnerSample.CompiledSource(), RunnerSample.Directory);
        var worldMap = Assert.IsType<WorldMap2D>(program.WorldMap);
        var worldTiles = Assert.IsType<WorldTileGrid>(program.WorldTileGrid);
        Assert.NotEqual(0, worldTiles.TileIdAt(0, 36));
        Assert.NotEqual(0, worldTiles.TileIdAt(16, 34));
        Assert.NotEqual(0, worldTiles.TileIdAt(4, 38));
        Assert.Equal(WorldTileFlags.Solid, worldMap.FlagsAt(30, 30));
        Assert.Equal(WorldTileFlags.Solid, worldMap.FlagsAt(4, 38));
        Assert.Equal(WorldTileFlags.Empty, worldMap.FlagsAt(16, 14));
    }

    [Fact]
    public void GameBoy_runner_bounces_player_down_when_head_hits_solid_ceiling()
    {
        var source = RunnerSample.FlattenedSource();

        Assert.Contains("CeilingProbeTopOffset = 28", source);
        Assert.Contains("CeilingProbeHeight = 4", source);
        Assert.Contains("BounceVelocity = 32", source);
        Assert.Contains("inline void BounceDown()", source);
        Assert.Contains("velocityY = Jump.BounceVelocity;", source);
        Assert.Contains("inline void ResolveCeilingHit(PlayerState player, Pixel screenX, Pixel footWorldY)", source);
        Assert.Contains("i16 headProbeY = footWorldY - CollisionProbe.CeilingProbeTopOffset;", source);
        Assert.Contains("Camera.AabbTiles(screenX, headProbeY, Sprite.Width(mario_player), CollisionProbe.CeilingProbeHeight, CollisionFlag.Solid)", source);
        Assert.Contains("player.BounceDown();", source);
        Assert.Contains("frame.ResolveCeilingHit(player, screenX, footWorldY);", source);

        var ceilingStart = source.IndexOf("inline void ResolveCeilingHit", StringComparison.Ordinal);
        Assert.True(ceilingStart >= 0);
        var ceilingEnd = source.IndexOf("inline pure bool IsRespawning", ceilingStart, StringComparison.Ordinal);
        Assert.True(ceilingEnd > ceilingStart);
        var ceilingBlock = source[ceilingStart..ceilingEnd];
        Assert.Contains("player.velocityY < 0", ceilingBlock);

        var landingCall = source.IndexOf("frame.ResolveLanding(player, screenX, previousFootWorldY, footWorldY);", StringComparison.Ordinal);
        var ceilingCall = source.IndexOf("frame.ResolveCeilingHit(player, screenX, footWorldY);", StringComparison.Ordinal);
        var jumpInputCall = source.IndexOf("player.HandleJumpInput(view.speed);", StringComparison.Ordinal);
        Assert.True(ceilingCall > landingCall, "Ceiling resolution should run after solid landing resolution.");
        Assert.True(jumpInputCall > ceilingCall, "Ceiling resolution should clear the jump before jump input is consumed.");

        var rom = GameBoyRomCompiler.CompileSource(RunnerSample.CompiledSource(), RunnerSample.Directory);
        AssertRunnerMbc1Rom(rom);
    }

    [Fact]
    public void GameBoy_runner_uses_4_4_horizontal_acceleration_and_skid_model()
    {
        var source = RunnerSample.FlattenedSource();

        Assert.Contains("enum Direction", source);
        Assert.Contains("Walk = 20", source);
        Assert.Contains("RunMax = 32", source);
        Assert.Contains("Subpixel = 16", source);
        Assert.Contains("Acceleration = 1", source);
        Assert.Contains("Friction = 1", source);
        Assert.Contains("SkidAcceleration = 2", source);
        Assert.Contains("MaxSteps = 2", source);

        var cameraStart = source.IndexOf("class CameraState", StringComparison.Ordinal);
        var frameStart = source.IndexOf("class FrameState", StringComparison.Ordinal);
        Assert.True(cameraStart >= 0);
        Assert.True(frameStart > cameraStart);
        var cameraBlock = source[cameraStart..frameStart];

        Assert.Contains("u8 speed;", cameraBlock);
        Assert.Contains("u8 direction;", cameraBlock);
        Assert.Contains("u8 movementRemainder;", cameraBlock);
        Assert.Contains("inline void UpdateIntent(u8 desiredDirection, bool grounded)", cameraBlock);
        Assert.Contains("inline void ApplySkid(u8 desiredDirection)", cameraBlock);
        Assert.Contains("speed -= MotionSpeed.SkidAcceleration;", cameraBlock);
        Assert.Contains("direction = desiredDirection;", cameraBlock);
        Assert.Contains("UpdateFacing(player, desiredDirection);", cameraBlock);
        Assert.Contains("movementRemainder += speed;", cameraBlock);
        Assert.Contains("void ApplyMotionStep(PlayerState player, Pixel wallProbeY, Pixel collisionCameraX)", cameraBlock);
        Assert.Contains("movementRemainder -= MotionSpeed.Subpixel;", cameraBlock);
        Assert.Contains("MoveRightOnePixel(player, wallProbeY, collisionCameraX);", cameraBlock);
        Assert.Contains("MoveLeftOnePixel(player, wallProbeY, collisionCameraX);", cameraBlock);

        Assert.DoesNotContain("StartDirection", cameraBlock);

        var rom = GameBoyRomCompiler.CompileSource(RunnerSample.CompiledSource(), RunnerSample.Directory);
        AssertRunnerMbc1Rom(rom);
    }

    [Fact]
    public void GameBoy_runner_accelerates_toward_the_input_speed_target()
    {
        var source = RunnerSample.FlattenedSource();

        Assert.Contains("RunMax = 32", source);
        Assert.Contains("Acceleration = 1", source);

        var cameraStart = source.IndexOf("class CameraState", StringComparison.Ordinal);
        var frameStart = source.IndexOf("class FrameState", StringComparison.Ordinal);
        Assert.True(cameraStart >= 0);
        Assert.True(frameStart > cameraStart);
        var cameraBlock = source[cameraStart..frameStart];

        Assert.Contains("inline void Accelerate(bool grounded)", cameraBlock);
        Assert.Contains("inline void ApplyFriction()", cameraBlock);
        Assert.Contains("if (grounded)", cameraBlock);
        Assert.Contains("if (Input.IsDown(Button.B))", cameraBlock);
        Assert.Contains("if (speed < MotionSpeed.RunMax)", cameraBlock);
        Assert.Contains("speed += MotionSpeed.Acceleration;", cameraBlock);
        Assert.Contains("else if (speed < MotionSpeed.Walk)", cameraBlock);
        Assert.Contains("else if (speed > MotionSpeed.Walk)", cameraBlock);
        Assert.Contains("speed -= MotionSpeed.Friction;", cameraBlock);
        Assert.Contains("direction = Direction.None;", cameraBlock);

        // Traction owns acceleration and friction; opposite air input only trims inherited momentum.
        Assert.Contains("Accelerate(grounded);", cameraBlock);
        Assert.Contains("ApplySkid(desiredDirection);", cameraBlock);
        Assert.Contains("UpdateIntent(desiredDirection, player.grounded);", cameraBlock);

        var motionStart = cameraBlock.IndexOf("inline void ApplyMotion(PlayerState player, Pixel wallProbeY)", StringComparison.Ordinal);
        Assert.True(motionStart >= 0);
        var horizontalStart = cameraBlock.IndexOf("inline void HandleHorizontalInput", motionStart, StringComparison.Ordinal);
        Assert.True(horizontalStart > motionStart);
        var motionBlock = cameraBlock[motionStart..horizontalStart];
        Assert.Contains("while (steps < MotionSpeed.MaxSteps)", motionBlock);
        Assert.Contains("let collisionCameraX = x;", motionBlock);
        Assert.Equal(2, CountOccurrences(cameraBlock, "let screenX = player.x - collisionCameraX;"));
        Assert.Equal(1, CountOccurrences(motionBlock, "ApplyMotionStep(player, wallProbeY, collisionCameraX);"));
        Assert.DoesNotContain("while (movementRemainder >= MotionSpeed.Subpixel)", motionBlock);

        // Regression guard: every collision substep projects against the camera state from the start
        // of motion while source camera state advances per pixel and syncs once after both probes.
        Assert.Contains("player.x += 1;", cameraBlock);
        Assert.Contains("x += 1;", cameraBlock);
        Assert.Contains("player.x -= 1;", cameraBlock);
        Assert.Contains("x -= 1;", cameraBlock);
        Assert.DoesNotContain("Camera.SetPosition", motionBlock);
        Assert.DoesNotContain("view.ApplyFramePosition();", source);
        Assert.Equal(1, CountOccurrences(source, "view.ApplyPosition();"));

        var rom = GameBoyRomCompiler.CompileSource(RunnerSample.CompiledSource(), RunnerSample.Directory);
        AssertRunnerMbc1Rom(rom);
    }

    [Fact]
    public void GameBoy_runner_uses_smb3_4_4_speed_scaled_variable_jump_height()
    {
        var source = RunnerSample.FlattenedSource();

        Assert.Contains("StandingVelocity = -56", source);
        Assert.Contains("WalkingVelocity = -58", source);
        Assert.Contains("RunningVelocity = -60", source);
        Assert.Contains("PSpeedVelocity = -64", source);
        Assert.Contains("HeldGravityThreshold = -32", source);
        Assert.Contains("HeldGravity = 1", source);
        Assert.Contains("ReleasedGravity = 5", source);
        Assert.Contains("TerminalVelocity = 69", source);
        Assert.Contains("Subpixel = 16", source);
        Assert.Contains("Pixel verticalSubpixel;", source);
        Assert.DoesNotContain("heldGravityTicks", source);

        var gravityStart = source.IndexOf("inline void ApplyGravity()", StringComparison.Ordinal);
        var landStart = source.IndexOf("void Land(Pixel targetY)", StringComparison.Ordinal);
        Assert.True(gravityStart >= 0);
        Assert.True(landStart > gravityStart);
        var gravityBlock = source[gravityStart..landStart];
        Assert.Contains("if (jumping && Input.IsDown(Button.A) && velocityY < Jump.HeldGravityThreshold)", gravityBlock);
        Assert.Contains("velocityY += Jump.HeldGravity;", gravityBlock);
        Assert.Contains("velocityY += Jump.ReleasedGravity;", gravityBlock);
        Assert.Contains("if (velocityY > Jump.TerminalVelocity)", gravityBlock);
        Assert.Contains("Pixel verticalMotion = verticalSubpixel + velocityY;", gravityBlock);
        Assert.Contains("while (verticalMotion < 0)", gravityBlock);
        Assert.Contains("while (verticalMotion >= Jump.Subpixel)", gravityBlock);
        Assert.Contains("if (!grounded)", gravityBlock);
        Assert.Contains("verticalSubpixel = verticalMotion;", gravityBlock);

        var jumpStart = source.IndexOf("inline void StartJump(u8 horizontalSpeed)", StringComparison.Ordinal);
        var animationStart = source.IndexOf("inline void SelectDisplayFrame(bool moving)", StringComparison.Ordinal);
        Assert.True(jumpStart >= 0);
        Assert.True(animationStart > jumpStart);
        var jumpBlock = source[jumpStart..animationStart];
        Assert.Contains("velocityY = Jump.StandingVelocity;", jumpBlock);
        Assert.Contains("if (horizontalSpeed > 0)", jumpBlock);
        Assert.Contains("velocityY = Jump.WalkingVelocity;", jumpBlock);
        Assert.Contains("if (horizontalSpeed > MotionSpeed.Walk)", jumpBlock);
        Assert.Contains("velocityY = Jump.RunningVelocity;", jumpBlock);
        Assert.Contains("if (horizontalSpeed >= MotionSpeed.RunMax)", jumpBlock);
        Assert.Contains("velocityY = Jump.PSpeedVelocity;", jumpBlock);

        Assert.Contains("inline void HandleJumpInput(u8 horizontalSpeed)", source);
        Assert.Contains("StartJump(horizontalSpeed);", source);
        Assert.Contains("player.HandleJumpInput(view.speed);", source);
        Assert.DoesNotContain("Input.HoldTicks(Button.A)", source);

        var rom = GameBoyRomCompiler.CompileSource(RunnerSample.CompiledSource(), RunnerSample.Directory);
        AssertRunnerMbc1Rom(rom);
    }

    [Fact]
    public void GameBoy_runner_uses_actor_feet_holes_failure_tiles_and_reset_state()
    {
        var source = RunnerSample.FlattenedSource();

        Assert.DoesNotContain("Pixel footTile;", source);
        Assert.Contains("i16 footWorldY = player.y + Player.FootOffset;", source);
        Assert.Contains("u8 respawnPhase;", source);

        Assert.Contains("""World.Load("assets/maps/stage1.tmj");""", source);
        Assert.DoesNotContain("World.Column(", source);
        Assert.DoesNotContain("World.Flags(", source);
        Assert.DoesNotContain("World.Map(", source);
        Assert.DoesNotContain("World.Column(", source);
        Assert.DoesNotContain("inline pure Pixel WrapWorldX(Pixel x) => x;", source);
        Assert.DoesNotContain("playerWorldX", source);
        Assert.Contains("i16 footWorldY = player.y + Player.FootOffset;", source);
        Assert.DoesNotContain("if (velocityY < 0)", source);
        Assert.DoesNotContain("y = 0;", source);
        Assert.Contains("player.velocityY >= 0", source);
        Assert.Contains("i16 footTile = Camera.AabbHitTop(screenX, footWorldY - CollisionProbe.LandingSearchTopOffset, Sprite.Width(mario_player), CollisionProbe.LandingSearchHeight, CollisionFlag.Landable);", source);
        Assert.Contains("player.Land(footTile - Player.FootOffset);", source);
        Assert.DoesNotContain("camera_span_has_flags(", source);
        Assert.DoesNotContain("camera_span_has_tile(", source);
        Assert.DoesNotContain("camera_span_tile_at(", source);
        Assert.DoesNotContain("footLeftX", source);
        Assert.DoesNotContain("footCenterX", source);
        Assert.DoesNotContain("footRightX", source);
        Assert.DoesNotContain("map_tile_at(player", source);
        Assert.DoesNotContain("failTile", source);
        Assert.DoesNotContain("hazardHit", source);
        Assert.DoesNotContain("BounceFromHazard", source);
        Assert.DoesNotContain("EnemyState", source);
        Assert.DoesNotContain("if (footTile != 3)", source);
        Assert.Contains("if (!player.grounded && player.y >= Player.FallResetY)", source);
        Assert.Contains("respawnPhase = 1;", source);
        Assert.Contains("if (frame.IsRespawning())", source);
        Assert.Contains("frame.AdvanceRespawn(player, view);", source);
        Assert.Contains("player.Land(Player.StartY);", source);
        Assert.Contains("velocityY = 0;", source);
        Assert.Contains("jumping = false;", source);

        var rom = GameBoyRomCompiler.CompileSource(RunnerSample.CompiledSource(), RunnerSample.Directory);
        AssertRunnerMbc1Rom(rom);
    }

    [Fact]
    public void GameBoy_runner_freezes_input_and_physics_while_respawning()
    {
        var source = RunnerSample.FlattenedSource();

        var resetStart = source.IndexOf("frame.AdvanceRespawn(player, view);", StringComparison.Ordinal);
        var normalStart = source.IndexOf("else", resetStart, StringComparison.Ordinal);
        var jumpStart = source.IndexOf("player.HandleJumpInput(view.speed);", StringComparison.Ordinal);
        var movementStart = source.IndexOf("view.HandleHorizontalInput(player, movementFootWorldY);", StringComparison.Ordinal);

        Assert.True(resetStart >= 0);
        Assert.True(normalStart > resetStart);
        Assert.True(jumpStart > normalStart, "Jump input should remain inside the normal-play branch.");
        Assert.True(movementStart > normalStart, "Horizontal input should remain inside the normal-play branch.");

        var rom = GameBoyRomCompiler.CompileSource(RunnerSample.CompiledSource(), RunnerSample.Directory);
        AssertRunnerMbc1Rom(rom);
    }

    [Fact]
    public void GameBoy_runner_keeps_ground_alignment_and_reset_animation_state()
    {
        var source = RunnerSample.FlattenedSource();

        Assert.Contains("StartY = 273", source);
        Assert.Contains("FootOffset = 31", source);
        Assert.DoesNotContain("TopWrapY", source);
        Assert.Contains("player.Land(Player.StartY);", source);
        Assert.Equal(1, CountOccurrences(source, "y = Player.StartY;"));
        Assert.Equal(1, CountOccurrences(source, "player.Land(footTile - Player.FootOffset);"));
        Assert.DoesNotContain("player.y = 77;", source);
        Assert.Contains("""World.Load("assets/maps/stage1.tmj");""", source);
        Assert.DoesNotContain("World.Column(", source);
        Assert.DoesNotContain("World.Flags(", source);
        Assert.DoesNotContain("World.Map(", source);
        Assert.DoesNotContain("World.Column(", source);
        Assert.DoesNotContain("Tilemap.Set(", source);
        Assert.Contains("i16 footTile = Camera.AabbHitTop(screenX, footWorldY - CollisionProbe.LandingSearchTopOffset, Sprite.Width(mario_player), CollisionProbe.LandingSearchHeight, CollisionFlag.Landable);", source);
        Assert.Contains("player.velocityY >= 0", source);
        Assert.DoesNotContain("camera_span_has_flags(", source);
        Assert.DoesNotContain("failTile", source);
        Assert.DoesNotContain("hazardHit", source);
        Assert.DoesNotContain("if (footTile != 3)", source);

        var resetStart = source.IndexOf("void AdvanceRespawn(PlayerState player, CameraState view)", StringComparison.Ordinal);
        Assert.True(resetStart >= 0);
        var resetEnd = source.IndexOf("void SetupVideo()", resetStart, StringComparison.Ordinal);
        Assert.True(resetEnd > resetStart);
        var resetBlock = source[resetStart..resetEnd];
        Assert.Contains("player.displayFrame = 0;", resetBlock);
        Assert.DoesNotContain("displayFlipX = false;", resetBlock);
        Assert.DoesNotContain("animTick = 0;", resetBlock);
        Assert.DoesNotContain("view.moving = 0;", resetBlock);

        Assert.Contains("player.Land(Player.StartY);", resetBlock);
        Assert.Contains("respawnPhase = 0;", resetBlock);

        var rom = GameBoyRomCompiler.CompileSource(RunnerSample.CompiledSource(), RunnerSample.Directory);
        AssertRunnerMbc1Rom(rom);
    }

}
