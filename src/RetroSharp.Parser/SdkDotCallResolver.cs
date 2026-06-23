namespace RetroSharp.Parser;

public enum SdkDotCallKind
{
    SdkModule,
    Receiver,
    Unknown,
}

// Single source of truth for the dot-call lowering decision shared by constant
// folding and semantic analysis. The canonical precedence is receiver-first: a
// receiver in scope (a value/variable, detected by scope in semantic analysis or by
// a matching receiver-method signature during folding) shadows an SDK module of the
// same name, matching lexical scoping. Otherwise a known SDK module name resolves as
// an SDK call; otherwise the call is unknown. Keeping this rule in one place stops
// the front-end and the folder from drifting into different receiver-vs-module
// precedence.
//
// Note: during folding there is no scope, so "hasReceiver" is a receiver-method
// signature match. This coincides with the scope-based decision for every valid
// program; giving the folder full variable scope (to also unify the pathological
// case where a receiver method name collides with an SDK method on a bare module)
// remains a larger, separately-scoped change.
public static class SdkDotCallResolver
{
    public static SdkDotCallKind Resolve(bool isKnownSdkModule, bool hasReceiver)
    {
        if (hasReceiver)
        {
            return SdkDotCallKind.Receiver;
        }

        return isKnownSdkModule ? SdkDotCallKind.SdkModule : SdkDotCallKind.Unknown;
    }
}
