using System.Globalization;
using CSharpFunctionalExtensions;
using RetroSharp.Core;
using RetroSharp.Core.Sdk.Tiled;
using RetroSharp.Core.Targeting;
using RetroSharp.Parser;

namespace RetroSharp.Sdk;

public static partial class ActorFrameworkLowerer
{
    private static partial class Actors
    {
        private const string ActorStructName = "Actor";
        private const string AnimationFrameIntrinsic = "animation_frame";
        private const string CameraScreenAabbHitTopIntrinsic = "camera_screen_aabb_hit_top";

        private static readonly IReadOnlyDictionary<string, int> BehaviorIds = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["Walker"] = 1,
            ["Flyer"] = 2,
            ["Patrol"] = 3,
            ["Shooter"] = 4,
            ["Chaser"] = 5,
            ["Hazard"] = 6,
        };

        private static readonly IReadOnlyDictionary<ActorFrameworkRole, EnemyLookupDescriptor> EnemyLookupFunctions = new Dictionary<ActorFrameworkRole, EnemyLookupDescriptor>
        {
            [ActorFrameworkRole.ActorEnemyBehavior] = new("enemy_behavior", "Behavior", "Enemies.Behavior"),
            [ActorFrameworkRole.ActorEnemySpeed] = new("enemy_speed", "Speed", "Enemies.Speed"),
            [ActorFrameworkRole.ActorEnemyHp] = new("enemy_hp", "Hp", "Enemies.Hp"),
            [ActorFrameworkRole.ActorEnemyCooldown] = new("enemy_cooldown", "Cooldown", "Enemies.Cooldown"),
            [ActorFrameworkRole.ActorEnemyContactDamage] = new("enemy_contact_damage", "ContactDamage", "Enemies.ContactDamage"),
            [ActorFrameworkRole.ActorEnemyHitboxWidth] = new("enemy_hitbox_width", "HitboxWidth", "Enemies.HitboxWidth"),
            [ActorFrameworkRole.ActorEnemyHitboxHeight] = new("enemy_hitbox_height", "HitboxHeight", "Enemies.HitboxHeight"),
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

        private sealed record EnemyLookupDescriptor(string FunctionName, string ConstantSuffix, string DisplayName);

        private sealed record EnemySpriteBudget(EnemyDef Def, ActorMetaspriteGeometry Geometry, int BusiestRelativeScanlineSprites);

        public static string? LookupDisplayName(ActorFrameworkRole role)
        {
            return EnemyLookupFunctions.TryGetValue(role, out var lookup) ? lookup.DisplayName : null;
        }

        public static void CollectDirectives(StatementSyntax statement, ActorFrameworkState state)
        {
            if (!TryActorFrameworkStatementCall(statement, state, out var call))
            {
                return;
            }

            switch (call.Role)
            {
                case ActorFrameworkRole.ActorPool:
                    state.AddPool(ReadPool(call));
                    break;
                case ActorFrameworkRole.ActorEnemyDef:
                    state.AddEnemyDef(ReadDefinition(call));
                    break;
                case ActorFrameworkRole.ActorSpawnLayer:
                case ActorFrameworkRole.ActorSpawnWindow:
                    state.AddSpawnLayer(ReadSpawnDirective(call, state.BaseDirectory));
                    break;
            }
        }

        public static ActorPool ReadPool(ActorFrameworkCall call)
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

        private static EnemyDef ReadDefinition(ActorFrameworkCall call)
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

        public static ActorSpawnLayer ReadSpawnDirective(ActorFrameworkCall call, string baseDirectory)
        {
            var parameters = call.Parameters.ToList();
            if (call.Role == ActorFrameworkRole.ActorSpawnLayer && (parameters.Count != 3 || parameters[0] is not IdentifierSyntax))
            {
                throw new InvalidOperationException("Actors.SpawnLayer expects a pool identifier, a map path string, and a layer name string.");
            }

            if (call.Role == ActorFrameworkRole.ActorSpawnWindow && (parameters.Count != 5 || parameters[0] is not IdentifierSyntax))
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
            if (call.Role == ActorFrameworkRole.ActorSpawnWindow)
            {
                windowLeft = RequiredLiteralByte(parameters[3], "Actors.SpawnWindow argument 4");
                windowWidth = RequiredLiteralByte(parameters[4], "Actors.SpawnWindow argument 5");
            }

            return new ActorSpawnLayer(call.SpawnMethodName, poolName.Identifier, mapPath, layerName, windowLeft, windowWidth, spawns, RuntimeName: string.Empty);
        }

        public static bool TryRewrite(
            StatementSyntax statement,
            ActorFrameworkState state,
            out IReadOnlyList<StatementSyntax> statements)
        {
            if (TryActorFrameworkStatementCall(statement, state, out var call))
            {
                switch (call.Role)
                {
                    case ActorFrameworkRole.ActorSpawnLayer:
                    case ActorFrameworkRole.ActorSpawnWindow:
                        statements = RewriteSpawnDirective(call, state);
                        return true;
                    case ActorFrameworkRole.ActorPool:
                        statements = PoolDeclarations(state.Pool(ReadPool(call).Name), state);
                        return true;
                    case ActorFrameworkRole.ActorEnemyDef:
                        statements = [];
                        return true;
                }
            }

            if (statement is ExpressionStatementSyntax { Expression: QualifiedCallSyntax poolCall }
                && state.TryPool(poolCall.Qualifier, out var pool))
            {
                statements = poolCall.Method switch
                {
                    "Update" => [PoolUpdateLoop(pool, poolCall, state)],
                    "Draw" => PoolDrawStatements(pool, poolCall, state),
                    "TouchTiles" => PoolTouchTilesStatements(pool, poolCall, state),
                    "LandOnTiles" => PoolLandOnTilesStatements(pool, poolCall, state),
                    "TouchPlayer" => PoolTouchPlayerStatements(pool, poolCall, state),
                    _ => [RewriteStatement(statement, state)!],
                };
                return true;
            }

            statements = [];
            return false;
        }

        public static bool TryRewriteExpression(
            ExpressionSyntax expression,
            ActorFrameworkState state,
            out ExpressionSyntax rewritten)
        {
            if (expression is FunctionCall functionCall &&
                state.Roles.TryRole(functionCall, out var call))
            {
                rewritten = RewriteExpressionCall(call, state);
                return true;
            }

            rewritten = null!;
            return false;
        }

        public static ForSyntax PoolUpdateLoop(ActorPool pool, QualifiedCallSyntax call, ActorFrameworkState state)
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

            return PoolLoop(
                pool,
                indexName,
                ActiveGuard(
                    pool.Name,
                    indexName,
                    PooledKindDispatch(pool.Name, indexName, branches, $"{pool.Name} actor dispatch requires at least one Enemies.Def declaration.")));
        }

