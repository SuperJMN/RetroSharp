using System.Globalization;
using System.Text.Json;
using CSharpFunctionalExtensions;
using RetroSharp.Core;
using RetroSharp.Core.Targeting;
using RetroSharp.Parser;

namespace RetroSharp.Sdk;

public static partial class ActorFrameworkLowerer
{
    private static partial class Actors
    {
        public static void ValidatePoolSpriteBudgets(
            ActorFrameworkState state,
            Target2DCapabilities capabilities,
            IReadOnlySet<string> drawnPools,
            Func<string, ActorMetaspriteGeometry> metaspriteGeometry)
        {

            if (state.Actors.Pools.Count == 0 || state.Actors.EnemyDefs.Count == 0)
            {
                return;
            }

            if (drawnPools.Count == 0)
            {
                return;
            }

            var enemyBudgets = state.Actors.EnemyDefs
                .Where(def => def.Sprite is not null)
                .Select(def =>
                {
                    var geometry = metaspriteGeometry(def.Sprite!);
                    return new EnemySpriteBudget(
                        def,
                        geometry,
                        BusiestRelativeScanlineSpriteCount(geometry));
                })
                .ToList();

            if (enemyBudgets.Count == 0)
            {
                return;
            }

            foreach (var pool in state.Actors.Pools.Where(pool => drawnPools.Contains(pool.Name)))
            {
                ValidatePoolSpriteBudget(capabilities, pool, enemyBudgets);
            }
        }

        public static void CollectDrawnPools(BlockSyntax block, ISet<string> drawnPools)
        {
            WalkStatements(block, statement =>
            {
                if (statement is ExpressionStatementSyntax { Expression: QualifiedCallSyntax { Method: "Draw" } drawCall })
                {
                    drawnPools.Add(drawCall.Qualifier);
                }
            });
        }

        public static void CollectUsedEnemyLookupMethods(BlockSyntax block, ActorFrameworkState state)
        {
            WalkStatements(block, statement => CollectStatementExpressionFacts(statement, state));
        }

        private static void CollectStatementExpressionFacts(StatementSyntax statement, ActorFrameworkState state)
        {
            switch (statement)
            {
                case ExpressionStatementSyntax expressionStatement:
                    CollectExpressionFacts(expressionStatement.Expression, state);
                    break;
                case ConstDeclarationSyntax constant:
                    CollectExpressionFacts(constant.Value, state);
                    break;
                case DeclarationSyntax declaration:
                    if (declaration.ArrayLength.HasValue)
                    {
                        CollectExpressionFacts(declaration.ArrayLength.Value, state);
                    }

                    if (declaration.Initialization.HasValue)
                    {
                        CollectExpressionFacts(declaration.Initialization.Value, state);
                    }

                    break;
                case IfElseSyntax ifElse:
                    CollectExpressionFacts(ifElse.Condition, state);
                    break;
                case WhileSyntax whileSyntax:
                    CollectExpressionFacts(whileSyntax.Condition, state);
                    break;
                case DoWhileSyntax doWhileSyntax:
                    CollectExpressionFacts(doWhileSyntax.Condition, state);
                    break;
                case RangeForSyntax rangeForSyntax:
                    CollectExpressionFacts(rangeForSyntax.Start, state);
                    CollectExpressionFacts(rangeForSyntax.End, state);
                    break;
                case ForSyntax forSyntax:
                    if (forSyntax.Initializer.HasValue)
                    {
                        CollectStatementExpressionFacts(forSyntax.Initializer.Value, state);
                    }

                    if (forSyntax.Condition.HasValue)
                    {
                        CollectExpressionFacts(forSyntax.Condition.Value, state);
                    }

                    if (forSyntax.Increment.HasValue)
                    {
                        CollectExpressionFacts(forSyntax.Increment.Value, state);
                    }

                    break;
                case SwitchSyntax switchSyntax:
                    CollectExpressionFacts(switchSyntax.Subject, state);
                    foreach (var pattern in switchSyntax.Cases.SelectMany(switchCase => switchCase.Patterns))
                    {
                        CollectSwitchPatternFacts(pattern, state);
                    }

                    break;
                case ReturnSyntax returnSyntax when returnSyntax.Expression.HasValue:
                    CollectExpressionFacts(returnSyntax.Expression.Value, state);
                    break;
            }
        }

        private static void CollectExpressionFacts(ExpressionSyntax expression, ActorFrameworkState state)
        {
            switch (expression)
            {
                case FunctionCall call:
                    if (state.Roles.TryRole(call, out var actorCall) && EnemyLookupFunctions.ContainsKey(actorCall.Role))
                    {
                        state.Actors.RecordEnemyLookupMethod(actorCall.Role);
                    }

                    foreach (var parameter in call.Parameters)
                    {
                        CollectExpressionFacts(parameter, state);
                    }

                    break;
                case QualifiedCallSyntax call:
                    foreach (var parameter in call.Parameters)
                    {
                        CollectExpressionFacts(parameter, state);
                    }

                    break;
                case AssignmentSyntax assignment:
                    CollectLValueFacts(assignment.Left, state);
                    CollectExpressionFacts(assignment.Right, state);
                    break;
                case BinaryExpressionSyntax binary:
                    CollectExpressionFacts(binary.Left, state);
                    CollectExpressionFacts(binary.Right, state);
                    break;
                case ArrayInitializerSyntax arrayInitializer:
                    foreach (var element in arrayInitializer.Elements)
                    {
                        CollectExpressionFacts(element, state);
                    }

                    break;
                case StructInitializerSyntax structInitializer:
                    foreach (var field in structInitializer.Fields)
                    {
                        CollectExpressionFacts(field.Expression, state);
                    }

                    break;
                case ConditionalExpressionSyntax conditional:
                    CollectExpressionFacts(conditional.Condition, state);
                    CollectExpressionFacts(conditional.WhenTrue, state);
                    CollectExpressionFacts(conditional.WhenFalse, state);
                    break;
                case SwitchExpressionSyntax switchExpression:
                    CollectExpressionFacts(switchExpression.Subject, state);
                    foreach (var arm in switchExpression.Arms)
                    {
                        foreach (var pattern in arm.Patterns)
                        {
                            CollectSwitchPatternFacts(pattern, state);
                        }

                        CollectExpressionFacts(arm.Value, state);
                    }

                    if (switchExpression.DefaultValue.HasValue)
                    {
                        CollectExpressionFacts(switchExpression.DefaultValue.Value, state);
                    }

                    break;
                case PipelineExpressionSyntax pipeline:
                    CollectExpressionFacts(pipeline.Value, state);
                    foreach (var argument in pipeline.Steps.SelectMany(step => step.Arguments))
                    {
                        CollectExpressionFacts(argument, state);
                    }

                    break;
                case UnaryExpressionSyntax unary:
                    CollectExpressionFacts(unary.Operand, state);
                    break;
                case CastSyntax cast:
                    CollectExpressionFacts(cast.Expression, state);
                    break;
                case NamedArgumentSyntax namedArgument:
                    CollectExpressionFacts(namedArgument.Expression, state);
                    break;
                case MemberAccessSyntax memberAccess:
                    CollectExpressionFacts(memberAccess.Target, state);
                    break;
                case IndexExpressionSyntax indexExpression:
                    CollectExpressionFacts(indexExpression.Index, state);
                    break;
                case PostfixMutationSyntax postfix:
                    CollectLValueFacts(postfix.Target, state);
                    break;
            }
        }

