const __retrosharp_sdk_library_gb = 1;
const __retrosharp_sdk_library_nes = 1;

enum Button { A, B, Select, Start, Right, Left, Up, Down }

[intrinsic("wait_frame")]
extern void portable2d_wait_frame();

[intrinsic("poll_input")]
extern void portable2d_poll_input();

[intrinsic("audio_init")]
extern void portable2d_audio_init();

[intrinsic("audio_update")]
extern void portable2d_audio_update();

class Video
{
    static inline void WaitVBlank()
    {
        portable2d_wait_frame();
    }
}

class Input
{
    static inline void Poll()
    {
        portable2d_poll_input();
    }
}

class Audio
{
    static inline void Init()
    {
        portable2d_audio_init();
    }

    static inline void Update()
    {
        portable2d_audio_update();
    }
}
