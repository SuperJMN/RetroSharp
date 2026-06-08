namespace RetroSharp.GameBoy.Tests;

using RetroSharp.Core.Targeting;
using RetroSharp.GameBoy;
using Xunit;

public sealed class GameBoyTargetCapabilitiesTests
{
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
        Assert.Equal(20, capabilities.MaxBackgroundTileWritesPerFrame);
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
    }
}
