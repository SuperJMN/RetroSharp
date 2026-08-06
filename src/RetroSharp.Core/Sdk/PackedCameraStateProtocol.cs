namespace RetroSharp.Core.Sdk;

// Target-neutral packed-camera slot protocol vocabulary. Game Boy and NES each stage a
// scrolled edge through the same prepare/commit state machine, and describe it with the
// same axis and direction values; only the per-slot metadata's physical byte offsets and
// overall slot size are target owned (each target's own runtime declares those). These
// values are compared against and stored by each target's own emitted machine code
// directly, so every one is part of the shipped contract: renumbering a member here would
// silently corrupt an already-emitted comparison instead of failing a build or a test.
//
// NES also composes two of its own retry/prefetch flag bits onto a direction byte to
// support multi-call column staging, and uses its own "still preparing, call again"
// sentinel. Those are target-owned extensions of this vocabulary that Game Boy's slot
// layout has no equivalent multi-call staging for, so they stay declared next to NES's
// own runtime rather than widening this shared set.
public static class PackedCameraStateProtocol
{
    public const byte Empty = 0;
    public const byte Requested = 1;
    public const byte Preparing = 2;
    public const byte Resident = 3;
    public const byte Committing = 4;
    public const byte Released = 5;

    public const byte Column = 1;
    public const byte Row = 2;

    public const byte Negative = 1;
    public const byte Positive = 2;

    public const byte NoSlot = 0xFF;
}
