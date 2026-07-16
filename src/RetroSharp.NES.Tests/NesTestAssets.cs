namespace RetroSharp.NES.Tests;

using System.Buffers.Binary;
using System.IO.Compression;

internal static class NesTestAssets
{
    internal static string WriteSpritePng(string fileName, int frameWidth, int frameHeight, params string[][] frames)
    {
        var palette = new[]
        {
            (R: (byte)0x00, G: (byte)0x00, B: (byte)0x00, A: (byte)0x00),
            (R: (byte)0xFF, G: (byte)0xFF, B: (byte)0xFF, A: (byte)0xFF),
            (R: (byte)0xB8, G: (byte)0xB8, B: (byte)0xB8, A: (byte)0xFF),
            (R: (byte)0x00, G: (byte)0x00, B: (byte)0x00, A: (byte)0xFF),
        };

        return WriteSpritePng(fileName, frameWidth, frameHeight, palette, frames);
    }

    internal static string WriteSpritePng(
        string fileName,
        int frameWidth,
        int frameHeight,
        IReadOnlyList<(byte R, byte G, byte B, byte A)> palette,
        params string[][] frames)
    {
        var directory = Path.Combine(Path.GetTempPath(), "RetroSharp.NES.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        WriteSpritePngFile(directory, fileName, frameWidth, frameHeight, palette, frames);
        return directory;
    }

    internal static void WriteSpritePngFile(
        string directory,
        string fileName,
        int frameWidth,
        int frameHeight,
        IReadOnlyList<(byte R, byte G, byte B, byte A)> palette,
        params string[][] frames)
    {
        var width = frameWidth * frames.Length;
        var height = frameHeight;
        var rgba = new byte[width * height * 4];

        for (var frameIndex = 0; frameIndex < frames.Length; frameIndex++)
        {
            var frame = frames[frameIndex];
            for (var y = 0; y < frameHeight; y++)
            {
                for (var x = 0; x < frameWidth; x++)
                {
                    var color = palette[frame[y][x] - '0'];
                    var targetX = frameIndex * frameWidth + x;
                    var offset = (y * width + targetX) * 4;
                    rgba[offset] = color.R;
                    rgba[offset + 1] = color.G;
                    rgba[offset + 2] = color.B;
                    rgba[offset + 3] = color.A;
                }
            }
        }

        File.WriteAllBytes(Path.Combine(directory, fileName), EncodeRgbaPng(width, height, rgba));
    }

    internal static string[] Rows(int width, int height, params string[] overrides)
    {
        var rows = Enumerable.Repeat(new string('0', width), height).ToArray();
        for (var i = 0; i < overrides.Length; i++)
        {
            rows[i] = overrides[i];
        }

        return rows;
    }

    internal static int TileColor(IReadOnlyList<byte> tile, int x, int y)
    {
        var bit = 7 - x;
        var plane0 = (tile[y] >> bit) & 1;
        var plane1 = (tile[y + 8] >> bit) & 1;
        return plane0 | (plane1 << 1);
    }

    private static byte[] EncodeRgbaPng(int width, int height, byte[] rgba)
    {
        using var output = new MemoryStream();
        output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr[0..4], width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr[4..8], height);
        ihdr[8] = 8;
        ihdr[9] = 6;
        WritePngChunk(output, "IHDR", ihdr);

        using var raw = new MemoryStream();
        for (var y = 0; y < height; y++)
        {
            raw.WriteByte(0);
            raw.Write(rgba, y * width * 4, width * 4);
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            raw.Position = 0;
            raw.CopyTo(zlib);
        }

        WritePngChunk(output, "IDAT", compressed.ToArray());
        WritePngChunk(output, "IEND", []);
        return output.ToArray();
    }

    private static void WritePngChunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);

        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);

        var crc = Crc32(typeBytes, data);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        output.Write(crcBytes);
    }

    private static uint Crc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in type)
        {
            crc = UpdateCrc32(crc, value);
        }

        foreach (var value in data)
        {
            crc = UpdateCrc32(crc, value);
        }

        return crc ^ 0xFFFFFFFFu;
    }

    private static uint UpdateCrc32(uint crc, byte value)
    {
        crc ^= value;
        for (var i = 0; i < 8; i++)
        {
            crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
        }

        return crc;
    }
}
