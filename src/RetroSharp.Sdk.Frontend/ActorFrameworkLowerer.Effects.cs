using System.Globalization;
using CSharpFunctionalExtensions;
using RetroSharp.Core;
using RetroSharp.Parser;

namespace RetroSharp.Sdk;

public static partial class ActorFrameworkLowerer
{
    private static partial class Effects
    {
        private const string EffectStructName = "Effect";
        private const string EffectSpawnRequestStructName = "EffectSpawnRequest";

        public static void CollectDirectives(StatementSyntax statement, ActorFrameworkState state)
        {
            switch (statement)
            {
                case ExpressionStatementSyntax { Expression: QualifiedCallSyntax { Qualifier: "Effects", Method: "Pool" } call }:
                    state.Effects.AddPool(ReadPool(call));
                    break;
                case ExpressionStatementSyntax { Expression: QualifiedCallSyntax { Qualifier: "Effects", Method: "Def" } call }:
                    state.Effects.AddDefinition(ReadDefinition(call));
                    break;
            }
        }

        public static EffectPool ReadPool(QualifiedCallSyntax call)
        {
            var parameters = call.Parameters.ToList();
            if (parameters.Count == 0 || parameters[0] is not IdentifierSyntax poolName)
            {
                throw new InvalidOperationException("Effects.Pool expects a pool identifier followed by named literal limits.");
            }

            var name = poolName.Identifier;
            var namedArguments = NamedArguments(parameters.Skip(1), $"Effects.Pool for '{name}'");
            var capacity = OptionalEffectLiteralByte(namedArguments, "capacity", 8, name);
            var requestCapacity = OptionalEffectLiteralByte(namedArguments, "requests", 8, name);

            foreach (var argumentName in namedArguments.Keys)
            {
                if (argumentName is not "capacity" and not "requests")
                {
                    throw new InvalidOperationException($"Effects.Pool for '{name}' has unsupported property '{argumentName}'.");
                }
            }

            if (capacity == 0 || requestCapacity == 0)
            {
                throw new InvalidOperationException($"Effects.Pool for '{name}' requires capacity and request limits from 1 to 255.");
            }

            return new EffectPool(name, capacity, requestCapacity);
        }

        public static EffectDef ReadDefinition(QualifiedCallSyntax call)
        {
            var parameters = call.Parameters.ToList();
            if (parameters.Count == 0 || parameters[0] is not IdentifierSyntax effectName)
            {
                throw new InvalidOperationException("Effects.Def expects an effect identifier as its first argument.");
            }

            var name = effectName.Identifier;
            var namedArguments = NamedArguments(parameters.Skip(1), $"Effects.Def for '{name}'");
            var sprite = RequiredEffectIdentifier(namedArguments, "sprite", name);
            var lifetime = RequiredEffectLiteralByte(namedArguments, "lifetime", name);

            foreach (var argumentName in namedArguments.Keys)
            {
                if (argumentName is not "sprite" and not "lifetime")
                {
                    throw new InvalidOperationException($"Effects.Def for '{name}' has unsupported property '{argumentName}'.");
                }
            }

            return new EffectDef(name, sprite, lifetime);
        }

        public static bool TryRewrite(
            StatementSyntax statement,
            ActorFrameworkState state,
            out IReadOnlyList<StatementSyntax> statements)
        {
            switch (statement)
            {
                case ExpressionStatementSyntax { Expression: QualifiedCallSyntax { Qualifier: "Effects", Method: "Pool" } call }:
                    statements = EffectPoolDeclarations(state.Effects.Pool(ReadPool(call).Name));
                    return true;
                case ExpressionStatementSyntax { Expression: QualifiedCallSyntax { Qualifier: "Effects", Method: "Def" } }:
                    statements = [];
                    return true;
                case ExpressionStatementSyntax { Expression: QualifiedCallSyntax call }
                    when state.Effects.TryPool(call.Qualifier, out var pool):
                    statements = call.Method switch
                    {
                        "Request" => EffectRequestStatements(pool, call, state),
                        "ProcessRequests" => EffectProcessRequestStatements(pool, call, state),
                        "Update" => UpdateStatements(pool, call, state),
                        "Draw" => DrawStatements(pool, call, state),
                        _ => [RewriteStatement(statement, state)!],
                    };
                    return true;
                default:
                    statements = [];
                    return false;
            }
        }

