using System.Buffers.Binary;
using System.IO.Compression;

namespace RetroSharp.GameBoy;

internal static class GameBoyPngSpriteSheet
{
    public static List<List<string>> ReadFrames(string path, int frameWidth, int frameHeight)
    {
        if (frameWidth <= 0 || frameHeight <= 0)
        {
            throw new InvalidOperationException("PNG sprite frame dimensions must be positive.");
        }

        var image = PngImage.Read(path);
        if (image.Width % frameWidth != 0)
        {
            throw new InvalidOperationException($"PNG sprite sheet '{path}' width must be a multiple of the frame width.");
        }

        if (image.Height != frameHeight)
        {
            throw new InvalidOperationException($"PNG sprite sheet '{path}' height must match the frame height.");
        }

        var frameCount = image.Width / frameWidth;
        if (frameCount == 0)
        {
            throw new InvalidOperationException($"PNG sprite sheet '{path}' must contain at least one frame.");
        }

        var frames = new List<List<string>>();
        for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            var frame = new List<string>();
            for (var y = 0; y < frameHeight; y++)
            {
                var row = new char[frameWidth];
                for (var x = 0; x < frameWidth; x++)
                {
                    row[x] = (char)('0' + image.ColorIndexes[y * image.Width + frameIndex * frameWidth + x]);
                }

                frame.Add(new string(row));
            }

            frames.Add(frame);
        }

