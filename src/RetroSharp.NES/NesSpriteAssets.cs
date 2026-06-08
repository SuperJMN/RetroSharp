using System.Text.Json;

namespace RetroSharp.NES;

internal sealed class NesCompiledSpriteAsset
{
    public required string Name { get; init; }

    public required int FirstTile { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }

    public required int LogicalWidth { get; init; }

    public required int LogicalHeight { get; init; }

    public required int FrameCount { get; init; }

    public required int TilesPerFrame { get; init; }

    public required byte[] TileData { get; init; }

    public required IReadOnlyList<NesMetaspritePiece> Pieces { get; init; }

    public int TileCount => TileData.Length / 16;
}

internal readonly record struct NesMetaspritePiece(int XOffset, int YOffset, int TileOffset);

internal static class NesSpriteAssetCompiler
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static NesCompiledSpriteAsset CompileFromFile(string name, string path, int firstTile)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"NES sprite asset file '{path}' does not exist.");
        }

        if (!Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"NES sprite asset '{path}' must be a .json file for the current sprite spike.");
        }

        var frames = ReadJsonFrames(path);
        var frameCount = frames.Count;
        var logicalWidth = frames[0][0].Length;
        var logicalHeight = frames[0].Count;

        frames = PadFramesToHardwareCells(frames);
        var width = frames[0][0].Length;
        var height = frames[0].Count;
        var pieces = BuildPieces(width, height);
        var tilesPerFrame = pieces.Count;
        var tileCount = frames.Count * tilesPerFrame;

        if (firstTile + tileCount > 256)
        {
            throw new InvalidOperationException($"NES sprite asset '{name}' exceeds the 256 tile pattern-table index range.");
        }

        var tileData = new byte[tileCount * 16];
        for (var frameIndex = 0; frameIndex < frames.Count; frameIndex++)
        {
            var frame = frames[frameIndex];
            for (var pieceIndex = 0; pieceIndex < pieces.Count; pieceIndex++)
            {
                var piece = pieces[pieceIndex];
                var tile = frameIndex * tilesPerFrame + pieceIndex;
                WriteTile(frame, piece.XOffset, piece.YOffset, tileData, tile);
            }
        }

        return new NesCompiledSpriteAsset
        {
            Name = name,
            FirstTile = firstTile,
            Width = width,
            Height = height,
            LogicalWidth = logicalWidth,
            LogicalHeight = logicalHeight,
            FrameCount = frameCount,
            TilesPerFrame = tilesPerFrame,
            TileData = tileData,
            Pieces = pieces,
        };
    }

    private static List<List<string>> ReadJsonFrames(string path)
    {
        var document = JsonSerializer.Deserialize<SpriteAssetDocument>(File.ReadAllText(path), JsonOptions)
                       ?? throw new InvalidOperationException($"NES sprite asset file '{path}' is empty.");

        if (document.Platforms is null)
        {
            throw new InvalidOperationException($"NES sprite asset '{path}' must define a platforms object.");
        }

        var platform = document.Platforms
            .Where(pair => pair.Key.Equals("nes", StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair.Value)
            .FirstOrDefault();

        if (platform is null)
        {
            throw new InvalidOperationException($"NES sprite asset '{path}' does not contain a NES platform variant.");
        }

        return ValidateFrames(platform.Frames, path);
    }

    private static List<List<string>> ValidateFrames(List<List<string>>? frames, string path)
    {
        if (frames is null || frames.Count == 0)
        {
            throw new InvalidOperationException($"NES sprite asset '{path}' must contain at least one frame.");
        }

        if (frames[0].Count == 0)
        {
            throw new InvalidOperationException($"NES sprite asset '{path}' frames must contain at least one row.");
        }

        var height = frames[0].Count;
        var width = frames[0][0].Length;
        if (width == 0)
        {
            throw new InvalidOperationException($"NES sprite asset '{path}' frames must contain at least one column.");
        }

        for (var frameIndex = 0; frameIndex < frames.Count; frameIndex++)
        {
            var frame = frames[frameIndex];
            if (frame.Count != height)
            {
                throw new InvalidOperationException($"NES sprite asset '{path}' frame {frameIndex} has inconsistent height.");
            }

            for (var rowIndex = 0; rowIndex < frame.Count; rowIndex++)
            {
                var row = frame[rowIndex];
                if (row.Length != width)
                {
                    throw new InvalidOperationException($"NES sprite asset '{path}' frame {frameIndex}, row {rowIndex} has inconsistent width.");
                }

                foreach (var pixel in row)
                {
                    if (pixel is < '0' or > '3')
                    {
                        throw new InvalidOperationException($"NES sprite asset '{path}' can only use NES color indexes 0, 1, 2, and 3.");
                    }
                }
            }
        }

        return frames;
    }

    private static List<List<string>> PadFramesToHardwareCells(List<List<string>> frames)
    {
        var width = frames[0][0].Length;
        var height = frames[0].Count;
        var paddedWidth = RoundUp(width, 8);
        var paddedHeight = RoundUp(height, 8);
        var pieceCount = paddedWidth / 8 * (paddedHeight / 8);
        if (pieceCount > 64)
        {
            throw new InvalidOperationException($"NES sprite asset needs {pieceCount} hardware sprites, but the hardware limit is 64.");
        }

        if (paddedWidth == width && paddedHeight == height)
        {
            return frames;
        }

        return frames
            .Select(frame =>
            {
                var padded = frame
                    .Select(row => row.PadRight(paddedWidth, '0'))
                    .ToList();

                while (padded.Count < paddedHeight)
                {
                    padded.Add(new string('0', paddedWidth));
                }

                return padded;
            })
            .ToList();
    }

    private static int RoundUp(int value, int unit)
    {
        return ((value + unit - 1) / unit) * unit;
    }

    private static IReadOnlyList<NesMetaspritePiece> BuildPieces(int width, int height)
    {
        var pieces = new List<NesMetaspritePiece>();
        for (var y = 0; y < height; y += 8)
        {
            for (var x = 0; x < width; x += 8)
            {
                pieces.Add(new NesMetaspritePiece(x, y, pieces.Count));
            }
        }

        return pieces;
    }

    private static void WriteTile(IReadOnlyList<string> frame, int sourceX, int sourceY, byte[] tileData, int tile)
    {
        for (var row = 0; row < 8; row++)
        {
            var plane0 = 0;
            var plane1 = 0;
            for (var col = 0; col < 8; col++)
            {
                var color = frame[sourceY + row][sourceX + col] - '0';
                var bit = 7 - col;
                if ((color & 1) != 0) plane0 |= 1 << bit;
                if ((color & 2) != 0) plane1 |= 1 << bit;
            }

            var offset = tile * 16;
            tileData[offset + row] = (byte)plane0;
            tileData[offset + 8 + row] = (byte)plane1;
        }
    }

    private sealed class SpriteAssetDocument
    {
        public Dictionary<string, SpriteAssetPlatform>? Platforms { get; set; }
    }

    private sealed class SpriteAssetPlatform
    {
        public List<List<string>>? Frames { get; set; }
    }
}
