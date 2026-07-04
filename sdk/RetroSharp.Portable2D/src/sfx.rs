[intrinsic("sfx_play")]
extern void portable2d_sfx_play(i16 sound);

class Sfx
{
    static inline [resource("sfx_asset")] void Asset(i16 name, i16 path)
    {
    }

    static inline void Play(i16 sound)
    {
        portable2d_sfx_play(sound);
    }
}
