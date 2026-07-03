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

public static class ActorFrameworkLowerer
{
    private const string ActorStructName = "Actor";
    private const string ActorCameraXLowFunction = "__rs_actor_camera_x_lo";
    private const string ActorCameraXHighFunction = "__rs_actor_camera_x_hi";
    private const string ActorCameraYLowFunction = "__rs_actor_camera_y_lo";
    private const string ActorCameraYHighFunction = "__rs_actor_camera_y_hi";
    private const string AnimationFrameIntrinsic = "animation_frame";
    private const string CameraScreenAabbTilesIntrinsic = "camera_screen_aabb_tiles";
    private const string CameraScreenAabbHitTopIntrinsic = "camera_screen_aabb_hit_top";
    private const string SpriteDrawIntrinsic = "sprite_draw";

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

    private static readonly IReadOnlyList<string> SpawnInitialFieldNames =
    [
        "active",
        "vx",
        "vy",
        "state",
        "timer",
        "facing",
        "animTick",
        "health",
    ];

    public static ProgramSyntax Lower(ProgramSyntax program, Target2DCapabilities capabilities, bool supportsUpdate, bool supportsDraw, string? baseDirectory = null)
    {
        var state = new ActorFrameworkState(
            capabilities,
            supportsUpdate,
            supportsDraw,
            baseDirectory,
            IntrinsicFunctionIndex.Build(program));
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
                throw new InvalidOperationException("Actors.Pool cannot generate framework struct 'Actor' because a struct named 'Actor' is already declared.");
            }