        private static void CollectLValueFacts(LValue lValue, ActorFrameworkState state)
        {
            switch (lValue)
            {
                case MemberAccessLValue memberAccess:
                    CollectExpressionFacts(memberAccess.MemberAccess, state);
                    break;
                case IndexLValue index:
                    CollectExpressionFacts(index.Index, state);
                    break;
                case PointerDerefLValue pointer:
                    CollectExpressionFacts(pointer.Expression, state);
                    break;
            }
        }

        private static void CollectSwitchPatternFacts(SwitchCasePatternSyntax pattern, ActorFrameworkState state)
        {
            CollectExpressionFacts(pattern.Start, state);
            if (pattern.End.HasValue)
            {
                CollectExpressionFacts(pattern.End.Value, state);
            }
        }

        private static void ValidatePoolSpriteBudget(
            Target2DCapabilities capabilities,
            ActorPool pool,
            IReadOnlyList<EnemySpriteBudget> enemyBudgets)
        {
            var largestMetasprite = enemyBudgets
                .OrderByDescending(budget => budget.Geometry.HardwareSpriteCount)
                .ThenBy(budget => budget.Def.Name, StringComparer.Ordinal)
                .First();
            var frameSprites = pool.Capacity * largestMetasprite.Geometry.HardwareSpriteCount;
            if (frameSprites > capabilities.SpriteCount)
            {
                throw new InvalidOperationException(
                    $"Target '{capabilities.Name}' supports {capabilities.SpriteCount} hardware sprites per frame, but Actors.Pool for '{pool.Name}' can draw up to {frameSprites} because capacity {pool.Capacity} times Enemies.Def '{largestMetasprite.Def.Name}' sprite '{largestMetasprite.Def.Sprite}' uses {HardwareSpriteCountText(largestMetasprite.Geometry.HardwareSpriteCount)}.");
            }

            var busiestScanline = enemyBudgets
                .OrderByDescending(budget => budget.BusiestRelativeScanlineSprites)
                .ThenBy(budget => budget.Def.Name, StringComparer.Ordinal)
                .First();
            var scanlineSprites = pool.Capacity * busiestScanline.BusiestRelativeScanlineSprites;
            if (scanlineSprites > capabilities.MaxSpritesPerScanline)
            {
                throw new InvalidOperationException(
                    $"Target '{capabilities.Name}' supports {capabilities.MaxSpritesPerScanline} hardware sprites per scanline, but Actors.Pool for '{pool.Name}' can draw up to {scanlineSprites} on one scanline because capacity {pool.Capacity} times Enemies.Def '{busiestScanline.Def.Name}' sprite '{busiestScanline.Def.Sprite}' uses {HardwareSpriteCountText(busiestScanline.BusiestRelativeScanlineSprites)} on its busiest scanline.");
            }
        }

        private static string HardwareSpriteCountText(int count)
        {
            return $"{count} hardware sprite{(count == 1 ? string.Empty : "s")}";
        }

        private static int BusiestRelativeScanlineSpriteCount(ActorMetaspriteGeometry geometry)
        {
            if (geometry.PieceYOffsets.Count == 0 || geometry.HardwareSpriteHeight <= 0)
            {
                return geometry.HardwareSpriteCount;
            }

            var counts = new Dictionary<int, int>();
            foreach (var offset in geometry.PieceYOffsets)
            {
                for (var scanline = offset; scanline < offset + geometry.HardwareSpriteHeight; scanline++)
                {
                    counts[scanline] = counts.GetValueOrDefault(scanline) + 1;
                }
            }

            return counts.Values.DefaultIfEmpty(0).Max();
        }

        private static string? OptionalIdentifier(
            IReadOnlyDictionary<string, ExpressionSyntax> arguments,
            string name,
            string enemyName)
        {
            if (!arguments.TryGetValue(name, out var expression))
            {
                return null;
            }

            if (expression is IdentifierSyntax identifier)
            {
                return identifier.Identifier;
            }

            throw new InvalidOperationException($"Enemies.Def for '{enemyName}' requires '{name}' to be an identifier.");
        }

        private static string RequiredIdentifier(
            IReadOnlyDictionary<string, ExpressionSyntax> arguments,
            string name,
            string enemyName)
        {
            if (!arguments.TryGetValue(name, out var expression))
            {
                throw new InvalidOperationException($"Enemies.Def for '{enemyName}' requires '{name}'.");
            }

            if (expression is IdentifierSyntax identifier)
            {
                return identifier.Identifier;
            }

            throw new InvalidOperationException($"Enemies.Def for '{enemyName}' requires '{name}' to be an identifier.");
        }

        private static int OptionalLiteralByte(
            IReadOnlyDictionary<string, ExpressionSyntax> arguments,
            string name,
            int defaultValue,
            string enemyName)
        {
            if (!arguments.TryGetValue(name, out var expression))
            {
                return defaultValue;
            }

            if (TryLiteralByte(expression, out var value))
            {
                return value;
            }

            throw new InvalidOperationException($"Enemies.Def for '{enemyName}' requires '{name}' to be a literal byte value.");
        }

