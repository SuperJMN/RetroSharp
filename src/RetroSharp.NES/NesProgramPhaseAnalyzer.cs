using CSharpFunctionalExtensions;
using RetroSharp.Core.Sdk;
using RetroSharp.Parser;
using RetroSharp.Sdk;

namespace RetroSharp.NES;

internal sealed record NesMainPlacementPlan(IReadOnlyList<NesMainPlacementUnitPlan> Units);

internal sealed record NesMainPlacementUnitPlan(
    string Name,
    NesPrgPlacementPhase Phase,
    BlockSyntax Block,
    bool EmitsTerminalIdleLoop);

internal static class NesProgramPhaseAnalyzer
{
    internal static NesMainPlacementPlan Analyze(NesVideoProgram program) =>
        Analyze(program.MainBlock, program.Functions, program.TargetIntrinsics);

    internal static NesMainPlacementPlan Analyze(
        BlockSyntax mainBlock,
        IReadOnlyDictionary<string, FunctionSyntax> functions,
        TargetIntrinsicCatalog targetIntrinsics)
    {
        var context = new AnalysisContext(functions, targetIntrinsics);
        context.ValidateAcyclicMain(mainBlock);

        var statements = mainBlock.Statements;
        var frameLoopIndex = -1;
        for (var index = 0; index < statements.Count; index++)
        {
            if (statements[index] is WhileSyntax whileSyntax &&
                IsConstantTrue(whileSyntax.Condition) &&
                context.ContainsFrameBoundary(whileSyntax.Body))
            {
                frameLoopIndex = index;
                break;
            }
        }

        if (frameLoopIndex < 0)
        {
            return new NesMainPlacementPlan(
            [
                new NesMainPlacementUnitPlan(
                    NesRomBuilder.MainInitPlacementUnitName,
                    NesPrgPlacementPhase.OneShot,
                    Slice(statements, 0, statements.Count),
                    EmitsTerminalIdleLoop: true),
            ]);
        }

        var units = new List<NesMainPlacementUnitPlan>(3);
        if (frameLoopIndex > 0)
        {
            units.Add(new NesMainPlacementUnitPlan(
                NesRomBuilder.MainInitPlacementUnitName,
                NesPrgPlacementPhase.Cold,
                Slice(statements, 0, frameLoopIndex),
                EmitsTerminalIdleLoop: false));
        }

        units.Add(new NesMainPlacementUnitPlan(
            NesRomBuilder.MainFramePlacementUnitName,
            NesPrgPlacementPhase.Hot,
            Slice(statements, frameLoopIndex, 1),
            EmitsTerminalIdleLoop: false));

        units.Add(new NesMainPlacementUnitPlan(
            NesRomBuilder.MainTailPlacementUnitName,
            NesPrgPlacementPhase.Cold,
            Slice(statements, frameLoopIndex + 1, statements.Count - frameLoopIndex - 1),
            EmitsTerminalIdleLoop: true));

        return new NesMainPlacementPlan(units);
    }

    private static BlockSyntax Slice(IReadOnlyList<StatementSyntax> statements, int start, int count) =>
        new(statements.Skip(start).Take(count).ToList());

    private static bool IsConstantTrue(ExpressionSyntax expression)
    {
        if (expression is CastSyntax cast)
        {
            return IsConstantTrue(cast.Expression);
        }

        if (expression is IdentifierSyntax { Identifier: "true" })
        {
            return true;
        }

        if (expression is IdentifierSyntax { Identifier: "false" })
        {
            return false;
        }

        if (expression is not ConstantSyntax)
        {
            return false;
        }

        return NesVideoProgram.ConstValue(expression, "while condition") != 0;
    }

    internal static void VisitBlockCalls(BlockSyntax block, Action<string> call)
    {
        foreach (var statement in block.Statements)
        {
            VisitStatementCalls(statement, call);
        }
    }

