namespace RetroSharp.Sdk;

using RetroSharp.Core.Sdk;
using RetroSharp.Parser;

// Target-neutral collector for the portable audio SDK stream. Audio is kept
// separate from Sdk2DOperation so frame/video lowering does not need to know
// about BGM state or music asset identity.
public static class SdkAudioOperationCollector
{
    public static IReadOnlyList<SdkAudioOperation> Collect(
        BlockSyntax mainBlock,
        IReadOnlyDictionary<string, FunctionSyntax> functions,
        string targetName,
        TargetIntrinsicCatalog? targetIntrinsics = null)
    {
        var collector = new Collector(functions, targetName, targetIntrinsics: targetIntrinsics);
        collector.CollectBlock(mainBlock);
        return collector.Operations;
    }

    public static SdkAudioProgram CollectProgram(
        BlockSyntax mainBlock,
        IReadOnlyDictionary<string, FunctionSyntax> functions,
        string targetName,
        IReadOnlySet<string> subroutineNames,
        TargetIntrinsicCatalog? targetIntrinsics = null)
    {
        var collector = new Collector(functions, targetName, subroutineNames, targetIntrinsics);
        collector.CollectBlock(mainBlock);
        return collector.Program;
    }

    private sealed class Collector(
        IReadOnlyDictionary<string, FunctionSyntax> functions,
        string targetName,
        IReadOnlySet<string>? subroutineNames = null,
        TargetIntrinsicCatalog? targetIntrinsics = null)
    {
        private readonly IReadOnlySet<string> subroutineNames = subroutineNames ?? new HashSet<string>(StringComparer.Ordinal);
        private readonly TargetIntrinsicCatalog? targetIntrinsics = targetIntrinsics;
        private readonly List<SdkAudioStreamItem> mainItems = [];
        private readonly Dictionary<string, IReadOnlyList<SdkAudioStreamItem>> subroutineStreams = [];
        private readonly HashSet<string> userFunctionCallStack = [];
        private List<SdkAudioStreamItem> currentItems = [];

        public IReadOnlyList<SdkAudioOperation> Operations => FlattenOps(mainItems);

        public SdkAudioProgram Program => new(mainItems, subroutineStreams);

        public void CollectBlock(BlockSyntax block)
        {
            currentItems = mainItems;
            CollectBlockCore(block);
        }

        private void AddOp(SdkAudioOperation operation)
        {
            currentItems.Add(new SdkAudioStreamItem.Op(operation));
        }

        private static IReadOnlyList<SdkAudioOperation> FlattenOps(IReadOnlyList<SdkAudioStreamItem> items)
        {
            var ops = new List<SdkAudioOperation>(items.Count);
            foreach (var item in items)
            {
                if (item is not SdkAudioStreamItem.Op op)
                {
                    throw new InvalidOperationException("Cannot flatten an SDK audio stream that contains subroutine calls.");
                }

                ops.Add(op.Operation);
            }

            return ops;
        }

        private void CollectBlockCore(BlockSyntax block)
        {
            foreach (var statement in block.Statements)
            {
                CollectStatement(statement);
            }
        }

        private void CollectStatement(StatementSyntax statement)
        {
            switch (statement)
            {
                case DeclarationSyntax declaration:
                    if (declaration.ArrayLength.HasValue)
                    {
                        CollectExpression(declaration.ArrayLength.Value);
                    }

                    if (declaration.Initialization.HasValue)
                    {
                        CollectExpression(declaration.Initialization.Value);
                    }

                    break;
                case ExpressionStatementSyntax { Expression: FunctionCall call }:
                    CollectCall(call);
                    break;
                case ExpressionStatementSyntax { Expression: AssignmentSyntax assignment }:
                    CollectLValue(assignment.Left);
                    CollectExpression(assignment.Right);
                    break;
                case WhileSyntax loop:
                    CollectExpression(loop.Condition);
                    CollectBlock(loop.Body);
                    break;
                case DoWhileSyntax loop:
                    CollectBlock(loop.Body);
                    CollectExpression(loop.Condition);
                    break;
                case LoopSyntax loop:
                    CollectBlock(loop.Body);
                    break;
                case RangeForSyntax loop:
                    CollectStatement(RangeForLowerer.Lower(loop));
                    break;
                case ForSyntax loop:
                    if (loop.Initializer.HasValue)
                    {
                        CollectStatement(loop.Initializer.Value);
                    }

                    if (loop.Condition.HasValue)
                    {
                        CollectExpression(loop.Condition.Value);
                    }

                    CollectBlock(loop.Body);
                    if (loop.Increment.HasValue)
                    {
                        CollectExpression(loop.Increment.Value);
                    }

                    break;
                case IfElseSyntax branch:
                    CollectExpression(branch.Condition);
                    CollectBlock(branch.ThenBlock);
                    if (branch.ElseBlock.HasValue)
                    {
                        CollectBlock(branch.ElseBlock.Value);
                    }

                    break;
            }
        }

        private void CollectCall(FunctionCall call)
        {
            switch (call.Name)
            {
                case "audio_init":
                    SdkCallReader.RequireArity(call, 0);
                    AddOp(new SdkAudioOperation.InitializeAudio());
                    break;
                case "audio_update":
                    SdkCallReader.RequireArity(call, 0);
                    AddOp(new SdkAudioOperation.UpdateAudio());
                    break;
                case "music_play":
                    SdkCallReader.RequireArity(call, 1);
                    AddOp(new SdkAudioOperation.PlayMusic(SdkCallReader.IdentifierArg(call.Parameters.ElementAt(0), "music_play argument 1")));
                    break;
                case "music_stop":
                    SdkCallReader.RequireArity(call, 0);
                    AddOp(new SdkAudioOperation.StopMusic());
                    break;
                case "music_asset":
                    SdkCallReader.RequireArity(call, 2);
                    CollectCallArguments(call);
                    break;
                default:
                    if (CollectTargetIntrinsic(call))
                    {
                        break;
                    }

                    CollectCallArguments(call);
                    CollectUserFunction(call);
                    break;
            }
        }

        private bool CollectTargetIntrinsic(FunctionCall call)
        {
            if (targetIntrinsics is null ||
                !functions.TryGetValue(call.Name, out var function) ||
                !function.IsExtern)
            {
                return false;
            }

            var intrinsic = TargetIntrinsicResolver.Resolve(function, targetIntrinsics);
            switch (intrinsic.Operation)
            {
                case TargetIntrinsicOperation.InitializeAudio:
                    SdkCallReader.RequireArity(call, intrinsic.Arity);
                    AddOp(new SdkAudioOperation.InitializeAudio());
                    return true;
                case TargetIntrinsicOperation.UpdateAudio:
                    SdkCallReader.RequireArity(call, intrinsic.Arity);
                    AddOp(new SdkAudioOperation.UpdateAudio());
                    return true;
                case TargetIntrinsicOperation.StopMusic:
                    SdkCallReader.RequireArity(call, intrinsic.Arity);
                    AddOp(new SdkAudioOperation.StopMusic());
                    return true;
                case TargetIntrinsicOperation.PlayMusic:
                    var resolved = TargetIntrinsicResolver.ResolveCall(function, call, targetIntrinsics);
                    var themeId = resolved.CompileTimeOperands
                        .FirstOrDefault(operand => operand.Role == TargetIntrinsicOperandRole.AssetRef)
                        ?.Identifier
                        ?? throw new InvalidOperationException(
                            $"Intrinsic '{intrinsic.Name}' requires a compile-time music asset operand.");
                    AddOp(new SdkAudioOperation.PlayMusic(themeId));
                    return true;
                default:
                    return false;
            }
        }

        private void CollectExpression(ExpressionSyntax expression)
        {
            switch (expression)
            {
                case FunctionCall call:
                    CollectValueCall(call);
                    break;
                case NamedArgumentSyntax namedArgument:
                    CollectExpression(namedArgument.Expression);
                    break;
                case ArrayInitializerSyntax arrayInitializer:
                    foreach (var element in arrayInitializer.Elements)
                    {
                        CollectExpression(element);
                    }

                    break;
                case StructInitializerSyntax structInitializer:
                    foreach (var field in structInitializer.Fields)
                    {
                        CollectExpression(field.Expression);
                    }

                    break;
                case BinaryExpressionSyntax binary:
                    CollectExpression(binary.Left);
                    CollectExpression(binary.Right);
                    break;
                case ConditionalExpressionSyntax conditional:
                    CollectExpression(conditional.Condition);
                    CollectExpression(conditional.WhenTrue);
                    CollectExpression(conditional.WhenFalse);
                    break;
                case CastSyntax cast:
                    CollectExpression(cast.Expression);
                    break;
                case IndexExpressionSyntax indexExpression:
                    CollectExpression(indexExpression.Index);
                    break;
            }
        }

        private void CollectLValue(LValue lValue)
        {
            if (lValue is IndexLValue index)
            {
                CollectExpression(index.Index);
            }
        }

        private void CollectValueCall(FunctionCall call)
        {
            if (CollectUserValueFunction(call))
            {
                return;
            }

            CollectCallArguments(call);
        }

        private void CollectCallArguments(FunctionCall call)
        {
            foreach (var parameter in call.Parameters)
            {
                CollectExpression(parameter);
            }
        }

        private void CollectUserFunction(FunctionCall call)
        {
            if (!functions.TryGetValue(call.Name, out var function))
            {
                return;
            }

            if (subroutineNames.Contains(call.Name))
            {
                CollectSubroutineCall(call, function);
                return;
            }

            if (!userFunctionCallStack.Add(function.Name))
            {
                throw new InvalidOperationException($"Recursive {targetName} user function call '{function.Name}' is not supported.");
            }

            try
            {
                CollectBlockCore(ParameterSubstitution.Substitute(function, call, targetName));
            }
            finally
            {
                userFunctionCallStack.Remove(function.Name);
            }
        }

        private void CollectSubroutineCall(FunctionCall call, FunctionSyntax function)
        {
            currentItems.Add(new SdkAudioStreamItem.CallSubroutine(call.Name));
            if (subroutineStreams.ContainsKey(call.Name))
            {
                return;
            }

            if (!userFunctionCallStack.Add(function.Name))
            {
                throw new InvalidOperationException($"Recursive {targetName} user function call '{function.Name}' is not supported.");
            }

            var bodyItems = new List<SdkAudioStreamItem>();
            var previousItems = currentItems;
            currentItems = bodyItems;
            try
            {
                CollectBlockCore(ParameterSubstitution.Substitute(function, call, targetName));
            }
            finally
            {
                currentItems = previousItems;
                userFunctionCallStack.Remove(function.Name);
            }

            subroutineStreams[call.Name] = bodyItems;
        }

        private bool CollectUserValueFunction(FunctionCall call)
        {
            if (!functions.TryGetValue(call.Name, out var function))
            {
                return false;
            }

            if (function.IsExtern)
            {
                return false;
            }

            if (!userFunctionCallStack.Add(function.Name))
            {
                throw new InvalidOperationException($"Recursive {targetName} user function call '{function.Name}' is not supported.");
            }

            try
            {
                CollectExpression(ParameterSubstitution.SubstituteReturnExpression(function, call, targetName));
            }
            finally
            {
                userFunctionCallStack.Remove(function.Name);
            }

            return true;
        }
    }
}
