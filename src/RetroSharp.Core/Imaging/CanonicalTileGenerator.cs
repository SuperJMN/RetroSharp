namespace RetroSharp.Core.Imaging;

// Produces target-neutral 8x8 four-tone tile patterns from a tileset image. A
// source tileset tile is downscaled to (tilesWide*8 x tilesHigh*8) and each 8x8
// output cell is reduced to luminance tones 0..3 (0 = lightest). These canonical
// patterns are the shared logical tile unit: each target encodes a tone grid into
// its own 2bpp tile byte format and maps the four tones to its hardware palette.
public static class CanonicalTileGenerator
{
    // Returns tilesWide*tilesHigh canonical patterns (row-major byte[64] of tones 0..3),
    // one per 8x8 cell, for the tileset tile localId.
    public static IReadOnlyList<byte[]> BuildTiles(
        PngImage image,
        int localId,
        int tileCount,
        int columns,
        int tileWidth,
        int tileHeight,
        int tilesWide,
        int tilesHigh,
        string context)
    {
        if (localId < 0 || localId >= tileCount)
        {
            throw new InvalidOperationException($"{context} references tile id {localId}, which is outside the tileset.");
        }

        var sourceX = localId % columns * tileWidth;
        var sourceY = localId / columns * tileHeight;
        if (sourceX + tileWidth > image.Width || sourceY + tileHeight > image.Height)
        {
            throw new InvalidOperationException($"{context} references tile id {localId}, which exceeds the tileset image.");
        }

        var targetPixelWidth = tilesWide * 8;
        var targetPixelHeight = tilesHigh * 8;
        var tiles = new List<byte[]>(tilesWide * tilesHigh);
        for (var tileY = 0; tileY < tilesHigh; tileY++)
        {
            for (var tileX = 0; tileX < tilesWide; tileX++)
            {
                var pattern = new byte[64];
                for (var outY = 0; outY < 8; outY++)
                {
                    var targetY = tileY * 8 + outY;
                    var yStart = sourceY + targetY * tileHeight / targetPixelHeight;
                    var yEnd = sourceY + Math.Min(tileHeight, Math.Max((targetY + 1) * tileHeight / targetPixelHeight, targetY * tileHeight / targetPixelHeight + 1));
                    for (var outX = 0; outX < 8; outX++)
                    {
                        var targetX = tileX * 8 + outX;
                        var xStart = sourceX + targetX * tileWidth / targetPixelWidth;
                        var xEnd = sourceX + Math.Min(tileWidth, Math.Max((targetX + 1) * tileWidth / targetPixelWidth, targetX * tileWidth / targetPixelWidth + 1));
                        pattern[outY * 8 + outX] = (byte)QuantizeTone(image, xStart, xEnd, yStart, yEnd);
                    }
                }

                tiles.Add(pattern);
            }
        }

        return tiles;
    }

    private static int QuantizeTone(PngImage image, int xStart, int xEnd, int yStart, int yEnd)
    {
        var totalR = 0;
        var totalG = 0;
        var totalB = 0;
        var count = 0;
        for (var y = yStart; y < yEnd; y++)
        {
            for (var x = xStart; x < xEnd; x++)
            {
                var offset = image.PixelOffset(x, y);
                if (image.RgbaPixels[offset + 3] < 128)
                {
                    continue;
                }

                totalR += image.RgbaPixels[offset];
                totalG += image.RgbaPixels[offset + 1];
                totalB += image.RgbaPixels[offset + 2];
                count++;
            }
        }

        if (count == 0)
        {
            return 0;
        }

        var r = totalR / count;
        var g = totalG / count;
        var b = totalB / count;
        var luminance = (r * 299 + g * 587 + b * 114) / 1000;
        return luminance switch
        {
            >= 192 => 0,
            >= 128 => 1,
            >= 64 => 2,
            _ => 3,
        };
    }
}