    private static void VisitStatementCalls(StatementSyntax statement, Action<string> call)
    {
        switch (statement)
        {
            case ConstDeclarationSyntax constDeclaration:
                VisitExpressionCalls(constDeclaration.Value, call);
                break;
            case DeclarationSyntax declaration:
                VisitMaybeExpressionCalls(declaration.ArrayLength, call);
                VisitMaybeExpressionCalls(declaration.Initialization, call);
                break;
            case ExpressionStatementSyntax expressionStatement:
                VisitExpressionCalls(expressionStatement.Expression, call);
                break;
            case WhileSyntax whileSyntax:
                VisitExpressionCalls(whileSyntax.Condition, call);
                VisitBlockCalls(whileSyntax.Body, call);
                break;
            case DoWhileSyntax doWhileSyntax:
                VisitBlockCalls(doWhileSyntax.Body, call);
                VisitExpressionCalls(doWhileSyntax.Condition, call);
                break;
            case RangeForSyntax rangeForSyntax:
                VisitExpressionCalls(rangeForSyntax.Start, call);
                VisitExpressionCalls(rangeForSyntax.End, call);
                VisitBlockCalls(rangeForSyntax.Body, call);
                break;
            case ForSyntax forSyntax:
                if (forSyntax.Initializer.HasValue)
                {
                    VisitStatementCalls(forSyntax.Initializer.Value, call);
                }

                VisitMaybeExpressionCalls(forSyntax.Condition, call);
                VisitMaybeExpressionCalls(forSyntax.Increment, call);
                VisitBlockCalls(forSyntax.Body, call);
                break;
            case IfElseSyntax ifElseSyntax:
                VisitExpressionCalls(ifElseSyntax.Condition, call);
                VisitBlockCalls(ifElseSyntax.ThenBlock, call);
                if (ifElseSyntax.ElseBlock.HasValue)
                {
                    VisitBlockCalls(ifElseSyntax.ElseBlock.Value, call);
                }

                break;
            case SwitchSyntax switchSyntax:
                VisitExpressionCalls(switchSyntax.Subject, call);
                foreach (var switchCase in switchSyntax.Cases)
                {
                    foreach (var pattern in switchCase.Patterns)
                    {
                        VisitSwitchCasePatternCalls(pattern, call);
                    }

                    VisitBlockCalls(switchCase.Block, call);
                }

                if (switchSyntax.DefaultBlock.HasValue)
                {
                    VisitBlockCalls(switchSyntax.DefaultBlock.Value, call);
                }

                break;
            case ReturnSyntax returnSyntax:
                VisitMaybeExpressionCalls(returnSyntax.Expression, call);
                break;
        }
    }

    private static void VisitExpressionCalls(ExpressionSyntax expression, Action<string> call)
    {
        switch (expression)
        {
            case FunctionCall functionCall:
                call(functionCall.Name);
                foreach (var parameter in functionCall.Parameters)
                {
                    VisitExpressionCalls(parameter, call);
                }

                break;
            case QualifiedCallSyntax qualifiedCall:
                foreach (var parameter in qualifiedCall.Parameters)
                {
                    VisitExpressionCalls(parameter, call);
                }

                break;
            case NamedArgumentSyntax namedArgument:
                VisitExpressionCalls(namedArgument.Expression, call);
                break;
            case AssignmentSyntax assignment:
                VisitLValueCalls(assignment.Left, call);
                VisitExpressionCalls(assignment.Right, call);
                break;
            case PostfixMutationSyntax postfixMutation:
                VisitLValueCalls(postfixMutation.Target, call);
                break;
            case MemberAccessSyntax memberAccess:
                VisitExpressionCalls(memberAccess.Target, call);
                break;
            case IndexExpressionSyntax indexExpression:
                VisitExpressionCalls(indexExpression.Index, call);
                break;
            case CastSyntax cast:
                VisitExpressionCalls(cast.Expression, call);
                break;
            case UnaryExpressionSyntax unary:
                VisitExpressionCalls(unary.Operand, call);
                break;
            case BinaryExpressionSyntax binary:
                VisitExpressionCalls(binary.Left, call);
                VisitExpressionCalls(binary.Right, call);
                break;
            case ConditionalExpressionSyntax conditional:
                VisitExpressionCalls(conditional.Condition, call);
                VisitExpressionCalls(conditional.WhenTrue, call);
                VisitExpressionCalls(conditional.WhenFalse, call);
                break;
            case SwitchExpressionSyntax switchExpression:
                VisitExpressionCalls(switchExpression.Subject, call);
                foreach (var arm in switchExpression.Arms)
                {
                    foreach (var pattern in arm.Patterns)
                    {
                        VisitSwitchCasePatternCalls(pattern, call);
                    }

                    VisitExpressionCalls(arm.Value, call);
                }

                VisitMaybeExpressionCalls(switchExpression.DefaultValue, call);
                break;
            case PipelineExpressionSyntax pipeline:
                VisitExpressionCalls(pipeline.Value, call);
                foreach (var step in pipeline.Steps)
                {
                    call(step.FunctionName);
                    foreach (var argument in step.Arguments)
                    {
                        VisitExpressionCalls(argument, call);
                    }
                }

                break;
            case ArrayInitializerSyntax arrayInitializer:
                foreach (var element in arrayInitializer.Elements)
                {
                    VisitExpressionCalls(element, call);
                }

                break;
            case StructInitializerSyntax structInitializer:
                foreach (var field in structInitializer.Fields)
                {
                    VisitExpressionCalls(field.Expression, call);
                }

                break;
        }
    }

