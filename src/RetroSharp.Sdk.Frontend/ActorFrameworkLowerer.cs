using System.Globalization;
using System.Text.Json;
using CSharpFunctionalExtensions;
using RetroSharp.Core;
using RetroSharp.Core.Sdk.Tiled;
using RetroSharp.Core.Targeting;
using RetroSharp.Parser;

namespace RetroSharp.Sdk;

public static class ActorFrameworkLowerer
{
    private const string ActorStructName = "Actor";
    private const string ActorCameraXLowFunction = "__rs_actor_camera_x_lo";
    private const string ActorCameraXHighFunction = "__rs_actor_camera_x_hi";

    private static readonly IReadOnlyDictionary<string, int> BehaviorIds = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["Walker"] = 1,
        ["Flyer"] = 2,
        ["Patrol"] = 3,
        ["Shooter"] = 4,
        ["Chaser"] = 5,
        ["Hazard"] = 6,
    };

    private static readonly IReadOnlyDictionary<string, string> EnemyLookupFunctions = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Behavior"] = "enemy_behavior",
        ["Speed"] = "enemy_speed",
        ["Hp"] = "enemy_hp",
        ["Cooldown"] = "enemy_cooldown",
        ["ContactDamage"] = "enemy_contact_damage",
        ["HitboxWidth"] = "enemy_hitbox_width",
        ["HitboxHeight"] = "enemy_hitbox_height",
    };

    public static ProgramSyntax Lower(ProgramSyntax program, Target2DCapabilities capabilities, bool supportsUpdate, bool supportsDraw, string? baseDirectory = null)
    {
        var state = new ActorFrameworkState(capabilities, supportsUpdate, supportsDraw, baseDirectory);
        foreach (var function in program.Functions)
        {
            CollectDirectives(function.Block, state);
        }

        if (!state.HasDirectives)
        {
            return program;
        }

        state.ValidateSpawnLayers();

        var structs = program.Structs.ToList();
        if (state.Pools.Count != 0)
        {
            if (structs.Any(structSyntax => structSyntax.Name == ActorStructName))
            {
                throw new InvalidOperationException("actor.Pool cannot generate framework struct 'Actor' because a struct named 'Actor' is already declared.");
            }

            structs.Add(ActorStruct());
        }

        var functions = program.Functions
            .Select(function => new FunctionSyntax(
                function.Type,
                function.Name,
                function.Parameters,
                RewriteBlock(function.Block, state),
                function.IsExpressionBodied,
                function.IsInline,
                function.IsPure,
                function.IsExtern,
                function.Attributes))
            .Concat(GeneratedLookupFunctions(state.EnemyDefs))
            .ToList();

        return new ProgramSyntax(
            program.TypeAliases,
            program.Constants.Concat(GeneratedConstants(state.EnemyDefs)).ToList(),
            program.Enums,
            structs,
            functions);
    }

    private static void CollectDirectives(BlockSyntax block, ActorFrameworkState state)
    {
        foreach (var statement in block.Statements)
        {
            switch (statement)
            {
                case ExpressionStatementSyntax { Expression: SdkDotCallSyntax { Module: "actor", Method: "Pool" } poolCall }:
                    state.AddPool(ReadPool(poolCall));
                    break;
                case ExpressionStatementSyntax { Expression: SdkDotCallSyntax { Module: "enemy", Method: "Def" } defCall }:
                    state.AddEnemyDef(ReadEnemyDef(defCall));
                    break;
                case ExpressionStatementSyntax { Expression: SdkDotCallSyntax { Module: "actor", Method: "SpawnLayer" or "SpawnWindow" } spawnCall }:
                    state.AddSpawnLayer(ReadSpawnDirective(spawnCall, state.BaseDirectory));
                    break;
                case IfElseSyntax ifElse:
                    CollectDirectives(ifElse.ThenBlock, state);
                    if (ifElse.ElseBlock.HasValue)
                    {
                        CollectDirectives(ifElse.ElseBlock.Value, state);
                    }
                    break;
                case WhileSyntax whileSyntax:
                    CollectDirectives(whileSyntax.Body, state);
                    break;
                case DoWhileSyntax doWhileSyntax:
                    CollectDirectives(doWhileSyntax.Body, state);
                    break;
                case LoopSyntax loopSyntax:
                    CollectDirectives(loopSyntax.Body, state);
                    break;
                case RangeForSyntax rangeForSyntax:
                    CollectDirectives(rangeForSyntax.Body, state);
                    break;
                case ForSyntax forSyntax:
                    CollectDirectives(forSyntax.Body, state);
                    break;
                case SwitchSyntax switchSyntax:
                    foreach (var switchCase in switchSyntax.Cases)
                    {
                        CollectDirectives(switchCase.Block, state);
                    }

                    if (switchSyntax.DefaultBlock.HasValue)
                    {
                        CollectDirectives(switchSyntax.DefaultBlock.Value, state);
                    }
                    break;
            }
        }
    }

    private static ActorPool ReadPool(SdkDotCallSyntax call)
    {
        var parameters = call.Parameters.ToList();
        if (parameters.Count != 2 || parameters[0] is not IdentifierSyntax poolName)
        {
            throw new InvalidOperationException("actor.Pool expects a pool identifier and a literal capacity.");
        }

        var name = poolName.Identifier;
        if (!TryLiteralByte(parameters[1], out var capacity) || capacity == 0)
        {
            throw new InvalidOperationException($"actor.Pool for '{name}' requires a literal capacity from 1 to 255.");
        }

        return new ActorPool(name, capacity);
    }

    private static ActorSpawnLayer ReadSpawnDirective(SdkDotCallSyntax call, string baseDirectory)
    {
        var parameters = call.Parameters.ToList();
        if (call.Method == "SpawnLayer" && (parameters.Count != 3 || parameters[0] is not IdentifierSyntax))
        {
            throw new InvalidOperationException("actor.SpawnLayer expects a pool identifier, a map path string, and a layer name string.");
        }

        if (call.Method == "SpawnWindow" && (parameters.Count != 5 || parameters[0] is not IdentifierSyntax))
        {
            throw new InvalidOperationException("actor.SpawnWindow expects a pool identifier, a map path string, a layer name string, a literal left edge, and a literal width.");
        }

        var poolName = (IdentifierSyntax)parameters[0];
        var mapPath = StringLiteral(parameters[1], "actor.SpawnLayer argument 2");
        var layerName = StringLiteral(parameters[2], "actor.SpawnLayer argument 3");
        var fullPath = Path.GetFullPath(Path.Combine(baseDirectory, mapPath));
        var map = LogicalTiledMapImporter.Load(fullPath);
        if (!map.ActorSpawnLayers.TryGetValue(layerName, out var spawns))
        {
            throw new InvalidOperationException($"actor.SpawnLayer could not find object layer '{layerName}' in Tiled map '{Path.GetFileName(mapPath)}'.");
        }

        int? windowLeft = null;
        int? windowWidth = null;
        if (call.Method == "SpawnWindow")
        {
            windowLeft = RequiredLiteralByte(parameters[3], "actor.SpawnWindow argument 4");
            windowWidth = RequiredLiteralByte(parameters[4], "actor.SpawnWindow argument 5");
            spawns = spawns
                .Where(spawn => spawn.X >= windowLeft.Value && spawn.X < windowLeft.Value + windowWidth.Value)
                .ToList();
        }

        return new ActorSpawnLayer(call.Method, poolName.Identifier, mapPath, layerName, windowLeft, windowWidth, spawns);
    }

    private static EnemyDef ReadEnemyDef(SdkDotCallSyntax call)
    {
        var parameters = call.Parameters.ToList();
        if (parameters.Count == 0 || parameters[0] is not IdentifierSyntax enemyName)
        {
            throw new InvalidOperationException("enemy.Def expects an enemy identifier as its first argument.");
        }

        var namedArguments = new Dictionary<string, ExpressionSyntax>(StringComparer.Ordinal);
        foreach (var parameter in parameters.Skip(1))
        {
            if (parameter is not NamedArgumentSyntax namedArgument)
            {
                throw new InvalidOperationException($"enemy.Def for '{enemyName.Identifier}' expects named arguments after the enemy identifier.");
            }

            if (!namedArguments.TryAdd(namedArgument.Name, namedArgument.Expression))
            {
                throw new InvalidOperationException($"enemy.Def for '{enemyName.Identifier}' supplies '{namedArgument.Name}' more than once.");
            }
        }

        var sprite = OptionalIdentifier(namedArguments, "sprite", enemyName.Identifier);
        var behavior = RequiredIdentifier(namedArguments, "behavior", enemyName.Identifier);
        if (!BehaviorIds.ContainsKey(behavior))
        {
            throw new InvalidOperationException($"Unknown actor behavior '{behavior}'.");
        }

        var animation = OptionalIdentifier(namedArguments, "animation", enemyName.Identifier);
        var speed = OptionalLiteralByte(namedArguments, "speed", 0, enemyName.Identifier);
        var hp = OptionalLiteralByte(namedArguments, "hp", 1, enemyName.Identifier);
        var cooldown = OptionalLiteralByte(namedArguments, "cooldown", 0, enemyName.Identifier);
        var contactDamage = OptionalLiteralByte(namedArguments, "contactDamage", 0, enemyName.Identifier);
        var hitboxWidth = OptionalLiteralByte(namedArguments, "hitboxWidth", 0, enemyName.Identifier);
        var hitboxHeight = OptionalLiteralByte(namedArguments, "hitboxHeight", 0, enemyName.Identifier);

        foreach (var name in namedArguments.Keys)
        {
            if (name is not "sprite" and not "behavior" and not "animation" and not "speed" and not "hp" and not "cooldown" and not "contactDamage" and not "hitboxWidth" and not "hitboxHeight")
            {
                throw new InvalidOperationException($"enemy.Def for '{enemyName.Identifier}' has unsupported property '{name}'.");
            }
        }

        return new EnemyDef(enemyName.Identifier, sprite, behavior, animation, speed, hp, cooldown, contactDamage, hitboxWidth, hitboxHeight);
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

        throw new InvalidOperationException($"enemy.Def for '{enemyName}' requires '{name}' to be an identifier.");
    }

    private static string RequiredIdentifier(
        IReadOnlyDictionary<string, ExpressionSyntax> arguments,
        string name,
        string enemyName)
    {
        if (!arguments.TryGetValue(name, out var expression))
        {
            throw new InvalidOperationException($"enemy.Def for '{enemyName}' requires '{name}'.");
        }

        if (expression is IdentifierSyntax identifier)
        {
            return identifier.Identifier;
        }

        throw new InvalidOperationException($"enemy.Def for '{enemyName}' requires '{name}' to be an identifier.");
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

        throw new InvalidOperationException($"enemy.Def for '{enemyName}' requires '{name}' to be a literal byte value.");
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

    private static string StringLiteral(ExpressionSyntax expression, string context)
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
        if (statement is ExpressionStatementSyntax { Expression: SdkDotCallSyntax { Module: "actor", Method: "SpawnLayer" or "SpawnWindow" } spawnCall })
        {
            return SpawnAssignments(state.SpawnLayer(spawnCall));
        }

        var rewritten = RewriteStatement(statement, state);
        return rewritten is null ? [] : [rewritten];
    }

    private static StatementSyntax? RewriteStatement(StatementSyntax statement, ActorFrameworkState state)
    {
        return statement switch
        {
            ExpressionStatementSyntax { Expression: SdkDotCallSyntax { Module: "actor", Method: "Pool" } poolCall } =>
                PoolDeclaration(state.Pool(poolCall)),
            ExpressionStatementSyntax { Expression: SdkDotCallSyntax { Module: "enemy", Method: "Def" } } => null,
            ExpressionStatementSyntax { Expression: SdkDotCallSyntax poolCall } when state.TryPool(poolCall.Module, out var pool) && poolCall.Method == "Update" =>
                PoolUpdateLoop(pool, poolCall, state),
            ExpressionStatementSyntax { Expression: SdkDotCallSyntax poolCall } when state.TryPool(poolCall.Module, out var pool) && poolCall.Method == "Draw" =>
                PoolDrawLoop(pool, poolCall, state),
            ExpressionStatementSyntax { Expression: SdkDotCallSyntax poolCall } when state.TryPool(poolCall.Module, out var pool) && poolCall.Method == "TouchTiles" =>
                PoolTouchTilesLoop(pool, poolCall, state),
            ExpressionStatementSyntax { Expression: SdkDotCallSyntax poolCall } when state.TryPool(poolCall.Module, out var pool) && poolCall.Method == "LandOnTiles" =>
                PoolLandOnTilesLoop(pool, poolCall, state),
            ExpressionStatementSyntax { Expression: SdkDotCallSyntax poolCall } when state.TryPool(poolCall.Module, out var pool) && poolCall.Method == "TouchPlayer" =>
                PoolTouchPlayerLoop(pool, poolCall, state),
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
            LoopSyntax loopSyntax => new LoopSyntax(RewriteBlock(loopSyntax.Body, state)),
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

    private static DeclarationSyntax PoolDeclaration(ActorPool pool)
    {
        return new DeclarationSyntax(
            ActorStructName,
            pool.Name,
            Maybe.From<ExpressionSyntax>(new ConstantSyntax(pool.Capacity.ToString(CultureInfo.InvariantCulture))),
            Maybe<ExpressionSyntax>.None);
    }

    private static ForSyntax PoolUpdateLoop(ActorPool pool, SdkDotCallSyntax call, ActorFrameworkState state)
    {
        RequireNoArguments(call);
        if (!state.SupportsUpdate)
        {
            throw new InvalidOperationException($"Target '{state.TargetName}' does not support actor Update yet.");
        }

        RequireEnemyDefs(state, $"{pool.Name}.Update");

        var indexName = $"__{pool.Name}_update_i";
        var branches = state.EnemyDefs
            .Select(def => new KindBranch(
                def.Name,
                new BlockSyntax(UpdateStatementsFor(pool, indexName, def).ToList())))
            .ToList();

        return PoolLoop(pool, indexName, ActiveGuard(pool.Name, indexName, KindDispatch(pool.Name, indexName, branches)));
    }

    private static IReadOnlyList<StatementSyntax> UpdateStatementsFor(ActorPool pool, string indexName, EnemyDef def)
    {
        IReadOnlyList<StatementSyntax> statements = def.Behavior switch
        {
            "Walker" =>
                AddWorldX(pool, indexName, new IdentifierSyntax($"{def.Name}Speed")),
            "Flyer" =>
            [
                FieldAssignment(pool.Name, indexName, "y", "+=", new IdentifierSyntax($"{def.Name}Speed")),
            ],
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

    private static ForSyntax PoolDrawLoop(ActorPool pool, SdkDotCallSyntax call, ActorFrameworkState state)
    {
        RequireNoArguments(call);
        if (!state.SupportsDraw)
        {
            throw new InvalidOperationException($"Target '{state.TargetName}' does not support actor Draw yet.");
        }

        RequireEnemyDefs(state, $"{pool.Name}.Draw");
        var missingSprite = state.EnemyDefs.FirstOrDefault(def => def.Sprite is null);
        if (missingSprite is not null)
        {
            throw new InvalidOperationException($"{pool.Name}.Draw requires enemy.Def for '{missingSprite.Name}' to declare a sprite identifier.");
        }

        var indexName = $"__{pool.Name}_draw_i";
        var branches = state.EnemyDefs
            .Select(def => new KindBranch(def.Name, ActorDrawBlock(pool, indexName, def, state.ScreenWidth)))
            .ToList();

        return PoolLoop(pool, indexName, ActiveGuard(pool.Name, indexName, KindDispatch(pool.Name, indexName, branches)));
    }

    private static BlockSyntax ActorDrawBlock(ActorPool pool, string indexName, EnemyDef def, int screenWidth)
    {
        var statements = new List<StatementSyntax>();
        var projection = BuildActorScreenProjection(pool, indexName, def, "draw", screenWidth);
        statements.AddRange(projection.Declarations);

        ExpressionSyntax frame = new ConstantSyntax("0");
        if (def.Animation is not null)
        {
            var frameVariable = $"__{pool.Name}_draw_frame_{def.Name}";
            statements.Add(new DeclarationSyntax(
                "u8",
                frameVariable,
                Maybe<ExpressionSyntax>.None,
                Maybe.From<ExpressionSyntax>(new SdkDotCallSyntax(
                    "animation",
                    "Frame",
                    [
                        new IdentifierSyntax(def.Animation),
                        PoolField(pool.Name, indexName, "animTick"),
                    ]))));
            frame = new IdentifierSyntax(frameVariable);
        }

        statements.Add(new IfElseSyntax(
            projection.Visible,
            new BlockSyntax([
                new ExpressionStatementSyntax(new SdkDotCallSyntax(
                    "sprite",
                    "Draw",
                    [
                        new IdentifierSyntax(def.Sprite!),
                        projection.ScreenX,
                        PoolField(pool.Name, indexName, "y"),
                        frame,
                        new IdentifierSyntax("false"),
                        new ConstantSyntax("0"),
                    ])),
            ]),
            Maybe<BlockSyntax>.None));

        return new BlockSyntax(statements);
    }

    private static ForSyntax PoolTouchTilesLoop(ActorPool pool, SdkDotCallSyntax call, ActorFrameworkState state)
    {
        var parameters = RequireArguments(call, 2);
        RequireEnemyDefs(state, $"{pool.Name}.TouchTiles");

        var yOffset = RequiredLiteralByte(parameters[0], $"{pool.Name}.TouchTiles argument 1");
        var indexName = $"__{pool.Name}_touch_i";
        var branches = state.EnemyDefs
            .Select(def => new KindBranch(def.Name, TouchTilesBlock(pool, indexName, def, yOffset, parameters[1], state.ScreenWidth)))
            .ToList();

        return PoolLoop(pool, indexName, ActiveGuard(pool.Name, indexName, KindDispatch(pool.Name, indexName, branches)));
    }

    private static BlockSyntax TouchTilesBlock(
        ActorPool pool,
        string indexName,
        EnemyDef def,
        int yOffset,
        ExpressionSyntax flags,
        int screenWidth)
    {
        var projection = BuildActorScreenProjection(pool, indexName, def, "touch", screenWidth);
        var collision = new IfElseSyntax(
            new BinaryExpressionSyntax(
                new SdkDotCallSyntax(
                    "camera",
                    "AabbTiles",
                    [
                        projection.ScreenX,
                        OffsetPoolField(pool.Name, indexName, "y", yOffset, subtract: false),
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

        return new BlockSyntax(projection.Declarations.Concat([
            new IfElseSyntax(projection.Visible, new BlockSyntax([collision]), Maybe<BlockSyntax>.None),
        ]).ToList());
    }

    private static ForSyntax PoolLandOnTilesLoop(ActorPool pool, SdkDotCallSyntax call, ActorFrameworkState state)
    {
        var parameters = RequireArguments(call, 3);
        RequireEnemyDefs(state, $"{pool.Name}.LandOnTiles");

        var searchTopOffset = RequiredLiteralByte(parameters[0], $"{pool.Name}.LandOnTiles argument 1");
        var searchHeight = RequiredLiteralByte(parameters[1], $"{pool.Name}.LandOnTiles argument 2");
        var indexName = $"__{pool.Name}_land_i";
        var branches = state.EnemyDefs
            .Select(def => new KindBranch(def.Name, LandOnTilesBlock(pool, indexName, def, searchTopOffset, searchHeight, parameters[2], state.ScreenWidth)))
            .ToList();

        return PoolLoop(pool, indexName, ActiveGuard(pool.Name, indexName, KindDispatch(pool.Name, indexName, branches)));
    }

    private static BlockSyntax LandOnTilesBlock(
        ActorPool pool,
        string indexName,
        EnemyDef def,
        int searchTopOffset,
        int searchHeight,
        ExpressionSyntax flags,
        int screenWidth)
    {
        var projection = BuildActorScreenProjection(pool, indexName, def, "land", screenWidth);
        var hitTop = $"__{pool.Name}_land_hit_{def.Name}";
        var hitTopDeclaration = new DeclarationSyntax(
            "u8",
            hitTop,
            Maybe<ExpressionSyntax>.None,
            Maybe.From<ExpressionSyntax>(new SdkDotCallSyntax(
                "camera",
                "AabbHitTop",
                [
                    projection.ScreenX,
                    OffsetPoolField(pool.Name, indexName, "y", searchTopOffset, subtract: true),
                    new ConstantSyntax(def.HitboxWidth.ToString(CultureInfo.InvariantCulture)),
                    new ConstantSyntax(searchHeight.ToString(CultureInfo.InvariantCulture)),
                    flags,
                ])));
        var hitTopGuard = new IfElseSyntax(
            new BinaryExpressionSyntax(new IdentifierSyntax(hitTop), new ConstantSyntax("255"), Operator.NotEqual),
            new BlockSyntax([
                FieldAssignment(pool.Name, indexName, "y", "=", new IdentifierSyntax(hitTop)),
                FieldAssignment(pool.Name, indexName, "vy", "=", new ConstantSyntax("0")),
                FieldAssignment(pool.Name, indexName, "state", "=", new ConstantSyntax("1")),
            ]),
            Maybe<BlockSyntax>.None);

        return new BlockSyntax(projection.Declarations.Concat([
            new IfElseSyntax(
                projection.Visible,
                new BlockSyntax([hitTopDeclaration, hitTopGuard]),
                Maybe<BlockSyntax>.None),
        ]).ToList());
    }

    private static ForSyntax PoolTouchPlayerLoop(ActorPool pool, SdkDotCallSyntax call, ActorFrameworkState state)
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
        var branches = state.EnemyDefs
            .Select(def => new KindBranch(def.Name, TouchPlayerBlock(pool, indexName, def, playerX, playerY, playerRight, playerBottom, state.ScreenWidth)))
            .ToList();

        return PoolLoop(pool, indexName, ActiveGuard(pool.Name, indexName, KindDispatch(pool.Name, indexName, branches)));
    }

    private static BlockSyntax TouchPlayerBlock(
        ActorPool pool,
        string indexName,
        EnemyDef def,
        int playerX,
        int playerY,
        int playerRight,
        int playerBottom,
        int screenWidth)
    {
        var projection = BuildActorScreenProjection(pool, indexName, def, "player", screenWidth);
        var overlapsX = And(
            new BinaryExpressionSyntax(projection.ScreenX, Constant(playerRight), Operator.LessThan),
            new BinaryExpressionSyntax(
                new BinaryExpressionSyntax(projection.ScreenX, Constant(def.HitboxWidth), Operator.Get("+")),
                Constant(playerX),
                Operator.Get(">")));
        var overlapsY = And(
            new BinaryExpressionSyntax(PoolField(pool.Name, indexName, "y"), Constant(playerBottom), Operator.LessThan),
            new BinaryExpressionSyntax(OffsetPoolField(pool.Name, indexName, "y", def.HitboxHeight, subtract: false), Constant(playerY), Operator.Get(">")));

        var touchPlayer = new IfElseSyntax(
            And(overlapsX, overlapsY),
            new BlockSyntax([
                FieldAssignment(pool.Name, indexName, "state", "=", new ConstantSyntax((def.ContactDamage == 0 ? 1 : def.ContactDamage).ToString(CultureInfo.InvariantCulture))),
            ]),
            Maybe<BlockSyntax>.None);

        return new BlockSyntax(projection.Declarations.Concat([
            new IfElseSyntax(projection.Visible, new BlockSyntax([touchPlayer]), Maybe<BlockSyntax>.None),
        ]).ToList());
    }

    private static ActorScreenProjection BuildActorScreenProjection(ActorPool pool, string indexName, EnemyDef def, string phase, int screenWidth)
    {
        var cameraXLow = $"__{pool.Name}_{phase}_camera_x_lo_{def.Name}";
        var cameraXHigh = $"__{pool.Name}_{phase}_camera_x_hi_{def.Name}";
        var screenX = $"__{pool.Name}_{phase}_screen_x_{def.Name}";
        var screenXIdentifier = new IdentifierSyntax(screenX);

        var declarations = new List<StatementSyntax>
        {
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
            new DeclarationSyntax(
                "u8",
                screenX,
                Maybe<ExpressionSyntax>.None,
                Maybe.From<ExpressionSyntax>(new BinaryExpressionSyntax(
                    PoolField(pool.Name, indexName, "x"),
                    new IdentifierSyntax(cameraXLow),
                    Operator.Get("-")))),
        };

        var sameCameraPage = And(
            new BinaryExpressionSyntax(PoolField(pool.Name, indexName, "xHi"), new IdentifierSyntax(cameraXHigh), Operator.Equal),
            new BinaryExpressionSyntax(PoolField(pool.Name, indexName, "x"), new IdentifierSyntax(cameraXLow), Operator.Get(">=")));
        var nextCameraPage = And(
            new BinaryExpressionSyntax(
                PoolField(pool.Name, indexName, "xHi"),
                new BinaryExpressionSyntax(new IdentifierSyntax(cameraXHigh), new ConstantSyntax("1"), Operator.Get("+")),
                Operator.Equal),
            new BinaryExpressionSyntax(PoolField(pool.Name, indexName, "x"), new IdentifierSyntax(cameraXLow), Operator.LessThan));
        ExpressionSyntax visible = Or(sameCameraPage, nextCameraPage);
        if (screenWidth < 256)
        {
            visible = And(
                visible,
                new BinaryExpressionSyntax(new IdentifierSyntax(screenX), new ConstantSyntax(screenWidth.ToString(CultureInfo.InvariantCulture)), Operator.LessThan));
        }

        return new ActorScreenProjection(declarations, screenXIdentifier, visible);
    }

    private static IReadOnlyList<ExpressionSyntax> RequireArguments(SdkDotCallSyntax call, int count)
    {
        var parameters = call.Parameters.ToList();
        if (parameters.Count != count)
        {
            throw new InvalidOperationException($"{call.Module}.{call.Method} expects {count} arguments, got {parameters.Count}.");
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

    private static void RequireNoArguments(SdkDotCallSyntax call)
    {
        if (call.Parameters.Any())
        {
            throw new InvalidOperationException($"{call.Module}.{call.Method} does not accept arguments.");
        }
    }

    private static void RequireEnemyDefs(ActorFrameworkState state, string callName)
    {
        if (state.EnemyDefs.Count == 0)
        {
            throw new InvalidOperationException($"{callName} requires at least one enemy.Def declaration.");
        }
    }

    private static ForSyntax PoolLoop(ActorPool pool, string indexName, StatementSyntax bodyStatement)
    {
        return new ForSyntax(
            Maybe.From<StatementSyntax>(new DeclarationSyntax("u8", indexName, Maybe<ExpressionSyntax>.None, Maybe.From<ExpressionSyntax>(new ConstantSyntax("0")))),
            Maybe.From<ExpressionSyntax>(new BinaryExpressionSyntax(new IdentifierSyntax(indexName), new CountOfSyntax(pool.Name), Operator.LessThan)),
            Maybe.From<ExpressionSyntax>(new AssignmentSyntax(new IdentifierLValue(indexName), "+=", new ConstantSyntax("1"))),
            new BlockSyntax([bodyStatement]));
    }

    private static IfElseSyntax ActiveGuard(string poolName, string indexName, StatementSyntax bodyStatement)
    {
        return new IfElseSyntax(
            new BinaryExpressionSyntax(PoolField(poolName, indexName, "active"), new ConstantSyntax("0"), Operator.NotEqual),
            new BlockSyntax([bodyStatement]),
            Maybe<BlockSyntax>.None);
    }

    private static IfElseSyntax KindDispatch(string poolName, string indexName, IReadOnlyList<KindBranch> branches)
    {
        if (branches.Count == 0)
        {
            throw new InvalidOperationException($"{poolName} actor dispatch requires at least one enemy.Def declaration.");
        }

        var first = branches[0];
        var elseBlock = branches.Count == 1
            ? Maybe<BlockSyntax>.None
            : Maybe.From(new BlockSyntax([KindDispatch(poolName, indexName, branches.Skip(1).ToList())]));

        return new IfElseSyntax(
            new BinaryExpressionSyntax(PoolField(poolName, indexName, "kind"), new IdentifierSyntax(first.Kind), Operator.Equal),
            first.Block,
            elseBlock);
    }

    private static MemberAccessSyntax PoolField(string poolName, string indexName, string fieldName)
    {
        return new MemberAccessSyntax(new IndexExpressionSyntax(poolName, new IdentifierSyntax(indexName)), fieldName);
    }

    private static MemberAccessSyntax PoolField(string poolName, int index, string fieldName)
    {
        return new MemberAccessSyntax(new IndexExpressionSyntax(poolName, new ConstantSyntax(index.ToString(CultureInfo.InvariantCulture))), fieldName);
    }

    private static IReadOnlyList<StatementSyntax> SpawnAssignments(ActorSpawnLayer layer)
    {
        var statements = new List<StatementSyntax>();
        for (var index = 0; index < layer.Spawns.Count; index++)
        {
            var spawn = layer.Spawns[index];
            var (xLow, xHigh) = SplitWorldX(spawn.X, $"actor.{layer.MethodName} spawn {index} X");
            if (!spawn.Fields.ContainsKey("active"))
            {
                statements.Add(FieldAssignment(layer.PoolName, index, "active", "=", new ConstantSyntax("1")));
            }

            statements.Add(FieldAssignment(layer.PoolName, index, "kind", "=", new IdentifierSyntax(spawn.Kind)));
            statements.Add(FieldAssignment(layer.PoolName, index, "x", "=", new ConstantSyntax(xLow.ToString(CultureInfo.InvariantCulture))));
            statements.Add(FieldAssignment(layer.PoolName, index, "xHi", "=", new ConstantSyntax(xHigh.ToString(CultureInfo.InvariantCulture))));
            statements.Add(FieldAssignment(layer.PoolName, index, "y", "=", new ConstantSyntax(CheckedByte(spawn.Y, $"actor.{layer.MethodName} spawn {index} Y").ToString(CultureInfo.InvariantCulture))));

            foreach (var field in spawn.Fields)
            {
                statements.Add(FieldAssignment(layer.PoolName, index, field.Key, "=", new ConstantSyntax(field.Value.ToString(CultureInfo.InvariantCulture))));
            }
        }

        return statements;
    }

    private static (int Low, int High) SplitWorldX(int value, string context)
    {
        if (value is < 0 or > 65535)
        {
            throw new InvalidOperationException($"{context} must be between 0 and 65535.");
        }

        return (value & 0xFF, value >> 8);
    }

    private static ExpressionSyntax RewriteExpression(ExpressionSyntax expression, ActorFrameworkState state)
    {
        return expression switch
        {
            SdkDotCallSyntax { Module: "enemy" } enemyCall => RewriteEnemyCall(enemyCall, state),
            SdkDotCallSyntax { Module: "actor" } actorCall => throw new InvalidOperationException($"actor.{actorCall.Method} can only be used as a statement."),
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

    private static ExpressionSyntax RewriteEnemyCall(SdkDotCallSyntax call, ActorFrameworkState state)
    {
        if (call.Method == "Def")
        {
            throw new InvalidOperationException("enemy.Def can only be used as a statement.");
        }

        if (!EnemyLookupFunctions.TryGetValue(call.Method, out var functionName))
        {
            throw new InvalidOperationException($"Unknown actor framework enemy helper 'enemy.{call.Method}'.");
        }

        if (state.EnemyDefs.Count == 0)
        {
            throw new InvalidOperationException($"enemy.{call.Method} requires at least one enemy.Def declaration.");
        }

        return new FunctionCall(functionName, call.Parameters.Select(parameter => RewriteExpression(parameter, state)));
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

    private static IEnumerable<ConstDeclarationSyntax> GeneratedConstants(IReadOnlyList<EnemyDef> enemyDefs)
    {
        foreach (var behavior in enemyDefs.Select(def => def.Behavior).Distinct(StringComparer.Ordinal))
        {
            if (!BehaviorIds.TryGetValue(behavior, out var id))
            {
                throw new InvalidOperationException($"Unknown actor behavior '{behavior}'.");
            }

            yield return Constant(behavior, id);
        }

        for (var index = 0; index < enemyDefs.Count; index++)
        {
            var def = enemyDefs[index];
            yield return Constant(def.Name, index + 1);
            yield return Constant($"{def.Name}Behavior", new IdentifierSyntax(def.Behavior));
            yield return Constant($"{def.Name}Speed", def.Speed);
            yield return Constant($"{def.Name}Hp", def.Hp);
            yield return Constant($"{def.Name}Cooldown", def.Cooldown);
            yield return Constant($"{def.Name}ContactDamage", def.ContactDamage);
            yield return Constant($"{def.Name}HitboxWidth", def.HitboxWidth);
            yield return Constant($"{def.Name}HitboxHeight", def.HitboxHeight);
        }
    }

    private static ConstDeclarationSyntax Constant(string name, int value)
    {
        return Constant(name, new ConstantSyntax(value.ToString(CultureInfo.InvariantCulture)));
    }

    private static ConstDeclarationSyntax Constant(string name, ExpressionSyntax value)
    {
        return new ConstDeclarationSyntax(Maybe<string>.None, name, value);
    }

    private static IEnumerable<FunctionSyntax> GeneratedLookupFunctions(IReadOnlyList<EnemyDef> enemyDefs)
    {
        if (enemyDefs.Count == 0)
        {
            yield break;
        }

        yield return LookupFunction("enemy_behavior", enemyDefs, "Behavior");
        yield return LookupFunction("enemy_speed", enemyDefs, "Speed");
        yield return LookupFunction("enemy_hp", enemyDefs, "Hp");
        yield return LookupFunction("enemy_cooldown", enemyDefs, "Cooldown");
        yield return LookupFunction("enemy_contact_damage", enemyDefs, "ContactDamage");
        yield return LookupFunction("enemy_hitbox_width", enemyDefs, "HitboxWidth");
        yield return LookupFunction("enemy_hitbox_height", enemyDefs, "HitboxHeight");
    }

    private static FunctionSyntax LookupFunction(string functionName, IReadOnlyList<EnemyDef> enemyDefs, string suffix)
    {
        ExpressionSyntax value = new ConstantSyntax("0");
        foreach (var def in enemyDefs.Reverse())
        {
            value = new ConditionalExpressionSyntax(
                new BinaryExpressionSyntax(new IdentifierSyntax("kind"), new IdentifierSyntax(def.Name), Operator.Equal),
                new IdentifierSyntax($"{def.Name}{suffix}"),
                value);
        }

        return new FunctionSyntax(
            "u8",
            functionName,
            [new ParameterSyntax("u8", "kind")],
            new BlockSyntax([new ReturnSyntax(Maybe.From(value))]),
            isExpressionBodied: true,
            isInline: true);
    }

    private static StructSyntax ActorStruct()
    {
        return new StructSyntax(
            ActorStructName,
            [
                new StructFieldSyntax("u8", "kind"),
                new StructFieldSyntax("u8", "active"),
                new StructFieldSyntax("u8", "x"),
                new StructFieldSyntax("u8", "xHi"),
                new StructFieldSyntax("u8", "y"),
                new StructFieldSyntax("i8", "vx"),
                new StructFieldSyntax("i8", "vy"),
                new StructFieldSyntax("u8", "state"),
                new StructFieldSyntax("u8", "timer"),
                new StructFieldSyntax("u8", "facing"),
                new StructFieldSyntax("u8", "animTick"),
                new StructFieldSyntax("u8", "health"),
            ]);
    }

    private static Maybe<TOut> MapMaybe<TIn, TOut>(Maybe<TIn> maybe, Func<TIn, TOut> selector)
    {
        return maybe.HasValue ? Maybe.From(selector(maybe.Value)) : Maybe<TOut>.None;
    }

    private sealed class ActorFrameworkState(Target2DCapabilities capabilities, bool supportsUpdate, bool supportsDraw, string? baseDirectory)
    {
        private readonly Dictionary<string, ActorPool> pools = new(StringComparer.Ordinal);
        private readonly List<ActorPool> poolsInOrder = [];
        private readonly Dictionary<string, EnemyDef> enemyDefs = new(StringComparer.Ordinal);
        private readonly List<EnemyDef> enemyDefsInOrder = [];
        private readonly Dictionary<ActorSpawnLayerKey, ActorSpawnLayer> spawnLayers = [];

        public string TargetName { get; } = capabilities.Name;
        public string BaseDirectory { get; } = baseDirectory ?? Directory.GetCurrentDirectory();
        public bool SupportsUpdate { get; } = supportsUpdate;
        public bool SupportsDraw { get; } = supportsDraw;
        public int ScreenWidth { get; } = capabilities.ScreenPixels.Width;
        public IReadOnlyList<ActorPool> Pools => poolsInOrder;
        public IReadOnlyList<EnemyDef> EnemyDefs => enemyDefsInOrder;
        public bool HasDirectives => pools.Count != 0 || enemyDefs.Count != 0 || spawnLayers.Count != 0;

        public void AddPool(ActorPool pool)
        {
            if (pool.Capacity > capabilities.SpriteCount)
            {
                throw new InvalidOperationException($"Target '{capabilities.Name}' supports actor pools up to {capabilities.SpriteCount} slots, but actor.Pool for '{pool.Name}' declares {pool.Capacity}.");
            }

            if (!pools.TryAdd(pool.Name, pool))
            {
                throw new InvalidOperationException($"actor.Pool for '{pool.Name}' is already declared.");
            }

            poolsInOrder.Add(pool);
        }

        public ActorPool Pool(SdkDotCallSyntax call)
        {
            var pool = ReadPool(call);
            return pools[pool.Name];
        }

        public bool TryPool(string name, out ActorPool pool)
        {
            return pools.TryGetValue(name, out pool!);
        }

        public void AddEnemyDef(EnemyDef def)
        {
            if (!enemyDefs.TryAdd(def.Name, def))
            {
                throw new InvalidOperationException($"enemy.Def for '{def.Name}' is already declared.");
            }

            enemyDefsInOrder.Add(def);
        }

        public void AddSpawnLayer(ActorSpawnLayer spawnLayer)
        {
            var key = ActorSpawnLayerKey.From(spawnLayer.MethodName, spawnLayer.PoolName, spawnLayer.MapPath, spawnLayer.LayerName, spawnLayer.WindowLeft, spawnLayer.WindowWidth);
            if (!spawnLayers.TryAdd(key, spawnLayer))
            {
                throw new InvalidOperationException($"actor.{spawnLayer.MethodName} for pool '{spawnLayer.PoolName}' and layer '{spawnLayer.LayerName}' is already declared.");
            }
        }

        public ActorSpawnLayer SpawnLayer(SdkDotCallSyntax call)
        {
            var parameters = call.Parameters.ToList();
            if (call.Method == "SpawnLayer" && (parameters.Count != 3 || parameters[0] is not IdentifierSyntax))
            {
                throw new InvalidOperationException("actor.SpawnLayer expects a pool identifier, a map path string, and a layer name string.");
            }

            if (call.Method == "SpawnWindow" && (parameters.Count != 5 || parameters[0] is not IdentifierSyntax))
            {
                throw new InvalidOperationException("actor.SpawnWindow expects a pool identifier, a map path string, a layer name string, a literal left edge, and a literal width.");
            }

            var windowLeft = call.Method == "SpawnWindow" ? RequiredLiteralByte(parameters[3], "actor.SpawnWindow argument 4") : (int?)null;
            var windowWidth = call.Method == "SpawnWindow" ? RequiredLiteralByte(parameters[4], "actor.SpawnWindow argument 5") : (int?)null;
            var poolName = (IdentifierSyntax)parameters[0];
            var key = ActorSpawnLayerKey.From(
                call.Method,
                poolName.Identifier,
                StringLiteral(parameters[1], "actor.SpawnLayer argument 2"),
                StringLiteral(parameters[2], "actor.SpawnLayer argument 3"),
                windowLeft,
                windowWidth);
            return spawnLayers[key];
        }

        public void ValidateSpawnLayers()
        {
            foreach (var spawnLayer in spawnLayers.Values)
            {
                if (!pools.TryGetValue(spawnLayer.PoolName, out var pool))
                {
                    throw new InvalidOperationException($"actor.SpawnLayer references undeclared pool '{spawnLayer.PoolName}'.");
                }

                if (spawnLayer.Spawns.Count > pool.Capacity)
                {
                    throw new InvalidOperationException($"actor.{spawnLayer.MethodName} for pool '{spawnLayer.PoolName}' reads {spawnLayer.Spawns.Count} spawn(s) from layer '{spawnLayer.LayerName}', exceeding the declared capacity {pool.Capacity}.");
                }

                foreach (var spawn in spawnLayer.Spawns)
                {
                    if (!enemyDefs.ContainsKey(spawn.Kind))
                    {
                        throw new InvalidOperationException($"actor.SpawnLayer layer '{spawnLayer.LayerName}' references unknown actor kind '{spawn.Kind}'. Declare enemy.Def({spawn.Kind}, ...).");
                    }
                }
            }
        }
    }

    private sealed record ActorPool(string Name, int Capacity);

    private sealed record ActorSpawnLayer(
        string MethodName,
        string PoolName,
        string MapPath,
        string LayerName,
        int? WindowLeft,
        int? WindowWidth,
        IReadOnlyList<LogicalActorSpawn> Spawns);

    private sealed record ActorSpawnLayerKey(string MethodName, string PoolName, string MapPath, string LayerName, int? WindowLeft, int? WindowWidth)
    {
        public static ActorSpawnLayerKey From(string methodName, string poolName, string mapPath, string layerName, int? windowLeft, int? windowWidth) =>
            new(methodName, poolName, mapPath, layerName, windowLeft, windowWidth);
    }

    private sealed record ActorScreenProjection(IReadOnlyList<StatementSyntax> Declarations, IdentifierSyntax ScreenX, ExpressionSyntax Visible);

    private sealed record KindBranch(string Kind, BlockSyntax Block);

    private sealed record EnemyDef(
        string Name,
        string? Sprite,
        string Behavior,
        string? Animation,
        int Speed,
        int Hp,
        int Cooldown,
        int ContactDamage,
        int HitboxWidth,
        int HitboxHeight);
}
