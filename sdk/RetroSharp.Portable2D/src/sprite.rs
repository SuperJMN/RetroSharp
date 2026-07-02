[intrinsic("sprite_draw")]
extern void portable2d_sprite_draw(i16 spriteId, i16 x, i16 y, i16 frame, bool flipX, i16 paletteSlot);

class Sprite
{
    static inline void Draw(i16 spriteId, i16 x, i16 y, i16 frame, bool flipX = false, i16 paletteSlot = 0)
    {
        portable2d_sprite_draw(spriteId, x, y, frame, flipX, paletteSlot);
    }
}
