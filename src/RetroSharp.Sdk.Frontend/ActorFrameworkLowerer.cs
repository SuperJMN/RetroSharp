using System.Globalization;
using System.Text.Json;
using CSharpFunctionalExtensions;
using RetroSharp.Core;
using RetroSharp.Core.Sdk.Tiled;
using RetroSharp.Core.Targeting;
using RetroSharp.Parser;

namespace RetroSharp.Sdk;

public sealed record ActorMetaspriteGeometry(
    int HardwareSpriteCount,
    IReadOnlyList<int> PieceYOffsets,
    int HardwareSpriteHeight);

public static partial class ActorFrameworkLowerer
{
    private const string ActorCameraXLowFunction = "__rs_actor_camera_x_lo";
    private const string ActorCameraXHighFunction = "__rs_actor_camera_x_hi";
    private const string ActorCameraYLowFunction = "__rs_actor_camera_y_lo";
    private const string ActorCameraYHighFunction = "__rs_actor_camera_y_hi";
    private const string CameraScreenAabbTilesIntrinsic = "camera_screen_aabb_tiles";
    private const string SpriteDrawIntrinsic = "sprite_draw";

    public static ProgramSyntax Lower(ProgramSyntax program, Target2DCapabilities capabilities, bool supportsUpdate, bool supportsDraw, string? baseDirectory = null)
    {
        var plan = Analyze(program, capabilities, supportsUpdate, supportsDraw, baseDirectory);
        return Lower(program, plan);
    }

    internal static ActorFrameworkLoweringPlan Analyze(
        ProgramSyntax program,
        Target2DCapabilities capabilities,
        bool supportsUpdate,
        bool supportsDraw,
        string? baseDirectory = null)
    {
        return ActorFrameworkLoweringPlan.Analyze(program, capabilities, supportsUpdate, supportsDraw, baseDirectory);
    }

    internal static ProgramSyntax Lower(ProgramSyntax program, ActorFrameworkLoweringPlan plan)
    {
        return plan.Lower(program);
    }

    private static ProgramSyntax LowerAnalyzed(ProgramSyntax program, ActorFrameworkState state)
    {
        return GeneratedProgramArtifacts.Build(program, state);
    }

    public static void ValidatePoolSpriteBudgets(
        ProgramSyntax program,
        Target2DCapabilities capabilities,
        Func<string, ActorMetaspriteGeometry> metaspriteGeometry,
        string? baseDirectory = null)
    {
        var plan = Analyze(
            program,
            capabilities,
            supportsUpdate: true,
            supportsDraw: true,
            baseDirectory);
        ValidatePoolSpriteBudgets(plan, metaspriteGeometry);
    }

    internal static void ValidatePoolSpriteBudgets(
        ActorFrameworkLoweringPlan plan,
        Func<string, ActorMetaspriteGeometry> metaspriteGeometry)
    {
        plan.ValidatePoolSpriteBudgets(metaspriteGeometry);
    }

    private static bool TryActorFrameworkStatementCall(
        StatementSyntax statement,
        ActorFrameworkState state,
        out ActorFrameworkCall call)
    {
        if (statement is ExpressionStatementSyntax { Expression: FunctionCall functionCall } &&
            state.Roles.TryRole(functionCall, out call))
        {
            return true;
        }

        call = null!;
        return false;
    }

    private static void WalkStatements(BlockSyntax block, Action<StatementSyntax> visit)
    {
        foreach (var statement in block.Statements)
        {
            visit(statement);
            switch (statement)
            {
                case IfElseSyntax ifElse:
                    WalkStatements(ifElse.ThenBlock, visit);
                    if (ifElse.ElseBlock.HasValue)
                    {
                        WalkStatements(ifElse.ElseBlock.Value, visit);
                    }

                    break;
                case WhileSyntax whileSyntax:
                    WalkStatements(whileSyntax.Body, visit);
                    break;
                case DoWhileSyntax doWhileSyntax:
                    WalkStatements(doWhileSyntax.Body, visit);
                    break;
                case RangeForSyntax rangeForSyntax:
                    WalkStatements(rangeForSyntax.Body, visit);
                    break;
                case ForSyntax forSyntax:
                    WalkStatements(forSyntax.Body, visit);
                    break;
                case SwitchSyntax switchSyntax:
                    foreach (var switchCase in switchSyntax.Cases)
                    {
                        WalkStatements(switchCase.Block, visit);
                    }

                    if (switchSyntax.DefaultBlock.HasValue)
                    {
                        WalkStatements(switchSyntax.DefaultBlock.Value, visit);
                    }

                    break;
            }
        }
    }

