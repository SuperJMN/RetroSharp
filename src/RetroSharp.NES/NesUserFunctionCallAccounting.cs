namespace RetroSharp.NES;

/// <summary>
/// Where a user function runs relative to the frame loop that
/// <see cref="NesProgramPhaseAnalyzer"/> found, expressed in the same vocabulary as
/// <see cref="NesPrgPlacementPhase"/>.
/// </summary>
internal enum NesUserFunctionPhase
{
    /// <summary>Only reached from startup/teardown code, never from the frame loop.</summary>
    Cold,

    /// <summary>Reached from the frame placement unit, so it runs every frame.</summary>
    Hot,

    /// <summary>The program has no frame loop at all, so every call runs once.</summary>
    OneShot,
}

/// <summary>
/// Argument shape of one call, split so that a future call ABI can budget the runtime part
/// without pretending that compile-time shape/resource operands cost anything at runtime.
/// Aggregates are counted, not converted into a byte claim, because NES has no user-function
/// ABI yet and their cost depends on whether that ABI copies or addresses them.
/// </summary>
internal sealed record NesUserFunctionArguments(
    int RuntimeBytes,
    int ReferenceArguments,
    int CompileTimeOperands)
{
    internal static readonly NesUserFunctionArguments None = new(0, 0, 0);

    internal NesUserFunctionArguments Max(NesUserFunctionArguments other) => new(
        Math.Max(RuntimeBytes, other.RuntimeBytes),
        Math.Max(ReferenceArguments, other.ReferenceArguments),
        Math.Max(CompileTimeOperands, other.CompileTimeOperands));
}

/// <summary>
/// How the runtime compiler realised one recorded user-function call.
/// </summary>
internal enum NesUserFunctionEmission
{
    /// <summary>The body was substituted and emitted at the call site.</summary>
    Inlined,

    /// <summary>The single shared body an outlined specialization emits, reached by <c>JSR</c>.</summary>
    OutlinedBody,

    /// <summary>A call site that only emits the <c>JSR</c> into a shared body.</summary>
    OutlinedCall,
}

/// <summary>
/// One emitted expansion or call of a user function, recorded while the runtime compiler emitted it.
/// <see cref="EmittedBytes"/> is inclusive of nested expansions because that is what outlining
/// the expansion would remove; <see cref="Parent"/> reconstructs the nesting.
/// </summary>
internal sealed record NesUserFunctionExpansion(
    string Function,
    int Parent,
    string PlacementUnit,
    NesPrgPlacementPhase Phase,
    int LoopDepth,
    int EmittedBytes,
    NesUserFunctionArguments Arguments,
    NesUserFunctionEmission Emission = NesUserFunctionEmission.Inlined,
    string? Specialization = null)
{
    /// <summary>True when this recording is code physically emitted for the function's own body.</summary>
    internal bool IsEmittedBody => Emission is not NesUserFunctionEmission.OutlinedCall;

    /// <summary>
    /// True when the call sits inside a loop nested below the placement unit's own loop, so it runs
    /// an unknown number of times per visit instead of exactly once.
    /// </summary>
    internal bool Repeats => LoopDepth > (Phase is NesPrgPlacementPhase.Hot ? 1 : 0);
}

/// <summary>
/// Collected projection: the single body an outlined/shared lowering would emit for a function.
/// <see cref="Calls"/> lists the direct user-function calls that body contains, which is what the
/// runtime-work projection multiplies so a shared body cannot hide executed calls.
/// </summary>
internal sealed record NesUserFunctionBody(
    string Function,
    int EmittedBytes,
    int SelfBytes,
    NesUserFunctionArguments Arguments,
    IReadOnlyList<string> Calls);

/// <summary>Runtime-work projection: one entry per executed call, shared bodies expanded.</summary>
internal sealed record NesUserFunctionCall(
    string Function,
    string Caller,
    string PlacementUnit,
    NesPrgPlacementPhase Phase,
    bool Repeats);

