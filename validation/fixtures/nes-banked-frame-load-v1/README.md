# NES banked frame-load canary v1

This fixture is the stable behavioral canary for executable-code-banked NES
frame presentation. It combines a scrolling Tiled `WorldPack`, retained player
and Actor Framework sprites, Right+B input, music updates, collision work, and
two-pixel camera motion without depending on the editable runner.

The focused NES test compiles this source twice through internal target seams:
fixed MMC3 execution is the control, while forced `ProgramR6` execution is the
code-banked candidate. The in-process `NesTestCpu` observer is retained for
build, liveness, input progress, audio activity, reset safety, and safe PPU/OAM
writes. It does not decide smoothness: it reported batching for both this
fixture's physically smooth fixed control and the known-bad banked candidate.

Physical acceptance uses requested camera X 96 through 224 and user or
integrator playback to judge whether visible hold/catch-up remains objectionable.
The in-process transition sequence stays diagnostic until it can distinguish a
perceptually good control from a known-bad candidate. Resets and unsafe PPU/OAM
writes remain hard gates; exact ROM bytes, cycle counts, framebuffer pixels,
and sprite poses are diagnostics rather than fixture contracts.

The editable runner remains the user-visible integration scenario. This
fixture exists to keep the target-owned scheduling and banking interaction
repeatable when runner content changes.
