using System.Text.Json;

namespace RetroSharp.GameBoy;

internal sealed class GameBoyCompiledSpriteAsset
{
    public required string Name { get; init; }

    public required int FirstTile { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }

    public required int FrameCount { get; init; }

    public required int TilesPerFrame { get; init; }

    public required byte[] TileData { get; init; }

    public required IReadOnlyList<GameBoyMetaspritePiece> Pieces { get; init; }

    public int TileCount => TileData.Length / 16;
}

internal readonly record struct GameBoyMetaspritePiece(int XOffset, int YOffset, int TileOffset);

internal static class GameBoySpriteAssetCompiler
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static GameBoyCompiledSpriteAsset CompileFromFile(string name, string path, int firstTile, int? frameWidth = null, int? frameHeight = null)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Game Boy sprite asset file '{path}' does not exist.");
        }

        var frames = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".json" => ReadJsonFrames(path),
            ".png" => ReadPngFrames(path, frameWidth, frameHeight),
            _ => throw new InvalidOperationException($"Game Boy sprite asset '{path}' must be a .json or .png file."),
        };

        frames = PadFramesToHardwareCells(frames);
        var width = frames[0][0].Length;
        var height = frames[0].Count;
        var pieces = BuildPieces(width, height);
        var tilesPerFrame = pieces.Count * 2;
        var tileCount = frames.Count * tilesPerFrame;

        if ((firstTile & 1) != 0)
        {
            throw new InvalidOperationException("Game Boy 8x16 sprite assets must start at an even tile index.");
        }

        if (firstTile + tileCount > 256)
        {
            throw new InvalidOperationException($"Game Boy sprite asset '{name}' exceeds the 256 tile VRAM index range.");
        }

        var tileData = new byte[tileCount * 16];
        for (var frameIndex = 0; frameIndex < frames.Count; frameIndex++)
        {
            var frame = frames[frameIndex];
            for (var pieceIndex = 0; pieceIndex < pieces.Count; pieceIndex++)
            {
                var piece = pieces[pieceIndex];
                var tile = frameIndex * tilesPerFrame + pieceIndex * 2;
                WriteTile(frame, piece.XOffset, piece.YOffset, tileData, tile);
                WriteTile(frame, piece.XOffset, piece.YOffset + 8, tileData, tile + 1);
            }
        }

        return new GameBoyCompiledSpriteAsset
        {
            Name = name,
            FirstTile = firstTile,
            Width = width,
            Height = height,
            FrameCount = frames.Count,
            TilesPerFrame = tilesPerFrame,
            TileData = tileData,
            Pieces = pieces,
        };
    }

    private static List<List<string>> ReadJsonFrames(string path)
    {
        var document = JsonSerializer.Deserialize<SpriteAssetDocument>(File.ReadAllText(path), JsonOptions)
                       ?? throw new InvalidOperationException($"Game Boy sprite asset file '{path}' is empty.");

        var platform = SelectGameBoyPlatform(document, path);
        return ValidateFrames(platform.Frames, path);
    }

    private static List<List<string>> ReadPngFrames(string path, int? frameWidth, int? frameHeight)
    {
        if (frameWidth is null || frameHeight is null)
        {
            throw new InvalidOperationException($"PNG sprite asset '{path}' requires frame width and height arguments.");
        }

        return ValidateFrames(GameBoyPngSpriteSheet.ReadFrames(path, frameWidth.Value, frameHeight.Value), path);
    }

    private static SpriteAssetPlatform SelectGameBoyPlatform(SpriteAssetDocument document, string path)
    {
        if (document.Platforms is null)
        {
            throw new InvalidOperationException($"Sprite asset '{path}' must define a platforms object.");
        }

        var platform = document.Platforms
            .Where(pair => pair.Key.Equals("gb", StringComparison.OrdinalIgnoreCase)
                           || pair.Key.Equals("gameboy", StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair.Value)
            .FirstOrDefault();

        return platform ?? throw new InvalidOperationException($"Sprite asset '{path}' does not contain a Game Boy platform variant.");
    }

    private static List<List<string>> ValidateFrames(List<List<string>>? frames, string path)
    {
        if (frames is null || frames.Count == 0)
        {
            throw new InvalidOperationException($"Sprite asset '{path}' must contain at least one frame.");
        }

        if (frames[0].Count == 0)
        {
            throw new InvalidOperationException($"Sprite asset '{path}' frames must contain at least one row.");
        }

        var height = frames[0].Count;
        var width = frames[0][0].Length;
        if (width == 0)
        {
            throw new InvalidOperationException($"Sprite asset '{path}' frames must contain at least one column.");
        }

        for (var frameIndex = 0; frameIndex < frames.Count; frameIndex++)
        {
            var frame = frames[frameIndex];
            if (frame.Count != height)
            {
                throw new InvalidOperationException($"Sprite asset '{path}' frame {frameIndex} has inconsistent height.");
            }

            for (var rowIndex = 0; rowIndex < frame.Count; rowIndex++)
            {
                var row = frame[rowIndex];
                if (row.Length != width)
                {
                    throw new InvalidOperationException($"Sprite asset '{path}' frame {frameIndex}, row {rowIndex} has inconsistent width.");
                }

                foreach (var pixel in row)
                {
                    if (pixel is < '0' or > '3')
                    {
                        throw new InvalidOperationException($"Sprite asset '{path}' can only use Game Boy color indexes 0, 1, 2, and 3.");
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
        var paddedHeight = RoundUp(height, 16);
        var pieceCount = paddedWidth / 8 * (paddedHeight / 16);
        if (pieceCount > 40)
        {
            throw new InvalidOperationException($"Game Boy sprite asset needs {pieceCount} hardware sprites, but the hardware limit is 40.");
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

    private static IReadOnlyList<GameBoyMetaspritePiece> BuildPieces(int width, int height)
    {
        var pieces = new List<GameBoyMetaspritePiece>();
        for (var y = 0; y < height; y += 16)
        {
            for (var x = 0; x < width; x += 8)
            {
                pieces.Add(new GameBoyMetaspritePiece(x, y, pieces.Count * 2));
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

            var offset = tile * 16 + row * 2;
            tileData[offset] = (byte)plane0;
            tileData[offset + 1] = (byte)plane1;
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