    private static void CollectDirectives(BlockSyntax block, ActorFrameworkState state)
    {
        WalkStatements(block, statement =>
        {
            Actors.CollectDirectives(statement, state);
            Projectiles.CollectDirectives(statement, state);
            Effects.CollectDirectives(statement, state);
            CollectCameraConfiguration(statement, state);
        });
    }

    private static void CollectCameraConfiguration(StatementSyntax statement, ActorFrameworkState state)
    {
        switch (statement)
        {
            case ExpressionStatementSyntax { Expression: QualifiedCallSyntax { Method: "Init" } cameraInitCall }
                when cameraInitCall.Qualifier.EndsWith("Camera", StringComparison.Ordinal):
                state.MarkCameraConfigured();
                break;
            case ExpressionStatementSyntax { Expression: FunctionCall { Name: var cameraCallName } }
                when cameraCallName.EndsWith("Camera_Init", StringComparison.Ordinal):
                state.MarkCameraConfigured();
                break;
        }
    }

    private static IReadOnlyDictionary<string, ExpressionSyntax> NamedArguments(IEnumerable<ExpressionSyntax> parameters, string context)
    {
        var namedArguments = new Dictionary<string, ExpressionSyntax>(StringComparer.Ordinal);
        foreach (var parameter in parameters)
        {
            if (parameter is not NamedArgumentSyntax namedArgument)
            {
                throw new InvalidOperationException($"{context} expects named arguments.");
            }

            if (!namedArguments.TryAdd(namedArgument.Name, namedArgument.Expression))
            {
                throw new InvalidOperationException($"{context} supplies '{namedArgument.Name}' more than once.");
            }
        }

        return namedArguments;
    }

    private static bool TryLiteralByte(ExpressionSyntax expression, out int value)
    {
        if (expression is ConstantSyntax constant &&
            IntegerLiteral.TryParse(Convert.ToString(constant.Value, CultureInfo.InvariantCulture), out value) &&
            value is >= 0 and <= 255)
        {
            return true;
        }

        value = 0;
        return false;
    }

    private static BlockSyntax RewriteBlock(BlockSyntax block, ActorFrameworkState state)
    {
        var statements = new List<StatementSyntax>();
        foreach (var statement in block.Statements)
        {
            statements.AddRange(RewriteStatements(statement, state));
        }

        return new BlockSyntax(statements);
    }

    private static IEnumerable<StatementSyntax> RewriteStatements(StatementSyntax statement, ActorFrameworkState state)
    {
        if (Actors.TryRewrite(statement, state, out var actorStatements))
        {
            return actorStatements;
        }

        if (Projectiles.TryRewrite(statement, state, out var projectileStatements))
        {
            return projectileStatements;
        }

        if (Effects.TryRewrite(statement, state, out var effectStatements))
        {
            return effectStatements;
        }

        var rewritten = RewriteStatement(statement, state);
        return rewritten is null ? [] : [rewritten];
    }

