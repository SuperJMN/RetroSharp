namespace RetroSharp.GameBoy.Tests;

using RetroSharp.Core.Sdk;
using RetroSharp.Core.Targeting;
using RetroSharp.GameBoy;
using Xunit;

public sealed class GameBoyTargetCapabilitiesTests
{
    [Fact]
    public void Descriptor_reports_supported_music_formats()
    {
        var capabilities = GameBoyTarget.AudioCapabilities;

        Assert.True(capabilities.SupportsBgm);
        Assert.Equal(["uge", "gbapu", "vgm"], capabilities.SupportedMusicFormats);
    }

    [Fact]
    public void Descriptor_reports_dmg_2d_hardware_limits()
    {
        var capabilities = GameBoyTarget.Capabilities;

        Assert.Equal("gb", capabilities.Name);
        Assert.Equal(new Size2D(160, 144), capabilities.ScreenPixels);
        Assert.Equal(new Size2D(20, 18), capabilities.ScreenTiles);
        Assert.Equal(new Size2D(8, 8), capabilities.TileSize);
        Assert.Equal(new Size2D(32, 32), capabilities.BackgroundBufferTiles);
        Assert.True(capabilities.SupportsScrollAxis(ScrollAxes.Horizontal));
        Assert.True(capabilities.SupportsScrollAxis(ScrollAxes.Vertical));
        Assert.True(capabilities.SupportsFineScrollX);
        Assert.True(capabilities.SupportsFineScrollY);
        Assert.True(capabilities.StaggersCameraMovementStreams);
        Assert.Equal(21, capabilities.MaxBackgroundTileWritesPerFrame);
        Assert.Equal(0, capabilities.MaxAttributeWritesPerFrame);
        Assert.Equal(40, capabilities.SpriteCount);
        Assert.True(capabilities.SupportsSpriteSize(SpriteSizeMode.Sprite8x8));
        Assert.True(capabilities.SupportsSpriteSize(SpriteSizeMode.Sprite8x16));
        Assert.False(capabilities.SupportsSpriteSize(SpriteSizeMode.Sprite16x16));
        Assert.Equal(10, capabilities.MaxSpritesPerScanline);
        Assert.Equal(2, capabilities.SpritePaletteSlots);
        Assert.Equal(1, capabilities.BackgroundPaletteSlots);
        Assert.True(capabilities.SupportsSpriteTransform(SpriteTransform.FlipX));
        Assert.True(capabilities.SupportsSpriteTransform(SpriteTransform.FlipY));
        Assert.True(capabilities.SupportsHudMode(HudMode.Window));
        Assert.True(capabilities.SupportsHudMode(HudMode.Sprite));
        Assert.False(capabilities.SupportsHudMode(HudMode.SplitScroll));
        Assert.True(capabilities.SupportsCollisionQuery(CollisionQueryMode.WorldTileFlags));
        Assert.True(capabilities.SupportsCollisionQuery(CollisionQueryMode.WorldAabb));
        Assert.True(capabilities.SupportsCollisionQuery(CollisionQueryMode.CameraRelativeAabb));
        Assert.True(capabilities.SupportsCollisionQuery(CollisionQueryMode.CameraRelativeAabbHitTop));
    }

    [Fact]
    public void Descriptor_reports_game_boy_intrinsic_contracts()
    {
        var waitFrame = ResolveIntrinsic("wait_frame");
        Assert.Equal(TargetIntrinsicOperation.WaitFrame, waitFrame.Operation);
        Assert.Equal(TargetIntrinsicReturnKind.Void, waitFrame.ReturnKind);
        Assert.Empty(waitFrame.RequiredCapabilities);

        var musicPlay = ResolveIntrinsic("music_play");
        Assert.Equal(TargetIntrinsicOperation.PlayMusic, musicPlay.Operation);
        Assert.Equal(TargetIntrinsicReturnKind.Void, musicPlay.ReturnKind);
        Assert.Contains(TargetIntrinsicCapabilityRequirement.BackgroundMusic, musicPlay.RequiredCapabilities);
        Assert.Contains(musicPlay.CompileTimeOperands, operand => operand.Role == TargetIntrinsicOperandRole.AssetRef);

        var worldTileFlags = ResolveIntrinsic("world_tile_flags_at");
        Assert.Equal(TargetIntrinsicOperation.ReadWorldTileFlags, worldTileFlags.Operation);
        Assert.Equal(TargetIntrinsicReturnKind.I16, worldTileFlags.ReturnKind);
        Assert.Contains(TargetIntrinsicCapabilityRequirement.WorldTileFlags, worldTileFlags.RequiredCapabilities);

        var spriteDraw = ResolveIntrinsic("sprite_draw");
        Assert.Equal(TargetIntrinsicOperation.DrawLogicalSprite, spriteDraw.Operation);
        Assert.Equal(TargetIntrinsicReturnKind.Void, spriteDraw.ReturnKind);
        Assert.Contains(TargetIntrinsicCapabilityRequirement.LogicalSprites, spriteDraw.RequiredCapabilities);
        Assert.Contains(spriteDraw.CompileTimeOperands, operand => operand.Role == TargetIntrinsicOperandRole.AssetRef);

        var cameraAabb = ResolveIntrinsic("camera_aabb_tiles");
        Assert.Equal(TargetIntrinsicOperation.CameraAabbTiles, cameraAabb.Operation);
        Assert.Equal(TargetIntrinsicReturnKind.I16, cameraAabb.ReturnKind);
        Assert.Contains(TargetIntrinsicCapabilityRequirement.CameraRelativeAabb, cameraAabb.RequiredCapabilities);
        Assert.Contains(cameraAabb.CompileTimeOperands, operand => operand.Role == TargetIntrinsicOperandRole.WorldId);
    }

    [Fact]
    public void Descriptor_accepts_one_visible_background_edge_stream_per_frame()
    {
        var capabilities = GameBoyTarget.Capabilities;

        Sdk2DOperationValidator.Validate(
            capabilities,
            new Sdk2DOperation.StreamMapRow(TargetRow: 0, SourceRow: 0, X: 0, Width: capabilities.ScreenTiles.Width + 1));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            Sdk2DOperationValidator.Validate(
                capabilities,
                new Sdk2DOperation.StreamMapRow(TargetRow: 0, SourceRow: 0, X: 0, Width: capabilities.ScreenTiles.Width + 2)));

        Assert.Equal("Target 'gb' supports 21 background tile writes per frame, but 22 are required for streaming a visible map row.", exception.Message);
    }

    [Fact]
    public void Descriptor_accepts_diagonal_camera_movement_by_staggering_visible_edge_streams()
    {
        var capabilities = GameBoyTarget.Capabilities;

        Sdk2DOperationValidator.Validate(
            capabilities,
            new Sdk2DOperation.SetCameraPosition(
                X: 1,
                Y: 1,
                Axes: ScrollAxes.Horizontal | ScrollAxes.Vertical));
    }

    private static TargetIntrinsicDescriptor ResolveIntrinsic(string intrinsicId)
    {
        Assert.True(GameBoyTarget.Intrinsics.TryResolve(intrinsicId, out var descriptor));
        return descriptor;
    }
}
