namespace RetroSharp.Sdk;

using RetroSharp.Core.Sdk;
using RetroSharp.Core.Targeting;
using RetroSharp.Parser;

// Target-neutral collector that walks a parsed main block and inlined user
// functions to produce the portable Sdk2DOperation list before any target lowers
// it. Targets share this so the portable boundary is not Game Boy-only.
public static class Sdk2DOperationCollector
{
    public static IReadOnlyList<Sdk2DOperation> Collect(
        BlockSyntax mainBlock,
        IReadOnlyDictionary<string, FunctionSyntax> functions,
        string targetName)
    {
        var collector = new Collector(functions, targetName);
        collector.CollectBlock(mainBlock);
        return collector.Operations;
    }

    // Shared reader so a target lowering a camera position drives emission from
    // the same operation the collector produces, instead of re-deriving it.
    public static Sdk2DOperation.SetCameraPosition ReadSetCameraPosition(FunctionCall call)
    {
        SdkCallReader.RequireArity(call, 2);
        var args = call.Parameters.ToList();
        var x = ReadByteExpression(args[0], "camera_set_position argument 1");
        var y = ReadByteExpression(args[1], "camera_set_position argument 2");
        return new Sdk2DOperation.SetCameraPosition(x, y, AxesFor(x, y));
    }

    public static Sdk2DOperation.DrawLogicalSprite ReadDrawLogicalSprite(FunctionCall call)
    {
        var args = call.Parameters.ToList();
        if (args.Count is not 4 and not 5 and not 6)
        {
            throw new InvalidOperationException($"sprite_draw expects 4, 5, or 6 arguments, got {args.Count}.");
        }

        var spriteId = SdkCallReader.IdentifierArg(args[0], "sprite_draw argument 1");
        var x = ReadByteExpression(args[1], "sprite_draw argument 2");
        var y = ReadByteExpression(args[2], "sprite_draw argument 3");
        var frame = ReadByteExpression(args[3], "sprite_draw argument 4");
        var flipX = ReadFlipXExpression(args);
        var paletteSlot = args.Count < 6
            ? 0
            : SdkCallReader.ConstValue(args[5], "sprite_draw argument 6");

        return new Sdk2DOperation.DrawLogicalSprite(
            spriteId,
            x,
            y,
            frame,
            flipX,
            paletteSlot,
            SpriteTransform.None);
    }

    public static Sdk2DOperation.StreamMapColumn ReadStreamMapColumn(FunctionCall call)
    {
        SdkCallReader.RequireArity(call, 4);
        var args = call.Parameters.ToList();
        var targetColumn = ReadByteExpression(args[0], "map_stream_column argument 1");
        var sourceColumn = ReadByteExpression(args[1], "map_stream_column argument 2");
        var y = SdkCallReader.ConstValue(args[2], "map_stream_column argument 3");
        var height = SdkCallReader.ConstValue(args[3], "map_stream_column argument 4");
        return new Sdk2DOperation.StreamMapColumn(targetColumn, sourceColumn, y, height);
    }

    public static Sdk2DOperation.CameraAabbTiles ReadCameraAabbTiles(FunctionCall call)
    {
        SdkCallReader.RequireArity(call, 5);
        var args = call.Parameters.ToList();
        var screenX = ConstRange(args[0], 0, 255, "camera_aabb_tiles argument 1");
        var (worldY, worldYOffset) = ReadByteExpressionWithConstantOffset(args[1], "camera_aabb_tiles argument 2");
        var width = ReadAabbExtent(args[2], "camera_aabb_tiles argument 3");
        var height = ConstRange(args[3], 0, 255, "camera_aabb_tiles argument 4");
        var flags = (WorldTileFlags)ConstRange(
            args[4],
            0,
            (int)(WorldTileFlags.Solid | WorldTileFlags.Hazard | WorldTileFlags.Platform),
            "camera_aabb_tiles argument 5");

        return new Sdk2DOperation.CameraAabbTiles(
            WorldId: "default",
            ScreenX: screenX,
            WorldY: worldY,
            WorldYOffset: worldYOffset,
            Width: width,
            Height: height,
            Flags: flags);
    }