    private static StatementSyntax? RewriteStatement(StatementSyntax statement, ActorFrameworkState state)
    {
        return statement switch
        {
            ConstDeclarationSyntax constant => new ConstDeclarationSyntax(
                constant.TypeAnnotation,
                constant.Name,
                RewriteExpression(constant.Value, state)),
            DeclarationSyntax declaration => new DeclarationSyntax(
                declaration.Type,
                declaration.Name,
                MapMaybe(declaration.ArrayLength, expression => RewriteExpression(expression, state)),
                MapMaybe(declaration.Initialization, expression => RewriteExpression(expression, state)),
                declaration.IsImmutable),
            ExpressionStatementSyntax expressionStatement => new ExpressionStatementSyntax(RewriteExpression(expressionStatement.Expression, state)),
            IfElseSyntax ifElse => new IfElseSyntax(
                RewriteExpression(ifElse.Condition, state),
                RewriteBlock(ifElse.ThenBlock, state),
                MapMaybe(ifElse.ElseBlock, block => RewriteBlock(block, state))),
            WhileSyntax whileSyntax => new WhileSyntax(RewriteExpression(whileSyntax.Condition, state), RewriteBlock(whileSyntax.Body, state)),
            DoWhileSyntax doWhileSyntax => new DoWhileSyntax(RewriteBlock(doWhileSyntax.Body, state), RewriteExpression(doWhileSyntax.Condition, state)),
            RangeForSyntax rangeForSyntax => new RangeForSyntax(
                rangeForSyntax.Type,
                rangeForSyntax.Identifier,
                RewriteExpression(rangeForSyntax.Start, state),
                RewriteExpression(rangeForSyntax.End, state),
                RewriteBlock(rangeForSyntax.Body, state)),
            ForSyntax forSyntax => new ForSyntax(
                MapMaybe(forSyntax.Initializer, initializer => RewriteStatement(initializer, state)!),
                MapMaybe(forSyntax.Condition, condition => RewriteExpression(condition, state)),
                MapMaybe(forSyntax.Increment, increment => RewriteExpression(increment, state)),
                RewriteBlock(forSyntax.Body, state)),
            SwitchSyntax switchSyntax => new SwitchSyntax(
                RewriteExpression(switchSyntax.Subject, state),
                switchSyntax.Cases.Select(switchCase => new SwitchCaseSyntax(
                    switchCase.Patterns.Select(pattern => RewriteSwitchPattern(pattern, state)).ToList(),
                    RewriteBlock(switchCase.Block, state))).ToList(),
                MapMaybe(switchSyntax.DefaultBlock, block => RewriteBlock(block, state))),
            ReturnSyntax returnSyntax => new ReturnSyntax(MapMaybe(returnSyntax.Expression, expression => RewriteExpression(expression, state))),
            _ => statement,
        };
    }

    private static ExpressionStatementSyntax FieldAssignment(string poolName, string indexName, string fieldName, string operatorSymbol, ExpressionSyntax value)
    {
        return new ExpressionStatementSyntax(new AssignmentSyntax(
            new MemberAccessLValue(PoolField(poolName, indexName, fieldName)),
            operatorSymbol,
            value));
    }

    private static ExpressionStatementSyntax FieldAssignment(string poolName, int index, string fieldName, string operatorSymbol, ExpressionSyntax value)
    {
        return new ExpressionStatementSyntax(new AssignmentSyntax(
            new MemberAccessLValue(PoolField(poolName, index, fieldName)),
            operatorSymbol,
            value));
    }

    private static FunctionCall IntrinsicCall(
        ActorFrameworkState state,
        string intrinsicId,
        IEnumerable<ExpressionSyntax> parameters)
    {
        return new FunctionCall(state.IntrinsicFunction(intrinsicId), parameters);
    }

    private static IReadOnlyList<ExpressionSyntax> RequireArguments(QualifiedCallSyntax call, int count)
    {
        var parameters = call.Parameters.ToList();
        if (parameters.Count != count)
        {
            throw new InvalidOperationException($"{call.Qualifier}.{call.Method} expects {count} arguments, got {parameters.Count}.");
        }

        return parameters;
    }

    private static int RequiredLiteralByte(ExpressionSyntax expression, string context)
    {
        if (TryLiteralByte(expression, out var value))
        {
            return value;
        }

        throw new InvalidOperationException($"{context} must be a literal byte value.");
    }

    private static ExpressionSyntax OffsetExpression(ExpressionSyntax expression, int offset, bool subtract)
    {
        if (offset == 0)
        {
            return expression;
        }

        return new BinaryExpressionSyntax(
            expression,
            new ConstantSyntax(offset.ToString(CultureInfo.InvariantCulture)),
            Operator.Get(subtract ? "-" : "+"));
    }

    private static ConstantSyntax Constant(int value)
    {
        return new ConstantSyntax(value.ToString(CultureInfo.InvariantCulture));
    }

    private static BinaryExpressionSyntax And(ExpressionSyntax left, ExpressionSyntax right)
    {
        return new BinaryExpressionSyntax(left, right, Operator.Get("&&"));
    }

    private static BinaryExpressionSyntax Or(ExpressionSyntax left, ExpressionSyntax right)
    {
        return new BinaryExpressionSyntax(left, right, Operator.Get("||"));
    }

    private static void RequireNoArguments(QualifiedCallSyntax call)
    {
        if (call.Parameters.Any())
        {
            throw new InvalidOperationException($"{call.Qualifier}.{call.Method} does not accept arguments.");
        }
    }