        public static string StringLiteral(ExpressionSyntax expression, string context)
        {
            if (expression is not ConstantSyntax constant)
            {
                throw new InvalidOperationException($"{context} must be a string literal.");
            }

            var text = Convert.ToString(constant.Value, CultureInfo.InvariantCulture);
            if (text is null || !text.StartsWith('"'))
            {
                throw new InvalidOperationException($"{context} must be a string literal.");
            }

            try
            {
                return JsonSerializer.Deserialize<string>(text) ?? throw new InvalidOperationException($"{context} must be a string literal.");
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"{context} must be a valid string literal.", ex);
            }
        }

        private static IReadOnlyList<StatementSyntax> UpdateStatementsFor(ActorPool pool, string indexName, EnemyDef def)
        {
            IReadOnlyList<StatementSyntax> statements = def.Behavior switch
            {
                "Walker" =>
                    AddWorldX(pool, indexName, new IdentifierSyntax($"{def.Name}Speed")),
                "Flyer" =>
                    AddWorldY(pool, indexName, new IdentifierSyntax($"{def.Name}Speed")),
                "Patrol" =>
                [
                    FacingMove(pool, indexName, def),
                    FieldAssignment(pool.Name, indexName, "timer", "+=", new ConstantSyntax("1")),
                    TimerCooldown(pool, indexName, def, [
                        FieldAssignment(pool.Name, indexName, "timer", "=", new ConstantSyntax("0")),
                        FieldAssignment(pool.Name, indexName, "facing", "^=", new ConstantSyntax("1")),
                    ]),
                ],
                "Shooter" =>
                [
                    FieldAssignment(pool.Name, indexName, "timer", "+=", new ConstantSyntax("1")),
                    TimerCooldown(
                        pool,
                        indexName,
                        def,
                        [
                            FieldAssignment(pool.Name, indexName, "timer", "=", new ConstantSyntax("0")),
                            FieldAssignment(pool.Name, indexName, "state", "=", new ConstantSyntax("1")),
                        ],
                        [
                            FieldAssignment(pool.Name, indexName, "state", "=", new ConstantSyntax("0")),
                        ]),
                ],
                "Chaser" =>
                [
                    FacingMove(pool, indexName, def),
                ],
                "Hazard" =>
                [
                    FieldAssignment(pool.Name, indexName, "state", "=", new IdentifierSyntax($"{def.Name}ContactDamage")),
                ],
                _ => throw new InvalidOperationException($"{pool.Name}.Update does not support actor behavior '{def.Behavior}' yet."),
            };

            if (def.Animation is not null)
            {
                statements = statements.Concat([
                    FieldAssignment(pool.Name, indexName, "animTick", "+=", new ConstantSyntax("1")),
                ]).ToList();
            }

            return statements;
        }

        private static IfElseSyntax FacingMove(ActorPool pool, string indexName, EnemyDef def)
        {
            var speed = new IdentifierSyntax($"{def.Name}Speed");
            return new IfElseSyntax(
                new BinaryExpressionSyntax(PoolField(pool.Name, indexName, "facing"), new ConstantSyntax("0"), Operator.Equal),
                new BlockSyntax(AddWorldX(pool, indexName, speed).ToList()),
                Maybe.From(new BlockSyntax(SubtractWorldX(pool, indexName, speed).ToList())));
        }

        private static IReadOnlyList<StatementSyntax> AddWorldX(ActorPool pool, string indexName, ExpressionSyntax amount)
        {
            return
            [
                FieldAssignment(pool.Name, indexName, "x", "+=", amount),
                new IfElseSyntax(
                    new BinaryExpressionSyntax(PoolField(pool.Name, indexName, "x"), amount, Operator.LessThan),
                    new BlockSyntax([FieldAssignment(pool.Name, indexName, "xHi", "+=", new ConstantSyntax("1"))]),
                    Maybe<BlockSyntax>.None),
            ];
        }

        private static IReadOnlyList<StatementSyntax> SubtractWorldX(ActorPool pool, string indexName, ExpressionSyntax amount)
        {
            return
            [
                new IfElseSyntax(
                    new BinaryExpressionSyntax(PoolField(pool.Name, indexName, "x"), amount, Operator.LessThan),
                    new BlockSyntax([FieldAssignment(pool.Name, indexName, "xHi", "-=", new ConstantSyntax("1"))]),
                    Maybe<BlockSyntax>.None),
                FieldAssignment(pool.Name, indexName, "x", "-=", amount),
            ];
        }

        private static IReadOnlyList<StatementSyntax> AddWorldY(ActorPool pool, string indexName, ExpressionSyntax amount)
        {
            return
            [
                FieldAssignment(pool.Name, indexName, "y", "+=", amount),
                new IfElseSyntax(
                    new BinaryExpressionSyntax(PoolField(pool.Name, indexName, "y"), amount, Operator.LessThan),
                    new BlockSyntax([FieldAssignment(pool.Name, indexName, "yHi", "+=", new ConstantSyntax("1"))]),
                    Maybe<BlockSyntax>.None),
            ];
        }

        private static IfElseSyntax TimerCooldown(
            ActorPool pool,
            string indexName,
            EnemyDef def,
            IReadOnlyList<StatementSyntax> whenReady,
            IReadOnlyList<StatementSyntax>? whenWaiting = null)
        {
            return new IfElseSyntax(
                new BinaryExpressionSyntax(PoolField(pool.Name, indexName, "timer"), new IdentifierSyntax($"{def.Name}Cooldown"), Operator.Equal),
                new BlockSyntax(whenReady.ToList()),
                whenWaiting is null ? Maybe<BlockSyntax>.None : Maybe.From(new BlockSyntax(whenWaiting.ToList())));
        }