        public static void ValidateSpawnLayers(ActorFrameworkState state)
        {
            foreach (var layer in state.SpawnLayers)
            {
                if (!state.TryPool(layer.PoolName, out var pool))
                {
                    throw new InvalidOperationException($"Actors.SpawnLayer references undeclared pool '{layer.PoolName}'.");
                }

                if (layer.Spawns.Count > 255)
                {
                    throw new InvalidOperationException($"Actors.{layer.MethodName} for pool '{layer.PoolName}' reads {layer.Spawns.Count} spawn(s) from layer '{layer.LayerName}', exceeding the fixed runtime spawn table limit 255.");
                }

                var windowWidth = layer.WindowWidth ?? state.ScreenWidth;
                var simultaneousSpawns = MaxSimultaneousSpawns(layer.Spawns, windowWidth);
                if (simultaneousSpawns > pool.Capacity)
                {
                    throw new InvalidOperationException($"Actors.{layer.MethodName} for pool '{layer.PoolName}' can activate {simultaneousSpawns} spawn(s) in one camera window from layer '{layer.LayerName}', exceeding the declared capacity {pool.Capacity}.");
                }

                foreach (var spawn in layer.Spawns)
                {
                    if (!state.EnemyDefs.Any(def => def.Name == spawn.Kind))
                    {
                        throw new InvalidOperationException($"Actors.SpawnLayer layer '{layer.LayerName}' references unknown actor kind '{spawn.Kind}'. Declare Enemies.Def({spawn.Kind}, ...).");
                    }
                }
            }
        }

