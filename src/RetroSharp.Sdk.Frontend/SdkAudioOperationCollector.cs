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
        string targetName)
    {
        var collector = new Collector(functions, targetName);
        collector.CollectBlock(mainBlock);
        return collector.Operations;
    }

    private sealed class Collector(IReadOnlyDictionary<string, FunctionSyntax> functions, string targetName)
    {
        private readonly List<SdkAudioOperation> operations = [];
        private readonly HashSet<string> userFunctionCallStack = [];

        public IReadOnlyList<SdkAudioOperation> Operations => operations;

        public void CollectBlock(BlockSyntax block)
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
                    operations.Add(new SdkAudioOperation.InitializeAudio());
                    break;
                case "audio_update":
                    SdkCallReader.RequireArity(call, 0);
                    operations.Add(new SdkAudioOperation.UpdateAudio());
                    break;
                case "music_play":
                    SdkCallReader.RequireArity(call, 1);
                    operations.Add(new SdkAudioOperation.PlayMusic(SdkCallReader.IdentifierArg(call.Parameters.ElementAt(0), "music_play argument 1")));
                    break;
                case "music_stop":
                    SdkCallReader.RequireArity(call, 0);
                    operations.Add(new SdkAudioOperation.StopMusic());
                    break;
                case "music_asset":
                    SdkCallReader.RequireArity(call, 2);
                    CollectCallArguments(call);
                    break;
                default:
                    CollectCallArguments(call);
                    CollectUserFunction(call);
                    break;
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

            if (!userFunctionCallStack.Add(function.Name))
            {
                throw new InvalidOperationException($"Recursive {targetName} user function call '{function.Name}' is not supported.");
            }

            try
            {
                CollectBlock(ParameterSubstitution.Substitute(function, call, targetName));
            }
            finally
            {
                userFunctionCallStack.Remove(function.Name);
            }
        }

        private bool CollectUserValueFunction(FunctionCall call)
        {
            if (!functions.TryGetValue(call.Name, out var function))
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
