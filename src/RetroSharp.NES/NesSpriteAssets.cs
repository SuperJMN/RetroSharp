using System.Text.Json;
using RetroSharp.Core.Imaging;

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

    public required IReadOnlyList<byte[]?> SuggestedPalettes { get; init; }

    public byte[]? SuggestedPalette => SuggestedPalettes.Count > 0 ? SuggestedPalettes[0] : null;

    public int TileCount => TileData.Length / 16;

    public int MaxPaletteSlotOffset => Pieces.Count == 0 ? 0 : Pieces.Max(piece => piece.PaletteSlotOffset);
}

internal readonly record struct NesMetaspritePiece(
    int XOffset,
    int YOffset,
    int TileOffset,
    int PaletteSlotOffset = 0,
    bool Optional = false,
    int LayerIndex = 0);

internal readonly record struct NesPaletteColor(byte Index, byte R, byte G, byte B);

internal static class NesSpriteAssetCompiler
{
    private static readonly NesPaletteColor[] HardwarePalette =
    [
        new(0x00, 0x75, 0x75, 0x75),
        new(0x01, 0x27, 0x1B, 0x8F),
        new(0x02, 0x00, 0x00, 0xAB),
        new(0x03, 0x47, 0x00, 0x9F),
        new(0x04, 0x8F, 0x00, 0x77),
        new(0x05, 0xAB, 0x00, 0x13),
        new(0x06, 0xA7, 0x00, 0x00),
        new(0x07, 0x7F, 0x0B, 0x00),
        new(0x08, 0x43, 0x2F, 0x00),
        new(0x09, 0x00, 0x47, 0x00),
        new(0x0A, 0x00, 0x51, 0x00),
        new(0x0B, 0x00, 0x3F, 0x17),
        new(0x0C, 0x1B, 0x3F, 0x5F),
        new(0x0F, 0x00, 0x00, 0x00),
        new(0x10, 0xBC, 0xBC, 0xBC),
        new(0x11, 0x00, 0x73, 0xEF),
        new(0x12, 0x23, 0x3B, 0xEF),
        new(0x13, 0x83, 0x00, 0xF3),
        new(0x14, 0xBF, 0x00, 0xBF),
        new(0x15, 0xE7, 0x00, 0x5B),
        new(0x16, 0xD8, 0x28, 0x00),
        new(0x17, 0xC8, 0x4C, 0x0C),
        new(0x18, 0x88, 0x70, 0x00),
        new(0x19, 0x00, 0x97, 0x00),
        new(0x1A, 0x00, 0xAB, 0x00),
        new(0x1B, 0x00, 0x93, 0x3B),
        new(0x1C, 0x00, 0x83, 0x8B),
        new(0x1D, 0x00, 0x00, 0x00),
        new(0x20, 0xFF, 0xFF, 0xFF),
        new(0x21, 0x3F, 0xBF, 0xFF),
        new(0x22, 0x5F, 0x97, 0xFF),
        new(0x23, 0xA7, 0x8B, 0xFD),
        new(0x24, 0xF7, 0x7B, 0xFF),
        new(0x25, 0xFF, 0x77, 0xB7),
        new(0x26, 0xFC, 0x74, 0x60),
        new(0x27, 0xFF, 0x9B, 0x3B),
        new(0x28, 0xF3, 0xBF, 0x3F),
        new(0x29, 0x83, 0xD3, 0x13),
        new(0x2A, 0x4F, 0xDF, 0x4B),
        new(0x2B, 0x58, 0xF8, 0x98),
        new(0x2C, 0x00, 0xEB, 0xDB),
        new(0x2D, 0x00, 0x00, 0x00),
        new(0x30, 0xFF, 0xFF, 0xFF),
        new(0x31, 0xAB, 0xE7, 0xFF),
        new(0x32, 0xC7, 0xD7, 0xFF),
        new(0x33, 0xD7, 0xCB, 0xFF),
        new(0x34, 0xFF, 0xC7, 0xFF),
        new(0x35, 0xFF, 0xC7, 0xDB),
        new(0x36, 0xFC, 0xBC, 0xB0),
        new(0x37, 0xFF, 0xDB, 0xAB),
        new(0x38, 0xFF, 0xE7, 0xA3),
        new(0x39, 0xE3, 0xFF, 0xA3),
        new(0x3A, 0xAB, 0xF3, 0xBF),
        new(0x3B, 0xB3, 0xFF, 0xCF),
        new(0x3C, 0x9F, 0xFF, 0xF3),
        new(0x3D, 0x00, 0x00, 0x00),
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static NesCompiledSpriteAsset CompileFromFile(string name, string path, int firstTile, int? frameWidth = null, int? frameHeight = null)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"NES sprite asset file '{path}' does not exist.");
        }

        var sprite = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".json" => ReadJsonFrames(path),
            ".png" => ReadPngFrames(path, frameWidth, frameHeight),
            _ => throw new InvalidOperationException($"NES sprite asset '{path}' must be a .json or .png file."),
        };

        var sourceFrames = sprite.Layers[0].Frames;
        var frameCount = sourceFrames.Count;
        var logicalWidth = sourceFrames[0][0].Length;
        var logicalHeight = sourceFrames[0].Count;

        var layers = sprite.Layers
            .Select(layer => layer with { Frames = PadFramesToHardwareCells(layer.Frames) })
            .ToList();
        var frames = layers[0].Frames;
        var width = frames[0][0].Length;
        var height = frames[0].Count;
        ValidateLayerGeometry(layers, frameCount, width, height);

        var pieces = BuildPieces(layers, width, height);
        var tilesPerFrame = pieces.Count;
        var tileCount = frames.Count * tilesPerFrame;

        if (firstTile + tileCount > 256)
        {
            throw new InvalidOperationException($"NES sprite asset '{name}' exceeds the 256 tile pattern-table index range.");
        }

        var tileData = new byte[tileCount * 16];
        for (var frameIndex = 0; frameIndex < frames.Count; frameIndex++)
        {
            for (var pieceIndex = 0; pieceIndex < pieces.Count; pieceIndex++)
            {
                var piece = pieces[pieceIndex];
                var frame = layers[piece.LayerIndex].Frames[frameIndex];
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
            SuggestedPalettes = layers.Select(layer => layer.SuggestedPalette).ToArray(),
        };
    }

    private static SpriteSource ReadJsonFrames(string path)
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

        return new SpriteSource(
        [
            new SpriteLayerSource(
                ValidateFrames(platform.Frames, path),
                SuggestedPalette: null,
                PaletteSlotOffset: 0,
                Optional: false,
                IncludeAllHardwareCells: true),
        ]);
    }

    private static SpriteSource ReadPngFrames(string path, int? frameWidth, int? frameHeight)
    {
        if (frameWidth is null || frameHeight is null)
        {
            throw new InvalidOperationException($"PNG sprite asset '{path}' requires frame width and height arguments.");
        }

        if (frameWidth <= 0 || frameHeight <= 0)
        {
            throw new InvalidOperationException("PNG sprite frame dimensions must be positive.");
        }

        var image = PngImage.Read(path);
        if (image.Width % frameWidth.Value != 0)
        {
            throw new InvalidOperationException($"PNG sprite sheet '{path}' width must be a multiple of the frame width.");
        }

        if (image.Height != frameHeight.Value)
        {
            throw new InvalidOperationException($"PNG sprite sheet '{path}' height must match the frame height.");
        }

        var frameCount = image.Width / frameWidth.Value;
        if (frameCount == 0)
        {
            throw new InvalidOperationException($"PNG sprite sheet '{path}' must contain at least one frame.");
        }

        var colorMap = BuildPngColorMap(image);
        var layers = new List<SpriteLayerSource>
        {
            new(
                ReadPngFrames(path, image, frameWidth.Value, frameHeight.Value, colorMap?.BaseColorIndexes),
                colorMap?.BaseSuggestedPalette,
                PaletteSlotOffset: 0,
                Optional: false,
                IncludeAllHardwareCells: true),
        };

        if (colorMap?.OverlayColorIndexes.Count > 0)
        {
            layers.Add(
                new SpriteLayerSource(
                    ReadPngFrames(path, image, frameWidth.Value, frameHeight.Value, colorMap.OverlayColorIndexes),
                    colorMap.OverlaySuggestedPalette,
                    PaletteSlotOffset: 1,
                    Optional: true,
                    IncludeAllHardwareCells: false));
        }

        return new SpriteSource(layers);
    }

    private static List<List<string>> ReadPngFrames(
        string path,
        PngImage image,
        int frameWidth,
        int frameHeight,
        IReadOnlyDictionary<int, int>? colorIndexes)
    {
        var frameCount = image.Width / frameWidth;
        var frames = new List<List<string>>();
        for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            var frame = new List<string>();
            for (var y = 0; y < frameHeight; y++)
            {
                var row = new char[frameWidth];
                for (var x = 0; x < frameWidth; x++)
                {
                    row[x] = (char)('0' + SpriteTone(image, frameIndex * frameWidth + x, y, colorIndexes));
                }

                frame.Add(new string(row));
            }

            frames.Add(frame);
        }

        return ValidateFrames(frames, path);
    }

    private static PngColorMap? BuildPngColorMap(PngImage image)
    {
        var colorCounts = new Dictionary<int, ColorCount>();
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var offset = image.PixelOffset(x, y);
                if (image.RgbaPixels[offset + 3] < 128)
                {
                    continue;
                }

                var color = new SpriteColor(
                    image.RgbaPixels[offset],
                    image.RgbaPixels[offset + 1],
                    image.RgbaPixels[offset + 2]);
                var key = color.ToRgbKey();
                colorCounts[key] = colorCounts.TryGetValue(key, out var count)
                    ? count.Increment()
                    : new ColorCount(color, 1);
            }
        }

        if (colorCounts.Count == 0)
        {
            return null;
        }

        var orderedColors = colorCounts.Values
            .OrderByDescending(color => color.Count)
            .ThenBy(color => color.Color.ToRgbKey())
            .ToList();
        var representatives = orderedColors
            .Take(3)
            .Select(color => color.Color)
            .ToList();

        var indexes = AssignSpritePaletteIndexes(representatives);
        var baseColorIndexes = indexes.ToDictionary(pair => pair.Key.ToRgbKey(), pair => pair.Value);
        var suggestedPalette = BuildSuggestedPalette(indexes);
        var overlayRepresentatives = orderedColors
            .Skip(3)
            .Take(3)
            .Select(color => color.Color)
            .ToList();
        if (overlayRepresentatives.Count == 0)
        {
            return new PngColorMap(baseColorIndexes, suggestedPalette, new Dictionary<int, int>(), null);
        }

        var overlayIndexes = AssignSpritePaletteIndexes(overlayRepresentatives);
        var overlayColorIndexes = orderedColors
            .Skip(3)
            .ToDictionary(
                color => color.Color.ToRgbKey(),
                color => overlayIndexes[NearestColor(color.Color, overlayRepresentatives)]);
        var overlaySuggestedPalette = BuildSuggestedPalette(overlayIndexes);

        return new PngColorMap(baseColorIndexes, suggestedPalette, overlayColorIndexes, overlaySuggestedPalette);
    }

    private static byte[] BuildSuggestedPalette(IReadOnlyDictionary<SpriteColor, int> indexes)
    {
        var suggestedPalette = new byte[] { 0x0F, 0x30, 0x10, 0x0F };
        foreach (var (representative, index) in indexes)
        {
            suggestedPalette[index] = NearestNesPaletteIndex(representative);
        }

        return suggestedPalette;
    }

    private static Dictionary<SpriteColor, int> AssignSpritePaletteIndexes(IReadOnlyList<SpriteColor> colors)
    {
        if (colors.Count == 1)
        {
            var color = colors[0];
            return new Dictionary<SpriteColor, int>
            {
                [color] = Luminance(color) switch
                {
                    >= 192 => 1,
                    >= 96 => 2,
                    _ => 3,
                },
            };
        }

        return colors
            .OrderByDescending(Luminance)
            .ThenBy(color => color.ToRgbKey())
            .Select((color, index) => (color, paletteIndex: index + 1))
            .ToDictionary(item => item.color, item => item.paletteIndex);
    }

    private static SpriteColor NearestColor(SpriteColor color, IReadOnlyList<SpriteColor> candidates)
    {
        return candidates
            .OrderBy(candidate => ColorDistanceSquared(color, candidate))
            .ThenBy(candidate => candidate.ToRgbKey())
            .First();
    }

    private static byte NearestNesPaletteIndex(SpriteColor color)
    {
        return HardwarePalette
            .OrderBy(candidate => ColorDistanceSquared(color, candidate))
            .ThenBy(candidate => candidate.Index)
            .First()
            .Index;
    }

    private static int SpriteTone(PngImage image, int x, int y, IReadOnlyDictionary<int, int>? colorIndexes)
    {
        var offset = image.PixelOffset(x, y);
        if (image.RgbaPixels[offset + 3] < 128)
        {
            return 0;
        }

        var color = new SpriteColor(
            image.RgbaPixels[offset],
            image.RgbaPixels[offset + 1],
            image.RgbaPixels[offset + 2]);

        if (colorIndexes is not null)
        {
            return colorIndexes.TryGetValue(color.ToRgbKey(), out var index) ? index : 0;
        }

        return Luminance(color) switch
        {
            >= 192 => 1,
            >= 96 => 2,
            _ => 3,
        };
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

    private static void ValidateLayerGeometry(
        IReadOnlyList<SpriteLayerSource> layers,
        int frameCount,
        int width,
        int height)
    {
        foreach (var layer in layers)
        {
            if (layer.Frames.Count != frameCount ||
                layer.Frames[0].Count != height ||
                layer.Frames[0][0].Length != width)
            {
                throw new InvalidOperationException("NES PNG sprite layers must have identical frame geometry.");
            }
        }
    }

    private static IReadOnlyList<NesMetaspritePiece> BuildPieces(
        IReadOnlyList<SpriteLayerSource> layers,
        int width,
        int height)
    {
        var pieces = new List<NesMetaspritePiece>();
        for (var layerIndex = 0; layerIndex < layers.Count; layerIndex++)
        {
            var layer = layers[layerIndex];
            for (var y = 0; y < height; y += 8)
            {
                for (var x = 0; x < width; x += 8)
                {
                    if (!layer.IncludeAllHardwareCells && !CellHasOpaquePixel(layer.Frames, x, y))
                    {
                        continue;
                    }

                    pieces.Add(
                        new NesMetaspritePiece(
                            x,
                            y,
                            pieces.Count,
                            layer.PaletteSlotOffset,
                            layer.Optional,
                            layerIndex));
                }
            }
        }

        return pieces;
    }

    private static bool CellHasOpaquePixel(IReadOnlyList<List<string>> frames, int sourceX, int sourceY)
    {
        foreach (var frame in frames)
        {
            for (var row = 0; row < 8; row++)
            {
                for (var col = 0; col < 8; col++)
                {
                    if (frame[sourceY + row][sourceX + col] != '0')
                    {
                        return true;
                    }
                }
            }
        }

        return false;
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

    private sealed record SpriteSource(IReadOnlyList<SpriteLayerSource> Layers);

    private sealed record SpriteLayerSource(
        List<List<string>> Frames,
        byte[]? SuggestedPalette,
        int PaletteSlotOffset,
        bool Optional,
        bool IncludeAllHardwareCells);

    private sealed record PngColorMap(
        IReadOnlyDictionary<int, int> BaseColorIndexes,
        byte[] BaseSuggestedPalette,
        IReadOnlyDictionary<int, int> OverlayColorIndexes,
        byte[]? OverlaySuggestedPalette);

    private readonly record struct ColorCount(SpriteColor Color, int Count)
    {
        public ColorCount Increment() => this with { Count = Count + 1 };
    }

    private readonly record struct SpriteColor(byte R, byte G, byte B)
    {
        public int ToRgbKey() => (R << 16) | (G << 8) | B;
    }

    private static int Luminance(SpriteColor color)
    {
        return (color.R * 299 + color.G * 587 + color.B * 114) / 1000;
    }

    private static int ColorDistanceSquared(SpriteColor left, SpriteColor right)
    {
        var r = left.R - right.R;
        var g = left.G - right.G;
        var b = left.B - right.B;
        return (r * r) + (g * g) + (b * b);
    }

    private static int ColorDistanceSquared(SpriteColor left, NesPaletteColor right)
    {
        var r = left.R - right.R;
        var g = left.G - right.G;
        var b = left.B - right.B;
        return (r * r) + (g * g) + (b * b);
    }
}
