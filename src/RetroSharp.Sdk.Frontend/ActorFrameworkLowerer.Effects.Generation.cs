using CSharpFunctionalExtensions;
using RetroSharp.Core;
using RetroSharp.Parser;

namespace RetroSharp.Sdk;

public static partial class ActorFrameworkLowerer
{
    private static partial class Effects
    {
        private static string RequiredEffectIdentifier(
            IReadOnlyDictionary<string, ExpressionSyntax> arguments,
            string name,
            string effectName)
        {
            if (!arguments.TryGetValue(name, out var expression))
            {
                throw new InvalidOperationException($"Effects.Def for '{effectName}' requires '{name}'.");
            }

            if (expression is IdentifierSyntax identifier)
            {
                return identifier.Identifier;
            }

            throw new InvalidOperationException($"Effects.Def for '{effectName}' requires '{name}' to be an identifier.");
        }

        private static int RequiredEffectLiteralByte(
            IReadOnlyDictionary<string, ExpressionSyntax> arguments,
            string name,
            string effectName)
        {
            if (!arguments.TryGetValue(name, out var expression))
            {
                throw new InvalidOperationException($"Effects.Def for '{effectName}' requires '{name}'.");
            }

            if (TryLiteralByte(expression, out var value))
            {
                return value;
            }

            throw new InvalidOperationException($"Effects.Def for '{effectName}' requires '{name}' to be a literal byte value.");
        }

        private static int OptionalEffectLiteralByte(
            IReadOnlyDictionary<string, ExpressionSyntax> arguments,
            string name,
            int defaultValue,
            string poolName)
        {
            if (!arguments.TryGetValue(name, out var expression))
            {
                return defaultValue;
            }

            if (TryLiteralByte(expression, out var value))
            {
                return value;
            }

            throw new InvalidOperationException($"Effects.Pool for '{poolName}' requires literal byte limits for capacity and requests.");
        }

        private static IReadOnlyList<StatementSyntax> EffectRequestStatements(EffectPool pool, QualifiedCallSyntax call, ActorFrameworkState state)
        {
            var parameters = call.Parameters.Select(parameter => RewriteExpression(parameter, state)).ToList();
            if (parameters.Count != 3)
            {
                throw new InvalidOperationException($"{pool.Name}.Request expects kind, x, and y.");
            }

            if (parameters[0] is not IdentifierSyntax kind)
            {
                throw new InvalidOperationException($"{pool.Name}.Request kind must be an effect identifier.");
            }

            state.EffectDef(kind.Identifier);
            return EnqueueStatements(
                pool,
                kind,
                parameters[1],
                Constant(0),
                parameters[2],
                Constant(0),
                state.NextEffectRequestPrefix(pool, "request"));
        }

        public static IReadOnlyList<StatementSyntax> EnqueueStatements(
            EffectPool pool,
            IdentifierSyntax kind,
            ExpressionSyntax x,
            ExpressionSyntax xHi,
            ExpressionSyntax y,
            ExpressionSyntax yHi,
            string prefix)
        {
            var indexName = $"{prefix}_i";
            var writtenName = $"{prefix}_written";
            return
            [
                new DeclarationSyntax("u8", writtenName, Maybe<ExpressionSyntax>.None, Maybe.From<ExpressionSyntax>(Constant(0))),
                ArrayLoop(
                    pool.RequestArrayName,
                    indexName,
                    new IfElseSyntax(
                        And(
                            new BinaryExpressionSyntax(new IdentifierSyntax(writtenName), Constant(0), Operator.Equal),
                            new BinaryExpressionSyntax(PoolField(pool.RequestArrayName, indexName, "active"), Constant(0), Operator.Equal)),
                        new BlockSyntax([
                            FieldAssignment(pool.RequestArrayName, indexName, "active", "=", Constant(1)),
                            FieldAssignment(pool.RequestArrayName, indexName, "kind", "=", kind),
                            FieldAssignment(pool.RequestArrayName, indexName, "x", "=", x),
                            FieldAssignment(pool.RequestArrayName, indexName, "xHi", "=", xHi),
                            FieldAssignment(pool.RequestArrayName, indexName, "y", "=", y),
                            FieldAssignment(pool.RequestArrayName, indexName, "yHi", "=", yHi),
                            Assign(new IdentifierLValue(writtenName), Constant(1)),
                        ]),
                        Maybe<BlockSyntax>.None)),
            ];
        }