        public static bool TryRewriteExpression(ExpressionSyntax expression, out ExpressionSyntax rewritten)
        {
            if (expression is QualifiedCallSyntax { Qualifier: "Effects" } call)
            {
                throw new InvalidOperationException($"Effects.{call.Method} can only be used as a statement.");
            }

            rewritten = null!;
            return false;
        }

        public static IReadOnlyList<StatementSyntax> UpdateStatements(EffectPool pool, QualifiedCallSyntax call, ActorFrameworkState state)
        {
            RequireNoArguments(call);
            RequireEffectDefs(state, $"{pool.Name}.Update");

            var indexName = $"__{pool.Name}_update_i";
            var branches = state.Effects.Definitions
                .Select(def => new KindBranch(def.Name, new BlockSyntax([
                    FieldAssignment(pool.Name, indexName, "age", "+=", Constant(1)),
                    new IfElseSyntax(
                        new BinaryExpressionSyntax(PoolField(pool.Name, indexName, "age"), new IdentifierSyntax($"{def.Name}Lifetime"), Operator.Equal),
                        new BlockSyntax([FieldAssignment(pool.Name, indexName, "active", "=", Constant(0))]),
                        Maybe<BlockSyntax>.None),
                ])))
                .ToList();

            return
            [
                ArrayLoop(
                    pool.Name,
                    indexName,
                    new IfElseSyntax(
                        new BinaryExpressionSyntax(PoolField(pool.Name, indexName, "active"), Constant(0), Operator.NotEqual),
                        new BlockSyntax([PooledKindDispatch(pool.Name, indexName, branches, $"{pool.Name}.Update effect dispatch requires at least one Effects.Def declaration.")]),
                        Maybe<BlockSyntax>.None)),
            ];
        }

        public static IReadOnlyList<StatementSyntax> DrawStatements(EffectPool pool, QualifiedCallSyntax call, ActorFrameworkState state)
        {
            RequireNoArguments(call);
            if (!state.SupportsDraw)
            {
                throw new InvalidOperationException($"Target '{state.TargetName}' does not support effect Draw yet.");
            }

            RequireEffectDefs(state, $"{pool.Name}.Draw");
            var statements = PooledCameraDeclarations(pool.Name, "draw", state.ConfiguresCamera).ToList();
            for (var slot = 0; slot < pool.Capacity; slot++)
            {
                var phase = $"draw_{slot.ToString(CultureInfo.InvariantCulture)}";
                var indexName = $"__{pool.Name}_{phase}_i";
                var projection = BuildPooledScreenProjection(
                    pool.Name,
                    indexName,
                    pool.Name,
                    phase,
                    state.ScreenWidth,
                    state.ScreenHeight,
                    cameraPhase: "draw");
                var branches = state.Effects.Definitions
                    .Select(def => new KindBranch(
                        def.Name,
                        StableSpriteDrawBlock(
                            pool.Name,
                            indexName,
                            $"__{pool.Name}_{phase}",
                            def.Name,
                            def.Sprite,
                            projection,
                            state.ScreenHeight,
                            state)))
                    .ToList();

                statements.Add(new DeclarationSyntax(
                    "u8",
                    indexName,
                    Maybe<ExpressionSyntax>.None,
                    Maybe.From<ExpressionSyntax>(Constant(slot))));
                statements.AddRange(projection.Declarations);
                statements.Add(PooledKindDispatch(pool.Name, indexName, branches, $"{pool.Name}.Draw effect dispatch requires at least one Effects.Def declaration."));
            }

            return statements;
        }