/// <summary>
/// Per-function accounting under the current inline-expansion lowering.
/// <c>EmittedCopies</c> and <c>EmittedBodyBytes</c> are the collected/emission projection (one body
/// per function, physically emitted <c>EmittedCopies</c> times today); <c>Calls</c> and
/// <c>CallsPerFrame</c> are the runtime-work projection, which counts executed calls even when a
/// body is emitted once. Byte counts are inclusive of nested expansions, so a caller's bytes overlap
/// its callees'; <c>TotalSelfBytes</c> is the non-overlapping share.
/// <c>CallsPerFrame</c> counts the calls the frame placement unit reaches; it is not path sensitive
/// and does not multiply loop iterations, so it is a lower bound whenever <c>RepeatsPerFrame</c> is
/// set and an upper bound over mutually exclusive branches otherwise.
/// </summary>
internal sealed record NesUserFunctionAccounting(
    string Name,
    NesUserFunctionPhase Phase,
    int EmittedCopies,
    int Calls,
    int CallsPerFrame,
    bool RepeatsPerFrame,
    int EmittedBodyBytes,
    int EmittedBodySelfBytes,
    int TotalEmittedBytes,
    int TotalSelfBytes,
    int DuplicatedBytes,
    NesUserFunctionArguments Arguments);

internal sealed record NesUserFunctionCallAccountingReport(
    bool HasFrameLoop,
    IReadOnlyList<NesUserFunctionAccounting> Functions,
    IReadOnlyList<NesUserFunctionBody> Collected,
    IReadOnlyList<NesUserFunctionCall> ForRuntimeWork)
{
    internal static readonly NesUserFunctionCallAccountingReport Empty =
        new(HasFrameLoop: false, [], [], []);

    internal NesUserFunctionAccounting? Function(string name) =>
        Functions.FirstOrDefault(function => string.Equals(function.Name, name, StringComparison.Ordinal));

    /// <summary>Bytes inline expansion spends beyond one body per function, without double counting nesting.</summary>
    internal int DuplicatedBytes => Functions.Sum(function => function.TotalSelfBytes) -
                                    Functions.Sum(function => function.EmittedBodySelfBytes);
}

/// <summary>
/// Turns recorded inline expansions into the two projections #516 established for SDK operation
/// streams: <see cref="Collected"/> yields one body per user function, while
/// <see cref="ForRuntimeWork"/> expands every executed call through those bodies. Sharing a body
/// must never make its executed calls disappear from budget inputs, so calls per frame are counted
/// from the runtime-work projection rather than from emitted copies.
/// </summary>
internal static class NesUserFunctionCallAccounting
{
    internal const string ProgramCaller = "program";

    internal static NesUserFunctionCallAccountingReport Create(
        IReadOnlyList<NesUserFunctionExpansion> expansions,
        bool hasFrameLoop)
    {
        ArgumentNullException.ThrowIfNull(expansions);
        if (expansions.Count == 0)
        {
            return NesUserFunctionCallAccountingReport.Empty with { HasFrameLoop = hasFrameLoop };
        }

        var collected = Collected(expansions);
        var runtimeWork = ForRuntimeWork(expansions);
        var selfBytes = SelfBytes(expansions);
        var functions = collected
            .Select(body => Summarize(body, expansions, selfBytes, runtimeWork, hasFrameLoop))
            .ToArray();

        return new NesUserFunctionCallAccountingReport(hasFrameLoop, functions, collected, runtimeWork);
    }