        private static IReadOnlyList<StatementSyntax> EffectProcessRequestStatements(EffectPool pool, QualifiedCallSyntax call, ActorFrameworkState state)
        {
            RequireNoArguments(call);
            RequireEffectDefs(state, $"{pool.Name}.ProcessRequests");

            var requestIndex = $"__{pool.Name}_process_request_i";
            var branches = state.EffectDefs
                .Select(def => new KindBranch(def.Name, new BlockSyntax(EffectSpawnFromRequestStatements(pool, requestIndex, def).ToList())))
                .ToList();

            return
            [
                ArrayLoop(
                    pool.RequestArrayName,
                    requestIndex,
                    new IfElseSyntax(
                        new BinaryExpressionSyntax(PoolField(pool.RequestArrayName, requestIndex, "active"), Constant(0), Operator.NotEqual),
                        new BlockSyntax([
                            new DeclarationSyntax("u8", $"__{pool.Name}_process_request_x", Maybe<ExpressionSyntax>.None, Maybe.From<ExpressionSyntax>(PoolField(pool.RequestArrayName, requestIndex, "x"))),
                            new DeclarationSyntax("u8", $"__{pool.Name}_process_request_xHi", Maybe<ExpressionSyntax>.None, Maybe.From<ExpressionSyntax>(PoolField(pool.RequestArrayName, requestIndex, "xHi"))),
                            new DeclarationSyntax("u8", $"__{pool.Name}_process_request_y", Maybe<ExpressionSyntax>.None, Maybe.From<ExpressionSyntax>(PoolField(pool.RequestArrayName, requestIndex, "y"))),
                            new DeclarationSyntax("u8", $"__{pool.Name}_process_request_yHi", Maybe<ExpressionSyntax>.None, Maybe.From<ExpressionSyntax>(PoolField(pool.RequestArrayName, requestIndex, "yHi"))),
                            PooledKindDispatch(pool.RequestArrayName, requestIndex, branches, "effect request dispatch requires at least one Effects.Def declaration."),
                            FieldAssignment(pool.RequestArrayName, requestIndex, "active", "=", Constant(0)),
                        ]),
                        Maybe<BlockSyntax>.None)),
            ];
        }

        private static IReadOnlyList<StatementSyntax> EffectSpawnFromRequestStatements(EffectPool pool, string requestIndex, EffectDef def)
        {
            var indexName = $"__{pool.Name}_process_{def.Name}_i";
            var writtenName = $"__{pool.Name}_process_{def.Name}_written";

            return
            [
                new DeclarationSyntax("u8", writtenName, Maybe<ExpressionSyntax>.None, Maybe.From<ExpressionSyntax>(Constant(0))),
                ArrayLoop(
                    pool.Name,
                    indexName,
                    new IfElseSyntax(
                        And(
                            new BinaryExpressionSyntax(new IdentifierSyntax(writtenName), Constant(0), Operator.Equal),
                            new BinaryExpressionSyntax(PoolField(pool.Name, indexName, "active"), Constant(0), Operator.Equal)),
                        new BlockSyntax([
                            FieldAssignment(pool.Name, indexName, "active", "=", Constant(1)),
                            FieldAssignment(pool.Name, indexName, "kind", "=", new IdentifierSyntax(def.Name)),
                            FieldAssignment(pool.Name, indexName, "x", "=", new IdentifierSyntax($"__{pool.Name}_process_request_x")),
                            FieldAssignment(pool.Name, indexName, "xHi", "=", new IdentifierSyntax($"__{pool.Name}_process_request_xHi")),
                            FieldAssignment(pool.Name, indexName, "y", "=", new IdentifierSyntax($"__{pool.Name}_process_request_y")),
                            FieldAssignment(pool.Name, indexName, "yHi", "=", new IdentifierSyntax($"__{pool.Name}_process_request_yHi")),
                            FieldAssignment(pool.Name, indexName, "age", "=", Constant(0)),
                            Assign(new IdentifierLValue(writtenName), Constant(1)),
                        ]),
                        Maybe<BlockSyntax>.None)),
            ];
        }

        private static void RequireEffectDefs(ActorFrameworkState state, string callName)
        {
            if (state.EffectDefs.Count == 0)
            {
                throw new InvalidOperationException($"{callName} requires at least one Effects.Def declaration.");
            }
        }

    }
}
