[intrinsic("animation_frame")]
extern i16 portable2d_animation_frame(i16 name, i16 tick);

class Animation
{
    static inline [resource("animation_clip")] void Clip(i16 name, i16 firstFrame, i16 duration)
    {
    }

    static inline i16 Frame(i16 name, i16 tick)
    {
        return portable2d_animation_frame(name, tick);
    }
}
