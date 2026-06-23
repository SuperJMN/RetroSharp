namespace RetroSharp.Core.Sdk;

using RetroSharp.Core.Targeting;

public static class SdkPaletteValidator
{
    public static void Validate(Target2DCapabilities capabilities, PaletteKind kind, int slot, int colorCount)
    {
        if (colorCount != 4)
        {
            throw new InvalidOperationException($"palette declarations must contain exactly 4 colors, got {colorCount}.");
        }

        var maxSlot = kind switch
        {
            PaletteKind.Background => capabilities.BackgroundPaletteSlots - 1,
            PaletteKind.Sprite => capabilities.SpritePaletteSlots - 1,
            _ => throw new InvalidOperationException($"Unsupported palette kind '{kind}'."),
        };

        if (slot < 0 || slot > maxSlot)
        {
            throw new InvalidOperationException(
                $"Target '{capabilities.Name}' supports {PaletteName(kind)} palette slots 0..{maxSlot}, but palette slot {slot} was requested.");
        }
    }

    private static string PaletteName(PaletteKind kind)
    {
        return kind switch
        {
            PaletteKind.Background => "background",
            PaletteKind.Sprite => "sprite",
            _ => throw new InvalidOperationException($"Unsupported palette kind '{kind}'."),
        };
    }
}
