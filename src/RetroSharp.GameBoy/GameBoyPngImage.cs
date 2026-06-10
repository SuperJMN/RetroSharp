using System.Buffers.Binary;
using System.IO.Compression;

namespace RetroSharp.GameBoy;

internal sealed class GameBoyPngImage
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public required int Width { get; init; }

    public required int Height { get; init; }

    public required byte[] RgbaPixels { get; init; }

    public static GameBoyPngImage Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < Signature.Length || !bytes.AsSpan(0, Signature.Length).SequenceEqual(Signature))
        {
            throw new InvalidOperationException($"PNG image '{path}' is not a PNG file.");
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
                throw new InvalidOperationException($"PNG image '{path}' has a truncated chunk.");
            }

            var length = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset, 4));
            offset += 4;
            var type = System.Text.Encoding.ASCII.GetString(bytes, offset, 4);
            offset += 4;

            if (length < 0 || offset + length + 4 > bytes.Length)
            {
                throw new InvalidOperationException($"PNG image '{path}' has an invalid chunk length.");
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
                        throw new InvalidOperationException($"PNG image '{path}' uses unsupported compression, filter, or interlace settings.");
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
            throw new InvalidOperationException($"PNG image '{path}' is missing its header.");
        }

        if (bitDepth != 8)
        {
            throw new InvalidOperationException($"PNG image '{path}' must use 8-bit channels.");
        }

        var bytesPerPixel = colorType switch
        {
            2 => 3,
            3 => 1,
            6 => 4,
            _ => throw new InvalidOperationException($"PNG image '{path}' uses unsupported color type {colorType}."),
        };

        if (colorType == 3 && palette is null)
        {
            throw new InvalidOperationException($"Indexed PNG image '{path}' is missing its palette.");
        }

        var raw = Inflate(idat.ToArray());
        var scanlineLength = width.Value * bytesPerPixel;
        var expectedLength = height.Value * (scanlineLength + 1);
        if (raw.Length != expectedLength)
        {
            throw new InvalidOperationException($"PNG image '{path}' has unexpected image data length.");
        }

        var pixels = Unfilter(raw, width.Value, height.Value, bytesPerPixel);
        return new GameBoyPngImage
        {
            Width = width.Value,
            Height = height.Value,
            RgbaPixels = colorType switch
            {
                2 => ExpandRgb(pixels),
                3 => ExpandIndexed(pixels, palette!, transparency, path),
                6 => pixels,
                _ => throw new InvalidOperationException($"PNG image '{path}' uses unsupported color type {colorType}."),
            },
        };
    }

    public int PixelOffset(int x, int y) => (y * Width + x) * 4;

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

    private static byte[] ExpandRgb(byte[] pixels)
    {
        var result = new byte[pixels.Length / 3 * 4];
        for (var i = 0; i < pixels.Length / 3; i++)
        {
            result[i * 4] = pixels[i * 3];
            result[i * 4 + 1] = pixels[i * 3 + 1];
            result[i * 4 + 2] = pixels[i * 3 + 2];
            result[i * 4 + 3] = 255;
        }

        return result;
    }

    private static byte[] ExpandIndexed(byte[] pixels, byte[] palette, byte[]? transparency, string path)
    {
        if (palette.Length % 3 != 0)
        {
            throw new InvalidOperationException($"Indexed PNG image '{path}' has an invalid palette.");
        }

        var result = new byte[pixels.Length * 4];
        for (var i = 0; i < pixels.Length; i++)
        {
            var index = pixels[i];
            var paletteOffset = index * 3;
            if (paletteOffset + 2 >= palette.Length)
            {
                throw new InvalidOperationException($"Indexed PNG image '{path}' references a missing palette color.");
            }

            result[i * 4] = palette[paletteOffset];
            result[i * 4 + 1] = palette[paletteOffset + 1];
            result[i * 4 + 2] = palette[paletteOffset + 2];
            result[i * 4 + 3] = transparency is not null && index < transparency.Length ? transparency[index] : (byte)255;
        }

        return result;
    }
}
