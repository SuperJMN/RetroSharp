using System.Text;
using RetroSharp.Core.Sdk;
using RetroSharp.Parser;
using RetroSharp.Sdk;

namespace RetroSharp.NES;

/// <summary>
/// Why one reachable user function is not outlined, in the order
/// <see cref="NesUserFunctionOutliner.Plan(NesVideoProgram)"/> tests the gates.
/// </summary>
internal enum NesUserFunctionOutlineRejection
{
    /// <summary>Nothing rejected it; it is outlined.</summary>
    None,

    /// <summary>Reached from the frame placement unit, so #514's cost model owns it.</summary>
    FrameReachable,

    /// <summary>A value helper, kept on the expression substitution path.</summary>
    ValueHelper,

    /// <summary>Called from exactly one site, where a <c>JSR</c>/<c>RTS</c> pair saves nothing.</summary>
    SingleCallSite,

    /// <summary>Its closure consumes the positional SDK operation stream or a target intrinsic.</summary>
    StreamBearing,
}

/// <summary>
/// One reachable user function and the gate that decided its fate, so that outlining headroom is
/// auditable from a build instead of re-derived by an investigation.
/// </summary>
internal sealed record NesUserFunctionOutlineCandidate(
    string Function,
    NesUserFunctionPhase Phase,
    int CallSites,
    NesUserFunctionOutlineRejection Rejection);

/// <summary>
/// One user function the outliner decided to emit once and reach with <c>JSR</c>.
/// </summary>
internal sealed record NesOutlinedUserFunctionDecision(
    string Function,
    NesUserFunctionPhase Phase,
    int CallSites,
    bool OverridesInlineHint);

/// <summary>
/// One emitted body: a single specialization of an outlined function, reachable by
/// <see cref="Label"/>. <see cref="Call"/> carries the compile-time operands that were baked into
/// this specialization, so the body is emitted with the same substitution an inline expansion
/// would have used at any of its call sites.
/// </summary>
internal sealed record NesOutlinedUserFunctionBody(
    string Label,
    FunctionSyntax Function,
    FunctionCall Call,
    NesUserFunctionPhase Phase);

/// <summary>
/// The outlining decision for one program, taken once and then consulted at every call site.
/// </summary>
/// <remarks>
/// <para>
/// Eligibility, specialization keys, labels and the emission queue all live here so that
/// <see cref="NesRuntimeCompiler"/> and <see cref="NesRomBuilder"/> only carry mechanism.
/// </para>
/// <para>
/// The calling convention is deliberately the smallest one that recovers the duplication this
/// program actually contains: <c>JSR body</c> / <c>RTS</c>, with no argument marshalling at all.
/// Every argument must be a compile-time operand (a constant, or a stable reference to storage the
/// body can address directly), so it is baked into the specialization key instead of being passed.
/// A call whose argument is a computed expression falls back to inline expansion. #514 proposes
/// giving those a statically allocated argument frame; measured against every NES sample and
/// validation fixture that recovers 0 B today, because no shipped program has a cold or one-shot
/// function that reaches this gate at all. <see cref="Candidates"/> reports the gate that really
/// rejected each function so that headroom stays auditable instead of assumed.
/// </para>
/// </remarks>
internal sealed class NesUserFunctionOutliner
{
    private static readonly string[] BuiltInStatementCalls =
    [
        "tilemap_set",
        "tilemap_fill",
        "map_stream_column",
        "map_stream_row",
        "hud_set_tile",
    ];

    private readonly IReadOnlyDictionary<string, NesOutlinedUserFunctionDecision> decisions;
    private readonly IReadOnlyList<NesUserFunctionOutlineCandidate> candidates;
    private readonly Dictionary<string, string> labelsBySpecialization = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> specializationsByFunction = new(StringComparer.Ordinal);
    private readonly Queue<NesOutlinedUserFunctionBody> pending = new();
    private readonly List<NesOutlinedUserFunctionBody> bodies = [];

    private NesUserFunctionOutliner(
        IReadOnlyDictionary<string, NesOutlinedUserFunctionDecision> decisions,
        IReadOnlyList<NesUserFunctionOutlineCandidate> candidates)
    {
        this.decisions = decisions;
        this.candidates = candidates;
    }

    internal static NesUserFunctionOutliner Empty { get; } =
        new(new Dictionary<string, NesOutlinedUserFunctionDecision>(StringComparer.Ordinal), []);

    /// <summary>Functions this plan will outline, ordered by name.</summary>
    internal IReadOnlyList<NesOutlinedUserFunctionDecision> Decisions => decisions.Values
        .OrderBy(decision => decision.Function, StringComparer.Ordinal)
        .ToArray();

