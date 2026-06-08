namespace RetroSharp.GameBoy;

using RetroSharp.Core.Sdk;
using RetroSharp.Parser;

internal static class GameBoySdkOperationCollector
{
    public static IReadOnlyList<Sdk2DOperation> Collect(BlockSyntax mainBlock, IReadOnlyDictionary<string, FunctionSyntax> functions)
    {
        var collector = new Collector(functions);
        collector.CollectBlock(mainBlock);
        return collector.Operations;
    }

    private sealed class Collector(IReadOnlyDictionary<string, FunctionSyntax> functions)
    {
        private readonly List<Sdk2DOperation> operations = [];
        private readonly HashSet<string> userFunctionCallStack = [];

        public IReadOnlyList<Sdk2DOperation> Operations => operations;

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
                case ExpressionStatementSyntax { Expression: FunctionCall call }:
                    CollectCall(call);
                    break;
                case WhileSyntax loop:
                    CollectBlock(loop.Body);
                    break;
                case IfElseSyntax branch:
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
                case "video_wait_vblank":
                    GameBoyVideoProgram.RequireArity(call, 0);
                    operations.Add(new Sdk2DOperation.WaitFrame());
                    break;
                case "input_poll":
                    GameBoyVideoProgram.RequireArity(call, 0);
                    operations.Add(new Sdk2DOperation.PollInput());
                    break;
                default:
                    CollectUserFunction(call);
                    break;
            }
        }

        private void CollectUserFunction(FunctionCall call)
        {
            if (!functions.TryGetValue(call.Name, out var function))
            {
                return;
            }

            GameBoyVideoProgram.RequireParameterlessUserFunction("Game Boy", call, function);
            if (!userFunctionCallStack.Add(function.Name))
            {
                throw new InvalidOperationException($"Recursive Game Boy user function call '{function.Name}' is not supported.");
            }

            try
            {
                CollectBlock(function.Block);
            }
            finally
            {
                userFunctionCallStack.Remove(function.Name);
            }
        }
    }
}