        public static void AddGeneratedStructs(ActorFrameworkState state, IList<StructSyntax> structs)
        {
            if (state.Effects.Pools.Count == 0)
            {
                return;
            }

            if (structs.Any(structSyntax => structSyntax.Name == EffectStructName))
            {
                throw new InvalidOperationException("Effects.Pool cannot generate framework struct 'Effect' because a struct named 'Effect' is already declared.");
            }

            if (structs.Any(structSyntax => structSyntax.Name == EffectSpawnRequestStructName))
            {
                throw new InvalidOperationException("Effects.Pool cannot generate framework struct 'EffectSpawnRequest' because a struct named 'EffectSpawnRequest' is already declared.");
            }

            structs.Add(EffectStruct());
            structs.Add(EffectSpawnRequestStruct());
        }

        public static IEnumerable<ConstDeclarationSyntax> GeneratedConstants(IReadOnlyList<EffectDef> effectDefs)
        {
            for (var index = 0; index < effectDefs.Count; index++)
            {
                var def = effectDefs[index];
                yield return Constant(def.Name, index + 1);
                yield return Constant($"{def.Name}Lifetime", def.Lifetime);
            }
        }

        private static IReadOnlyList<StatementSyntax> EffectPoolDeclarations(EffectPool pool)
        {
            return
            [
                new DeclarationSyntax(
                    EffectStructName,
                    pool.Name,
                    Maybe.From<ExpressionSyntax>(Constant(pool.Capacity)),
                    Maybe<ExpressionSyntax>.None),
                new DeclarationSyntax(
                    EffectSpawnRequestStructName,
                    pool.RequestArrayName,
                    Maybe.From<ExpressionSyntax>(Constant(pool.RequestCapacity)),
                    Maybe<ExpressionSyntax>.None),
                ArrayLoop(
                    pool.Name,
                    $"__{pool.Name}_init_i",
                    FieldAssignment(pool.Name, $"__{pool.Name}_init_i", "active", "=", Constant(0))),
                ArrayLoop(
                    pool.RequestArrayName,
                    $"__{pool.Name}_init_request_i",
                    FieldAssignment(pool.RequestArrayName, $"__{pool.Name}_init_request_i", "active", "=", Constant(0))),
            ];
        }

        public static IEnumerable<GeneratedName> GeneratedNames(ActorFrameworkState state)
        {
            if (state.Effects.Pools.Count != 0)
            {
                yield return new GeneratedName(EffectStructName, "framework struct 'Effect'");
                yield return new GeneratedName(EffectSpawnRequestStructName, "framework struct 'EffectSpawnRequest'");
            }

            foreach (var def in state.Effects.Definitions)
            {
                yield return new GeneratedName(def.Name, $"Effects.Def '{def.Name}' kind constant");
                yield return new GeneratedName($"{def.Name}Lifetime", $"Effects.Def '{def.Name}' lifetime constant");
            }
        }

        private static StructSyntax EffectStruct()
        {
            return new StructSyntax(
                EffectStructName,
                [
                    new StructFieldSyntax("u8", "kind"),
                    new StructFieldSyntax("u8", "active"),
                    new StructFieldSyntax("u8", "x"),
                    new StructFieldSyntax("u8", "xHi"),
                    new StructFieldSyntax("u8", "y"),
                    new StructFieldSyntax("u8", "yHi"),
                    new StructFieldSyntax("u8", "age"),
                ]);
        }

        private static StructSyntax EffectSpawnRequestStruct()
        {
            return new StructSyntax(
                EffectSpawnRequestStructName,
                [
                    new StructFieldSyntax("u8", "kind"),
                    new StructFieldSyntax("u8", "active"),
                    new StructFieldSyntax("u8", "x"),
                    new StructFieldSyntax("u8", "xHi"),
                    new StructFieldSyntax("u8", "y"),
                    new StructFieldSyntax("u8", "yHi"),
                ]);
        }
    }

    private sealed record EffectPool(string Name, int Capacity, int RequestCapacity)
    {
        public string RequestArrayName => $"{Name}Requests";
    }

    private sealed record EffectDef(
        string Name,
        string Sprite,
        int Lifetime);
}
