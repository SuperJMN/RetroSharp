[intrinsic("wait_frame")]
extern void acme_timing_wait_frame();

class Timing
{
    static inline void Tick()
    {
        const ticksPerFrame = Frame.Rules.TicksPerFrame;
        acme_timing_wait_frame();
    }
}
