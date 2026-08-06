namespace RetroSharp.Core.Sdk;

// Target-neutral outcome of a WorldPack runtime lookup or decode call. Game Boy and NES
// each compare and store this value directly in their own emitted machine code, so its
// byte values are part of the shipped contract: once a target's compiled code depends on
// one of these numbers, renumbering a member here would silently corrupt an
// already-emitted comparison instead of failing a build or a test.
public enum WorldPackRuntimeResult : byte
{
    Success = 0,
    Miss = 1,
    BoundsError = 2,
    Malformed = 3,
}