    /// <summary>
    /// Collected projection: one body per user function. Sizes take the widest observed
    /// specialization because a single shared body has to cover every call site.
    /// </summary>
    internal static IReadOnlyList<NesUserFunctionBody> Collected(
        IReadOnlyList<NesUserFunctionExpansion> expansions)
    {
        ArgumentNullException.ThrowIfNull(expansions);
        var selfBytes = SelfBytes(expansions);
        var directCalls = DirectCalls(expansions);
        return expansions
            .Select((expansion, index) => (expansion, index))
            .Where(entry => entry.expansion.IsEmittedBody)
            .GroupBy(entry => entry.expansion.Function, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                var widest = group.MaxBy(entry => entry.expansion.EmittedBytes);
                return new NesUserFunctionBody(
                    group.Key,
                    widest.expansion.EmittedBytes,
                    selfBytes[widest.index],
                    group.Aggregate(
                        NesUserFunctionArguments.None,
                        (arguments, entry) => arguments.Max(entry.expansion.Arguments)),
                    directCalls[widest.index].Select(callee => expansions[callee].Function).ToArray());
            })
            .ToArray();
    }

    /// <summary>
    /// Runtime-work projection: every executed call, expanded from the program roots through the
    /// bodies that contain them, exactly like <see cref="NesSdkProgramOperations.ForRuntimeWork"/>
    /// expands subroutine streams. Sharing a body must not remove its nested calls from this list,
    /// so nested calls are expanded per executing call rather than counted per emitted body.
    /// </summary>
    internal static IReadOnlyList<NesUserFunctionCall> ForRuntimeWork(
        IReadOnlyList<NesUserFunctionExpansion> expansions)
    {
        ArgumentNullException.ThrowIfNull(expansions);
        var directCalls = DirectCalls(expansions);
        var bodies = OutlinedBodies(expansions);
        var calls = new List<NesUserFunctionCall>();
        var callStack = new List<string>();
        for (var index = 0; index < expansions.Count; index++)
        {
            var expansion = expansions[index];
            if (expansion.Parent < 0 && expansion.Emission is not NesUserFunctionEmission.OutlinedBody)
            {
                Expand(index, ProgramCaller, expansions, directCalls, bodies, callStack, calls);
            }
        }

        return calls;
    }

    private static void Expand(
        int index,
        string caller,
        IReadOnlyList<NesUserFunctionExpansion> expansions,
        IReadOnlyList<IReadOnlyList<int>> directCalls,
        IReadOnlyDictionary<string, int> bodies,
        List<string> callStack,
        ICollection<NesUserFunctionCall> calls,
        string? placementUnit = null,
        NesPrgPlacementPhase? phase = null,
        bool repeats = false)
    {
        var expansion = expansions[index];
        // An outlined body is emitted outside every placement unit, so the executing context comes
        // from the call site that reached it rather than from where its bytes physically live.
        var unit = placementUnit ?? expansion.PlacementUnit;
        var executingPhase = phase ?? expansion.Phase;
        var executingRepeats = repeats || expansion.Repeats;
        calls.Add(new NesUserFunctionCall(
            expansion.Function,
            caller,
            unit,
            executingPhase,
            executingRepeats));

        if (callStack.Contains(expansion.Function, StringComparer.Ordinal))
        {
            var cycle = callStack.Skip(callStack.IndexOf(expansion.Function)).Append(expansion.Function);
            throw new InvalidOperationException(
                "NES user function call accounting does not support the recursive call cycle: " +
                $"{string.Join(" -> ", cycle)}.");
        }

        callStack.Add(expansion.Function);
        try
        {
            var source = expansion.Emission is NesUserFunctionEmission.OutlinedCall
                         && expansion.Specialization is { } specialization
                         && bodies.TryGetValue(specialization, out var bodyIndex)
                ? bodyIndex
                : index;
            foreach (var callee in directCalls[source])
            {
                Expand(
                    callee,
                    expansion.Function,
                    expansions,
                    directCalls,
                    bodies,
                    callStack,
                    calls,
                    unit,
                    executingPhase,
                    executingRepeats);
            }
        }
        finally
        {
            callStack.RemoveAt(callStack.Count - 1);
        }
    }

    private static NesUserFunctionAccounting Summarize(
        NesUserFunctionBody body,
        IReadOnlyList<NesUserFunctionExpansion> expansions,
        IReadOnlyList<int> selfBytes,
        IReadOnlyList<NesUserFunctionCall> runtimeWork,
        bool hasFrameLoop)
    {
        var emitted = expansions
            .Select((expansion, index) => (expansion, index))
            .Where(entry => entry.expansion.IsEmittedBody
                            && string.Equals(entry.expansion.Function, body.Function, StringComparison.Ordinal))
            .ToArray();
        var calls = runtimeWork
            .Where(call => string.Equals(call.Function, body.Function, StringComparison.Ordinal))
            .ToArray();
        var frameCalls = calls.Where(call => call.Phase is NesPrgPlacementPhase.Hot).ToArray();
        var totalEmittedBytes = emitted.Sum(entry => entry.expansion.EmittedBytes);

        return new NesUserFunctionAccounting(
            body.Function,
            Classify(hasFrameLoop, frameCalls.Length),
            emitted.Length,
            calls.Length,
            frameCalls.Length,
            frameCalls.Any(call => call.Repeats),
            body.EmittedBytes,
            body.SelfBytes,
            totalEmittedBytes,
            emitted.Sum(entry => selfBytes[entry.index]),
            totalEmittedBytes - body.EmittedBytes,
            body.Arguments);
    }

    private static NesUserFunctionPhase Classify(bool hasFrameLoop, int callsPerFrame)
    {
        if (!hasFrameLoop)
        {
            return NesUserFunctionPhase.OneShot;
        }

        return callsPerFrame > 0 ? NesUserFunctionPhase.Hot : NesUserFunctionPhase.Cold;
    }

    private static IReadOnlyList<int> SelfBytes(IReadOnlyList<NesUserFunctionExpansion> expansions)
    {
        var self = expansions.Select(expansion => expansion.EmittedBytes).ToArray();
        for (var index = 0; index < expansions.Count; index++)
        {
            var parent = expansions[index].Parent;
            if (parent >= 0 && expansions[index].Emission is NesUserFunctionEmission.Inlined)
            {
                self[parent] -= expansions[index].EmittedBytes;
            }
        }

        return self;
    }

    private static IReadOnlyList<IReadOnlyList<int>> DirectCalls(
        IReadOnlyList<NesUserFunctionExpansion> expansions)
    {
        var calls = expansions.Select(_ => new List<int>()).ToArray();
        for (var index = 0; index < expansions.Count; index++)
        {
            var parent = expansions[index].Parent;
            if (parent >= 0)
            {
                calls[parent].Add(index);
            }
        }

        return calls;
    }

    private static IReadOnlyDictionary<string, int> OutlinedBodies(
        IReadOnlyList<NesUserFunctionExpansion> expansions)
    {
        var bodies = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < expansions.Count; index++)
        {
            if (expansions[index] is { Emission: NesUserFunctionEmission.OutlinedBody, Specialization: { } label })
            {
                bodies[label] = index;
            }
        }

        return bodies;
    }
}