    private static ForSyntax ArrayLoop(string arrayName, string indexName, StatementSyntax bodyStatement)
    {
        return ArrayLoop(arrayName, indexName, [bodyStatement]);
    }

    private static ForSyntax ArrayLoop(string arrayName, string indexName, IReadOnlyList<StatementSyntax> bodyStatements)
    {
        return new ForSyntax(
            Maybe.From<StatementSyntax>(new DeclarationSyntax("u8", indexName, Maybe<ExpressionSyntax>.None, Maybe.From<ExpressionSyntax>(Constant(0)))),
            Maybe.From<ExpressionSyntax>(new BinaryExpressionSyntax(new IdentifierSyntax(indexName), new CountOfSyntax(arrayName), Operator.LessThan)),
            Maybe.From<ExpressionSyntax>(new AssignmentSyntax(new IdentifierLValue(indexName), "+=", Constant(1))),
            new BlockSyntax(bodyStatements.ToList()));
    }

    private static MemberAccessSyntax PoolField(string poolName, string indexName, string fieldName)
    {
        return new MemberAccessSyntax(new IndexExpressionSyntax(poolName, new IdentifierSyntax(indexName)), fieldName);
    }

    private static MemberAccessSyntax PoolField(string poolName, int index, string fieldName)
    {
        return new MemberAccessSyntax(new IndexExpressionSyntax(poolName, new ConstantSyntax(index.ToString(CultureInfo.InvariantCulture))), fieldName);
    }

    private static ExpressionStatementSyntax Assign(LValue target, ExpressionSyntax value)
    {
        return new ExpressionStatementSyntax(new AssignmentSyntax(target, value));
    }

    private static ExpressionSyntax RewriteExpression(ExpressionSyntax expression, ActorFrameworkState state)
    {
        if (Actors.TryRewriteExpression(expression, state, out var actorExpression))
        {
            return actorExpression;
        }

        if (Projectiles.TryRewriteExpression(expression, out var projectileExpression))
        {
            return projectileExpression;
        }

        if (Effects.TryRewriteExpression(expression, out var effectExpression))
        {
            return effectExpression;
        }

        return expression switch
        {
            AssignmentSyntax assignment => new AssignmentSyntax(RewriteLValue(assignment.Left, state), assignment.OperatorSymbol, RewriteExpression(assignment.Right, state)),
            BinaryExpressionSyntax binary => new BinaryExpressionSyntax(
                RewriteExpression(binary.Left, state),
                RewriteExpression(binary.Right, state),
                binary.Operator),
            ArrayInitializerSyntax arrayInitializer => new ArrayInitializerSyntax(arrayInitializer.Elements.Select(element => RewriteExpression(element, state))),
            StructInitializerSyntax structInitializer => new StructInitializerSyntax(structInitializer.Fields.Select(field => new StructFieldInitializerSyntax(field.Name, RewriteExpression(field.Expression, state)))),
            ConditionalExpressionSyntax conditional => new ConditionalExpressionSyntax(
                RewriteExpression(conditional.Condition, state),
                RewriteExpression(conditional.WhenTrue, state),
                RewriteExpression(conditional.WhenFalse, state)),
            SwitchExpressionSyntax switchExpression => new SwitchExpressionSyntax(
                RewriteExpression(switchExpression.Subject, state),
                switchExpression.Arms.Select(arm => new SwitchExpressionArmSyntax(
                    arm.Patterns.Select(pattern => RewriteSwitchPattern(pattern, state)).ToList(),
                    RewriteExpression(arm.Value, state))).ToList(),
                MapMaybe(switchExpression.DefaultValue, value => RewriteExpression(value, state))),
            PipelineExpressionSyntax pipeline => new PipelineExpressionSyntax(
                RewriteExpression(pipeline.Value, state),
                pipeline.Steps.Select(step => new PipelineStepSyntax(
                    step.FunctionName,
                    step.Arguments.Select(argument => RewriteExpression(argument, state)))).ToList()),
            UnaryExpressionSyntax unary => new UnaryExpressionSyntax(unary.OperatorSymbol, RewriteExpression(unary.Operand, state)),
            CastSyntax cast => new CastSyntax(cast.Type, RewriteExpression(cast.Expression, state)),
            FunctionCall call => new FunctionCall(call.Name, call.Parameters.Select(parameter => RewriteExpression(parameter, state))),
            NamedArgumentSyntax namedArgument => new NamedArgumentSyntax(namedArgument.Name, RewriteExpression(namedArgument.Expression, state)),
            MemberAccessSyntax memberAccess => new MemberAccessSyntax(RewriteExpression(memberAccess.Target, state), memberAccess.Member),
            IndexExpressionSyntax indexExpression => new IndexExpressionSyntax(indexExpression.BaseIdentifier, RewriteExpression(indexExpression.Index, state)),
            PostfixMutationSyntax postfix => new PostfixMutationSyntax(RewriteLValue(postfix.Target, state), postfix.OperatorSymbol),
            _ => expression,
        };
    }