    /// <summary>Every reachable user function with the gate that decided it, ordered by name.</summary>
    internal IReadOnlyList<NesUserFunctionOutlineCandidate> Candidates => candidates;

    /// <summary>Bodies actually reached and therefore emitted, in emission order.</summary>
    internal IReadOnlyList<NesOutlinedUserFunctionBody> Bodies => bodies;

    internal bool IsEmpty => decisions.Count == 0;

    internal static NesUserFunctionOutliner Plan(NesVideoProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        return Plan(
            NesProgramPhaseAnalyzer.Analyze(program),
            program.Functions,
            program.TargetIntrinsics,
            program.ResourceDeclarations);
    }

    internal static NesUserFunctionOutliner Plan(
        NesMainPlacementPlan placement,
        IReadOnlyDictionary<string, FunctionSyntax> functions,
        TargetIntrinsicCatalog targetIntrinsics,
        SdkResourceDeclarationRegistry resourceDeclarations)
    {
        ArgumentNullException.ThrowIfNull(placement);
        ArgumentNullException.ThrowIfNull(functions);

        var hasFrameLoop = placement.Units.Any(unit => unit.Phase is NesPrgPlacementPhase.Hot);
        var hot = new HashSet<string>(StringComparer.Ordinal);
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        var callSites = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var unit in placement.Units)
        {
            var unitFunctions = new HashSet<string>(StringComparer.Ordinal);
            Walk(unit.Block, functions, unitFunctions, callSites);
            reachable.UnionWith(unitFunctions);
            if (unit.Phase is NesPrgPlacementPhase.Hot)
            {
                hot.UnionWith(unitFunctions);
            }
        }

        var decisions = new Dictionary<string, NesOutlinedUserFunctionDecision>(StringComparer.Ordinal);
        var candidates = new List<NesUserFunctionOutlineCandidate>();
        foreach (var name in reachable.OrderBy(name => name, StringComparer.Ordinal))
        {
            if (!functions.TryGetValue(name, out var function))
            {
                continue;
            }

            var phase = hasFrameLoop
                ? hot.Contains(name) ? NesUserFunctionPhase.Hot : NesUserFunctionPhase.Cold
                : NesUserFunctionPhase.OneShot;
            var sites = callSites.GetValueOrDefault(name);
            var rejection = Reject(phase, function, sites, functions, targetIntrinsics, resourceDeclarations);
            candidates.Add(new NesUserFunctionOutlineCandidate(name, phase, sites, rejection));
            if (rejection is not NesUserFunctionOutlineRejection.None)
            {
                continue;
            }

            decisions.Add(name, new NesOutlinedUserFunctionDecision(name, phase, sites, function.IsInline));
        }