            structs.Add(ActorStruct());
        }

        var rewrittenFunctions = program.Functions
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
            .ToList();

        ValidateGeneratedNameCollisions(program, state);

        var functions = rewrittenFunctions
            .Concat(GeneratedLookupFunctions(state.EnemyDefs, state.UsedEnemyLookupMethods))
            .Concat(GeneratedSpawnLookupFunctions(state.SpawnLayers))
            .ToList();

        return new ProgramSyntax(
            program.Imports,
            program.TypeAliases,
            program.Constants.Concat(GeneratedConstants(state.EnemyDefs)).ToList(),
            program.Enums,
            structs,
            functions);
    }

    public static void ValidatePoolSpriteBudgets(
        ProgramSyntax program,
        Target2DCapabilities capabilities,
        Func<string, ActorMetaspriteGeometry> metaspriteGeometry,
        string? baseDirectory = null)
    {
        var state = new ActorFrameworkState(capabilities, supportsUpdate: true, supportsDraw: true, baseDirectory);
        foreach (var function in program.Functions)
        {
            CollectPoolBudgetDirectives(function.Block, state);
        }

        if (state.Pools.Count == 0 || state.EnemyDefs.Count == 0)
        {
            return;
        }

        var drawnPools = new HashSet<string>(StringComparer.Ordinal);
        foreach (var function in program.Functions)
        {
            CollectDrawnPools(function.Block, drawnPools);
        }

        if (drawnPools.Count == 0)
        {
            return;
        }

        var enemyBudgets = state.EnemyDefs
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

        foreach (var pool in state.Pools.Where(pool => drawnPools.Contains(pool.Name)))
        {
            ValidatePoolSpriteBudget(capabilities, pool, enemyBudgets);
        }
    }

    private static void CollectPoolBudgetDirectives(BlockSyntax block, ActorFrameworkState state)
    {
        WalkStatements(block, statement =>
        {
            switch (statement)
            {
                case ExpressionStatementSyntax { Expression: SdkDotCallSyntax { Module: "Actors", Method: "Pool" } poolCall }:
                    state.AddPool(ReadPool(poolCall));
                    break;
                case ExpressionStatementSyntax { Expression: SdkDotCallSyntax { Module: "Enemies", Method: "Def" } defCall }:
                    state.AddEnemyDef(ReadEnemyDef(defCall));
                    break;
            }
        });
    }

    private static void CollectDrawnPools(BlockSyntax block, ISet<string> drawnPools)
    {
        WalkStatements(block, statement =>
        {
            if (statement is ExpressionStatementSyntax { Expression: SdkDotCallSyntax { Method: "Draw" } drawCall })
            {
                drawnPools.Add(drawCall.Module);
            }
        });
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
                case LoopSyntax loopSyntax:
                    WalkStatements(loopSyntax.Body, visit);
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

    private static void CollectDirectives(BlockSyntax block, ActorFrameworkState state)
    {
        foreach (var statement in block.Statements)
        {
            switch (statement)
            {
                case ExpressionStatementSyntax { Expression: SdkDotCallSyntax { Module: "Actors", Method: "Pool" } poolCall }:
                    state.AddPool(ReadPool(poolCall));
                    break;
                case ExpressionStatementSyntax { Expression: SdkDotCallSyntax { Module: "Enemies", Method: "Def" } defCall }:
                    state.AddEnemyDef(ReadEnemyDef(defCall));
                    break;
                case ExpressionStatementSyntax { Expression: SdkDotCallSyntax { Module: "Actors", Method: "SpawnLayer" or "SpawnWindow" } spawnCall }:
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
            throw new InvalidOperationException("Actors.Pool expects a pool identifier and a literal capacity.");
        }

        var name = poolName.Identifier;
        if (!TryLiteralByte(parameters[1], out var capacity) || capacity == 0)
        {
            throw new InvalidOperationException($"Actors.Pool for '{name}' requires a literal capacity from 1 to 255.");
        }

        return new ActorPool(name, capacity);
    }

    private static ActorSpawnLayer ReadSpawnDirective(SdkDotCallSyntax call, string baseDirectory)
    {
        var parameters = call.Parameters.ToList();
        if (call.Method == "SpawnLayer" && (parameters.Count != 3 || parameters[0] is not IdentifierSyntax))
        {
            throw new InvalidOperationException("Actors.SpawnLayer expects a pool identifier, a map path string, and a layer name string.");
        }

        if (call.Method == "SpawnWindow" && (parameters.Count != 5 || parameters[0] is not IdentifierSyntax))
        {
            throw new InvalidOperationException("Actors.SpawnWindow expects a pool identifier, a map path string, a layer name string, a literal left edge, and a literal width.");
        }

        var poolName = (IdentifierSyntax)parameters[0];
        var mapPath = StringLiteral(parameters[1], "Actors.SpawnLayer argument 2");
        var layerName = StringLiteral(parameters[2], "Actors.SpawnLayer argument 3");
        var fullPath = Path.GetFullPath(Path.Combine(baseDirectory, mapPath));
        var map = LogicalTiledMapImporter.Load(fullPath);
        if (!map.ActorSpawnLayers.TryGetValue(layerName, out var spawns))
        {
            throw new InvalidOperationException($"Actors.SpawnLayer could not find object layer '{layerName}' in Tiled map '{Path.GetFileName(mapPath)}'.");
        }

        int? windowLeft = null;
        int? windowWidth = null;
        if (call.Method == "SpawnWindow")
        {
            windowLeft = RequiredLiteralByte(parameters[3], "Actors.SpawnWindow argument 4");
            windowWidth = RequiredLiteralByte(parameters[4], "Actors.SpawnWindow argument 5");
        }

        return new ActorSpawnLayer(call.Method, poolName.Identifier, mapPath, layerName, windowLeft, windowWidth, spawns, RuntimeName: string.Empty);
    }

    private static EnemyDef ReadEnemyDef(SdkDotCallSyntax call)
    {
        var parameters = call.Parameters.ToList();
        if (parameters.Count == 0 || parameters[0] is not IdentifierSyntax enemyName)
        {
            throw new InvalidOperationException("Enemies.Def expects an enemy identifier as its first argument.");
        }

        var namedArguments = new Dictionary<string, ExpressionSyntax>(StringComparer.Ordinal);
        foreach (var parameter in parameters.Skip(1))
        {
            if (parameter is not NamedArgumentSyntax namedArgument)
            {
                throw new InvalidOperationException($"Enemies.Def for '{enemyName.Identifier}' expects named arguments after the enemy identifier.");
            }

            if (!namedArguments.TryAdd(namedArgument.Name, namedArgument.Expression))
            {
                throw new InvalidOperationException($"Enemies.Def for '{enemyName.Identifier}' supplies '{namedArgument.Name}' more than once.");
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
                throw new InvalidOperationException($"Enemies.Def for '{enemyName.Identifier}' has unsupported property '{name}'.");
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
        if (statement is ExpressionStatementSyntax { Expression: SdkDotCallSyntax { Module: "Actors", Method: "SpawnLayer" or "SpawnWindow" } spawnCall })
        {
            var spawnLayer = state.SpawnLayer(spawnCall);
            return RuntimeSpawnActivationStatements(spawnLayer, state.NextActivationPrefix(spawnLayer), state.ScreenWidth);
        }

        if (statement is ExpressionStatementSyntax { Expression: SdkDotCallSyntax { Module: "Actors", Method: "Pool" } poolCall })
        {
            return PoolDeclarations(state.Pool(poolCall), state);
        }

        if (statement is ExpressionStatementSyntax { Expression: SdkDotCallSyntax poolSdkCall } && state.TryPool(poolSdkCall.Module, out var pool))
        {
            return poolSdkCall.Method switch
            {
                "Update" => [PoolUpdateLoop(pool, poolSdkCall, state)],
                "Draw" => PoolDrawStatements(pool, poolSdkCall, state),
                "TouchTiles" => PoolTouchTilesStatements(pool, poolSdkCall, state),
                "LandOnTiles" => PoolLandOnTilesStatements(pool, poolSdkCall, state),
                "TouchPlayer" => PoolTouchPlayerStatements(pool, poolSdkCall, state),
                _ => [RewriteStatement(statement, state)!],
            };
        }

        var rewritten = RewriteStatement(statement, state);
        return rewritten is null ? [] : [rewritten];
    }

    private static StatementSyntax? RewriteStatement(StatementSyntax statement, ActorFrameworkState state)
    {
        return statement switch
        {
            ExpressionStatementSyntax { Expression: SdkDotCallSyntax { Module: "Actors", Method: "Pool" } } => null,
            ExpressionStatementSyntax { Expression: SdkDotCallSyntax { Module: "Enemies", Method: "Def" } } => null,
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

    private static IReadOnlyList<StatementSyntax> PoolDeclarations(ActorPool pool, ActorFrameworkState state)
    {
        var declarations = new List<StatementSyntax> { PoolDeclaration(pool) };
        foreach (var spawnLayer in state.SpawnLayersFor(pool.Name).Where(layer => layer.Spawns.Count != 0))
        {
            declarations.Add(new DeclarationSyntax(
                "u8",
                $"{spawnLayer.RuntimeName}_used",
                Maybe.From<ExpressionSyntax>(new ConstantSyntax(spawnLayer.Spawns.Count.ToString(CultureInfo.InvariantCulture))),
                Maybe<ExpressionSyntax>.None));
        }

        return declarations;
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

    private static IReadOnlyList<StatementSyntax> PoolDrawStatements(ActorPool pool, SdkDotCallSyntax call, ActorFrameworkState state)
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
            throw new InvalidOperationException($"{pool.Name}.Draw requires Enemies.Def for '{missingSprite.Name}' to declare a sprite identifier.");
        }

        var indexName = $"__{pool.Name}_draw_i";
        var projection = BuildActorScreenProjection(pool, indexName, "draw", state.ScreenWidth, state.ScreenHeight);
        var branches = state.EnemyDefs
            .Select(def => new KindBranch(
                def.Name,
                pool.Capacity == 1
                    ? ActorDrawBlock(pool, indexName, def, projection, state.ScreenHeight, state)
                    : VisibleActorDrawBlock(pool, indexName, def, projection, state)))
            .ToList();
        var activeStatements = projection.Declarations
            .Append(KindDispatch(pool.Name, indexName, branches))
            .ToList();
        var loop = pool.Capacity == 1
            ? PoolLoop(pool, indexName, activeStatements)
            : PoolLoop(pool, indexName, ActiveGuard(pool.Name, indexName, activeStatements));

        return ActorCameraDeclarations(pool, "draw")
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

    private static BlockSyntax VisibleActorDrawBlock(
        ActorPool pool,
        string indexName,
        EnemyDef def,
        ActorScreenProjection projection,
        ActorFrameworkState state)
    {
        var statements = new List<StatementSyntax>();

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

        statements.Add(new IfElseSyntax(
            projection.Visible,
            new BlockSyntax([
                new ExpressionStatementSyntax(IntrinsicCall(
                    state,
                    SpriteDrawIntrinsic,
                    [
                        new IdentifierSyntax(def.Sprite!),
                        projection.ScreenX,
                        projection.ScreenY,
                        frame,
                        new IdentifierSyntax("false"),
                        new ConstantSyntax("0"),
                    ])),
            ]),
            Maybe<BlockSyntax>.None));

        return new BlockSyntax(statements);
    }

    private static IReadOnlyList<StatementSyntax> PoolTouchTilesStatements(ActorPool pool, SdkDotCallSyntax call, ActorFrameworkState state)
    {
        var parameters = RequireArguments(call, 2);
        RequireEnemyDefs(state, $"{pool.Name}.TouchTiles");

        var yOffset = RequiredLiteralByte(parameters[0], $"{pool.Name}.TouchTiles argument 1");
        var indexName = $"__{pool.Name}_touch_i";
        var projection = BuildActorScreenProjection(pool, indexName, "touch", state.ScreenWidth, state.ScreenHeight);
        var branches = state.EnemyDefs
            .Select(def => new KindBranch(def.Name, TouchTilesBlock(pool, indexName, def, yOffset, parameters[1], projection, state)))
            .ToList();
        var activeStatements = projection.Declarations
            .Append(KindDispatch(pool.Name, indexName, branches))
            .ToList();
        var loop = PoolLoop(pool, indexName, ActiveGuard(pool.Name, indexName, activeStatements));

        return ActorCameraDeclarations(pool, "touch")
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

    private static IReadOnlyList<StatementSyntax> PoolLandOnTilesStatements(ActorPool pool, SdkDotCallSyntax call, ActorFrameworkState state)
    {
        var parameters = RequireArguments(call, 3);
        RequireEnemyDefs(state, $"{pool.Name}.LandOnTiles");

        var searchTopOffset = RequiredLiteralByte(parameters[0], $"{pool.Name}.LandOnTiles argument 1");
        var searchHeight = RequiredLiteralByte(parameters[1], $"{pool.Name}.LandOnTiles argument 2");
        var indexName = $"__{pool.Name}_land_i";
        var projection = BuildActorScreenProjection(pool, indexName, "land", state.ScreenWidth, state.ScreenHeight);
        var branches = state.EnemyDefs
            .Select(def => new KindBranch(def.Name, LandOnTilesBlock(pool, indexName, def, searchTopOffset, searchHeight, parameters[2], projection, state)))
            .ToList();
        var activeStatements = projection.Declarations
            .Append(KindDispatch(pool.Name, indexName, branches))
            .ToList();
        var loop = PoolLoop(pool, indexName, ActiveGuard(pool.Name, indexName, activeStatements));

        return ActorCameraDeclarations(pool, "land")
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

    private static IReadOnlyList<StatementSyntax> PoolTouchPlayerStatements(ActorPool pool, SdkDotCallSyntax call, ActorFrameworkState state)
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
        var projection = BuildActorScreenProjection(pool, indexName, "player", state.ScreenWidth, state.ScreenHeight);
        var branches = state.EnemyDefs
            .Select(def => new KindBranch(def.Name, TouchPlayerBlock(pool, indexName, def, playerX, playerY, playerRight, playerBottom, projection)))
            .ToList();
        var activeStatements = projection.Declarations
            .Append(KindDispatch(pool.Name, indexName, branches))
            .ToList();
        var loop = PoolLoop(pool, indexName, ActiveGuard(pool.Name, indexName, activeStatements));

        return ActorCameraDeclarations(pool, "player")
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
            new BinaryExpressionSyntax(
                new BinaryExpressionSyntax(projection.ScreenX, Constant(def.HitboxWidth), Operator.Get("+")),
                Constant(playerX),
                Operator.Get(">")));
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

    private static IReadOnlyList<StatementSyntax> ActorCameraDeclarations(ActorPool pool, string phase)
    {
        var cameraXLow = $"__{pool.Name}_{phase}_camera_x_lo";
        var cameraXHigh = $"__{pool.Name}_{phase}_camera_x_hi";
        var cameraYLow = $"__{pool.Name}_{phase}_camera_y_lo";
        var cameraYHigh = $"__{pool.Name}_{phase}_camera_y_hi";

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
            new DeclarationSyntax(
                "u8",
                cameraYLow,
                Maybe<ExpressionSyntax>.None,
                Maybe.From<ExpressionSyntax>(new FunctionCall(ActorCameraYLowFunction, []))),
            new DeclarationSyntax(
                "u8",
                cameraYHigh,
                Maybe<ExpressionSyntax>.None,
                Maybe.From<ExpressionSyntax>(new FunctionCall(ActorCameraYHighFunction, []))),
        ];
    }

    private static ActorScreenProjection BuildActorScreenProjection(ActorPool pool, string indexName, string phase, int screenWidth, int screenHeight)
    {
        var cameraXLow = $"__{pool.Name}_{phase}_camera_x_lo";
        var cameraXHigh = $"__{pool.Name}_{phase}_camera_x_hi";
        var cameraYLow = $"__{pool.Name}_{phase}_camera_y_lo";
        var cameraYHigh = $"__{pool.Name}_{phase}_camera_y_hi";
        var screenX = $"__{pool.Name}_{phase}_screen_x";
        var screenY = $"__{pool.Name}_{phase}_screen_y";
        var visibleXName = $"__{pool.Name}_{phase}_visible_x";
        var visibleYName = $"__{pool.Name}_{phase}_visible_y";
        var screenXIdentifier = new IdentifierSyntax(screenX);
        var screenYIdentifier = new IdentifierSyntax(screenY);

        var declarations = new List<StatementSyntax>
        {
            new DeclarationSyntax(
                "u8",
                screenX,
                Maybe<ExpressionSyntax>.None,
                Maybe.From<ExpressionSyntax>(new BinaryExpressionSyntax(
                    PoolField(pool.Name, indexName, "x"),
                    new IdentifierSyntax(cameraXLow),
                    Operator.Get("-")))),
            new DeclarationSyntax(
                "u8",
                screenY,
                Maybe<ExpressionSyntax>.None,
                Maybe.From<ExpressionSyntax>(new BinaryExpressionSyntax(
                    PoolField(pool.Name, indexName, "y"),
                    new IdentifierSyntax(cameraYLow),
                    Operator.Get("-")))),
            new DeclarationSyntax(
                "u8",
                visibleXName,
                Maybe<ExpressionSyntax>.None,
                Maybe.From<ExpressionSyntax>(new ConstantSyntax("0"))),
            new DeclarationSyntax(
                "u8",
                visibleYName,
                Maybe<ExpressionSyntax>.None,
                Maybe.From<ExpressionSyntax>(new ConstantSyntax("0"))),
        };

        var sameCameraXPage = And(
            new BinaryExpressionSyntax(PoolField(pool.Name, indexName, "xHi"), new IdentifierSyntax(cameraXHigh), Operator.Equal),
            new BinaryExpressionSyntax(PoolField(pool.Name, indexName, "x"), new IdentifierSyntax(cameraXLow), Operator.Get(">=")));
        var nextCameraXPage = And(
            new BinaryExpressionSyntax(
                PoolField(pool.Name, indexName, "xHi"),
                new BinaryExpressionSyntax(new IdentifierSyntax(cameraXHigh), new ConstantSyntax("1"), Operator.Get("+")),
                Operator.Equal),
            new BinaryExpressionSyntax(PoolField(pool.Name, indexName, "x"), new IdentifierSyntax(cameraXLow), Operator.LessThan));
        ExpressionSyntax visibleXExpression = Or(sameCameraXPage, nextCameraXPage);
        if (screenWidth < 256)
        {
            visibleXExpression = And(
                visibleXExpression,
                new BinaryExpressionSyntax(new IdentifierSyntax(screenX), new ConstantSyntax(screenWidth.ToString(CultureInfo.InvariantCulture)), Operator.LessThan));
        }
        declarations.Add(new IfElseSyntax(
            visibleXExpression,
            new BlockSyntax([new ExpressionStatementSyntax(new AssignmentSyntax(new IdentifierLValue(visibleXName), "=", new ConstantSyntax("1")))]),
            Maybe<BlockSyntax>.None));

        var sameCameraYPage = And(
            new BinaryExpressionSyntax(PoolField(pool.Name, indexName, "yHi"), new IdentifierSyntax(cameraYHigh), Operator.Equal),
            new BinaryExpressionSyntax(PoolField(pool.Name, indexName, "y"), new IdentifierSyntax(cameraYLow), Operator.Get(">=")));
        var nextCameraYPage = And(
            new BinaryExpressionSyntax(
                PoolField(pool.Name, indexName, "yHi"),
                new BinaryExpressionSyntax(new IdentifierSyntax(cameraYHigh), new ConstantSyntax("1"), Operator.Get("+")),
                Operator.Equal),
            new BinaryExpressionSyntax(PoolField(pool.Name, indexName, "y"), new IdentifierSyntax(cameraYLow), Operator.LessThan));
        ExpressionSyntax visibleYExpression = Or(sameCameraYPage, nextCameraYPage);
        if (screenHeight < 256)
        {
            visibleYExpression = And(
                visibleYExpression,
                new BinaryExpressionSyntax(new IdentifierSyntax(screenY), new ConstantSyntax(screenHeight.ToString(CultureInfo.InvariantCulture)), Operator.LessThan));
        }
        declarations.Add(new IfElseSyntax(
            visibleYExpression,
            new BlockSyntax([new ExpressionStatementSyntax(new AssignmentSyntax(new IdentifierLValue(visibleYName), "=", new ConstantSyntax("1")))]),
            Maybe<BlockSyntax>.None));

        return new ActorScreenProjection(
            declarations,
            screenXIdentifier,
            screenYIdentifier,
            And(
                new BinaryExpressionSyntax(new IdentifierSyntax(visibleXName), new ConstantSyntax("0"), Operator.NotEqual),
                new BinaryExpressionSyntax(new IdentifierSyntax(visibleYName), new ConstantSyntax("0"), Operator.NotEqual)));
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

    private static IfElseSyntax KindDispatch(string poolName, string indexName, IReadOnlyList<KindBranch> branches)
    {
        if (branches.Count == 0)
        {
            throw new InvalidOperationException($"{poolName} actor dispatch requires at least one Enemies.Def declaration.");
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

    private static (int Low, int High) SplitWorldX(int value, string context)
    {
        if (value is < 0 or > 65535)
        {
            throw new InvalidOperationException($"{context} must be between 0 and 65535.");
        }

        return (value & 0xFF, value >> 8);
    }

    private static (int Low, int High) SplitWorldY(int value, string context) => SplitWorldX(value, context);

    private static ExpressionSyntax RewriteExpression(ExpressionSyntax expression, ActorFrameworkState state)
    {
        return expression switch
        {
            SdkDotCallSyntax { Module: "Enemies" } enemyCall => RewriteEnemyCall(enemyCall, state),
            SdkDotCallSyntax { Module: "Actors" } actorCall => throw new InvalidOperationException($"Actors.{actorCall.Method} can only be used as a statement."),
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
            throw new InvalidOperationException("Enemies.Def can only be used as a statement.");
        }

        if (!EnemyLookupFunctions.TryGetValue(call.Method, out var functionName))
        {
            throw new InvalidOperationException($"Unknown actor framework enemy helper 'Enemies.{call.Method}'.");
        }

        if (state.EnemyDefs.Count == 0)
        {
            throw new InvalidOperationException($"Enemies.{call.Method} requires at least one Enemies.Def declaration.");
        }

        state.RecordEnemyLookupMethod(call.Method);
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

    private static void ValidateGeneratedNameCollisions(ProgramSyntax program, ActorFrameworkState state)
    {
        var userSymbols = UserSymbols(program);
        var generatedNames = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var generatedName in GeneratedNames(state))
        {
            if (userSymbols.TryGetValue(generatedName.Name, out var userSymbol))
            {
                throw new InvalidOperationException($"actor framework cannot generate {generatedName.Origin} named '{generatedName.Name}' because {userSymbol} is already declared.");
            }

            if (!generatedNames.TryAdd(generatedName.Name, generatedName.Origin))
            {
                throw new InvalidOperationException($"actor framework cannot generate {generatedName.Origin} named '{generatedName.Name}' because {generatedNames[generatedName.Name]} also generates '{generatedName.Name}'.");
            }
        }
    }

    private static IReadOnlyDictionary<string, string> UserSymbols(ProgramSyntax program)
    {
        var symbols = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var typeAlias in program.TypeAliases)
        {
            symbols.TryAdd(typeAlias.Name, $"user type alias '{typeAlias.Name}'");
        }

        foreach (var constant in program.Constants)
        {
            symbols.TryAdd(constant.Name, $"user constant '{constant.Name}'");
        }

        foreach (var enumSyntax in program.Enums)
        {
            symbols.TryAdd(enumSyntax.Name, $"user enum '{enumSyntax.Name}'");
        }

        foreach (var structSyntax in program.Structs)
        {
            symbols.TryAdd(structSyntax.Name, $"user struct '{structSyntax.Name}'");
        }

        foreach (var function in program.Functions)
        {
            symbols.TryAdd(function.Name, $"user function '{function.Name}'");
        }

        return symbols;
    }

    private static IEnumerable<GeneratedName> GeneratedNames(ActorFrameworkState state)
    {
        if (state.Pools.Count != 0)
        {
            yield return new GeneratedName(ActorStructName, "framework struct 'Actor'");
        }

        foreach (var behavior in state.EnemyDefs.Select(def => def.Behavior).Distinct(StringComparer.Ordinal))
        {
            yield return new GeneratedName(behavior, $"actor behavior '{behavior}' constant");
        }

        foreach (var def in state.EnemyDefs)
        {
            yield return new GeneratedName(def.Name, $"Enemies.Def '{def.Name}' kind constant");
            yield return new GeneratedName($"{def.Name}Behavior", $"Enemies.Def '{def.Name}' behavior constant");
            yield return new GeneratedName($"{def.Name}Speed", $"Enemies.Def '{def.Name}' speed constant");
            yield return new GeneratedName($"{def.Name}Hp", $"Enemies.Def '{def.Name}' hp constant");
            yield return new GeneratedName($"{def.Name}Cooldown", $"Enemies.Def '{def.Name}' cooldown constant");
            yield return new GeneratedName($"{def.Name}ContactDamage", $"Enemies.Def '{def.Name}' contact damage constant");
            yield return new GeneratedName($"{def.Name}HitboxWidth", $"Enemies.Def '{def.Name}' hitbox width constant");
            yield return new GeneratedName($"{def.Name}HitboxHeight", $"Enemies.Def '{def.Name}' hitbox height constant");
        }

        foreach (var lookup in EnemyLookupFunctions.Where(pair => state.UsedEnemyLookupMethods.Contains(pair.Key)))
        {
            yield return new GeneratedName(lookup.Value, $"Enemies.{lookup.Key} lookup helper function");
        }

        foreach (var layer in state.SpawnLayers.Where(layer => layer.Spawns.Count != 0))
        {
            yield return new GeneratedName($"{layer.RuntimeName}_used", $"Actors.{layer.MethodName} layer '{layer.LayerName}' used array");
            foreach (var fieldName in SpawnLookupFieldNames())
            {
                yield return new GeneratedName($"{layer.RuntimeName}_{fieldName}", $"Actors.{layer.MethodName} layer '{layer.LayerName}' {fieldName} lookup function");
            }
        }
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

    private static IEnumerable<FunctionSyntax> GeneratedLookupFunctions(IReadOnlyList<EnemyDef> enemyDefs, IReadOnlySet<string> usedMethods)
    {
        if (enemyDefs.Count == 0)
        {
            yield break;
        }

        foreach (var lookup in EnemyLookupFunctions.Where(pair => usedMethods.Contains(pair.Key)))
        {
            yield return LookupFunction(lookup.Value, enemyDefs, lookup.Key);
        }
    }

    private static IEnumerable<FunctionSyntax> GeneratedSpawnLookupFunctions(IReadOnlyList<ActorSpawnLayer> spawnLayers)
    {
        foreach (var layer in spawnLayers.Where(layer => layer.Spawns.Count != 0))
        {
            yield return SpawnLookupFunction(layer, "kind", spawn => new IdentifierSyntax(spawn.Kind));
            yield return SpawnLookupFunction(layer, "x", spawn => new ConstantSyntax(SplitWorldX(spawn.X, "actor spawn X").Low.ToString(CultureInfo.InvariantCulture)));
            yield return SpawnLookupFunction(layer, "xHi", spawn => new ConstantSyntax(SplitWorldX(spawn.X, "actor spawn X").High.ToString(CultureInfo.InvariantCulture)));
            yield return SpawnLookupFunction(layer, "y", spawn => new ConstantSyntax(SplitWorldY(spawn.Y, "actor spawn Y").Low.ToString(CultureInfo.InvariantCulture)));
            yield return SpawnLookupFunction(layer, "yHi", spawn => new ConstantSyntax(SplitWorldY(spawn.Y, "actor spawn Y").High.ToString(CultureInfo.InvariantCulture)));
            foreach (var fieldName in SpawnInitialFieldNames)
            {
                yield return SpawnLookupFunction(layer, fieldName, spawn => new ConstantSyntax(SpawnInitialFieldValue(spawn, fieldName).ToString(CultureInfo.InvariantCulture)));
            }
        }
    }

    private static FunctionSyntax SpawnLookupFunction(ActorSpawnLayer layer, string fieldName, Func<LogicalActorSpawn, ExpressionSyntax> selector)
    {
        var values = layer.Spawns.Select(selector).ToList();
        ExpressionSyntax value = values[^1];
        var firstKey = ExpressionKey(values[0]);
        if (firstKey is null || values.Any(expression => ExpressionKey(expression) != firstKey))
        {
            for (var index = values.Count - 2; index >= 0; index--)
            {
                value = new ConditionalExpressionSyntax(
                    new BinaryExpressionSyntax(new IdentifierSyntax("index"), new ConstantSyntax(index.ToString(CultureInfo.InvariantCulture)), Operator.Equal),
                    values[index],
                    value);
            }
        }

        return new FunctionSyntax(
            "u8",
            $"{layer.RuntimeName}_{fieldName}",
            [new ParameterSyntax("u8", "index")],
            new BlockSyntax([new ReturnSyntax(Maybe.From(value))]),
            isExpressionBodied: true,
            isInline: true);
    }

    private static string? ExpressionKey(ExpressionSyntax expression)
    {
        return expression switch
        {
            ConstantSyntax constant => $"const:{constant.Value}",
            IdentifierSyntax identifier => $"identifier:{identifier.Identifier}",
            _ => null,
        };
    }

    private static int SpawnInitialFieldValue(LogicalActorSpawn spawn, string fieldName)
    {
        if (fieldName == "active" && !spawn.Fields.ContainsKey(fieldName))
        {
            return 1;
        }

        return spawn.Fields.TryGetValue(fieldName, out var value) ? CheckedByte(value, $"actor spawn field '{fieldName}'") : 0;
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
                new StructFieldSyntax("u8", "yHi"),
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

    private sealed class ActorFrameworkState(
        Target2DCapabilities capabilities,
        bool supportsUpdate,
        bool supportsDraw,
        string? baseDirectory,
        IReadOnlyDictionary<string, string>? intrinsicFunctionNames = null)
    {
        private readonly Dictionary<string, ActorPool> pools = new(StringComparer.Ordinal);
        private readonly List<ActorPool> poolsInOrder = [];
        private readonly Dictionary<string, EnemyDef> enemyDefs = new(StringComparer.Ordinal);
        private readonly List<EnemyDef> enemyDefsInOrder = [];
        private readonly Dictionary<ActorSpawnLayerKey, ActorSpawnLayer> spawnLayers = [];
        private readonly List<ActorSpawnLayer> spawnLayersInOrder = [];
        private readonly Dictionary<string, int> activationCallCounts = new(StringComparer.Ordinal);
        private readonly HashSet<string> usedEnemyLookupMethods = new(StringComparer.Ordinal);
        private readonly IReadOnlyDictionary<string, string> intrinsicFunctions = intrinsicFunctionNames ?? new Dictionary<string, string>(StringComparer.Ordinal);

        public string TargetName { get; } = capabilities.Name;
        public string BaseDirectory { get; } = baseDirectory ?? Directory.GetCurrentDirectory();
        public bool SupportsUpdate { get; } = supportsUpdate;
        public bool SupportsDraw { get; } = supportsDraw;
        public int ScreenWidth { get; } = capabilities.ScreenPixels.Width;
        public int ScreenHeight { get; } = capabilities.ScreenPixels.Height;
        public IReadOnlyList<ActorPool> Pools => poolsInOrder;
        public IReadOnlyList<EnemyDef> EnemyDefs => enemyDefsInOrder;
        public IReadOnlyList<ActorSpawnLayer> SpawnLayers => spawnLayersInOrder;
        public IReadOnlySet<string> UsedEnemyLookupMethods => usedEnemyLookupMethods;
        public bool HasDirectives => pools.Count != 0 || enemyDefs.Count != 0 || spawnLayers.Count != 0;

        public string IntrinsicFunction(string intrinsicId)
        {
            if (intrinsicFunctions.TryGetValue(intrinsicId, out var functionName))
            {
                return functionName;
            }

            throw new InvalidOperationException(
                $"Actor framework requires intrinsic '{intrinsicId}' to be declared by an imported source library.");
        }

        public void AddPool(ActorPool pool)
        {
            if (!pools.TryAdd(pool.Name, pool))
            {
                throw new InvalidOperationException($"Actors.Pool for '{pool.Name}' is already declared.");
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
                throw new InvalidOperationException($"Enemies.Def for '{def.Name}' is already declared.");
            }

            enemyDefsInOrder.Add(def);
        }

        public void RecordEnemyLookupMethod(string method)
        {
            usedEnemyLookupMethods.Add(method);
        }

        public void AddSpawnLayer(ActorSpawnLayer spawnLayer)
        {
            var key = ActorSpawnLayerKey.From(spawnLayer.MethodName, spawnLayer.PoolName, spawnLayer.MapPath, spawnLayer.LayerName, spawnLayer.WindowLeft, spawnLayer.WindowWidth);
            if (spawnLayers.ContainsKey(key))
            {
                return;
            }

            var runtimeName = $"__{spawnLayer.PoolName}_spawn_{spawnLayers.Count}";
            var runtimeLayer = spawnLayer with { RuntimeName = runtimeName };
            spawnLayers.Add(key, runtimeLayer);
            spawnLayersInOrder.Add(runtimeLayer);
        }

        public ActorSpawnLayer SpawnLayer(SdkDotCallSyntax call)
        {
            var parameters = call.Parameters.ToList();
            if (call.Method == "SpawnLayer" && (parameters.Count != 3 || parameters[0] is not IdentifierSyntax))
            {
                throw new InvalidOperationException("Actors.SpawnLayer expects a pool identifier, a map path string, and a layer name string.");
            }

            if (call.Method == "SpawnWindow" && (parameters.Count != 5 || parameters[0] is not IdentifierSyntax))
            {
                throw new InvalidOperationException("Actors.SpawnWindow expects a pool identifier, a map path string, a layer name string, a literal left edge, and a literal width.");
            }

            var windowLeft = call.Method == "SpawnWindow" ? RequiredLiteralByte(parameters[3], "Actors.SpawnWindow argument 4") : (int?)null;
            var windowWidth = call.Method == "SpawnWindow" ? RequiredLiteralByte(parameters[4], "Actors.SpawnWindow argument 5") : (int?)null;
            var poolName = (IdentifierSyntax)parameters[0];
            var key = ActorSpawnLayerKey.From(
                call.Method,
                poolName.Identifier,
                StringLiteral(parameters[1], "Actors.SpawnLayer argument 2"),
                StringLiteral(parameters[2], "Actors.SpawnLayer argument 3"),
                windowLeft,
                windowWidth);
            return spawnLayers[key];
        }

        public IEnumerable<ActorSpawnLayer> SpawnLayersFor(string poolName)
        {
            return spawnLayersInOrder.Where(layer => layer.PoolName == poolName);
        }

        public string NextActivationPrefix(ActorSpawnLayer spawnLayer)
        {
            activationCallCounts.TryGetValue(spawnLayer.RuntimeName, out var count);
            activationCallCounts[spawnLayer.RuntimeName] = count + 1;
            return $"{spawnLayer.RuntimeName}_call{count}";
        }

        public void ValidateSpawnLayers()
        {
            foreach (var spawnLayer in spawnLayers.Values)
            {
                if (!pools.TryGetValue(spawnLayer.PoolName, out var pool))
                {
                    throw new InvalidOperationException($"Actors.SpawnLayer references undeclared pool '{spawnLayer.PoolName}'.");
                }

                if (spawnLayer.Spawns.Count > 255)
                {
                    throw new InvalidOperationException($"Actors.{spawnLayer.MethodName} for pool '{spawnLayer.PoolName}' reads {spawnLayer.Spawns.Count} spawn(s) from layer '{spawnLayer.LayerName}', exceeding the fixed runtime spawn table limit 255.");
                }

                var windowWidth = spawnLayer.WindowWidth ?? ScreenWidth;
                var simultaneousSpawns = MaxSimultaneousSpawns(spawnLayer.Spawns, windowWidth);
                if (simultaneousSpawns > pool.Capacity)
                {
                    throw new InvalidOperationException($"Actors.{spawnLayer.MethodName} for pool '{spawnLayer.PoolName}' can activate {simultaneousSpawns} spawn(s) in one camera window from layer '{spawnLayer.LayerName}', exceeding the declared capacity {pool.Capacity}.");
                }

                foreach (var spawn in spawnLayer.Spawns)
                {
                    if (!enemyDefs.ContainsKey(spawn.Kind))
                    {
                        throw new InvalidOperationException($"Actors.SpawnLayer layer '{spawnLayer.LayerName}' references unknown actor kind '{spawn.Kind}'. Declare Enemies.Def({spawn.Kind}, ...).");
                    }
                }
            }
        }

        private static int MaxSimultaneousSpawns(IReadOnlyList<LogicalActorSpawn> spawns, int windowWidth)
        {
            if (windowWidth <= 0 || spawns.Count == 0)
            {
                return 0;
            }

            var xValues = spawns.Select(spawn => spawn.X).Order().ToArray();
            var max = 0;
            var right = 0;
            for (var left = 0; left < xValues.Length; left++)
            {
                while (right < xValues.Length && xValues[right] - xValues[left] < windowWidth)
                {
                    right++;
                }

                max = Math.Max(max, right - left);
            }

            return max;
        }
    }

    private sealed record ActorPool(string Name, int Capacity);

    private sealed record GeneratedName(string Name, string Origin);

    private sealed record ActorSpawnLayer(
        string MethodName,
        string PoolName,
        string MapPath,
        string LayerName,
        int? WindowLeft,
        int? WindowWidth,
        IReadOnlyList<LogicalActorSpawn> Spawns,
        string RuntimeName);

    private sealed record ActorSpawnLayerKey(string MethodName, string PoolName, string MapPath, string LayerName, int? WindowLeft, int? WindowWidth)
    {
        public static ActorSpawnLayerKey From(string methodName, string poolName, string mapPath, string layerName, int? windowLeft, int? windowWidth) =>
            new(methodName, poolName, mapPath, layerName, windowLeft, windowWidth);
    }

    private sealed record ActorScreenProjection(IReadOnlyList<StatementSyntax> Declarations, IdentifierSyntax ScreenX, IdentifierSyntax ScreenY, ExpressionSyntax Visible);

    private sealed record KindBranch(string Kind, BlockSyntax Block);

    private sealed record EnemySpriteBudget(EnemyDef Def, ActorMetaspriteGeometry Geometry, int BusiestRelativeScanlineSprites);

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
