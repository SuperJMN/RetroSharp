using RetroSharp.Core.Targeting;
using Xunit;

namespace RetroSharp.Core.Tests;

public class Target2DCapabilitiesTests
{
    [Fact]
    public void Descriptor_keeps_portable_2d_limits_without_target_builder_dependencies()
    {
        var capabilities = Capabilities(HudMode.Window | HudMode.Sprite);

        Assert.Equal("test", capabilities.Name);
        Assert.Equal(new Size2D(160, 144), capabilities.ScreenPixels);
        Assert.True(capabilities.SupportsScrollAxis(ScrollAxes.Horizontal));
        Assert.True(capabilities.SupportsScrollAxis(ScrollAxes.Vertical));
        Assert.False(capabilities.SupportsScrollAxis(ScrollAxes.None));
        Assert.True(capabilities.SupportsRuntimeBackgroundStreamingAxis(ScrollAxes.Horizontal));
        Assert.True(capabilities.SupportsRuntimeBackgroundStreamingAxis(ScrollAxes.Vertical));
        Assert.False(capabilities.SupportsRuntimeBackgroundStreamingAxis(ScrollAxes.None));
        Assert.True(capabilities.SupportsSpriteSize(SpriteSizeMode.Sprite8x16));
        Assert.False(capabilities.SupportsSpriteSize(SpriteSizeMode.Sprite16x16));
        Assert.True(capabilities.SupportsSpriteTransform(SpriteTransform.FlipX));
        Assert.True(capabilities.SupportsHudMode(HudMode.Window));
        Assert.False(capabilities.SupportsHudMode(HudMode.SplitScroll));
        Assert.True(capabilities.SupportsCollisionQuery(CollisionQueryMode.CameraRelativeAabb));
        Assert.True(capabilities.SupportsCollisionQuery(CollisionQueryMode.CameraRelativeAabbHitTop));
        Assert.False(capabilities.SupportsCollisionQuery(CollisionQueryMode.None));
    }

    [Fact]
    public void Hud_capability_check_accepts_disabled_hud_even_when_target_supports_no_hud()
    {
        var capabilities = Capabilities(HudMode.None);

        TargetCapabilityChecks.RequireHudMode(capabilities, HudMode.None);

        Assert.False(capabilities.SupportsHudMode(HudMode.None));
    }

    private static Target2DCapabilities Capabilities(HudMode hudModes)
    {
        return new Target2DCapabilities(
            Name: "test",
            ScreenPixels: new Size2D(160, 144),
            ScreenTiles: new Size2D(20, 18),
            TileSize: new Size2D(8, 8),
            BackgroundBufferTiles: new Size2D(32, 32),
            ScrollAxes: ScrollAxes.Horizontal | ScrollAxes.Vertical,
            SupportsFineScrollX: true,
            SupportsFineScrollY: true,
            MaxBackgroundTileWritesPerFrame: 32,
            MaxAttributeWritesPerFrame: 0,
            SpriteCount: 40,
            SpriteSizeModes: SpriteSizeMode.Sprite8x8 | SpriteSizeMode.Sprite8x16,
            MaxSpritesPerScanline: 10,
            SpritePaletteSlots: 2,
            BackgroundPaletteSlots: 1,
            SupportedSpriteTransforms: SpriteTransform.FlipX | SpriteTransform.FlipY,
            HudModes: hudModes,
            CollisionQueries: CollisionQueryMode.WorldTileFlags | CollisionQueryMode.WorldAabb | CollisionQueryMode.CameraRelativeAabb | CollisionQueryMode.CameraRelativeAabbHitTop);
    }
}
