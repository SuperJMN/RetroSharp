class Palette
{
    static inline [resource("palette_set")] void Set(i16 index, i16 color)
    {
    }

    static inline [resource("palette_background")] void Background(i16 slot, i16 c0, i16 c1, i16 c2, i16 c3)
    {
    }

    static inline [resource("palette_sprite")] void Sprite(i16 slot, i16 c0, i16 c1, i16 c2, i16 c3)
    {
    }
}

class ObjectPalette
{
    static inline [target("gb")] [resource("object_palette_set")] void Set(i16 index, i16 color)
    {
    }
}