    private static LValue RewriteLValue(LValue lValue, ActorFrameworkState state)
    {
        return lValue switch
        {
            MemberAccessLValue memberAccess => new MemberAccessLValue((MemberAccessSyntax)RewriteExpression(memberAccess.MemberAccess, state)),
            IndexLValue index => new IndexLValue(index.BaseIdentifier, RewriteExpression(index.Index, state)),
            PointerDerefLValue pointer => new PointerDerefLValue(RewriteExpression(pointer.Expression, state)),
            _ => lValue,
        };
    }

    private static SwitchCasePatternSyntax RewriteSwitchPattern(SwitchCasePatternSyntax pattern, ActorFrameworkState state)
    {
        return pattern.End.HasValue
            ? new SwitchCasePatternSyntax(RewriteExpression(pattern.Start, state), RewriteExpression(pattern.End.Value, state))
            : new SwitchCasePatternSyntax(RewriteExpression(pattern.Start, state));
    }

    private static ConstDeclarationSyntax Constant(string name, int value)
    {
        return Constant(name, new ConstantSyntax(value.ToString(CultureInfo.InvariantCulture)));
    }

    private static ConstDeclarationSyntax Constant(string name, ExpressionSyntax value)
    {
        return new ConstDeclarationSyntax(Maybe<string>.None, name, value);
    }

    private static Maybe<TOut> MapMaybe<TIn, TOut>(Maybe<TIn> maybe, Func<TIn, TOut> selector)
    {
        return maybe.HasValue ? Maybe.From(selector(maybe.Value)) : Maybe<TOut>.None;
    }

    internal sealed class ActorFrameworkLoweringPlan
    {
        private ProgramSyntax? sourceProgram;
        private readonly ActorFrameworkState state;
        private readonly HashSet<string> drawnActorPools;
        private PlanStage stage = PlanStage.Analyzed;

        private ActorFrameworkLoweringPlan(
            ProgramSyntax sourceProgram,
            Target2DCapabilities capabilities,
            ActorFrameworkState state,
            IEnumerable<string> drawnActorPools)
        {
            this.sourceProgram = sourceProgram;
            Capabilities = capabilities;
            this.state = state;
            this.drawnActorPools = new HashSet<string>(drawnActorPools, StringComparer.Ordinal);
        }

        internal static ActorFrameworkLoweringPlan Analyze(
            ProgramSyntax program,
            Target2DCapabilities capabilities,
            bool supportsUpdate,
            bool supportsDraw,
            string? baseDirectory)
        {
            var state = new ActorFrameworkState(
                capabilities,
                supportsUpdate,
                supportsDraw,
                baseDirectory,
                ActorFrameworkRoleIndex.Build(program),
                IntrinsicFunctionIndex.Build(program));
            foreach (var function in program.Functions)
            {
                CollectDirectives(function.Block, state);
            }

            var drawnActorPools = new HashSet<string>(StringComparer.Ordinal);
            foreach (var function in program.Functions)
            {
                Actors.CollectDrawnPools(function.Block, drawnActorPools);
                Actors.CollectUsedEnemyLookupMethods(function.Block, state);
            }

            var plan = new ActorFrameworkLoweringPlan(program, capabilities, state, drawnActorPools);
            plan.ValidateDirectives();
            return plan;
        }