    public static SdkByteExpression ReadByteExpression(ExpressionSyntax expression, string context)
    {
        switch (expression)
        {
            case ConstantSyntax:
                return new SdkByteExpression.Constant(CheckedByte(SdkCallReader.ConstValue(expression, context), context));
            case IdentifierSyntax { Identifier: "true" }:
                return new SdkByteExpression.Constant(1);
            case IdentifierSyntax { Identifier: "false" }:
                return new SdkByteExpression.Constant(0);
            case IdentifierSyntax identifier:
                return new SdkByteExpression.Variable(new SdkStorageLocation.Local(identifier.Identifier));
            case MemberAccessSyntax memberAccess:
                return new SdkByteExpression.Variable(ReadStorageLocation(memberAccess, context));
            case IndexExpressionSyntax indexExpression:
                return new SdkByteExpression.Variable(IndexedElementLocation(indexExpression.BaseIdentifier, indexExpression.Index, context));
            case CastSyntax cast:
                return ReadByteExpression(cast.Expression, context);
            default:
                throw new InvalidOperationException($"{context} must be a byte constant or local variable.");
        }
    }

    private static SdkStorageLocation ReadStorageLocation(ExpressionSyntax expression, string context)
    {
        return expression switch
        {
            IdentifierSyntax identifier => new SdkStorageLocation.Local(identifier.Identifier),
            MemberAccessSyntax memberAccess => new SdkStorageLocation.Field(
                ReadStorageLocation(memberAccess.Target, context),
                memberAccess.Member),
            _ => throw new InvalidOperationException($"{context} member access currently requires an identifier base."),
        };
    }

    private static SdkStorageLocation IndexedElementLocation(string baseIdentifier, ExpressionSyntax index, string context)
    {
        var value = CheckedByte(SdkCallReader.ConstValue(index, context), context);
        return new SdkStorageLocation.IndexedElement(baseIdentifier, value);
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
        var value = SdkCallReader.ConstValue(expression, context);
        if (value < min || value > max)
        {
            throw new InvalidOperationException($"{context} must be between {min} and {max}.");
        }

        return value;
    }

    private static SdkAabbExtent ReadAabbExtent(ExpressionSyntax expression, string context)
    {
        if (expression is FunctionCall { Name: "sprite_width" } spriteWidthCall)
        {
            SdkCallReader.RequireArity(spriteWidthCall, 1);
            return new SdkAabbExtent.SpriteWidth(SdkCallReader.IdentifierArg(spriteWidthCall.Parameters.ElementAt(0), "sprite_width argument 1"));
        }

        return new SdkAabbExtent.Constant(ConstRange(expression, 0, 255, context));
    }

    private static (SdkByteExpression Expression, int Offset) ReadByteExpressionWithConstantOffset(ExpressionSyntax expression, string context)
    {
        if (expression is BinaryExpressionSyntax { Operator.Symbol: "+" } plus)
        {
            if (TryConstValue(plus.Right, out var rightOffset))
            {
                return (ReadByteExpression(plus.Left, context), rightOffset);
            }

            if (TryConstValue(plus.Left, out var leftOffset))
            {
                return (ReadByteExpression(plus.Right, context), leftOffset);
            }
        }

        if (expression is BinaryExpressionSyntax { Operator.Symbol: "-" } minus
            && TryConstValue(minus.Right, out var offset))
        {
            return (ReadByteExpression(minus.Left, context), -offset);
        }

        return (ReadByteExpression(expression, context), 0);
    }

