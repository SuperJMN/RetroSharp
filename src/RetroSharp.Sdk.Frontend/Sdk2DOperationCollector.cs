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
        string targetName,
        Target2DCapabilities capabilities,
        TargetIntrinsicCatalog? targetIntrinsics = null)
    {
        var collector = new Collector(functions, targetName, capabilities, targetIntrinsics: targetIntrinsics);
        collector.CollectBlock(mainBlock);
        return collector.Operations;
    }

    // Collects a program split into a main stream plus per-subroutine streams for
    // the named functions. When subroutineNames is empty the main stream is a flat
    // sequence of Op items equivalent to Collect(...), so targets stay byte-identical.
    public static Sdk2DProgram CollectProgram(
        BlockSyntax mainBlock,
        IReadOnlyDictionary<string, FunctionSyntax> functions,
        string targetName,
        Target2DCapabilities capabilities,
        IReadOnlySet<string> subroutineNames,
        TargetIntrinsicCatalog? targetIntrinsics = null)
    {
        var collector = new Collector(functions, targetName, capabilities, subroutineNames, targetIntrinsics);
        collector.CollectBlock(mainBlock);
        return collector.Program;
    }

    public static IReadOnlyList<Sdk2DFrameBudget> CollectFrameBudgets(
        BlockSyntax mainBlock,
        IReadOnlyDictionary<string, FunctionSyntax> functions,
        string targetName,
        Func<Sdk2DOperation.DrawLogicalSprite, Sdk2DFrameBudget>? drawSpriteBudget = null,
        TargetIntrinsicCatalog? targetIntrinsics = null)
    {
        var collector = new FrameBudgetCollector(functions, targetName, drawSpriteBudget, targetIntrinsics);
        return collector.Collect(mainBlock);
    }

    // Shared reader so a target lowering a camera position drives emission from
    // the same operation the collector produces, instead of re-deriving it.
    public static Sdk2DOperation.SetCameraPosition ReadSetCameraPosition(FunctionCall call)
    {
        SdkCallReader.RequireArity(call, 2);
        var args = call.Parameters.ToList();
        var x = ReadByteExpression(args[0], "camera_set_position argument 1");
        var y = ReadByteExpression(args[1], "camera_set_position argument 2");
        var axes = AxesFor(x, y);
        return new Sdk2DOperation.SetCameraPosition(x, y, axes);
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

    public static Sdk2DOperation.StreamMapRow ReadStreamMapRow(FunctionCall call)
    {
        SdkCallReader.RequireArity(call, 4);
        var args = call.Parameters.ToList();
        var targetRow = SdkCallReader.ConstValue(args[0], "map_stream_row argument 1");
        var sourceRow = SdkCallReader.ConstValue(args[1], "map_stream_row argument 2");
        var x = SdkCallReader.ConstValue(args[2], "map_stream_row argument 3");
        var width = SdkCallReader.ConstValue(args[3], "map_stream_row argument 4");
        return new Sdk2DOperation.StreamMapRow(targetRow, sourceRow, x, width);
    }

    public static Sdk2DOperation.CameraAabbTiles ReadCameraAabbTiles(FunctionCall call)
    {
        SdkCallReader.RequireArity(call, 5);
        var args = call.Parameters.ToList();
        var screenX = ReadByteExpression(args[0], "camera_aabb_tiles argument 1");
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

    public static Sdk2DOperation.CameraAabbHitTop ReadCameraAabbHitTop(FunctionCall call)
    {
        SdkCallReader.RequireArity(call, 5);
        var args = call.Parameters.ToList();
        var screenX = ReadByteExpression(args[0], "camera_aabb_hit_top argument 1");
        var (worldY, worldYOffset) = ReadByteExpressionWithConstantOffset(args[1], "camera_aabb_hit_top argument 2");
        var width = ReadAabbExtent(args[2], "camera_aabb_hit_top argument 3");
        var height = ConstRange(args[3], 0, 255, "camera_aabb_hit_top argument 4");
        var flags = (WorldTileFlags)ConstRange(
            args[4],
            0,
            (int)(WorldTileFlags.Solid | WorldTileFlags.Hazard | WorldTileFlags.Platform),
            "camera_aabb_hit_top argument 5");

        return new Sdk2DOperation.CameraAabbHitTop(
            WorldId: "default",
            ScreenX: screenX,
            WorldY: worldY,
            WorldYOffset: worldYOffset,
            Width: width,
            Height: height,
            Flags: flags);
    }

    public static Sdk2DOperation.CameraScreenAabbTiles ReadCameraScreenAabbTiles(FunctionCall call)
    {
        SdkCallReader.RequireArity(call, 5);
        var args = call.Parameters.ToList();
        var screenX = ReadByteExpression(args[0], "camera_screen_aabb_tiles argument 1");
        var (screenY, screenYOffset) = ReadByteExpressionWithConstantOffset(args[1], "camera_screen_aabb_tiles argument 2");
        var width = ReadAabbExtent(args[2], "camera_screen_aabb_tiles argument 3");
        var height = ConstRange(args[3], 0, 255, "camera_screen_aabb_tiles argument 4");
        var flags = (WorldTileFlags)ConstRange(
            args[4],
            0,
            (int)(WorldTileFlags.Solid | WorldTileFlags.Hazard | WorldTileFlags.Platform),
            "camera_screen_aabb_tiles argument 5");

        return new Sdk2DOperation.CameraScreenAabbTiles(
            WorldId: "default",
            ScreenX: screenX,
            ScreenY: screenY,
            ScreenYOffset: screenYOffset,
            Width: width,
            Height: height,
            Flags: flags);
    }

    public static Sdk2DOperation.CameraScreenAabbHitTop ReadCameraScreenAabbHitTop(FunctionCall call)
    {
        SdkCallReader.RequireArity(call, 5);
        var args = call.Parameters.ToList();
        var screenX = ReadByteExpression(args[0], "camera_screen_aabb_hit_top argument 1");
        var (screenY, screenYOffset) = ReadByteExpressionWithConstantOffset(args[1], "camera_screen_aabb_hit_top argument 2");
        var width = ReadAabbExtent(args[2], "camera_screen_aabb_hit_top argument 3");
        var height = ConstRange(args[3], 0, 255, "camera_screen_aabb_hit_top argument 4");
        var flags = (WorldTileFlags)ConstRange(
            args[4],
            0,
            (int)(WorldTileFlags.Solid | WorldTileFlags.Hazard | WorldTileFlags.Platform),
            "camera_screen_aabb_hit_top argument 5");

        return new Sdk2DOperation.CameraScreenAabbHitTop(
            WorldId: "default",
            ScreenX: screenX,
            ScreenY: screenY,
            ScreenYOffset: screenYOffset,
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
            MemberAccessSyntax { Target: IndexExpressionSyntax indexExpression } memberAccess =>
                RuntimeIndexedFieldLocation(indexExpression, memberAccess.Member, context),
            MemberAccessSyntax memberAccess => new SdkStorageLocation.Field(
                ReadStorageLocation(memberAccess.Target, context),
                memberAccess.Member),
            _ => throw new InvalidOperationException($"{context} member access currently requires an identifier base."),
        };
    }

    private static SdkStorageLocation RuntimeIndexedFieldLocation(IndexExpressionSyntax indexExpression, string fieldName, string context)
    {
        if (TryConstValue(indexExpression.Index, out var index))
        {
            return new SdkStorageLocation.Field(
                new SdkStorageLocation.IndexedElement(indexExpression.BaseIdentifier, CheckedByte(index, context)),
                fieldName);
        }

        return new SdkStorageLocation.RuntimeIndexedField(
            indexExpression.BaseIdentifier,
            ReadByteExpression(indexExpression.Index, context),
            fieldName);
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

    private sealed class Collector
    {
        private readonly IReadOnlyDictionary<string, FunctionSyntax> functions;
        private readonly string targetName;
        private readonly Target2DCapabilities capabilities;
        private readonly IReadOnlySet<string> subroutineNames;
        private readonly TargetIntrinsicCatalog? targetIntrinsics;
        private readonly List<Sdk2DStreamItem> mainItems = [];
        private readonly Dictionary<string, IReadOnlyList<Sdk2DStreamItem>> subroutineStreams = [];
        private readonly HashSet<string> userFunctionCallStack = [];
        private List<Sdk2DStreamItem> currentItems;

        public Collector(
            IReadOnlyDictionary<string, FunctionSyntax> functions,
            string targetName,
            Target2DCapabilities capabilities,
            IReadOnlySet<string>? subroutineNames = null,
            TargetIntrinsicCatalog? targetIntrinsics = null)
        {
            this.functions = functions;
            this.targetName = targetName;
            this.capabilities = capabilities;
            this.subroutineNames = subroutineNames ?? new HashSet<string>(StringComparer.Ordinal);
            this.targetIntrinsics = targetIntrinsics;
            currentItems = mainItems;
        }

        // Flat operation list for the legacy inline-expanded path. Only valid when
        // no function is subroutined (every item is an Op).
        public IReadOnlyList<Sdk2DOperation> Operations => FlattenOps(mainItems);

        public Sdk2DProgram Program => new(mainItems, subroutineStreams);

        private void AddOp(Sdk2DOperation operation)
        {
            currentItems.Add(new Sdk2DStreamItem.Op(operation));
        }

        private static IReadOnlyList<Sdk2DOperation> FlattenOps(IReadOnlyList<Sdk2DStreamItem> items)
        {
            var ops = new List<Sdk2DOperation>(items.Count);
            foreach (var item in items)
            {
                if (item is not Sdk2DStreamItem.Op op)
                {
                    throw new InvalidOperationException("Cannot flatten an SDK stream that contains subroutine calls.");
                }

                ops.Add(op.Operation);
            }

            return ops;
        }

        private static bool TryOperationFor(TargetIntrinsicDescriptor intrinsic, out Sdk2DOperation operation)
        {
            switch (intrinsic.Operation)
            {
                case TargetIntrinsicOperation.WaitFrame:
                    operation = new Sdk2DOperation.WaitFrame();
                    return true;
                case TargetIntrinsicOperation.PollInput:
                    operation = new Sdk2DOperation.PollInput();
                    return true;
                default:
                    operation = null!;
                    return false;
            }
        }

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
                    AddOp(new Sdk2DOperation.WaitFrame());
                    break;
                case "input_poll":
                    SdkCallReader.RequireArity(call, 0);
                    AddOp(new Sdk2DOperation.PollInput());
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
                case "map_stream_row":
                    CollectStreamMapRow(call);
                    break;
                case "hud_set_tile":
                    CollectHudSetTile(call);
                    break;
                case "world_load":
                    SdkCallReader.RequireArity(call, 1);
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
            SdkCallReader.RequireArity(call, intrinsic.Arity);
            if (TryOperationFor(intrinsic, out var operation))
            {
                AddOp(operation);
            }

            return true;
        }

        private void CollectCameraSetPosition(FunctionCall call)
        {
            AddOp(ReadSetCameraPosition(call));
        }

        private void CollectCameraApply(FunctionCall call)
        {
            SdkCallReader.RequireArity(call, 0);
            AddOp(new Sdk2DOperation.ApplyCamera(capabilities.ScrollAxes));
        }

        private void CollectDrawLogicalSprite(FunctionCall call)
        {
            AddOp(ReadDrawLogicalSprite(call));
        }

        private void CollectStreamMapColumn(FunctionCall call)
        {
            AddOp(ReadStreamMapColumn(call));
        }

        private void CollectStreamMapRow(FunctionCall call)
        {
            AddOp(ReadStreamMapRow(call));
        }

        private void CollectHudSetTile(FunctionCall call)
        {
            SdkCallReader.RequireArity(call, 4);
            var args = call.Parameters.ToList();
            var mode = SdkCallReader.HudModeArg(args[0], "hud_set_tile argument 1");
            var x = ConstRange(args[1], 0, 31, "hud_set_tile argument 2");
            var y = ConstRange(args[2], 0, 31, "hud_set_tile argument 3");
            var tile = ConstRange(args[3], 0, 255, "hud_set_tile argument 4");

            AddOp(new Sdk2DOperation.SetHudTile(mode, x, y, tile));
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
                case "camera_aabb_hit_top":
                    CollectCameraAabbHitTop(call);
                    break;
                case "camera_screen_aabb_tiles":
                    CollectCameraScreenAabbTiles(call);
                    break;
                case "camera_screen_aabb_hit_top":
                    CollectCameraScreenAabbHitTop(call);
                    break;
                default:
                    if (CollectTargetValueIntrinsic(call))
                    {
                        break;
                    }

                    if (CollectUserValueFunction(call))
                    {
                        return;
                    }

                    break;
            }

            CollectCallArguments(call);
        }

        private bool CollectTargetValueIntrinsic(FunctionCall call)
        {
            if (targetIntrinsics is null ||
                !functions.TryGetValue(call.Name, out var function) ||
                !function.IsExtern)
            {
                return false;
            }

            var intrinsic = TargetIntrinsicResolver.Resolve(function, targetIntrinsics);
            if (intrinsic.Operation != TargetIntrinsicOperation.ReadWorldTileFlags)
            {
                return false;
            }

            CollectWorldTileFlagsAt(call);
            return true;
        }

        private void CollectWorldTileFlagsAt(FunctionCall call)
        {
            SdkCallReader.RequireArity(call, 2);
            var args = call.Parameters.ToList();
            var worldX = ReadByteExpression(args[0], "world_tile_flags_at argument 1");
            var worldY = ReadByteExpression(args[1], "world_tile_flags_at argument 2");
            AddOp(new Sdk2DOperation.ReadWorldTileFlags("default", worldX, worldY));
        }

        private void CollectCameraAabbTiles(FunctionCall call)
        {
            AddOp(ReadCameraAabbTiles(call));
        }

        private void CollectCameraAabbHitTop(FunctionCall call)
        {
            AddOp(ReadCameraAabbHitTop(call));
        }

        private void CollectCameraScreenAabbTiles(FunctionCall call)
        {
            AddOp(ReadCameraScreenAabbTiles(call));
        }

        private void CollectCameraScreenAabbHitTop(FunctionCall call)
        {
            AddOp(ReadCameraScreenAabbHitTop(call));
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
                CollectBlock(ParameterSubstitution.Substitute(function, call, targetName));
            }
            finally
            {
                userFunctionCallStack.Remove(function.Name);
            }
        }

        // Emits a marker for a function emitted as a shared subroutine and collects
        // its body once into a dedicated stream. Receiver parameters are bound to
        // their single instance via substitution; value parameters stay as local
        // reads (the consumer passes them through fixed slots).
        private void CollectSubroutineCall(FunctionCall call, FunctionSyntax function)
        {
            currentItems.Add(new Sdk2DStreamItem.CallSubroutine(call.Name));
            if (subroutineStreams.ContainsKey(call.Name))
            {
                return;
            }

            if (!userFunctionCallStack.Add(function.Name))
            {
                throw new InvalidOperationException($"Recursive {targetName} user function call '{function.Name}' is not supported.");
            }

            var bodyItems = new List<Sdk2DStreamItem>();
            var previousItems = currentItems;
            currentItems = bodyItems;
            try
            {
                CollectBlock(SubstituteReceiverParameters(function, call));
            }
            finally
            {
                currentItems = previousItems;
                userFunctionCallStack.Remove(function.Name);
            }

            subroutineStreams[call.Name] = bodyItems;
        }

        // Substitutes only receiver parameters (which are bound to a single shared
        // instance) and leaves value parameters in place so the subroutine body
        // reads them from their slots.
        private BlockSyntax SubstituteReceiverParameters(FunctionSyntax function, FunctionCall call)
        {
            var receiverArguments = new List<ExpressionSyntax>();
            var receiverParameters = new List<ParameterSyntax>();
            var callArguments = call.Parameters.ToList();
            for (var index = 0; index < function.Parameters.Count; index++)
            {
                if (function.Parameters[index].IsReceiver && index < callArguments.Count)
                {
                    receiverParameters.Add(function.Parameters[index]);
                    receiverArguments.Add(callArguments[index]);
                }
            }

            if (receiverParameters.Count == 0)
            {
                return function.Block;
            }

            var receiverFunction = new FunctionSyntax(
                function.Type,
                function.Name,
                receiverParameters,
                function.Block,
                function.IsExpressionBodied,
                function.IsInline,
                function.IsPure,
                function.IsExtern,
                function.Attributes);
            var receiverCall = new FunctionCall(function.Name, receiverArguments);
            return ParameterSubstitution.Substitute(receiverFunction, receiverCall, targetName);
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

    private sealed class FrameBudgetCollector(
        IReadOnlyDictionary<string, FunctionSyntax> functions,
        string targetName,
        Func<Sdk2DOperation.DrawLogicalSprite, Sdk2DFrameBudget>? drawSpriteBudget,
        TargetIntrinsicCatalog? targetIntrinsics)
    {
        private const int MaxLoopIterationsToAnalyze = 64;

        private readonly HashSet<string> userFunctionCallStack = [];

        public IReadOnlyList<Sdk2DFrameBudget> Collect(BlockSyntax block)
        {
            return CollectBlock(FrameBudgetState.Empty, block).CloseOpenFrames().FrameBudgets;
        }

        private FrameBudgetState CollectBlock(FrameBudgetState state, BlockSyntax block)
        {
            foreach (var statement in block.Statements)
            {
                state = CollectStatement(state, statement);
            }

            return state;
        }

        private FrameBudgetState CollectStatement(FrameBudgetState state, StatementSyntax statement)
        {
            switch (statement)
            {
                case DeclarationSyntax declaration:
                    if (declaration.ArrayLength.HasValue)
                    {
                        state = CollectExpression(state, declaration.ArrayLength.Value);
                    }

                    if (declaration.Initialization.HasValue)
                    {
                        state = CollectExpression(state, declaration.Initialization.Value);
                    }

                    return state;
                case ExpressionStatementSyntax { Expression: FunctionCall call }:
                    return CollectCall(state, call);
                case ExpressionStatementSyntax { Expression: AssignmentSyntax assignment }:
                    state = CollectLValue(state, assignment.Left);
                    return CollectExpression(state, assignment.Right);
                case WhileSyntax loop:
                    state = CollectExpression(state, loop.Condition);
                    return CollectLoop(state, loop.Body);
                case DoWhileSyntax loop:
                    state = CollectLoop(state, loop.Body);
                    return CollectExpression(state, loop.Condition);
                case LoopSyntax loop:
                    return CollectLoop(state, loop.Body);
                case RangeForSyntax loop:
                    return CollectStatement(state, RangeForLowerer.Lower(loop));
                case ForSyntax loop:
                    if (loop.Initializer.HasValue)
                    {
                        state = CollectStatement(state, loop.Initializer.Value);
                    }

                    if (loop.Condition.HasValue)
                    {
                        state = CollectExpression(state, loop.Condition.Value);
                    }

                    if (TryCountedLoopIterations(loop, out var iterations) && iterations <= MaxLoopIterationsToAnalyze)
                    {
                        for (var iteration = 0; iteration < iterations; iteration++)
                        {
                            state = CollectBlock(state, loop.Body);
                            if (loop.Increment.HasValue)
                            {
                                state = CollectExpression(state, loop.Increment.Value);
                            }

                            if (loop.Condition.HasValue)
                            {
                                state = CollectExpression(state, loop.Condition.Value);
                            }
                        }

                        return state;
                    }

                    state = CollectLoop(state, loop.Body);
                    if (loop.Increment.HasValue)
                    {
                        state = CollectExpression(state, loop.Increment.Value);
                    }

                    return state;
                case IfElseSyntax branch:
                    return CollectBranch(state, branch);
                default:
                    return state;
            }
        }

        private FrameBudgetState CollectLoop(FrameBudgetState state, BlockSyntax body)
        {
            var frameBudgets = new List<Sdk2DFrameBudget>(state.FrameBudgets);
            var possibleOpenBudgets = state.OpenBudgets.ToList();
            var iterationInputs = state.OpenBudgets.ToList();
            var sawBoundary = state.SawBoundary;

            for (var iteration = 0; iteration < MaxLoopIterationsToAnalyze; iteration++)
            {
                var iterationState = CollectBlock(
                    new FrameBudgetState(iterationInputs, [], sawBoundary: false),
                    body);

                frameBudgets.AddRange(iterationState.FrameBudgets);
                sawBoundary |= iterationState.SawBoundary;

                var updatedOpenBudgets = DistinctBudgets(possibleOpenBudgets.Concat(iterationState.OpenBudgets)).ToList();
                var changed = !BudgetsEqual(possibleOpenBudgets, updatedOpenBudgets);
                possibleOpenBudgets = updatedOpenBudgets;

                if (!changed && BudgetsEqual(iterationInputs, iterationState.OpenBudgets))
                {
                    break;
                }

                iterationInputs = iterationState.OpenBudgets.ToList();
            }

            return new FrameBudgetState(possibleOpenBudgets, frameBudgets, sawBoundary);
        }

        private static bool TryCountedLoopIterations(ForSyntax loop, out int iterations)
        {
            iterations = 0;
            if (loop.Initializer is not { HasValue: true, Value: DeclarationSyntax { Type: "u8" } declaration } ||
                declaration.Initialization is not { HasValue: true } initialization ||
                !TryConstValue(initialization.Value, out var start) ||
                loop.Condition is not { HasValue: true, Value: BinaryExpressionSyntax { Operator.Symbol: "<" } condition } ||
                condition.Left is not IdentifierSyntax conditionIdentifier ||
                conditionIdentifier.Identifier != declaration.Name ||
                !TryConstValue(condition.Right, out var end) ||
                loop.Increment is not { HasValue: true, Value: AssignmentSyntax { OperatorSymbol: "+=" } increment } ||
                increment.Left is not IdentifierLValue incrementIdentifier ||
                incrementIdentifier.Identifier != declaration.Name ||
                !TryConstValue(increment.Right, out var step) ||
                step <= 0)
            {
                return false;
            }

            if (start >= end)
            {
                iterations = 0;
                return true;
            }

            iterations = (end - start + step - 1) / step;
            return true;
        }

        private FrameBudgetState CollectBranch(FrameBudgetState state, IfElseSyntax branch)
        {
            state = CollectExpression(state, branch.Condition);

            var branchStart = new FrameBudgetState(
                state.OpenBudgets,
                [],
                sawBoundary: false);
            var thenState = CollectBlock(branchStart, branch.ThenBlock);
            var elseState = branch.ElseBlock.HasValue
                ? CollectBlock(branchStart, branch.ElseBlock.Value)
                : branchStart;

            return new FrameBudgetState(
                thenState.OpenBudgets.Concat(elseState.OpenBudgets),
                state.FrameBudgets.Concat(thenState.FrameBudgets).Concat(elseState.FrameBudgets),
                state.SawBoundary || thenState.SawBoundary || elseState.SawBoundary);
        }

        private FrameBudgetState CollectCall(FrameBudgetState state, FunctionCall call)
        {
            switch (call.Name)
            {
                case "video_wait_vblank":
                    SdkCallReader.RequireArity(call, 0);
                    return state.CloseOpenFrames();
                case "input_poll":
                    SdkCallReader.RequireArity(call, 0);
                    return state.CloseOpenFrames();
                case "map_stream_column":
                    return state.AddBudget(new Sdk2DFrameBudget(backgroundTileWrites: ReadStreamMapColumn(call).Height));
                case "map_stream_row":
                    return state.AddBudget(new Sdk2DFrameBudget(backgroundTileWrites: ReadStreamMapRow(call).Width));
                case "sprite_draw":
                    var draw = ReadDrawLogicalSprite(call);
                    return state.AddBudget(drawSpriteBudget?.Invoke(draw) ?? Sdk2DFrameBudget.Empty);
                default:
                    if (TryCollectTargetIntrinsic(state, call, out var intrinsicState))
                    {
                        return intrinsicState;
                    }

                    state = CollectCallArguments(state, call);
                    return CollectUserFunction(state, call);
            }
        }

        private bool TryCollectTargetIntrinsic(FrameBudgetState state, FunctionCall call, out FrameBudgetState result)
        {
            if (targetIntrinsics is null ||
                !functions.TryGetValue(call.Name, out var function) ||
                !function.IsExtern)
            {
                result = state;
                return false;
            }

            var intrinsic = TargetIntrinsicResolver.Resolve(function, targetIntrinsics);
            SdkCallReader.RequireArity(call, intrinsic.Arity);
            result = intrinsic.Operation switch
            {
                TargetIntrinsicOperation.WaitFrame or TargetIntrinsicOperation.PollInput => state.CloseOpenFrames(),
                _ => state,
            };
            return true;
        }

        private FrameBudgetState CollectExpression(FrameBudgetState state, ExpressionSyntax expression)
        {
            switch (expression)
            {
                case FunctionCall call:
                    return CollectValueCall(state, call);
                case NamedArgumentSyntax namedArgument:
                    return CollectExpression(state, namedArgument.Expression);
                case ArrayInitializerSyntax arrayInitializer:
                    foreach (var element in arrayInitializer.Elements)
                    {
                        state = CollectExpression(state, element);
                    }

                    return state;
                case StructInitializerSyntax structInitializer:
                    foreach (var field in structInitializer.Fields)
                    {
                        state = CollectExpression(state, field.Expression);
                    }

                    return state;
                case BinaryExpressionSyntax binary:
                    state = CollectExpression(state, binary.Left);
                    return CollectExpression(state, binary.Right);
                case ConditionalExpressionSyntax conditional:
                    state = CollectExpression(state, conditional.Condition);
                    var branchStart = new FrameBudgetState(state.OpenBudgets, [], sawBoundary: false);
                    var trueState = CollectExpression(branchStart, conditional.WhenTrue);
                    var falseState = CollectExpression(branchStart, conditional.WhenFalse);
                    return new FrameBudgetState(
                        trueState.OpenBudgets.Concat(falseState.OpenBudgets),
                        state.FrameBudgets.Concat(trueState.FrameBudgets).Concat(falseState.FrameBudgets),
                        state.SawBoundary || trueState.SawBoundary || falseState.SawBoundary);
                case CastSyntax cast:
                    return CollectExpression(state, cast.Expression);
                case IndexExpressionSyntax indexExpression:
                    return CollectExpression(state, indexExpression.Index);
                default:
                    return state;
            }
        }

        private FrameBudgetState CollectValueCall(FrameBudgetState state, FunctionCall call)
        {
            state = CollectCallArguments(state, call);
            return CollectUserValueFunction(state, call);
        }

        private FrameBudgetState CollectCallArguments(FrameBudgetState state, FunctionCall call)
        {
            foreach (var parameter in call.Parameters)
            {
                state = CollectExpression(state, parameter);
            }

            return state;
        }

        private FrameBudgetState CollectLValue(FrameBudgetState state, LValue lValue)
        {
            return lValue is IndexLValue index
                ? CollectExpression(state, index.Index)
                : state;
        }

        private FrameBudgetState CollectUserFunction(FrameBudgetState state, FunctionCall call)
        {
            if (!functions.TryGetValue(call.Name, out var function))
            {
                return state;
            }

            if (!userFunctionCallStack.Add(function.Name))
            {
                throw new InvalidOperationException($"Recursive {targetName} user function call '{function.Name}' is not supported.");
            }

            try
            {
                return CollectBlock(state, ParameterSubstitution.Substitute(function, call, targetName));
            }
            finally
            {
                userFunctionCallStack.Remove(function.Name);
            }
        }

        private FrameBudgetState CollectUserValueFunction(FrameBudgetState state, FunctionCall call)
        {
            if (!functions.TryGetValue(call.Name, out var function))
            {
                return state;
            }

            if (function.IsExtern)
            {
                return state;
            }

            if (!userFunctionCallStack.Add(function.Name))
            {
                throw new InvalidOperationException($"Recursive {targetName} user function call '{function.Name}' is not supported.");
            }

            try
            {
                return CollectExpression(state, ParameterSubstitution.SubstituteReturnExpression(function, call, targetName));
            }
            finally
            {
                userFunctionCallStack.Remove(function.Name);
            }
        }

        private static IReadOnlyList<Sdk2DFrameBudget> DistinctBudgets(IEnumerable<Sdk2DFrameBudget> budgets)
        {
            return budgets
                .GroupBy(BudgetKey, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToArray();
        }

        private static bool BudgetsEqual(IReadOnlyList<Sdk2DFrameBudget> left, IReadOnlyList<Sdk2DFrameBudget> right)
        {
            return left.Select(BudgetKey).Order(StringComparer.Ordinal).SequenceEqual(
                right.Select(BudgetKey).Order(StringComparer.Ordinal),
                StringComparer.Ordinal);
        }

        private static string BudgetKey(Sdk2DFrameBudget budget)
        {
            var scanlines = string.Join(
                ",",
                budget.HardwareSpritesByScanline
                    .OrderBy(pair => pair.Key)
                    .Select(pair => $"{pair.Key}:{pair.Value}"));

            return $"{budget.BackgroundTileWrites}|{budget.HardwareSprites}|{budget.SpriteSizeModes}|{scanlines}";
        }
    }

    private sealed class FrameBudgetState(
        IEnumerable<Sdk2DFrameBudget> openBudgets,
        IEnumerable<Sdk2DFrameBudget> frameBudgets,
        bool sawBoundary)
    {
        public static FrameBudgetState Empty { get; } = new([Sdk2DFrameBudget.Empty], [], sawBoundary: false);

        public IReadOnlyList<Sdk2DFrameBudget> OpenBudgets { get; } = DistinctBudgets(openBudgets);

        public IReadOnlyList<Sdk2DFrameBudget> FrameBudgets { get; } = frameBudgets.ToArray();

        public bool SawBoundary { get; } = sawBoundary;

        public FrameBudgetState AddBudget(Sdk2DFrameBudget budget)
        {
            return new FrameBudgetState(
                OpenBudgets.Select(openBudget => openBudget.Add(budget)),
                FrameBudgets,
                SawBoundary);
        }

        public FrameBudgetState CloseOpenFrames()
        {
            return new FrameBudgetState(
                [Sdk2DFrameBudget.Empty],
                FrameBudgets.Concat(
                    OpenBudgets.Where(budget => !budget.IsEmpty)),
                sawBoundary: true);
        }

        private static IReadOnlyList<Sdk2DFrameBudget> DistinctBudgets(IEnumerable<Sdk2DFrameBudget> budgets)
        {
            return budgets
                .GroupBy(BudgetKey, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToArray();
        }

        private static string BudgetKey(Sdk2DFrameBudget budget)
        {
            var scanlines = string.Join(
                ",",
                budget.HardwareSpritesByScanline
                    .OrderBy(pair => pair.Key)
                    .Select(pair => $"{pair.Key}:{pair.Value}"));

            return $"{budget.BackgroundTileWrites}|{budget.HardwareSprites}|{budget.SpriteSizeModes}|{scanlines}";
        }
    }
}
