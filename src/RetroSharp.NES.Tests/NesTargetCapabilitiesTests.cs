namespace RetroSharp.NES.Tests;

using RetroSharp.Core.Sdk;
using RetroSharp.Core.Targeting;
using RetroSharp.NES;
using Xunit;

public sealed class NesTargetCapabilitiesTests
{
    [Fact]
    public void Descriptor_reports_current_static_nes_drawing_limits()
    {
        var capabilities = NesTarget.Capabilities;

        Assert.Equal("nes", capabilities.Name);
        Assert.Equal(new Size2D(256, 240), capabilities.ScreenPixels);
        Assert.Equal(new Size2D(32, 30), capabilities.ScreenTiles);
        Assert.Equal(new Size2D(8, 8), capabilities.TileSize);
        Assert.Equal(new Size2D(64, 60), capabilities.BackgroundBufferTiles);
        Assert.Equal(64, capabilities.SpriteCount);
        Assert.True(capabilities.SupportsSpriteSize(SpriteSizeMode.Sprite8x8));
        Assert.True(capabilities.SupportsSpriteSize(SpriteSizeMode.Sprite8x16));
        Assert.False(capabilities.SupportsSpriteSize(SpriteSizeMode.Sprite16x16));
        Assert.Equal(8, capabilities.MaxSpritesPerScanline);
        Assert.Equal(4, capabilities.SpritePaletteSlots);
        Assert.Equal(4, capabilities.BackgroundPaletteSlots);
        Assert.True(capabilities.SupportsSpriteTransform(SpriteTransform.FlipX));
        Assert.True(capabilities.SupportsSpriteTransform(SpriteTransform.FlipY));
    }

    [Fact]
    public void Descriptor_reports_free_scroll_streaming_budget_for_large_worlds()
    {
        var capabilities = NesTarget.Capabilities;

        Assert.True(capabilities.SupportsScrollAxis(ScrollAxes.Horizontal));
        Assert.True(capabilities.SupportsScrollAxis(ScrollAxes.Vertical));
        Assert.True(capabilities.SupportsFineScrollX);
        Assert.True(capabilities.SupportsFineScrollY);
        Assert.Equal(32, capabilities.MaxBackgroundTileWritesPerFrame);
        Assert.True(capabilities.MaxAttributeWritesPerFrame > 0);
        Assert.True(capabilities.SupportsRuntimeBackgroundStreamingAxis(ScrollAxes.Horizontal));
        Assert.True(capabilities.SupportsRuntimeBackgroundStreamingAxis(ScrollAxes.Vertical));
        Assert.True(CanStreamVisibleColumn(capabilities));
        Assert.True(CanStreamVisibleRow(capabilities));
        Assert.False(capabilities.SupportsHudMode(HudMode.Window));
        Assert.False(capabilities.SupportsHudMode(HudMode.SplitScroll));
        Assert.False(capabilities.SupportsHudMode(HudMode.Sprite));
        Assert.False(capabilities.SupportsCollisionQuery(CollisionQueryMode.WorldTileFlags));
        Assert.False(capabilities.SupportsCollisionQuery(CollisionQueryMode.WorldAabb));
        Assert.True(capabilities.SupportsCollisionQuery(CollisionQueryMode.CameraRelativeAabb));
        Assert.True(capabilities.SupportsCollisionQuery(CollisionQueryMode.CameraRelativeAabbHitTop));
    }

    [Fact]
    public void Descriptor_reports_nes_intrinsic_contracts()
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

        var spriteDraw = ResolveIntrinsic("sprite_draw");
        Assert.Equal(TargetIntrinsicOperation.DrawLogicalSprite, spriteDraw.Operation);
        Assert.Equal(TargetIntrinsicReturnKind.Void, spriteDraw.ReturnKind);
        Assert.Contains(TargetIntrinsicCapabilityRequirement.LogicalSprites, spriteDraw.RequiredCapabilities);
        Assert.Contains(spriteDraw.CompileTimeOperands, operand => operand.Role == TargetIntrinsicOperandRole.AssetRef);

        var cameraHitTop = ResolveIntrinsic("camera_aabb_hit_top");
        Assert.Equal(TargetIntrinsicOperation.CameraAabbHitTop, cameraHitTop.Operation);
        Assert.Equal(TargetIntrinsicReturnKind.I16, cameraHitTop.ReturnKind);
        Assert.Contains(TargetIntrinsicCapabilityRequirement.CameraRelativeAabbHitTop, cameraHitTop.RequiredCapabilities);
        Assert.Contains(cameraHitTop.CompileTimeOperands, operand => operand.Role == TargetIntrinsicOperandRole.WorldId);

        Assert.False(NesTarget.Intrinsics.TryResolve("world_tile_flags_at", out _));
    }

    private static bool CanStreamVisibleColumn(Target2DCapabilities capabilities)
    {
        return capabilities.SupportsScrollAxis(ScrollAxes.Horizontal)
               && capabilities.SupportsRuntimeBackgroundStreamingAxis(ScrollAxes.Horizontal)
               && capabilities.MaxBackgroundTileWritesPerFrame >= capabilities.ScreenTiles.Height;
    }

    private static bool CanStreamVisibleRow(Target2DCapabilities capabilities)
    {
        return capabilities.SupportsScrollAxis(ScrollAxes.Vertical)
               && capabilities.SupportsRuntimeBackgroundStreamingAxis(ScrollAxes.Vertical)
               && capabilities.MaxBackgroundTileWritesPerFrame >= capabilities.ScreenTiles.Width;
    }

    private static TargetIntrinsicDescriptor ResolveIntrinsic(string intrinsicId)
    {
        Assert.True(NesTarget.Intrinsics.TryResolve(intrinsicId, out var descriptor));
        return descriptor;
    }
}
