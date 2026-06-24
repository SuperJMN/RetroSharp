namespace RetroSharp.NES;

internal static class NesPalette
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

    public static byte NearestIndex(byte r, byte g, byte b)
    {
        if (TryPreserveCyanHue(r, g, b, out var cyanIndex))
        {
            return cyanIndex;
        }

        var sourceHue = Hue(r, g, b);
        var sourceSaturation = Saturation(r, g, b);
        return HardwarePalette
            .OrderBy(candidate => PaletteMatchScore(r, g, b, sourceHue, sourceSaturation, candidate))
            .ThenBy(candidate => candidate.Index)
            .First()
            .Index;
    }

    public static int Luminance(byte r, byte g, byte b)
    {
        return (r * 299 + g * 587 + b * 114) / 1000;
    }

    private static bool TryPreserveCyanHue(byte r, byte g, byte b, out byte index)
    {
        if (g >= 180 && b >= 200 && r + 80 < g && r + 80 < b)
        {
            index = 0x2C;
            return true;
        }

        index = 0;
        return false;
    }

    private static double PaletteMatchScore(byte r, byte g, byte b, double sourceHue, double sourceSaturation, NesPaletteColor color)
    {
        var rgbDistance = ColorDistanceSquared(r, g, b, color);
        if (sourceSaturation < 0.35)
        {
            return rgbDistance;
        }

        var candidateSaturation = Saturation(color.R, color.G, color.B);
        if (candidateSaturation < 0.20)
        {
            return rgbDistance + 20000;
        }

        var hueDistance = HueDistance(sourceHue, Hue(color.R, color.G, color.B));
        return rgbDistance + hueDistance * hueDistance * 120;
    }

    private static int ColorDistanceSquared(byte r, byte g, byte b, NesPaletteColor color)
    {
        var dr = r - color.R;
        var dg = g - color.G;
        var db = b - color.B;
        return (dr * dr) + (dg * dg) + (db * db);
    }

    private static double Saturation(byte r, byte g, byte b)
    {
        var max = Math.Max(r, Math.Max(g, b)) / 255.0;
        if (max == 0)
        {
            return 0;
        }

        var min = Math.Min(r, Math.Min(g, b)) / 255.0;
        return (max - min) / max;
    }

    private static double Hue(byte r, byte g, byte b)
    {
        var rd = r / 255.0;
        var gd = g / 255.0;
        var bd = b / 255.0;
        var max = Math.Max(rd, Math.Max(gd, bd));
        var min = Math.Min(rd, Math.Min(gd, bd));
        var delta = max - min;
        if (delta == 0)
        {
            return 0;
        }

        double hue;
        if (max == rd)
        {
            hue = 60 * (((gd - bd) / delta) % 6);
        }
        else if (max == gd)
        {
            hue = 60 * (((bd - rd) / delta) + 2);
        }
        else
        {
            hue = 60 * (((rd - gd) / delta) + 4);
        }

        return hue < 0 ? hue + 360 : hue;
    }

    private static double HueDistance(double left, double right)
    {
        var distance = Math.Abs(left - right);
        return Math.Min(distance, 360 - distance);
    }
}
