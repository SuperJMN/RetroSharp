namespace RetroSharp.GameBoy;

using RetroSharp.Core.Sdk;
using RetroSharp.Core.Targeting;
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
                case DeclarationSyntax declaration:
                    if (declaration.Initialization.HasValue)
                    {
                        CollectExpression(declaration.Initialization.Value);
                    }

                    break;
                case ExpressionStatementSyntax { Expression: FunctionCall call }:
                    CollectCall(call);
                    break;
                case ExpressionStatementSyntax { Expression: AssignmentSyntax assignment }:
                    CollectExpression(assignment.Right);
                    break;
                case WhileSyntax loop:
                    CollectExpression(loop.Condition);
                    CollectBlock(loop.Body);
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
                case "video_wait_vblank":
                    GameBoyVideoProgram.RequireArity(call, 0);
                    operations.Add(new Sdk2DOperation.WaitFrame());
                    break;
                case "input_poll":
                    GameBoyVideoProgram.RequireArity(call, 0);
                    operations.Add(new Sdk2DOperation.PollInput());
                    break;
                case "camera_set_position":
                    CollectCameraSetPosition(call);
                    break;
                case "hud_set_tile":
                    CollectHudSetTile(call);
                    break;
                default:
                    CollectCallArguments(call);
                    CollectUserFunction(call);
                    break;
            }
        }

        private void CollectCameraSetPosition(FunctionCall call)
        {
            GameBoyVideoProgram.RequireArity(call, 2);
            var args = call.Parameters.ToList();
            var x = ByteExpression(args[0], "camera_set_position argument 1");
            var y = ByteExpression(args[1], "camera_set_position argument 2");
            operations.Add(new Sdk2DOperation.SetCameraPosition(x, y, AxesFor(x, y)));
        }

        private void CollectHudSetTile(FunctionCall call)
        {
            GameBoyVideoProgram.RequireArity(call, 4);
            var args = call.Parameters.ToList();
            var mode = GameBoyVideoProgram.HudModeArg(args[0], "hud_set_tile argument 1");
            var x = ConstRange(args[1], 0, 31, "hud_set_tile argument 2");
            var y = ConstRange(args[2], 0, 31, "hud_set_tile argument 3");
            var tile = ConstRange(args[3], 0, 255, "hud_set_tile argument 4");

            operations.Add(new Sdk2DOperation.SetHudTile(mode, x, y, tile));
        }

        private void CollectExpression(ExpressionSyntax expression)
        {
            switch (expression)
            {
                case FunctionCall call:
                    CollectValueCall(call);
                    break;
                case BinaryExpressionSyntax binary:
                    CollectExpression(binary.Left);
                    CollectExpression(binary.Right);
                    break;
            }
        }

        private void CollectValueCall(FunctionCall call)
        {
            switch (call.Name)
            {
                case "world_tile_flags_at":
                    CollectWorldTileFlagsAt(call);
                    break;
            }

            CollectCallArguments(call);
        }

        private void CollectWorldTileFlagsAt(FunctionCall call)
        {
            GameBoyVideoProgram.RequireArity(call, 2);
            var args = call.Parameters.ToList();
            var worldX = ByteExpression(args[0], "world_tile_flags_at argument 1");
            var worldY = ByteExpression(args[1], "world_tile_flags_at argument 2");
            operations.Add(new Sdk2DOperation.ReadWorldTileFlags("default", worldX, worldY));
        }

        private void CollectCallArguments(FunctionCall call)
        {
            foreach (var parameter in call.Parameters)
            {
                CollectExpression(parameter);
            }
        }

        private static SdkByteExpression ByteExpression(ExpressionSyntax expression, string context)
        {
            switch (expression)
            {
                case ConstantSyntax:
                    return new SdkByteExpression.Constant(CheckedByte(GameBoyVideoProgram.ConstValue(expression, context), context));
                case IdentifierSyntax { Identifier: "true" }:
                    return new SdkByteExpression.Constant(1);
                case IdentifierSyntax { Identifier: "false" }:
                    return new SdkByteExpression.Constant(0);
                case IdentifierSyntax identifier:
                    return new SdkByteExpression.Variable(identifier.Identifier);
                default:
                    throw new InvalidOperationException($"{context} must be a byte constant or local variable.");
            }
        }

        private static int CheckedByte(int value, string context)
        {
            if (value is < 0 or > 255)
            {
                throw new InvalidOperationException($"{context} must be between 0 and 255.");
            }

            return value;
        }

        private static int ConstRange(ExpressionSyntax expression, int min, int max, string context)
        {
            var value = GameBoyVideoProgram.ConstValue(expression, context);
            if (value < min || value > max)
            {
                throw new InvalidOperationException($"{context} must be between {min} and {max}.");
            }

            return value;
        }

        private static ScrollAxes AxesFor(SdkByteExpression x, SdkByteExpression y)
        {
            var axes = ScrollAxes.None;

            if (x is not SdkByteExpression.Constant { Value: 0 })
            {
                axes |= ScrollAxes.Horizontal;
            }

            if (y is not SdkByteExpression.Constant { Value: 0 })
            {
                axes |= ScrollAxes.Vertical;
            }

            return axes;
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
