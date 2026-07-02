[intrinsic("music_play")]
extern void portable2d_music_play(i16 theme);

[intrinsic("music_stop")]
extern void portable2d_music_stop();

class Music
{
    static inline [resource("music_asset")] void Asset(i16 name, i16 path)
    {
    }

    static inline void Play(i16 theme)
    {
        portable2d_music_play(theme);
    }

    static inline void Stop()
    {
        portable2d_music_stop();
    }
}