    private static void VisitSwitchCasePatternCalls(SwitchCasePatternSyntax pattern, Action<string> call)
    {
        VisitExpressionCalls(pattern.Start, call);
        VisitMaybeExpressionCalls(pattern.End, call);
    }

    private static void VisitMaybeExpressionCalls(Maybe<ExpressionSyntax> expression, Action<string> call)
    {
        if (expression.HasValue)
        {
            VisitExpressionCalls(expression.Value, call);
        }
    }

    private static void VisitLValueCalls(LValue lValue, Action<string> call)
    {
        switch (lValue)
        {
            case PointerDerefLValue pointer:
                VisitExpressionCalls(pointer.Expression, call);
                break;
            case IndexLValue index:
                VisitExpressionCalls(index.Index, call);
                break;
            case MemberAccessLValue member:
                VisitExpressionCalls(member.MemberAccess, call);
                break;
        }
    }

    private sealed class AnalysisContext(
        IReadOnlyDictionary<string, FunctionSyntax> functions,
        TargetIntrinsicCatalog targetIntrinsics)
    {
        private enum VisitState
        {
            Visiting,
            Visited,
        }

        private readonly Dictionary<string, VisitState> visitStates = new(StringComparer.Ordinal);
        private readonly Dictionary<string, bool> frameBoundaryMemo = new(StringComparer.Ordinal);
        private readonly List<string> callStack = [];

        internal void ValidateAcyclicMain(BlockSyntax mainBlock)
        {
            visitStates.Clear();
            callStack.Clear();
            visitStates["Main"] = VisitState.Visiting;
            callStack.Add("Main");
            VisitBlockCalls(mainBlock, ValidateCall);
            callStack.RemoveAt(callStack.Count - 1);
            visitStates["Main"] = VisitState.Visited;
        }

        internal bool ContainsFrameBoundary(BlockSyntax block) => BlockContainsFrameBoundary(block, []);

        private void ValidateCall(string name)
        {
            if (!functions.TryGetValue(name, out var function) || function.IsExtern)
            {
                return;
            }

            if (visitStates.TryGetValue(name, out var state))
            {
                if (state is VisitState.Visiting)
                {
                    var cycleStart = callStack.IndexOf(name);
                    var cycle = callStack.Skip(cycleStart).Append(name);
                    throw new InvalidOperationException(
                        $"NES phase analysis does not support recursive user function call cycle: {string.Join(" -> ", cycle)}.");
                }

                return;
            }

            visitStates[name] = VisitState.Visiting;
            callStack.Add(name);
            VisitBlockCalls(function.Block, ValidateCall);
            callStack.RemoveAt(callStack.Count - 1);
            visitStates[name] = VisitState.Visited;
        }

        private bool BlockContainsFrameBoundary(BlockSyntax block, HashSet<string> activeFunctions) =>
            block.Statements.Any(statement => StatementContainsFrameBoundary(statement, activeFunctions));

        private bool StatementContainsFrameBoundary(StatementSyntax statement, HashSet<string> activeFunctions) =>
            statement switch
            {
                ConstDeclarationSyntax constDeclaration => ExpressionContainsFrameBoundary(constDeclaration.Value, activeFunctions),
                DeclarationSyntax declaration =>
                    MaybeExpressionContainsFrameBoundary(declaration.ArrayLength, activeFunctions) ||
                    MaybeExpressionContainsFrameBoundary(declaration.Initialization, activeFunctions),
                ExpressionStatementSyntax expressionStatement => ExpressionContainsFrameBoundary(expressionStatement.Expression, activeFunctions),
                WhileSyntax whileSyntax =>
                    ExpressionContainsFrameBoundary(whileSyntax.Condition, activeFunctions) ||
                    BlockContainsFrameBoundary(whileSyntax.Body, activeFunctions),
                DoWhileSyntax doWhileSyntax =>
                    BlockContainsFrameBoundary(doWhileSyntax.Body, activeFunctions) ||
                    ExpressionContainsFrameBoundary(doWhileSyntax.Condition, activeFunctions),
                RangeForSyntax rangeForSyntax =>
                    ExpressionContainsFrameBoundary(rangeForSyntax.Start, activeFunctions) ||
                    ExpressionContainsFrameBoundary(rangeForSyntax.End, activeFunctions) ||
                    BlockContainsFrameBoundary(rangeForSyntax.Body, activeFunctions),
                ForSyntax forSyntax =>
                    MaybeStatementContainsFrameBoundary(forSyntax.Initializer, activeFunctions) ||
                    MaybeExpressionContainsFrameBoundary(forSyntax.Condition, activeFunctions) ||
                    MaybeExpressionContainsFrameBoundary(forSyntax.Increment, activeFunctions) ||
                    BlockContainsFrameBoundary(forSyntax.Body, activeFunctions),
                IfElseSyntax ifElseSyntax =>
                    ExpressionContainsFrameBoundary(ifElseSyntax.Condition, activeFunctions) ||
                    BlockContainsFrameBoundary(ifElseSyntax.ThenBlock, activeFunctions) ||
                    MaybeBlockContainsFrameBoundary(ifElseSyntax.ElseBlock, activeFunctions),
                SwitchSyntax switchSyntax =>
                    ExpressionContainsFrameBoundary(switchSyntax.Subject, activeFunctions) ||
                    switchSyntax.Cases.Any(@case => SwitchCaseContainsFrameBoundary(@case, activeFunctions)) ||
                    MaybeBlockContainsFrameBoundary(switchSyntax.DefaultBlock, activeFunctions),
                ReturnSyntax returnSyntax => MaybeExpressionContainsFrameBoundary(returnSyntax.Expression, activeFunctions),
                BreakSyntax or ContinueSyntax => false,
                _ => false,
            };

        private bool SwitchCaseContainsFrameBoundary(SwitchCaseSyntax @case, HashSet<string> activeFunctions) =>
            @case.Patterns.Any(pattern => SwitchCasePatternContainsFrameBoundary(pattern, activeFunctions)) ||
            BlockContainsFrameBoundary(@case.Block, activeFunctions);

        private bool SwitchCasePatternContainsFrameBoundary(
            SwitchCasePatternSyntax pattern,
            HashSet<string> activeFunctions) =>
            ExpressionContainsFrameBoundary(pattern.Start, activeFunctions) ||
            MaybeExpressionContainsFrameBoundary(pattern.End, activeFunctions);

        private bool ExpressionContainsFrameBoundary(ExpressionSyntax expression, HashSet<string> activeFunctions) =>
            expression switch
            {
                FunctionCall call =>
                    CallContainsFrameBoundary(call.Name, activeFunctions) ||
                    call.Parameters.Any(parameter => ExpressionContainsFrameBoundary(parameter, activeFunctions)),
                QualifiedCallSyntax call =>
                    call.Parameters.Any(parameter => ExpressionContainsFrameBoundary(parameter, activeFunctions)),
                NamedArgumentSyntax namedArgument => ExpressionContainsFrameBoundary(namedArgument.Expression, activeFunctions),
                AssignmentSyntax assignment =>
                    LValueContainsFrameBoundary(assignment.Left, activeFunctions) ||
                    ExpressionContainsFrameBoundary(assignment.Right, activeFunctions),
                PostfixMutationSyntax postfixMutation => LValueContainsFrameBoundary(postfixMutation.Target, activeFunctions),
                MemberAccessSyntax memberAccess => ExpressionContainsFrameBoundary(memberAccess.Target, activeFunctions),
                IndexExpressionSyntax indexExpression => ExpressionContainsFrameBoundary(indexExpression.Index, activeFunctions),
                CastSyntax cast => ExpressionContainsFrameBoundary(cast.Expression, activeFunctions),
                UnaryExpressionSyntax unary => ExpressionContainsFrameBoundary(unary.Operand, activeFunctions),
                BinaryExpressionSyntax binary =>
                    ExpressionContainsFrameBoundary(binary.Left, activeFunctions) ||
                    ExpressionContainsFrameBoundary(binary.Right, activeFunctions),
                ConditionalExpressionSyntax conditional =>
                    ExpressionContainsFrameBoundary(conditional.Condition, activeFunctions) ||
                    ExpressionContainsFrameBoundary(conditional.WhenTrue, activeFunctions) ||
                    ExpressionContainsFrameBoundary(conditional.WhenFalse, activeFunctions),
                SwitchExpressionSyntax switchExpression =>
                    ExpressionContainsFrameBoundary(switchExpression.Subject, activeFunctions) ||
                    switchExpression.Arms.Any(arm => SwitchExpressionArmContainsFrameBoundary(arm, activeFunctions)) ||
                    MaybeExpressionContainsFrameBoundary(switchExpression.DefaultValue, activeFunctions),
                PipelineExpressionSyntax pipeline =>
                    ExpressionContainsFrameBoundary(pipeline.Value, activeFunctions) ||
                    pipeline.Steps.Any(step => PipelineStepContainsFrameBoundary(step, activeFunctions)),
                ArrayInitializerSyntax arrayInitializer =>
                    arrayInitializer.Elements.Any(element => ExpressionContainsFrameBoundary(element, activeFunctions)),
                StructInitializerSyntax structInitializer =>
                    structInitializer.Fields.Any(field => ExpressionContainsFrameBoundary(field.Expression, activeFunctions)),
                ConstantSyntax or IdentifierSyntax or SizeOfSyntax or OffsetOfSyntax or CountOfSyntax => false,
                _ => false,
            };

        private bool SwitchExpressionArmContainsFrameBoundary(
            SwitchExpressionArmSyntax arm,
            HashSet<string> activeFunctions) =>
            arm.Patterns.Any(pattern => SwitchCasePatternContainsFrameBoundary(pattern, activeFunctions)) ||
            ExpressionContainsFrameBoundary(arm.Value, activeFunctions);

        private bool PipelineStepContainsFrameBoundary(PipelineStepSyntax step, HashSet<string> activeFunctions) =>
            CallContainsFrameBoundary(step.FunctionName, activeFunctions) ||
            step.Arguments.Any(argument => ExpressionContainsFrameBoundary(argument, activeFunctions));

        private bool CallContainsFrameBoundary(string name, HashSet<string> activeFunctions)
        {
            if (!functions.TryGetValue(name, out var function))
            {
                return false;
            }

            if (function.IsExtern)
            {
                return IsFrameBoundaryIntrinsic(function);
            }

            if (frameBoundaryMemo.TryGetValue(name, out var containsFrameBoundary))
            {
                return containsFrameBoundary;
            }

            if (!activeFunctions.Add(name))
            {
                throw new InvalidOperationException(
                    $"NES phase analysis encountered recursive user function '{name}' while searching for frame boundaries.");
            }

            containsFrameBoundary = BlockContainsFrameBoundary(function.Block, activeFunctions);
            activeFunctions.Remove(name);
            frameBoundaryMemo[name] = containsFrameBoundary;
            return containsFrameBoundary;
        }

        private bool IsFrameBoundaryIntrinsic(FunctionSyntax function)
        {
            try
            {
                return TargetIntrinsicResolver.Resolve(function, targetIntrinsics).Operation is TargetIntrinsicOperation.WaitFrame;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private bool MaybeStatementContainsFrameBoundary(
            Maybe<StatementSyntax> statement,
            HashSet<string> activeFunctions) =>
            statement.HasValue && StatementContainsFrameBoundary(statement.Value, activeFunctions);

        private bool MaybeExpressionContainsFrameBoundary(
            Maybe<ExpressionSyntax> expression,
            HashSet<string> activeFunctions) =>
            expression.HasValue && ExpressionContainsFrameBoundary(expression.Value, activeFunctions);

        private bool MaybeBlockContainsFrameBoundary(Maybe<BlockSyntax> block, HashSet<string> activeFunctions) =>
            block.HasValue && BlockContainsFrameBoundary(block.Value, activeFunctions);

        private bool LValueContainsFrameBoundary(LValue lValue, HashSet<string> activeFunctions) =>
            lValue switch
            {
                IdentifierLValue => false,
                PointerDerefLValue pointer => ExpressionContainsFrameBoundary(pointer.Expression, activeFunctions),
                IndexLValue index => ExpressionContainsFrameBoundary(index.Index, activeFunctions),
                MemberAccessLValue member => ExpressionContainsFrameBoundary(member.MemberAccess, activeFunctions),
                _ => false,
            };

    }
}