    private static SdkByteExpression? ReadFlipXExpression(IReadOnlyList<ExpressionSyntax> args)
    {
        if (args.Count < 5)
        {
            return null;
        }

        var flipX = args[4];
        if (TryConstValue(flipX, out var value) && value is not 0 and not 1)
        {
            throw new InvalidOperationException("sprite_draw argument 5 is portable flipX and must be 0, 1, true, false, or a local bool-like value. Use sprite_set for raw Game Boy OAM attributes.");
        }

        return ReadByteExpression(flipX, "sprite_draw argument 5");
    }

    private static bool TryConstValue(ExpressionSyntax expression, out int value)
    {
        if (expression is CastSyntax cast)
        {
            return TryConstValue(cast.Expression, out value);
        }

        if (expression is ConstantSyntax)
        {
            value = SdkCallReader.ConstValue(expression, "constant");
            return true;
        }

        value = 0;
        return false;
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

    private sealed class Collector(IReadOnlyDictionary<string, FunctionSyntax> functions, string targetName)
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
                case "video_wait_vblank":
                    SdkCallReader.RequireArity(call, 0);
                    operations.Add(new Sdk2DOperation.WaitFrame());
                    break;
                case "input_poll":
                    SdkCallReader.RequireArity(call, 0);
                    operations.Add(new Sdk2DOperation.PollInput());
                    break;
                case "camera_set_position":
                    CollectCameraSetPosition(call);
                    break;
                case "camera_apply":
                    CollectCameraApply(call);
                    break;
                case "sprite_draw":
                    CollectDrawLogicalSprite(call);
                    break;
                case "map_stream_column":
                    CollectStreamMapColumn(call);
                    break;
                case "hud_set_tile":
                    CollectHudSetTile(call);
                    break;
                case "world_load":
                    SdkCallReader.RequireArity(call, 1);
                    break;
                default:
                    CollectCallArguments(call);
                    CollectUserFunction(call);
                    break;
            }
        }

        private void CollectCameraSetPosition(FunctionCall call)
        {
            operations.Add(ReadSetCameraPosition(call));
        }

        private void CollectCameraApply(FunctionCall call)
        {
            SdkCallReader.RequireArity(call, 0);
            var axes = targetName == "NES"
                ? ScrollAxes.Horizontal
                : ScrollAxes.Horizontal | ScrollAxes.Vertical;
            operations.Add(new Sdk2DOperation.ApplyCamera(axes));
        }

        private void CollectDrawLogicalSprite(FunctionCall call)
        {
            operations.Add(ReadDrawLogicalSprite(call));
        }

        private void CollectStreamMapColumn(FunctionCall call)
        {
            operations.Add(ReadStreamMapColumn(call));
        }

        private void CollectHudSetTile(FunctionCall call)
        {
            SdkCallReader.RequireArity(call, 4);
            var args = call.Parameters.ToList();
            var mode = SdkCallReader.HudModeArg(args[0], "hud_set_tile argument 1");
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
            switch (call.Name)
            {
                case "world_tile_flags_at":
                    CollectWorldTileFlagsAt(call);
                    break;
                case "camera_aabb_tiles":
                    CollectCameraAabbTiles(call);
                    break;
                default:
                    if (CollectUserValueFunction(call))
                    {
                        return;
                    }

                    break;
            }

            CollectCallArguments(call);
        }

        private void CollectWorldTileFlagsAt(FunctionCall call)
        {
            SdkCallReader.RequireArity(call, 2);
            var args = call.Parameters.ToList();
            var worldX = ReadByteExpression(args[0], "world_tile_flags_at argument 1");
            var worldY = ReadByteExpression(args[1], "world_tile_flags_at argument 2");
            operations.Add(new Sdk2DOperation.ReadWorldTileFlags("default", worldX, worldY));
        }

        private void CollectCameraAabbTiles(FunctionCall call)
        {
            operations.Add(ReadCameraAabbTiles(call));
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
            return ReadByteExpression(expression, context);
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