        return frames;
    }

    private sealed class PngImage
    {
        private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

        public required int Width { get; init; }

        public required int Height { get; init; }

        public required byte[] ColorIndexes { get; init; }

        public static PngImage Read(string path)
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length < Signature.Length || !bytes.AsSpan(0, Signature.Length).SequenceEqual(Signature))
            {
                throw new InvalidOperationException($"PNG sprite sheet '{path}' is not a PNG file.");
            }

            int? width = null;
            int? height = null;
            byte? bitDepth = null;
            byte? colorType = null;
            byte[]? palette = null;
            byte[]? transparency = null;
            using var idat = new MemoryStream();

            var offset = Signature.Length;
            while (offset < bytes.Length)
            {
                if (offset + 12 > bytes.Length)
                {
                    throw new InvalidOperationException($"PNG sprite sheet '{path}' has a truncated chunk.");
                }

                var length = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset, 4));
                offset += 4;
                var type = System.Text.Encoding.ASCII.GetString(bytes, offset, 4);
                offset += 4;

                if (length < 0 || offset + length + 4 > bytes.Length)
                {
                    throw new InvalidOperationException($"PNG sprite sheet '{path}' has an invalid chunk length.");
                }

                var data = bytes.AsSpan(offset, length);
                offset += length + 4;

                switch (type)
                {
                    case "IHDR":
                        width = BinaryPrimitives.ReadInt32BigEndian(data[0..4]);
                        height = BinaryPrimitives.ReadInt32BigEndian(data[4..8]);
                        bitDepth = data[8];
                        colorType = data[9];
                        if (data[10] != 0 || data[11] != 0 || data[12] != 0)
                        {
                            throw new InvalidOperationException($"PNG sprite sheet '{path}' uses unsupported compression, filter, or interlace settings.");
                        }

                        break;
                    case "PLTE":
                        palette = data.ToArray();
                        break;
                    case "tRNS":
                        transparency = data.ToArray();
                        break;
                    case "IDAT":
                        idat.Write(data);
                        break;
                    case "IEND":
                        offset = bytes.Length;
                        break;
                }
            }

            if (width is null || height is null || bitDepth is null || colorType is null)
            {
                throw new InvalidOperationException($"PNG sprite sheet '{path}' is missing its header.");
            }

            if (bitDepth != 8)
            {
                throw new InvalidOperationException($"PNG sprite sheet '{path}' must use 8-bit channels.");
            }

            var bytesPerPixel = colorType switch
            {
                2 => 3,
                3 => 1,
                6 => 4,
                _ => throw new InvalidOperationException($"PNG sprite sheet '{path}' uses unsupported color type {colorType}."),
            };

            if (colorType == 3 && palette is null)
            {
                throw new InvalidOperationException($"Indexed PNG sprite sheet '{path}' is missing its palette.");
            }

            var raw = Inflate(idat.ToArray());
            var scanlineLength = width.Value * bytesPerPixel;
            var expectedLength = height.Value * (scanlineLength + 1);
            if (raw.Length != expectedLength)
            {
                throw new InvalidOperationException($"PNG sprite sheet '{path}' has unexpected image data length.");
            }

            var pixels = Unfilter(raw, width.Value, height.Value, bytesPerPixel);
            var colorIndexes = colorType switch
            {
                2 => MapRgb(pixels, path),
                3 => MapIndexed(pixels, palette!, transparency, path),
                6 => MapRgba(pixels, path),
                _ => throw new InvalidOperationException($"PNG sprite sheet '{path}' uses unsupported color type {colorType}."),
            };

            return new PngImage
            {
                Width = width.Value,
                Height = height.Value,
                ColorIndexes = colorIndexes,
            };
        }

        private static byte[] Inflate(byte[] compressed)
        {
            using var input = new MemoryStream(compressed);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            zlib.CopyTo(output);
            return output.ToArray();
        }

        private static byte[] Unfilter(byte[] raw, int width, int height, int bytesPerPixel)
        {
            var stride = width * bytesPerPixel;
            var result = new byte[stride * height];
            var sourceOffset = 0;
            for (var y = 0; y < height; y++)
            {
                var filter = raw[sourceOffset++];
                var rowOffset = y * stride;
                for (var x = 0; x < stride; x++)
                {
                    var value = raw[sourceOffset++];
                    var left = x >= bytesPerPixel ? result[rowOffset + x - bytesPerPixel] : 0;
                    var up = y > 0 ? result[rowOffset - stride + x] : 0;
                    var upLeft = y > 0 && x >= bytesPerPixel ? result[rowOffset - stride + x - bytesPerPixel] : 0;
                    result[rowOffset + x] = filter switch
                    {
                        0 => value,
                        1 => (byte)(value + left),
                        2 => (byte)(value + up),
                        3 => (byte)(value + ((left + up) / 2)),
                        4 => (byte)(value + Paeth(left, up, upLeft)),
                        _ => throw new InvalidOperationException($"Unsupported PNG filter type {filter}."),
                    };
                }
            }

            return result;
        }

        private static int Paeth(int left, int up, int upLeft)
        {
            var estimate = left + up - upLeft;
            var leftDistance = Math.Abs(estimate - left);
            var upDistance = Math.Abs(estimate - up);
            var upLeftDistance = Math.Abs(estimate - upLeft);

            if (leftDistance <= upDistance && leftDistance <= upLeftDistance) return left;
            return upDistance <= upLeftDistance ? up : upLeft;
        }

        private static byte[] MapRgb(byte[] pixels, string path)
        {
            var colorIndexer = new ColorIndexer(path);
            var result = new byte[pixels.Length / 3];
            for (var i = 0; i < result.Length; i++)
            {
                var rgb = pixels[i * 3] << 16 | pixels[i * 3 + 1] << 8 | pixels[i * 3 + 2];
                result[i] = colorIndexer.ColorIndexFor(rgb);
            }

            return result;
        }

        private static byte[] MapRgba(byte[] pixels, string path)
        {
            var colorIndexer = new ColorIndexer(path);
            var result = new byte[pixels.Length / 4];
            for (var i = 0; i < result.Length; i++)
            {
                var alpha = pixels[i * 4 + 3];
                if (alpha < 128)
                {
                    result[i] = 0;
                    continue;
                }

                var rgb = pixels[i * 4] << 16 | pixels[i * 4 + 1] << 8 | pixels[i * 4 + 2];
                result[i] = colorIndexer.ColorIndexFor(rgb);
            }

            return result;
        }

        private static byte[] MapIndexed(byte[] pixels, byte[] palette, byte[]? transparency, string path)
        {
            if (palette.Length < 12)
            {
                throw new InvalidOperationException($"Indexed PNG sprite sheet '{path}' must have at least four palette colors.");
            }

            var result = new byte[pixels.Length];
            for (var i = 0; i < pixels.Length; i++)
            {
                var index = pixels[i];
                var alpha = transparency is not null && index < transparency.Length ? transparency[index] : 255;
                if (alpha < 128)
                {
                    result[i] = 0;
                    continue;
                }

                if (index > 3)
                {
                    throw new InvalidOperationException($"Indexed PNG sprite sheet '{path}' can only use palette indexes 0, 1, 2, and 3.");
                }

                result[i] = index;
            }

            return result;
        }

        private sealed class ColorIndexer(string path)
        {
            private readonly Dictionary<int, byte> customColors = [];
            private readonly bool[] usedIndexes = new bool[4];

            public byte ColorIndexFor(int rgb)
            {
                if (KnownColorIndex(rgb) is { } known)
                {
                    usedIndexes[known] = true;
                    return known;
                }

                if (customColors.TryGetValue(rgb, out var existing))
                {
                    return existing;
                }

                for (byte index = 1; index <= 3; index++)
                {
                    if (usedIndexes[index])
                    {
                        continue;
                    }

                    usedIndexes[index] = true;
                    customColors.Add(rgb, index);
                    return index;
                }

                throw new InvalidOperationException($"PNG sprite sheet '{path}' can use at most three opaque sprite colors.");
            }

            private static byte? KnownColorIndex(int rgb) => rgb switch
            {
                0xE0F8D0 => 1,
                0x88C070 => 2,
                0x346856 => 3,
                0xFFFFFF => 1,
                0xB8B8B8 => 2,
                0x000000 => 3,
                _ => null,
            };
        }
    }
}
