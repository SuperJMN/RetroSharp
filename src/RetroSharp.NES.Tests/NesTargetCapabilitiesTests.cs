namespace RetroSharp.NES.Tests;

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
        Assert.Equal(new Size2D(32, 30), capabilities.BackgroundBufferTiles);
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
    public void Descriptor_blocks_runtime_features_not_lowered_by_the_static_nes_target()
    {
        var capabilities = NesTarget.Capabilities;

        Assert.True(capabilities.SupportsScrollAxis(ScrollAxes.Horizontal));
        Assert.False(capabilities.SupportsScrollAxis(ScrollAxes.Vertical));
        Assert.True(capabilities.SupportsFineScrollX);
        Assert.False(capabilities.SupportsFineScrollY);
        Assert.Equal(0, capabilities.MaxBackgroundTileWritesPerFrame);
        Assert.Equal(0, capabilities.MaxAttributeWritesPerFrame);
        Assert.False(CanStreamVisibleColumn(capabilities));
        Assert.False(CanStreamVisibleRow(capabilities));
        Assert.False(capabilities.SupportsHudMode(HudMode.Window));
        Assert.False(capabilities.SupportsHudMode(HudMode.SplitScroll));
        Assert.False(capabilities.SupportsHudMode(HudMode.Sprite));
    }

    private static bool CanStreamVisibleColumn(Target2DCapabilities capabilities)
    {
        return capabilities.SupportsScrollAxis(ScrollAxes.Horizontal)
               && capabilities.MaxBackgroundTileWritesPerFrame >= capabilities.ScreenTiles.Height;
    }

    private static bool CanStreamVisibleRow(Target2DCapabilities capabilities)
    {
        return capabilities.SupportsScrollAxis(ScrollAxes.Vertical)
               && capabilities.MaxBackgroundTileWritesPerFrame >= capabilities.ScreenTiles.Width;
    }
}