        internal bool HasDirectives => state.HasDirectives;
        internal int ActorPoolCount => state.Actors.Pools.Count;
        internal int EnemyDefinitionCount => state.Actors.EnemyDefs.Count;
        internal int SpawnLayerCount => state.Spawns.Layers.Count;
        internal int ProjectilePoolCount => state.Projectiles.Pools.Count;
        internal int ProjectileDefinitionCount => state.Projectiles.Definitions.Count;
        internal int EffectPoolCount => state.Effects.Pools.Count;
        internal int EffectDefinitionCount => state.Effects.Definitions.Count;
        internal string[] DrawnActorPoolNames => drawnActorPools.Order(StringComparer.Ordinal).ToArray();
        internal string[] GeneratedNames => GeneratedProgramArtifacts.GeneratedNames(state).Select(name => name.Name).ToArray();
        internal IReadOnlyDictionary<string, CompilerGeneratedRomTable> GeneratedRomTables =>
            Actors.GeneratedSpawnRomTables(state.Spawns.Layers, state.Actors.EnemyDefs);

        private Target2DCapabilities Capabilities { get; }

        private void ValidateDirectives()
        {
            if (!state.HasDirectives)
            {
                return;
            }

            Actors.ValidateSpawnLayers(state);
            Projectiles.ValidateEffects(state);
        }

        internal ProgramSyntax Lower(ProgramSyntax program)
        {
            if (!ReferenceEquals(sourceProgram, program))
            {
                throw new InvalidOperationException("Actor Framework lowering plan belongs to a different parsed program.");
            }

            if (stage != PlanStage.Analyzed)
            {
                throw new InvalidOperationException($"Actor Framework lowering plan cannot begin lowering from stage '{stage}'.");
            }

            stage = PlanStage.Lowering;
            try
            {
                var lowered = LowerAnalyzed(program, state);
                sourceProgram = null;
                stage = PlanStage.Lowered;
                return lowered;
            }
            catch
            {
                sourceProgram = null;
                stage = PlanStage.Failed;
                throw;
            }
        }

        internal void ValidatePoolSpriteBudgets(Func<string, ActorMetaspriteGeometry> metaspriteGeometry)
        {
            Actors.ValidatePoolSpriteBudgets(state, Capabilities, drawnActorPools, metaspriteGeometry);
        }

        private enum PlanStage
        {
            Analyzed,
            Lowering,
            Lowered,
            Failed,
        }
    }

    private sealed class ActorFrameworkState(
        Target2DCapabilities capabilities,
        bool supportsUpdate,
        bool supportsDraw,
        string? baseDirectory,
        ActorFrameworkRoleIndex roles,
        IReadOnlyDictionary<string, string>? intrinsicFunctionNames = null)
    {
        private readonly IReadOnlyDictionary<string, string> intrinsicFunctions = intrinsicFunctionNames ?? new Dictionary<string, string>(StringComparer.Ordinal);

        public string TargetName { get; } = capabilities.Name;
        public string BaseDirectory { get; } = baseDirectory ?? Directory.GetCurrentDirectory();
        public bool SupportsUpdate { get; } = supportsUpdate;
        public bool SupportsDraw { get; } = supportsDraw;
        public int ScreenWidth { get; } = capabilities.ScreenPixels.Width;
        public int ScreenHeight { get; } = capabilities.ScreenPixels.Height;
        public ActorState Actors { get; } = new();
        public SpawnState Spawns { get; } = new();
        public ProjectileState Projectiles { get; } = new();
        public EffectState Effects { get; } = new();
        public GeneratedCallState GeneratedCalls { get; } = new();
        public bool HasDirectives => Actors.HasDirectives || Spawns.HasDirectives || Projectiles.HasDirectives || Effects.HasDirectives;
        public ActorFrameworkRoleIndex Roles { get; } = roles;

        // True once the program is seen to call the camera-init facade (either the qualified
        // `<Camera>.Init(...)` form or its resolved static `..._Camera_Init(...)` call). Without a
        // configured camera it can never move from the origin, so projectile draws project against a
        // literal-0 camera instead of reading the target's camera runtime state, which is otherwise
        // uninitialized target memory whose power-on value differs per emulator and would shift
        // camera-less projectile draws off their spawner. Matching the resolved static call (not the
        // lowercase camera-init intrinsic) avoids a false positive from the library's own facade body.
        public bool ConfiguresCamera { get; private set; }

        public void MarkCameraConfigured() => ConfiguresCamera = true;

        public string IntrinsicFunction(string intrinsicId)
        {
            if (intrinsicFunctions.TryGetValue(intrinsicId, out var functionName))
            {
                return functionName;
            }

            throw new InvalidOperationException(
                $"Actor framework requires intrinsic '{intrinsicId}' to be declared by an imported source library.");
        }
    }

}
