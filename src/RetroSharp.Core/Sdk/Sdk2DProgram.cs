namespace RetroSharp.Core.Sdk;

// A portable SDK operation program split into a main stream plus per-subroutine
// streams. This is the foundation for emitting user functions as real
// subroutines (CALL/RET) instead of inlining them at every call site: each
// subroutined function gets its own ordered operation stream collected once,
// and call sites reference it through a CallSubroutine marker.
//
// When no function is designated as a subroutine the main stream is a flat
// sequence of Op items equivalent to the legacy inline-expanded operation list,
// so existing targets stay byte-identical.
public abstract record Sdk2DStreamItem : ISdkStreamItem<Sdk2DOperation>
{
    Sdk2DOperation? ISdkStreamItem<Sdk2DOperation>.Operation => OperationOrDefault;

    string? ISdkStreamItem<Sdk2DOperation>.SubroutineCallName => SubroutineCallNameOrDefault;

    private protected virtual Sdk2DOperation? OperationOrDefault => null;

    private protected virtual string? SubroutineCallNameOrDefault => null;

    // A concrete portable operation to emit in order.
    public sealed record Op(Sdk2DOperation Operation) : Sdk2DStreamItem
    {
        private protected override Sdk2DOperation? OperationOrDefault => Operation;
    }

    // A call to a user function emitted as a shared subroutine. The consumer
    // emits a machine CALL here and consumes the named subroutine stream when it
    // later emits that subroutine body.
    public sealed record CallSubroutine(string Name) : Sdk2DStreamItem
    {
        private protected override string? SubroutineCallNameOrDefault => Name;
    }
}

public sealed record Sdk2DProgram(
    IReadOnlyList<Sdk2DStreamItem> Main,
    IReadOnlyDictionary<string, IReadOnlyList<Sdk2DStreamItem>> Subroutines);
