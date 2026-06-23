namespace RetroSharp.Core.Tests;

using RetroSharp.Core.Imaging;
using Xunit;

public sealed class CanonicalTileGeneratorTests
{
    [Fact]
    public void Quantizes_an_image_tile_into_eight_by_eight_luminance_tones()
    {
        // 8x8 image: left half white (lightest tone 0), right half black (darkest tone 3).
        var pixels = new byte[8 * 8 * 4];
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                var offset = (y * 8 + x) * 4;
                var value = (byte)(x < 4 ? 255 : 0);
                pixels[offset] = value;
                pixels[offset + 1] = value;
                pixels[offset + 2] = value;
                pixels[offset + 3] = 255;
            }
        }

        var image = new PngImage { Width = 8, Height = 8, RgbaPixels = pixels };

        var tiles = CanonicalTileGenerator.BuildTiles(image, localId: 0, tileCount: 1, columns: 1, tileWidth: 8, tileHeight: 8, tilesWide: 1, tilesHigh: 1, context: "test");

        var pattern = Assert.Single(tiles);
        Assert.Equal(64, pattern.Length);
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                var expected = x < 4 ? 0 : 3;
                Assert.Equal(expected, pattern[y * 8 + x]);
            }
        }
    }

    [Fact]
    public void Rejects_a_tile_id_outside_the_tileset()
    {
        var image = new PngImage { Width = 8, Height = 8, RgbaPixels = new byte[8 * 8 * 4] };
        var error = Assert.Throws<InvalidOperationException>(
            () => CanonicalTileGenerator.BuildTiles(image, localId: 5, tileCount: 1, columns: 1, tileWidth: 8, tileHeight: 8, tilesWide: 1, tilesHigh: 1, context: "test"));
        Assert.Contains("outside the tileset", error.Message);
    }
}