        return new NesUserFunctionOutliner(decisions, candidates);
    }

    /// <summary>
    /// The single place the function-level gates are decided, so the build can report why a
    /// function was not outlined instead of leaving the reason implicit in control flow.
    /// </summary>
    private static NesUserFunctionOutlineRejection Reject(
        NesUserFunctionPhase phase,
        FunctionSyntax function,
        int callSites,
        IReadOnlyDictionary<string, FunctionSyntax> functions,
        TargetIntrinsicCatalog targetIntrinsics,
        SdkResourceDeclarationRegistry resourceDeclarations)
    {
        // #514 owns hot outlining, gated on its cost model. This slice never spends frame time.
        if (phase is NesUserFunctionPhase.Hot)
        {
            return NesUserFunctionOutlineRejection.FrameReachable;
        }

        // Value helpers stay substituted: they are lowered through the expression path and an
        // `inline` value helper is never overridable.
        if (!string.Equals(function.Type, "void", StringComparison.Ordinal))
        {
            return NesUserFunctionOutlineRejection.ValueHelper;
        }

        // A single call site would pay ~4 bytes for JSR/RTS and save nothing.
        if (callSites < 2)
        {
            return NesUserFunctionOutlineRejection.SingleCallSite;
        }

        return IsStreamFree(
            function.Name,
            functions,
            targetIntrinsics,
            resourceDeclarations,
            new HashSet<string>(StringComparer.Ordinal))
            ? NesUserFunctionOutlineRejection.None
            : NesUserFunctionOutlineRejection.StreamBearing;
    }

    /// <summary>
    /// Decides whether one call site can become a <c>JSR</c>, and to which body.
    /// </summary>
    /// <param name="operand">
    /// Resolves an argument to a stable compile-time token, or <see langword="null"/> when the
    /// argument needs runtime evaluation and therefore an argument frame this slice does not build.
    /// </param>
    internal bool TryOutlineCall(
        FunctionSyntax function,
        FunctionCall call,
        Func<ExpressionSyntax, string?> operand,
        out string label)
    {
        ArgumentNullException.ThrowIfNull(function);
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(operand);

        label = string.Empty;
        if (!decisions.TryGetValue(function.Name, out var decision))
        {
            return false;
        }

        IReadOnlyDictionary<string, ExpressionSyntax> bound;
        try
        {
            bound = ParameterSubstitution.BindParameters(function, call, "NES");
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        var specialization = new StringBuilder(function.Name);
        var arguments = new List<ExpressionSyntax>(function.Parameters.Count);
        foreach (var parameter in function.Parameters)
        {
            if (!bound.TryGetValue(parameter.Name, out var argument))
            {
                return false;
            }

            var token = operand(argument);
            if (token is null)
            {
                return false;
            }

            specialization.Append('|').Append(parameter.Name).Append('=').Append(token);
            arguments.Add(argument);
        }

        var key = specialization.ToString();
        if (labelsBySpecialization.TryGetValue(key, out var existing))
        {
            label = existing;
            return true;
        }

        var index = specializationsByFunction.GetValueOrDefault(function.Name);
        specializationsByFunction[function.Name] = index + 1;
        label = index == 0 ? $"user_fn_{function.Name}" : $"user_fn_{function.Name}__{index}";
        labelsBySpecialization.Add(key, label);

        var body = new NesOutlinedUserFunctionBody(
            label,
            function,
            new FunctionCall(function.Name, arguments),
            decision.Phase);
        pending.Enqueue(body);
        bodies.Add(body);
        return true;
    }

    /// <summary>
    /// Takes the next body still to emit. Emitting a body can queue further bodies, so callers must
    /// drain this until it returns <see langword="false"/>.
    /// </summary>
    internal bool TryDequeueBody(out NesOutlinedUserFunctionBody body)
    {
        if (pending.Count == 0)
        {
            body = null!;
            return false;
        }

        body = pending.Dequeue();
        return true;
    }

    private static void Walk(
        BlockSyntax block,
        IReadOnlyDictionary<string, FunctionSyntax> functions,
        ISet<string> visited,
        Dictionary<string, int> callSites)
    {
        var expand = new List<string>();
        NesProgramPhaseAnalyzer.VisitBlockCalls(block, name =>
        {
            if (!functions.TryGetValue(name, out var callee) || callee.IsExtern)
            {
                return;
            }

            callSites[name] = callSites.GetValueOrDefault(name) + 1;
            if (visited.Add(name))
            {
                expand.Add(name);
            }
        });

        foreach (var name in expand)
        {
            Walk(functions[name].Block, functions, visited, callSites);
        }
    }

    /// <summary>
    /// True when a body contains no work that the SDK operation stream or a target intrinsic would
    /// consume. <see cref="NesSdkStreamReader"/> replays <see cref="Sdk2DProgram"/> operations
    /// positionally, so a body emitted once but executed many times would desynchronise that
    /// stream. Restructuring the stream into per-subroutine streams belongs to a later slice.
    /// </summary>
    private static bool IsStreamFree(
        string name,
        IReadOnlyDictionary<string, FunctionSyntax> functions,
        TargetIntrinsicCatalog targetIntrinsics,
        SdkResourceDeclarationRegistry resourceDeclarations,
        ISet<string> visiting)
    {
        if (!visiting.Add(name))
        {
            return true;
        }

        if (!functions.TryGetValue(name, out var function))
        {
            return false;
        }

        if (function.IsExtern || targetIntrinsics.TryResolve(name, out _))
        {
            return false;
        }

        if (SdkResourceDeclarationResolver.TryResolve(function, out _, resourceDeclarations))
        {
            return false;
        }

        var free = true;
        NesProgramPhaseAnalyzer.VisitBlockCalls(function.Block, callee =>
        {
            if (!free)
            {
                return;
            }

            if (BuiltInStatementCalls.Contains(callee, StringComparer.Ordinal))
            {
                free = false;
                return;
            }

            if (targetIntrinsics.TryResolve(callee, out _))
            {
                free = false;
                return;
            }

            if (!functions.ContainsKey(callee))
            {
                // Anything the runtime compiler resolves elsewhere (SDK dot calls, unknown names)
                // is treated as stream-bearing until proven otherwise.
                free = false;
                return;
            }

            free = IsStreamFree(callee, functions, targetIntrinsics, resourceDeclarations, visiting);
        });

        return free;
    }
}