        private static IReadOnlyList<StatementSyntax> PoolDrawStatements(ActorPool pool, QualifiedCallSyntax call, ActorFrameworkState state)
        {
            RequireNoArguments(call);
            if (!state.SupportsDraw)
            {
                throw new InvalidOperationException($"Target '{state.TargetName}' does not support actor Draw yet.");
            }

            RequireEnemyDefs(state, $"{pool.Name}.Draw");
            var missingSprite = state.Actors.EnemyDefs.FirstOrDefault(def => def.Sprite is null);
            if (missingSprite is not null)
            {
                throw new InvalidOperationException($"{pool.Name}.Draw requires Enemies.Def for '{missingSprite.Name}' to declare a sprite identifier.");
            }

            var indexName = $"__{pool.Name}_draw_i";
            var projection = BuildPooledScreenProjection(pool.Name, indexName, pool.Name, "draw", state.ScreenWidth, state.ScreenHeight, margin: 0);
            var branches = state.Actors.EnemyDefs
                .Select(def => new KindBranch(
                    def.Name,
                    ActorDrawBlock(pool, indexName, def, projection, state.ScreenHeight, state)))
                .ToList();
            var activeStatements = projection.Declarations
                .Append(PooledKindDispatch(pool.Name, indexName, branches, $"{pool.Name} actor dispatch requires at least one Enemies.Def declaration."))
                .ToList();
            var loop = PoolLoop(pool, indexName, activeStatements);

            return PooledCameraDeclarations(pool.Name, "draw", configuresCamera: true)
                .Append(loop)
                .ToList();
        }

        private static BlockSyntax ActorDrawBlock(
            ActorPool pool,
            string indexName,
            EnemyDef def,
            ActorScreenProjection projection,
            int hiddenY,
            ActorFrameworkState state)
        {
            var statements = new List<StatementSyntax>();
            var drawX = $"__{pool.Name}_draw_x_{def.Name}";
            var drawY = $"__{pool.Name}_draw_y_{def.Name}";

            ExpressionSyntax frame = new ConstantSyntax("0");
            if (def.Animation is not null)
            {
                var frameVariable = $"__{pool.Name}_draw_frame_{def.Name}";
                statements.Add(new DeclarationSyntax(
                    "u8",
                    frameVariable,
                    Maybe<ExpressionSyntax>.None,
                    Maybe.From<ExpressionSyntax>(IntrinsicCall(
                        state,
                        AnimationFrameIntrinsic,
                        [
                            new IdentifierSyntax(def.Animation),
                            PoolField(pool.Name, indexName, "animTick"),
                        ]))));
                frame = new IdentifierSyntax(frameVariable);
            }

            statements.Add(new DeclarationSyntax(
                "u8",
                drawX,
                Maybe<ExpressionSyntax>.None,
                Maybe.From<ExpressionSyntax>(new ConstantSyntax("0"))));
            statements.Add(new DeclarationSyntax(
                "u8",
                drawY,
                Maybe<ExpressionSyntax>.None,
                Maybe.From<ExpressionSyntax>(new ConstantSyntax(hiddenY.ToString(CultureInfo.InvariantCulture)))));

            statements.Add(new IfElseSyntax(
                new BinaryExpressionSyntax(PoolField(pool.Name, indexName, "active"), new ConstantSyntax("0"), Operator.NotEqual),
                new BlockSyntax([
                    new IfElseSyntax(
                        projection.Visible,
                        new BlockSyntax([
                            new ExpressionStatementSyntax(new AssignmentSyntax(
                                new IdentifierLValue(drawX),
                                "=",
                                projection.ScreenX)),
                            new ExpressionStatementSyntax(new AssignmentSyntax(
                                new IdentifierLValue(drawY),
                                "=",
                                projection.ScreenY)),
                        ]),
                        Maybe<BlockSyntax>.None),
                ]),
                Maybe<BlockSyntax>.None));

            statements.Add(new ExpressionStatementSyntax(IntrinsicCall(
                state,
                SpriteDrawIntrinsic,
                [
                    new IdentifierSyntax(def.Sprite!),
                    new IdentifierSyntax(drawX),
                    new IdentifierSyntax(drawY),
                    frame,
                    new IdentifierSyntax("false"),
                    new ConstantSyntax("0"),
                ])));

            return new BlockSyntax(statements);
        }

        private static IReadOnlyList<StatementSyntax> PoolTouchTilesStatements(ActorPool pool, QualifiedCallSyntax call, ActorFrameworkState state)
        {
            var parameters = RequireArguments(call, 2);
            RequireEnemyDefs(state, $"{pool.Name}.TouchTiles");

            var yOffset = RequiredLiteralByte(parameters[0], $"{pool.Name}.TouchTiles argument 1");
            var indexName = $"__{pool.Name}_touch_i";
            var projection = BuildPooledScreenProjection(pool.Name, indexName, pool.Name, "touch", state.ScreenWidth, state.ScreenHeight, margin: 0);
            var branches = state.Actors.EnemyDefs
                .Select(def => new KindBranch(def.Name, TouchTilesBlock(pool, indexName, def, yOffset, parameters[1], projection, state)))
                .ToList();
            var activeStatements = projection.Declarations
                .Append(PooledKindDispatch(pool.Name, indexName, branches, $"{pool.Name} actor dispatch requires at least one Enemies.Def declaration."))
                .ToList();
            var loop = PoolLoop(pool, indexName, ActiveGuard(pool.Name, indexName, activeStatements));

            return PooledCameraDeclarations(pool.Name, "touch", configuresCamera: true)
                .Append(loop)
                .ToList();
        }

        private static BlockSyntax TouchTilesBlock(
            ActorPool pool,
            string indexName,
            EnemyDef def,
            int yOffset,
            ExpressionSyntax flags,
            ActorScreenProjection projection,
            ActorFrameworkState state)
        {
            var collision = new IfElseSyntax(
                new BinaryExpressionSyntax(
                    IntrinsicCall(
                        state,
                        CameraScreenAabbTilesIntrinsic,
                        [
                            new ConstantSyntax("\"default\""),
                            projection.ScreenX,
                            OffsetExpression(projection.ScreenY, yOffset, subtract: false),
                            new ConstantSyntax(def.HitboxWidth.ToString(CultureInfo.InvariantCulture)),
                            new ConstantSyntax(def.HitboxHeight.ToString(CultureInfo.InvariantCulture)),
                            flags,
                        ]),
                    new ConstantSyntax("0"),
                    Operator.NotEqual),
                new BlockSyntax([
                    FieldAssignment(pool.Name, indexName, "state", "=", new ConstantSyntax((def.ContactDamage == 0 ? 1 : def.ContactDamage).ToString(CultureInfo.InvariantCulture))),
                ]),
                Maybe<BlockSyntax>.None);

            return new BlockSyntax([
                new IfElseSyntax(projection.Visible, new BlockSyntax([collision]), Maybe<BlockSyntax>.None),
            ]);
        }