        public static void AddGeneratedStructs(ActorFrameworkState state, IList<StructSyntax> structs)
        {
            if (state.Pools.Count == 0)
            {
                return;
            }

            if (structs.Any(structSyntax => structSyntax.Name == ActorStructName))
            {
                throw new InvalidOperationException("Actors.Pool cannot generate framework struct 'Actor' because a struct named 'Actor' is already declared.");
            }

            structs.Add(ActorStruct());
        }

        public static IEnumerable<ConstDeclarationSyntax> GeneratedConstants(IReadOnlyList<EnemyDef> enemyDefs)
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

        public static IEnumerable<FunctionSyntax> GeneratedFunctions(ActorFrameworkState state) =>
            GeneratedLookupFunctions(state.EnemyDefs, state.UsedEnemyLookupMethods)
                .Concat(GeneratedSpawnLookupFunctions(state.SpawnLayers));

        private static IReadOnlyList<StatementSyntax> RewriteSpawnDirective(ActorFrameworkCall call, ActorFrameworkState state)
        {
            var layer = state.SpawnLayer(SpawnLayerKey(call));
            return RuntimeSpawnActivationStatements(layer, state.NextActivationPrefix(layer), state.ScreenWidth);
        }

        private static ActorSpawnLayerKey SpawnLayerKey(ActorFrameworkCall call)
        {
            var parameters = call.Parameters.ToList();
            if (call.Role == ActorFrameworkRole.ActorSpawnLayer && (parameters.Count != 3 || parameters[0] is not IdentifierSyntax))
            {
                throw new InvalidOperationException("Actors.SpawnLayer expects a pool identifier, a map path string, and a layer name string.");
            }

            if (call.Role == ActorFrameworkRole.ActorSpawnWindow && (parameters.Count != 5 || parameters[0] is not IdentifierSyntax))
            {
                throw new InvalidOperationException("Actors.SpawnWindow expects a pool identifier, a map path string, a layer name string, a literal left edge, and a literal width.");
            }

            var windowLeft = call.Role == ActorFrameworkRole.ActorSpawnWindow ? RequiredLiteralByte(parameters[3], "Actors.SpawnWindow argument 4") : (int?)null;
            var windowWidth = call.Role == ActorFrameworkRole.ActorSpawnWindow ? RequiredLiteralByte(parameters[4], "Actors.SpawnWindow argument 5") : (int?)null;
            var poolName = (IdentifierSyntax)parameters[0];
            return ActorSpawnLayerKey.From(
                call.SpawnMethodName,
                poolName.Identifier,
                StringLiteral(parameters[1], "Actors.SpawnLayer argument 2"),
                StringLiteral(parameters[2], "Actors.SpawnLayer argument 3"),
                windowLeft,
                windowWidth);
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

        private static IEnumerable<FunctionSyntax> GeneratedLookupFunctions(IReadOnlyList<EnemyDef> enemyDefs, IReadOnlySet<ActorFrameworkRole> usedMethods)
        {
            if (enemyDefs.Count == 0)
            {
                yield break;
            }

            foreach (var lookup in EnemyLookupFunctions.Where(pair => usedMethods.Contains(pair.Key)))
            {
                yield return LookupFunction(lookup.Value.FunctionName, enemyDefs, lookup.Value.ConstantSuffix);
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

        public static IEnumerable<GeneratedName> GeneratedNames(ActorFrameworkState state)
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
                yield return new GeneratedName(lookup.Value.FunctionName, $"{lookup.Value.DisplayName} lookup helper function");
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
    }

    private const string SdkRoleAttribute = "sdk_role";

    private static readonly IReadOnlyDictionary<string, ActorFrameworkRole> RolesByMetadata = new Dictionary<string, ActorFrameworkRole>(StringComparer.Ordinal)
    {
        ["actor_pool"] = ActorFrameworkRole.ActorPool,
        ["actor_spawn_layer"] = ActorFrameworkRole.ActorSpawnLayer,
        ["actor_spawn_window"] = ActorFrameworkRole.ActorSpawnWindow,
        ["actor_enemy_def"] = ActorFrameworkRole.ActorEnemyDef,
        ["actor_enemy_behavior"] = ActorFrameworkRole.ActorEnemyBehavior,
        ["actor_enemy_speed"] = ActorFrameworkRole.ActorEnemySpeed,
        ["actor_enemy_hp"] = ActorFrameworkRole.ActorEnemyHp,
        ["actor_enemy_cooldown"] = ActorFrameworkRole.ActorEnemyCooldown,
        ["actor_enemy_contact_damage"] = ActorFrameworkRole.ActorEnemyContactDamage,
        ["actor_enemy_hitbox_width"] = ActorFrameworkRole.ActorEnemyHitboxWidth,
        ["actor_enemy_hitbox_height"] = ActorFrameworkRole.ActorEnemyHitboxHeight,
    };

    private enum ActorFrameworkRole
    {
        ActorPool,
        ActorSpawnLayer,
        ActorSpawnWindow,
        ActorEnemyDef,
        ActorEnemyBehavior,
        ActorEnemySpeed,
        ActorEnemyHp,
        ActorEnemyCooldown,
        ActorEnemyContactDamage,
        ActorEnemyHitboxWidth,
        ActorEnemyHitboxHeight,
    }

    private sealed record ActorFrameworkCall(
        ActorFrameworkRole Role,
        IReadOnlyList<ExpressionSyntax> Parameters,
        string DisplayName)
    {
        public string SpawnMethodName => Role switch
        {
            ActorFrameworkRole.ActorSpawnLayer => "SpawnLayer",
            ActorFrameworkRole.ActorSpawnWindow => "SpawnWindow",
            _ => throw new InvalidOperationException($"Actor framework role '{Role}' is not a spawn directive."),
        };
    }

    private sealed class ActorFrameworkRoleIndex
    {
        private readonly IReadOnlyDictionary<string, ActorFrameworkRole> rolesByFunction;

        private ActorFrameworkRoleIndex(IReadOnlyDictionary<string, ActorFrameworkRole> rolesByFunction)
        {
            this.rolesByFunction = rolesByFunction;
        }

        public static ActorFrameworkRoleIndex Build(ProgramSyntax program)
        {
            var roles = new Dictionary<string, ActorFrameworkRole>(StringComparer.Ordinal);
            foreach (var function in program.Functions)
            {
                var roleName = TargetAttributeReader.StringArgument(function, SdkRoleAttribute);
                if (roleName is null)
                {
                    continue;
                }

                if (!RolesByMetadata.TryGetValue(roleName, out var role))
                {
                    throw new InvalidOperationException($"Unknown SDK role '{roleName}' on function '{function.Name}'.");
                }

                roles[function.Name] = role;
            }

            return new ActorFrameworkRoleIndex(roles);
        }

        public bool TryRole(FunctionCall call, out ActorFrameworkCall actorCall)
        {
            if (rolesByFunction.TryGetValue(call.Name, out var role))
            {
                actorCall = new ActorFrameworkCall(role, call.Parameters.ToList(), DisplayName(role));
                return true;
            }

            actorCall = null!;
            return false;
        }
    }

    private static string DisplayName(ActorFrameworkRole role)
    {
        return role switch
        {
            ActorFrameworkRole.ActorPool => "Actors.Pool",
            ActorFrameworkRole.ActorSpawnLayer => "Actors.SpawnLayer",
            ActorFrameworkRole.ActorSpawnWindow => "Actors.SpawnWindow",
            ActorFrameworkRole.ActorEnemyDef => "Enemies.Def",
            _ when Actors.LookupDisplayName(role) is { } lookupDisplayName => lookupDisplayName,
            _ => role.ToString(),
        };
    }

    private sealed record ActorPool(string Name, int Capacity);

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