/// <summary>
/// Records one inline expansion per emitted user-function call so that accounting reflects what the
/// runtime compiler really emitted instead of a re-derived estimate.
/// </summary>
internal sealed class NesUserFunctionCallRecorder(PrgBuilder builder)
{
    private readonly List<Recording> recordings = [];
    private readonly Stack<int> active = new();

    internal IReadOnlyList<NesUserFunctionExpansion> Expansions => recordings
        .Select(recording => new NesUserFunctionExpansion(
            recording.Function,
            recording.Parent,
            recording.PlacementUnit,
            recording.Phase,
            recording.LoopDepth,
            recording.EmittedBytes,
            recording.Arguments,
            recording.Emission,
            recording.Specialization))
        .ToArray();

    internal IDisposable EnterCall(
        string function,
        int loopDepth,
        NesUserFunctionArguments arguments,
        NesUserFunctionEmission emission = NesUserFunctionEmission.Inlined,
        string? specialization = null)
    {
        var recording = new Recording(
            function,
            active.Count > 0 ? active.Peek() : -1,
            builder.CurrentPlacementUnitLabel,
            builder.CurrentPlacementPhase,
            loopDepth,
            arguments,
            builder.EmittedByteCursor,
            emission,
            specialization);
        recordings.Add(recording);
        active.Push(recordings.Count - 1);
        return new CallScope(this, recording);
    }

    private void ExitCall(Recording recording)
    {
        recording.EmittedBytes = builder.EmittedByteCursor - recording.StartCursor;
        active.Pop();
    }

    private sealed class Recording(
        string function,
        int parent,
        string placementUnit,
        NesPrgPlacementPhase phase,
        int loopDepth,
        NesUserFunctionArguments arguments,
        int startCursor,
        NesUserFunctionEmission emission,
        string? specialization)
    {
        internal string Function { get; } = function;

        internal int Parent { get; } = parent;

        internal string PlacementUnit { get; } = placementUnit;

        internal NesPrgPlacementPhase Phase { get; } = phase;

        internal int LoopDepth { get; } = loopDepth;

        internal NesUserFunctionArguments Arguments { get; } = arguments;

        internal int StartCursor { get; } = startCursor;

        internal NesUserFunctionEmission Emission { get; } = emission;

        internal string? Specialization { get; } = specialization;

        internal int EmittedBytes { get; set; }
    }

    private sealed class CallScope(NesUserFunctionCallRecorder owner, Recording recording) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            owner.ExitCall(recording);
        }
    }
}