        private static IReadOnlyList<StatementSyntax> PoolLandOnTilesStatements(ActorPool pool, QualifiedCallSyntax call, ActorFrameworkState state)
        {
            var parameters = RequireArguments(call, 3);
            RequireEnemyDefs(state, $"{pool.Name}.LandOnTiles");

            var searchTopOffset = RequiredLiteralByte(parameters[0], $"{pool.Name}.LandOnTiles argument 1");
            var searchHeight = RequiredLiteralByte(parameters[1], $"{pool.Name}.LandOnTiles argument 2");
            var indexName = $"__{pool.Name}_land_i";
            var projection = BuildPooledScreenProjection(pool.Name, indexName, pool.Name, "land", state.ScreenWidth, state.ScreenHeight, margin: 0);
            var branches = state.Actors.EnemyDefs
                .Select(def => new KindBranch(def.Name, LandOnTilesBlock(pool, indexName, def, searchTopOffset, searchHeight, parameters[2], projection, state)))
                .ToList();
            var activeStatements = projection.Declarations
                .Append(PooledKindDispatch(pool.Name, indexName, branches, $"{pool.Name} actor dispatch requires at least one Enemies.Def declaration."))
                .ToList();
            var loop = PoolLoop(pool, indexName, ActiveGuard(pool.Name, indexName, activeStatements));

            return PooledCameraDeclarations(pool.Name, "land", configuresCamera: true)
                .Append(loop)
                .ToList();
        }

        private static BlockSyntax LandOnTilesBlock(
            ActorPool pool,
            string indexName,
            EnemyDef def,
            int searchTopOffset,
            int searchHeight,
            ExpressionSyntax flags,
            ActorScreenProjection projection,
            ActorFrameworkState state)
        {
            var hitTop = $"__{pool.Name}_land_hit_{def.Name}";
            var hitTopDeclaration = new DeclarationSyntax(
                "u8",
                hitTop,
                Maybe<ExpressionSyntax>.None,
                Maybe.From<ExpressionSyntax>(IntrinsicCall(
                    state,
                    CameraScreenAabbHitTopIntrinsic,
                    [
                        new ConstantSyntax("\"default\""),
                        projection.ScreenX,
                        OffsetExpression(projection.ScreenY, searchTopOffset, subtract: true),
                        new ConstantSyntax(def.HitboxWidth.ToString(CultureInfo.InvariantCulture)),
                        new ConstantSyntax(searchHeight.ToString(CultureInfo.InvariantCulture)),
                        flags,
                    ])));
            var hitTopGuard = new IfElseSyntax(
                new BinaryExpressionSyntax(new IdentifierSyntax(hitTop), new ConstantSyntax("255"), Operator.NotEqual),
                new BlockSyntax([
                    FieldAssignment(pool.Name, indexName, "y", "=", new IdentifierSyntax(hitTop)),
                    FieldAssignment(pool.Name, indexName, "y", "+=", new IdentifierSyntax($"__{pool.Name}_land_camera_y_lo")),
                    FieldAssignment(pool.Name, indexName, "yHi", "=", new IdentifierSyntax($"__{pool.Name}_land_camera_y_hi")),
                    new IfElseSyntax(
                        new BinaryExpressionSyntax(PoolField(pool.Name, indexName, "y"), new IdentifierSyntax(hitTop), Operator.LessThan),
                        new BlockSyntax([FieldAssignment(pool.Name, indexName, "yHi", "+=", new ConstantSyntax("1"))]),
                        Maybe<BlockSyntax>.None),
                    FieldAssignment(pool.Name, indexName, "vy", "=", new ConstantSyntax("0")),
                    FieldAssignment(pool.Name, indexName, "state", "=", new ConstantSyntax("1")),
                ]),
                Maybe<BlockSyntax>.None);

            return new BlockSyntax([
                new IfElseSyntax(
                    projection.Visible,
                    new BlockSyntax([hitTopDeclaration, hitTopGuard]),
                    Maybe<BlockSyntax>.None),
            ]);
        }

        private static IReadOnlyList<StatementSyntax> PoolTouchPlayerStatements(ActorPool pool, QualifiedCallSyntax call, ActorFrameworkState state)
        {
            var parameters = RequireArguments(call, 4);
            RequireEnemyDefs(state, $"{pool.Name}.TouchPlayer");

            var playerX = RequiredLiteralByte(parameters[0], $"{pool.Name}.TouchPlayer argument 1");
            var playerY = RequiredLiteralByte(parameters[1], $"{pool.Name}.TouchPlayer argument 2");
            var playerWidth = RequiredLiteralByte(parameters[2], $"{pool.Name}.TouchPlayer argument 3");
            var playerHeight = RequiredLiteralByte(parameters[3], $"{pool.Name}.TouchPlayer argument 4");
            var playerRight = CheckedByte(playerX + playerWidth, $"{pool.Name}.TouchPlayer player right edge");
            var playerBottom = CheckedByte(playerY + playerHeight, $"{pool.Name}.TouchPlayer player bottom edge");

            var indexName = $"__{pool.Name}_player_i";
            var projection = BuildPooledScreenProjection(pool.Name, indexName, pool.Name, "player", state.ScreenWidth, state.ScreenHeight, margin: 0);
            var branches = state.Actors.EnemyDefs
                .Select(def => new KindBranch(def.Name, TouchPlayerBlock(pool, indexName, def, playerX, playerY, playerRight, playerBottom, projection)))
                .ToList();
            var activeStatements = projection.Declarations
                .Append(PooledKindDispatch(pool.Name, indexName, branches, $"{pool.Name} actor dispatch requires at least one Enemies.Def declaration."))
                .ToList();
            var loop = PoolLoop(pool, indexName, ActiveGuard(pool.Name, indexName, activeStatements));

            return PooledCameraDeclarations(pool.Name, "player", configuresCamera: true)
                .Append(loop)
                .ToList();
        }

