const __retrosharp_sdk_library_gb = 1;
const __retrosharp_sdk_library_nes = 1;

enum Button { A, B, Select, Start, Right, Left, Up, Down }

[intrinsic("wait_frame")]
extern void portable2d_wait_frame();

[intrinsic("video_init")]
extern void portable2d_video_init();

[intrinsic("video_present")]
extern void portable2d_video_present();

[intrinsic("poll_input")]
extern void portable2d_poll_input();

[intrinsic("button_down")]
extern bool portable2d_button_down(Button button);

[intrinsic("button_just_pressed")]
extern bool portable2d_button_just_pressed(Button button);

[intrinsic("button_just_released")]
extern bool portable2d_button_just_released(Button button);

[intrinsic("button_hold_ticks")]
extern i16 portable2d_button_hold_ticks(Button button);

[intrinsic("audio_init")]
extern void portable2d_audio_init();

[intrinsic("audio_update")]
extern void portable2d_audio_update();

class Video
{
    static inline void Init()
    {
        portable2d_video_init();
    }

    static inline void WaitVBlank()
    {
        portable2d_wait_frame();
    }

    static inline void Present()
    {
        portable2d_video_present();
    }
}

class Input
{
    static inline void Poll()
    {
        portable2d_poll_input();
    }

    static inline bool IsDown(Button b)
    {
        return portable2d_button_down(b);
    }

    static inline bool WasPressed(Button b)
    {
        return portable2d_button_just_pressed(b);
    }

    static inline bool WasReleased(Button b)
    {
        return portable2d_button_just_released(b);
    }

    static inline i16 HoldTicks(Button b)
    {
        return portable2d_button_hold_ticks(b);
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
