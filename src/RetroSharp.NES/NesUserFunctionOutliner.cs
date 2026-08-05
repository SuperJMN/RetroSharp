using System.Text;
using RetroSharp.Core.Sdk;
using RetroSharp.Parser;
using RetroSharp.Sdk;

namespace RetroSharp.NES;

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
/// A call whose argument is a computed expression falls back to inline expansion; giving it a real
/// argument frame is #514's job, not this slice's.
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
    private readonly Dictionary<string, string> labelsBySpecialization = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> specializationsByFunction = new(StringComparer.Ordinal);
    private readonly Queue<NesOutlinedUserFunctionBody> pending = new();
    private readonly List<NesOutlinedUserFunctionBody> bodies = [];

    private NesUserFunctionOutliner(IReadOnlyDictionary<string, NesOutlinedUserFunctionDecision> decisions)
    {
        this.decisions = decisions;
    }

    internal static NesUserFunctionOutliner Empty { get; } =
        new(new Dictionary<string, NesOutlinedUserFunctionDecision>(StringComparer.Ordinal));

    /// <summary>Functions this plan will outline, ordered by name.</summary>
    internal IReadOnlyList<NesOutlinedUserFunctionDecision> Decisions => decisions.Values
        .OrderBy(decision => decision.Function, StringComparer.Ordinal)
        .ToArray();

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
        foreach (var name in reachable.OrderBy(name => name, StringComparer.Ordinal))
        {
            if (!functions.TryGetValue(name, out var function))
            {
                continue;
            }

            var phase = hasFrameLoop
                ? hot.Contains(name) ? NesUserFunctionPhase.Hot : NesUserFunctionPhase.Cold
                : NesUserFunctionPhase.OneShot;

            // #514 owns hot outlining, gated on its cost model. This slice never spends frame time.
            if (phase is NesUserFunctionPhase.Hot)
            {
                continue;
            }

            // Value helpers stay substituted: they are lowered through the expression path and an
            // `inline` value helper is never overridable.
            if (!string.Equals(function.Type, "void", StringComparison.Ordinal))
            {
                continue;
            }

            // A single call site would pay ~4 bytes for JSR/RTS and save nothing.
            var sites = callSites.GetValueOrDefault(name);
            if (sites < 2)
            {
                continue;
            }

            if (!IsStreamFree(name, functions, targetIntrinsics, resourceDeclarations, new HashSet<string>(StringComparer.Ordinal)))
            {
                continue;
            }

            decisions.Add(name, new NesOutlinedUserFunctionDecision(name, phase, sites, function.IsInline));
        }

        return new NesUserFunctionOutliner(decisions);
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