        private static BlockSyntax TouchPlayerBlock(
            ActorPool pool,
            string indexName,
            EnemyDef def,
            int playerX,
            int playerY,
            int playerRight,
            int playerBottom,
            ActorScreenProjection projection)
        {
            var overlapsX = And(
                new BinaryExpressionSyntax(projection.ScreenX, Constant(playerRight), Operator.LessThan),
                ActorRightOverlapsPlayerLeft(projection.ScreenX, def.HitboxWidth, playerX));
            var overlapsY = And(
                new BinaryExpressionSyntax(projection.ScreenY, Constant(playerBottom), Operator.LessThan),
                new BinaryExpressionSyntax(OffsetExpression(projection.ScreenY, def.HitboxHeight, subtract: false), Constant(playerY), Operator.Get(">")));

            var touchPlayer = new IfElseSyntax(
                And(overlapsX, overlapsY),
                new BlockSyntax([
                    FieldAssignment(pool.Name, indexName, "state", "=", new ConstantSyntax((def.ContactDamage == 0 ? 1 : def.ContactDamage).ToString(CultureInfo.InvariantCulture))),
                ]),
                Maybe<BlockSyntax>.None);

            return new BlockSyntax([
                new IfElseSyntax(projection.Visible, new BlockSyntax([touchPlayer]), Maybe<BlockSyntax>.None),
            ]);
        }

        private static ExpressionSyntax ActorRightOverlapsPlayerLeft(ExpressionSyntax actorScreenX, int actorWidth, int playerX)
        {
            if (actorWidth == 0)
            {
                return new BinaryExpressionSyntax(actorScreenX, Constant(playerX), Operator.Get(">"));
            }

            return Or(
                new BinaryExpressionSyntax(actorScreenX, Constant(playerX), Operator.GreaterThanOrEqual),
                new BinaryExpressionSyntax(
                    new BinaryExpressionSyntax(Constant(playerX), actorScreenX, Operator.Get("-")),
                    Constant(actorWidth),
                    Operator.LessThan));
        }

        private static IReadOnlyList<StatementSyntax> RuntimeSpawnActivationStatements(ActorSpawnLayer layer, string prefix, int screenWidth)
        {
            var windowLeft = layer.WindowLeft ?? 0;
            var windowWidth = layer.WindowWidth ?? screenWidth;
            var statements = SpawnRecycleStatements(layer, prefix, windowLeft, windowWidth).ToList();

            if (layer.Spawns.Count != 0)
            {
                statements.AddRange(SpawnActivationStatements(layer, prefix, windowLeft, windowWidth));
            }

            return statements;
        }

        private static IReadOnlyList<StatementSyntax> SpawnRecycleStatements(ActorSpawnLayer layer, string prefix, int windowLeft, int windowWidth)
        {
            var indexName = $"{prefix}_recycle_i";
            var projectionPrefix = $"{prefix}_recycle";
            var projection = BuildSpawnScreenProjection(
                PoolField(layer.PoolName, indexName, "x"),
                PoolField(layer.PoolName, indexName, "xHi"),
                projectionPrefix,
                windowLeft,
                windowWidth);

            var loop = PoolLoop(
                new ActorPool(layer.PoolName, 0),
                indexName,
                new IfElseSyntax(
                    new BinaryExpressionSyntax(PoolField(layer.PoolName, indexName, "active"), new ConstantSyntax("0"), Operator.NotEqual),
                    new BlockSyntax(projection.Declarations.Concat([
                        new IfElseSyntax(
                            new UnaryExpressionSyntax("!", projection.Visible),
                            new BlockSyntax([FieldAssignment(layer.PoolName, indexName, "active", "=", new ConstantSyntax("0"))]),
                            Maybe<BlockSyntax>.None),
                    ]).ToList()),
                    Maybe<BlockSyntax>.None));

            return SpawnCameraXDeclarations(projectionPrefix, windowLeft)
                .Append(loop)
                .ToList();
        }

        private static IReadOnlyList<StatementSyntax> SpawnActivationStatements(ActorSpawnLayer layer, string prefix, int windowLeft, int windowWidth)
        {
            var indexName = $"{prefix}_i";
            var used = new IndexExpressionSyntax($"{layer.RuntimeName}_used", new IdentifierSyntax(indexName));
            var declarations = SpawnValueDeclarations(layer, prefix, indexName).ToList();
            var projection = BuildSpawnScreenProjection(
                new IdentifierSyntax($"{prefix}_x_value"),
                new IdentifierSyntax($"{prefix}_xHi_value"),
                prefix,
                windowLeft,
                windowWidth);
            declarations.AddRange(projection.Declarations);

            var assignedName = $"{prefix}_assigned";
            var slotName = $"{prefix}_slot";
            var assignSlot = new IfElseSyntax(
                new BinaryExpressionSyntax(new IdentifierSyntax(assignedName), new ConstantSyntax("0"), Operator.Equal),
                new BlockSyntax([
                    new IfElseSyntax(
                        new BinaryExpressionSyntax(PoolField(layer.PoolName, slotName, "active"), new ConstantSyntax("0"), Operator.Equal),
                        new BlockSyntax(SpawnSlotAssignments(layer, prefix, indexName, slotName, assignedName).ToList()),
                        Maybe<BlockSyntax>.None),
                ]),
                Maybe<BlockSyntax>.None);

            declarations.Add(new IfElseSyntax(
                projection.Visible,
                new BlockSyntax([
                    new DeclarationSyntax("u8", assignedName, Maybe<ExpressionSyntax>.None, Maybe.From<ExpressionSyntax>(new ConstantSyntax("0"))),
                    PoolLoop(new ActorPool(layer.PoolName, 0), slotName, assignSlot),
                ]),
                Maybe<BlockSyntax>.None));

            var loop = new ForSyntax(
                Maybe.From<StatementSyntax>(new DeclarationSyntax("u8", indexName, Maybe<ExpressionSyntax>.None, Maybe.From<ExpressionSyntax>(new ConstantSyntax("0")))),
                Maybe.From<ExpressionSyntax>(new BinaryExpressionSyntax(
                    new IdentifierSyntax(indexName),
                    new ConstantSyntax(layer.Spawns.Count.ToString(CultureInfo.InvariantCulture)),
                    Operator.LessThan)),
                Maybe.From<ExpressionSyntax>(new AssignmentSyntax(new IdentifierLValue(indexName), "+=", new ConstantSyntax("1"))),
                new BlockSyntax([
                    new IfElseSyntax(
                        new BinaryExpressionSyntax(used, new ConstantSyntax("0"), Operator.Equal),
                        new BlockSyntax(declarations),
                        Maybe<BlockSyntax>.None),
                ]));

            return SpawnCameraXDeclarations(prefix, windowLeft)
                .Append(loop)
                .ToList();
        }

