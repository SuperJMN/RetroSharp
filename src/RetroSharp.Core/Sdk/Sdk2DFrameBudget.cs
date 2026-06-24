namespace RetroSharp.Core.Sdk;

using RetroSharp.Core.Targeting;

public sealed class Sdk2DFrameBudget
{
    public static Sdk2DFrameBudget Empty { get; } = new();

    public Sdk2DFrameBudget(
        int backgroundTileWrites = 0,
        int hardwareSprites = 0,
        SpriteSizeMode spriteSizeModes = SpriteSizeMode.None,
        IReadOnlyDictionary<int, int>? hardwareSpritesByScanline = null)
    {
        BackgroundTileWrites = backgroundTileWrites;
        HardwareSprites = hardwareSprites;
        SpriteSizeModes = spriteSizeModes;
        HardwareSpritesByScanline = hardwareSpritesByScanline?.ToDictionary() ?? new Dictionary<int, int>();
    }

    public int BackgroundTileWrites { get; }

    public int HardwareSprites { get; }

    public SpriteSizeMode SpriteSizeModes { get; }

    public IReadOnlyDictionary<int, int> HardwareSpritesByScanline { get; }

    public bool IsEmpty => BackgroundTileWrites == 0 && HardwareSprites == 0 && SpriteSizeModes == SpriteSizeMode.None && HardwareSpritesByScanline.Count == 0;

    public Sdk2DFrameBudget Add(Sdk2DFrameBudget other)
    {
        var scanlineCounts = HardwareSpritesByScanline.ToDictionary();
        foreach (var (scanline, count) in other.HardwareSpritesByScanline)
        {
            scanlineCounts[scanline] = scanlineCounts.GetValueOrDefault(scanline) + count;
        }

        return new Sdk2DFrameBudget(
            BackgroundTileWrites + other.BackgroundTileWrites,
            HardwareSprites + other.HardwareSprites,
            SpriteSizeModes | other.SpriteSizeModes,
            scanlineCounts);
    }
}