        private static IEnumerable<StatementSyntax> SpawnValueDeclarations(ActorSpawnLayer layer, string prefix, string indexName)
        {
            yield return SpawnValueDeclaration(layer, prefix, indexName, "kind");
            yield return SpawnValueDeclaration(layer, prefix, indexName, "x");
            yield return SpawnValueDeclaration(layer, prefix, indexName, "xHi");
            yield return SpawnValueDeclaration(layer, prefix, indexName, "y");
            yield return SpawnValueDeclaration(layer, prefix, indexName, "yHi");
            foreach (var fieldName in SpawnInitialFieldNames)
            {
                yield return SpawnValueDeclaration(layer, prefix, indexName, fieldName);
            }
        }

        private static DeclarationSyntax SpawnValueDeclaration(ActorSpawnLayer layer, string prefix, string indexName, string fieldName)
        {
            return new DeclarationSyntax(
                "u8",
                $"{prefix}_{fieldName}_value",
                Maybe<ExpressionSyntax>.None,
                Maybe.From<ExpressionSyntax>(new FunctionCall($"{layer.RuntimeName}_{fieldName}", [new IdentifierSyntax(indexName)])));
        }

        private static IEnumerable<StatementSyntax> SpawnSlotAssignments(
            ActorSpawnLayer layer,
            string prefix,
            string indexName,
            string slotName,
            string assignedName)
        {
            yield return FieldAssignment(layer.PoolName, slotName, "kind", "=", new IdentifierSyntax($"{prefix}_kind_value"));
            yield return FieldAssignment(layer.PoolName, slotName, "x", "=", new IdentifierSyntax($"{prefix}_x_value"));
            yield return FieldAssignment(layer.PoolName, slotName, "xHi", "=", new IdentifierSyntax($"{prefix}_xHi_value"));
            yield return FieldAssignment(layer.PoolName, slotName, "y", "=", new IdentifierSyntax($"{prefix}_y_value"));
            yield return FieldAssignment(layer.PoolName, slotName, "yHi", "=", new IdentifierSyntax($"{prefix}_yHi_value"));
            yield return FieldAssignment(layer.PoolName, slotName, "vx", "=", new IdentifierSyntax($"{prefix}_vx_value"));
            yield return FieldAssignment(layer.PoolName, slotName, "vy", "=", new IdentifierSyntax($"{prefix}_vy_value"));
            yield return FieldAssignment(layer.PoolName, slotName, "state", "=", new IdentifierSyntax($"{prefix}_state_value"));
            yield return FieldAssignment(layer.PoolName, slotName, "timer", "=", new IdentifierSyntax($"{prefix}_timer_value"));
            yield return FieldAssignment(layer.PoolName, slotName, "facing", "=", new IdentifierSyntax($"{prefix}_facing_value"));
            yield return FieldAssignment(layer.PoolName, slotName, "animTick", "=", new IdentifierSyntax($"{prefix}_animTick_value"));
            yield return FieldAssignment(layer.PoolName, slotName, "health", "=", new IdentifierSyntax($"{prefix}_health_value"));
            yield return FieldAssignment(layer.PoolName, slotName, "active", "=", new IdentifierSyntax($"{prefix}_active_value"));
            yield return new ExpressionStatementSyntax(new AssignmentSyntax(
                new IndexLValue($"{layer.RuntimeName}_used", new IdentifierSyntax(indexName)),
                "=",
                new ConstantSyntax("1")));
            yield return new ExpressionStatementSyntax(new AssignmentSyntax(
                new IdentifierLValue(assignedName),
                "=",
                new ConstantSyntax("1")));
        }

        private static ActorScreenProjection BuildSpawnScreenProjection(
            ExpressionSyntax worldXLow,
            ExpressionSyntax worldXHigh,
            string prefix,
            int windowLeft,
            int windowWidth)
        {
            var cameraXLow = $"{prefix}_camera_x_lo";
            var cameraXHigh = $"{prefix}_camera_x_hi";
            var declarations = new List<StatementSyntax>();

            var screenX = $"{prefix}_screen_x";
            declarations.Add(new DeclarationSyntax(
                "u8",
                screenX,
                Maybe<ExpressionSyntax>.None,
                Maybe.From<ExpressionSyntax>(new BinaryExpressionSyntax(
                    worldXLow,
                    new IdentifierSyntax(cameraXLow),
                    Operator.Get("-")))));

            var sameCameraPage = And(
                new BinaryExpressionSyntax(worldXHigh, new IdentifierSyntax(cameraXHigh), Operator.Equal),
                new BinaryExpressionSyntax(worldXLow, new IdentifierSyntax(cameraXLow), Operator.Get(">=")));
            var nextCameraPage = And(
                new BinaryExpressionSyntax(
                    worldXHigh,
                    new BinaryExpressionSyntax(new IdentifierSyntax(cameraXHigh), new ConstantSyntax("1"), Operator.Get("+")),
                    Operator.Equal),
                new BinaryExpressionSyntax(worldXLow, new IdentifierSyntax(cameraXLow), Operator.LessThan));
            ExpressionSyntax visible = Or(sameCameraPage, nextCameraPage);
            if (windowWidth < 256)
            {
                visible = And(
                    visible,
                    new BinaryExpressionSyntax(new IdentifierSyntax(screenX), new ConstantSyntax(windowWidth.ToString(CultureInfo.InvariantCulture)), Operator.LessThan));
            }

            return new ActorScreenProjection(declarations, new IdentifierSyntax(screenX), new IdentifierSyntax(screenX), visible);
        }

        private static IReadOnlyList<StatementSyntax> SpawnCameraXDeclarations(string prefix, int windowLeft)
        {
            var cameraXLow = $"{prefix}_camera_x_lo";
            var cameraXHigh = $"{prefix}_camera_x_hi";

            if (windowLeft == 0)
            {
                return
                [
                    new DeclarationSyntax(
                        "u8",
                        cameraXLow,
                        Maybe<ExpressionSyntax>.None,
                        Maybe.From<ExpressionSyntax>(new FunctionCall(ActorCameraXLowFunction, []))),
                    new DeclarationSyntax(
                        "u8",
                        cameraXHigh,
                        Maybe<ExpressionSyntax>.None,
                        Maybe.From<ExpressionSyntax>(new FunctionCall(ActorCameraXHighFunction, []))),
                ];
            }

            var baseCameraXLow = $"{prefix}_camera_base_x_lo";
            var baseCameraXHigh = $"{prefix}_camera_base_x_hi";
            return
            [
                new DeclarationSyntax(
                    "u8",
                    baseCameraXLow,
                    Maybe<ExpressionSyntax>.None,
                    Maybe.From<ExpressionSyntax>(new FunctionCall(ActorCameraXLowFunction, []))),
                new DeclarationSyntax(
                    "u8",
                    baseCameraXHigh,
                    Maybe<ExpressionSyntax>.None,
                    Maybe.From<ExpressionSyntax>(new FunctionCall(ActorCameraXHighFunction, []))),
                new DeclarationSyntax(
                    "u8",
                    cameraXLow,
                    Maybe<ExpressionSyntax>.None,
                    Maybe.From<ExpressionSyntax>(new BinaryExpressionSyntax(
                        new IdentifierSyntax(baseCameraXLow),
                        new ConstantSyntax(windowLeft.ToString(CultureInfo.InvariantCulture)),
                        Operator.Get("+")))),
                new DeclarationSyntax(
                    "u8",
                    cameraXHigh,
                    Maybe<ExpressionSyntax>.None,
                    Maybe.From<ExpressionSyntax>(new IdentifierSyntax(baseCameraXHigh))),
                new IfElseSyntax(
                    new BinaryExpressionSyntax(new IdentifierSyntax(cameraXLow), new IdentifierSyntax(baseCameraXLow), Operator.LessThan),
                    new BlockSyntax([
                        new ExpressionStatementSyntax(new AssignmentSyntax(new IdentifierLValue(cameraXHigh), "+=", new ConstantSyntax("1"))),
                    ]),
                    Maybe<BlockSyntax>.None),
            ];
        }

        private static int CheckedByte(int value, string context)
        {
            if (value is < 0 or > 255)
            {
                throw new InvalidOperationException($"{context} must be between 0 and 255.");
            }

            return value;
        }

        private static ExpressionSyntax OffsetPoolField(string poolName, string indexName, string fieldName, int offset, bool subtract)
        {
            if (offset == 0)
            {
                return PoolField(poolName, indexName, fieldName);
            }

            return new BinaryExpressionSyntax(
                PoolField(poolName, indexName, fieldName),
                new ConstantSyntax(offset.ToString(CultureInfo.InvariantCulture)),
                Operator.Get(subtract ? "-" : "+"));
        }

        public static void RequireEnemyDefs(ActorFrameworkState state, string callName)
        {
            if (state.Actors.EnemyDefs.Count == 0)
            {
                throw new InvalidOperationException($"{callName} requires at least one Enemies.Def declaration.");
            }
        }

        private static ForSyntax PoolLoop(ActorPool pool, string indexName, StatementSyntax bodyStatement)
        {
            return PoolLoop(pool, indexName, [bodyStatement]);
        }

        private static ForSyntax PoolLoop(ActorPool pool, string indexName, IReadOnlyList<StatementSyntax> bodyStatements)
        {
            return new ForSyntax(
                Maybe.From<StatementSyntax>(new DeclarationSyntax("u8", indexName, Maybe<ExpressionSyntax>.None, Maybe.From<ExpressionSyntax>(new ConstantSyntax("0")))),
                Maybe.From<ExpressionSyntax>(new BinaryExpressionSyntax(new IdentifierSyntax(indexName), new CountOfSyntax(pool.Name), Operator.LessThan)),
                Maybe.From<ExpressionSyntax>(new AssignmentSyntax(new IdentifierLValue(indexName), "+=", new ConstantSyntax("1"))),
                new BlockSyntax(bodyStatements.ToList()));
        }

        private static IfElseSyntax ActiveGuard(string poolName, string indexName, StatementSyntax bodyStatement)
        {
            return ActiveGuard(poolName, indexName, [bodyStatement]);
        }

        private static IfElseSyntax ActiveGuard(string poolName, string indexName, IReadOnlyList<StatementSyntax> bodyStatements)
        {
            return new IfElseSyntax(
                new BinaryExpressionSyntax(PoolField(poolName, indexName, "active"), new ConstantSyntax("0"), Operator.NotEqual),
                new BlockSyntax(bodyStatements.ToList()),
                Maybe<BlockSyntax>.None);
        }

        private static (int Low, int High) SplitWorldX(int value, string context)
        {
            if (value is < 0 or > 65535)
            {
                throw new InvalidOperationException($"{context} must be between 0 and 65535.");
            }

            return (value & 0xFF, value >> 8);
        }

        private static (int Low, int High) SplitWorldY(int value, string context) => SplitWorldX(value, context);

        public static ExpressionSyntax RewriteExpressionCall(ActorFrameworkCall call, ActorFrameworkState state)
        {
            if (call.Role == ActorFrameworkRole.ActorEnemyDef)
            {
                throw new InvalidOperationException("Enemies.Def can only be used as a statement.");
            }

            if (!EnemyLookupFunctions.TryGetValue(call.Role, out var lookup))
            {
                throw new InvalidOperationException($"{call.DisplayName} can only be used as a statement.");
            }

            if (state.Actors.EnemyDefs.Count == 0)
            {
                throw new InvalidOperationException($"{lookup.DisplayName} requires at least one Enemies.Def declaration.");
            }

            state.Actors.RecordEnemyLookupMethod(call.Role);
            return new FunctionCall(lookup.FunctionName, call.Parameters.Select(parameter => RewriteExpression(parameter, state)));
        }

        private static IEnumerable<string> SpawnLookupFieldNames()
        {
            yield return "kind";
            yield return "x";
            yield return "xHi";
            yield return "y";
            yield return "yHi";
            foreach (var fieldName in SpawnInitialFieldNames)
            {
                yield return fieldName;
            }
        }

    }
}
