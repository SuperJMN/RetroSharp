using CSharpFunctionalExtensions;
using RetroSharp.Parser;

namespace RetroSharp.Sdk;

public static partial class ActorFrameworkLowerer
{
    private static partial class Projectiles
    {
        private const string ProjectileStructName = "Projectile";
        private const string ProjectileSpawnRequestStructName = "ProjectileSpawnRequest";
        private static readonly IReadOnlyDictionary<string, int> ProjectileTeamIds = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["Hero"] = 1,
            ["Enemy"] = 2,
        };

        private static readonly IReadOnlyDictionary<string, int> ProjectileBehaviorIds = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["Linear"] = 1,
            ["GravityArc"] = 2,
        };

        private static readonly IReadOnlyDictionary<string, int> ProjectileTileCollisionIds = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["None"] = 0,
            ["Expire"] = 1,
            ["Bounce"] = 2,
        };

        public static void CollectDirectives(StatementSyntax statement, ActorFrameworkState state)
        {
            switch (statement)
            {
                case ExpressionStatementSyntax { Expression: QualifiedCallSyntax { Qualifier: "Projectiles", Method: "Pool" } call }:
                    state.Projectiles.AddPool(ReadPool(call));
                    break;
                case ExpressionStatementSyntax { Expression: QualifiedCallSyntax { Qualifier: "Projectiles", Method: "Def" } call }:
                    state.Projectiles.AddDefinition(ReadDefinition(call));
                    break;
            }
        }

        public static ProjectilePool ReadPool(QualifiedCallSyntax call)
        {
            var parameters = call.Parameters.ToList();
            if (parameters.Count == 0 || parameters[0] is not IdentifierSyntax poolName)
            {
                throw new InvalidOperationException("Projectiles.Pool expects a pool identifier followed by named literal limits.");
            }

            var name = poolName.Identifier;
            var namedArguments = NamedArguments(parameters.Skip(1), $"Projectiles.Pool for '{name}'");
            var heroCapacity = OptionalProjectileLiteralByte(namedArguments, "hero", 3, name);
            var enemyCapacity = OptionalProjectileLiteralByte(namedArguments, "enemy", 8, name);
            var requestCapacity = OptionalProjectileLiteralByte(namedArguments, "requests", 8, name);
            var offscreenMargin = OptionalProjectileLiteralByte(namedArguments, "offscreenMargin", 16, name);
            var effectPoolName = OptionalIdentifier(namedArguments, "effects", name, "Projectiles.Pool");

            foreach (var argumentName in namedArguments.Keys)
            {
                if (argumentName is not "hero" and not "enemy" and not "requests" and not "offscreenMargin" and not "effects")
                {
                    throw new InvalidOperationException($"Projectiles.Pool for '{name}' has unsupported property '{argumentName}'.");
                }
            }

            if (heroCapacity == 0 || enemyCapacity == 0 || requestCapacity == 0)
            {
                throw new InvalidOperationException($"Projectiles.Pool for '{name}' requires hero, enemy, and request capacities from 1 to 255.");
            }

            return new ProjectilePool(name, heroCapacity, enemyCapacity, requestCapacity, offscreenMargin, effectPoolName);
        }

        public static ProjectileDef ReadDefinition(QualifiedCallSyntax call)
        {
            var parameters = call.Parameters.ToList();
            if (parameters.Count == 0 || parameters[0] is not IdentifierSyntax projectileName)
            {
                throw new InvalidOperationException("Projectiles.Def expects a projectile identifier as its first argument.");
            }

            var name = projectileName.Identifier;
            var namedArguments = NamedArguments(parameters.Skip(1), $"Projectiles.Def for '{name}'");
            var team = RequiredProjectileIdentifier(namedArguments, "team", name);
            if (!ProjectileTeamIds.ContainsKey(team))
            {
                throw new InvalidOperationException($"Unknown projectile team '{team}'.");
            }

            var sprite = RequiredProjectileIdentifier(namedArguments, "sprite", name);
            var behavior = OptionalProjectileIdentifier(namedArguments, "behavior", "Linear", name);
            if (!ProjectileBehaviorIds.ContainsKey(behavior))
            {
                throw new InvalidOperationException($"Unknown projectile behavior '{behavior}'.");
            }

            var tileCollision = OptionalProjectileIdentifier(namedArguments, "tileCollision", "None", name);
            if (!ProjectileTileCollisionIds.ContainsKey(tileCollision))
            {
                throw new InvalidOperationException($"Unknown projectile tile collision '{tileCollision}'.");
            }

            var speedX = RequiredProjectileLiteralByte(namedArguments, "speedX", name);
            var speedY = RequiredProjectileLiteralByte(namedArguments, "speedY", name);
            var damage = RequiredProjectileLiteralByte(namedArguments, "damage", name);
            var lifetime = RequiredProjectileLiteralByte(namedArguments, "lifetime", name);
            var hitboxWidth = RequiredProjectileLiteralByte(namedArguments, "hitboxWidth", name);
            var hitboxHeight = RequiredProjectileLiteralByte(namedArguments, "hitboxHeight", name);
            var bounceSpeedY = OptionalProjectileDefLiteralByte(namedArguments, "bounceSpeedY", 0, name);
            if (tileCollision == "Bounce" && bounceSpeedY == 0)
            {
                throw new InvalidOperationException($"Projectiles.Def for '{name}' with tileCollision: Bounce requires 'bounceSpeedY' to be a non-zero literal byte value.");
            }

            var spawnEffect = OptionalProjectileIdentifier(namedArguments, "spawnEffect", name);
            var impactEffect = OptionalProjectileIdentifier(namedArguments, "impactEffect", name);
            var expireEffect = OptionalProjectileIdentifier(namedArguments, "expireEffect", name);

            foreach (var argumentName in namedArguments.Keys)
            {
                if (argumentName is not "team" and not "sprite" and not "behavior" and not "tileCollision" and not "speedX" and not "speedY" and not "damage" and not "lifetime" and not "hitboxWidth" and not "hitboxHeight" and not "bounceSpeedY" and not "spawnEffect" and not "impactEffect" and not "expireEffect")
                {
                    throw new InvalidOperationException($"Projectiles.Def for '{name}' has unsupported property '{argumentName}'.");
                }
            }

            return new ProjectileDef(name, team, sprite, behavior, tileCollision, speedX, speedY, damage, lifetime, hitboxWidth, hitboxHeight, bounceSpeedY, spawnEffect, impactEffect, expireEffect);
        }

        public static bool TryRewrite(
            StatementSyntax statement,
            ActorFrameworkState state,
            out IReadOnlyList<StatementSyntax> statements)
        {
            switch (statement)
            {
                case ExpressionStatementSyntax { Expression: QualifiedCallSyntax { Qualifier: "Projectiles", Method: "Pool" } call }:
                    statements = ProjectilePoolDeclarations(state.Projectiles.Pool(ReadPool(call).Name));
                    return true;
                case ExpressionStatementSyntax { Expression: QualifiedCallSyntax { Qualifier: "Projectiles", Method: "Def" } }:
                    statements = [];
                    return true;
                case ExpressionStatementSyntax { Expression: QualifiedCallSyntax call }
                    when state.Projectiles.TryPool(call.Qualifier, out var pool):
                    statements = call.Method switch
                    {
                        "Request" => ProjectileRequestStatements(pool, call, state),
                        "ProcessRequests" => ProjectileProcessRequestStatements(pool, call, state),
                        "Update" => UpdateStatements(pool, call, state),
                        "Draw" => ProjectileDrawStatements(pool, call, state),
                        "TouchTiles" => ProjectileTouchTilesStatements(pool, call, state),
                        "TouchActors" => ProjectileTouchActorsStatements(pool, call, state),
                        "TouchHero" => ProjectileTouchHeroStatements(pool, call, state),
                        _ => [RewriteStatement(statement, state)!],
                    };
                    return true;
                default:
                    statements = [];
                    return false;
            }
        }

        public static bool TryRewriteExpression(ExpressionSyntax expression, out ExpressionSyntax rewritten)
        {
            if (expression is QualifiedCallSyntax { Qualifier: "Projectiles" } call)
            {
                throw new InvalidOperationException($"Projectiles.{call.Method} can only be used as a statement.");
            }

            rewritten = null!;
            return false;
        }

        public static IReadOnlyList<StatementSyntax> UpdateStatements(ProjectilePool pool, QualifiedCallSyntax call, ActorFrameworkState state)
        {
            RequireNoArguments(call);
            RequireProjectileDefs(state, $"{pool.Name}.Update");

            return ProjectileUpdateTeamStatements(pool, "Hero", state)
                .Concat(ProjectileUpdateTeamStatements(pool, "Enemy", state))
                .ToList();
        }

        public static void ValidateEffects(ActorFrameworkState state)
        {
            foreach (var pool in state.Projectiles.Pools)
            {
                if (pool.EffectPoolName is not null && !state.Effects.TryPool(pool.EffectPoolName, out _))
                {
                    throw new InvalidOperationException($"Projectiles.Pool for '{pool.Name}' references unknown effect pool '{pool.EffectPoolName}'. Declare Effects.Pool({pool.EffectPoolName}, ...).");
                }
            }

            foreach (var def in state.Projectiles.Definitions)
            {
                foreach (var effectName in def.EffectNames())
                {
                    if (!state.Effects.Definitions.Any(effect => effect.Name == effectName))
                    {
                        throw new InvalidOperationException($"Projectiles.Def for '{def.Name}' references unknown effect kind '{effectName}'. Declare Effects.Def({effectName}, ...).");
                    }
                }
            }
        }

        public static void AddGeneratedStructs(ActorFrameworkState state, IList<StructSyntax> structs)
        {
            if (state.Projectiles.Pools.Count == 0)
            {
                return;
            }

            if (structs.Any(structSyntax => structSyntax.Name == ProjectileStructName))
            {
                throw new InvalidOperationException("Projectiles.Pool cannot generate framework struct 'Projectile' because a struct named 'Projectile' is already declared.");
            }

            if (structs.Any(structSyntax => structSyntax.Name == ProjectileSpawnRequestStructName))
            {
                throw new InvalidOperationException("Projectiles.Pool cannot generate framework struct 'ProjectileSpawnRequest' because a struct named 'ProjectileSpawnRequest' is already declared.");
            }

            structs.Add(ProjectileStruct());
            structs.Add(ProjectileSpawnRequestStruct());
        }

        public static IEnumerable<ConstDeclarationSyntax> GeneratedConstants(IReadOnlyList<ProjectileDef> projectileDefs)
        {
            foreach (var team in projectileDefs.Select(def => def.Team).Distinct(StringComparer.Ordinal))
            {
                if (!ProjectileTeamIds.TryGetValue(team, out var id))
                {
                    throw new InvalidOperationException($"Unknown projectile team '{team}'.");
                }

                yield return Constant(team, id);
            }

            foreach (var behavior in projectileDefs.Select(def => def.Behavior).Distinct(StringComparer.Ordinal))
            {
                if (!ProjectileBehaviorIds.TryGetValue(behavior, out var id))
                {
                    throw new InvalidOperationException($"Unknown projectile behavior '{behavior}'.");
                }

                yield return Constant(behavior, id);
            }

            for (var index = 0; index < projectileDefs.Count; index++)
            {
                var def = projectileDefs[index];
                if (!ProjectileTileCollisionIds.TryGetValue(def.TileCollision, out var tileCollisionId))
                {
                    throw new InvalidOperationException($"Unknown projectile tile collision '{def.TileCollision}'.");
                }

                yield return Constant(def.Name, index + 1);
                yield return Constant($"{def.Name}Team", new IdentifierSyntax(def.Team));
                yield return Constant($"{def.Name}Behavior", new IdentifierSyntax(def.Behavior));
                yield return Constant($"{def.Name}TileCollision", tileCollisionId);
                yield return Constant($"{def.Name}SpeedX", def.SpeedX);
                yield return Constant($"{def.Name}SpeedY", def.SpeedY);
                yield return Constant($"{def.Name}Damage", def.Damage);
                yield return Constant($"{def.Name}Lifetime", def.Lifetime);
                yield return Constant($"{def.Name}HitboxWidth", def.HitboxWidth);
                yield return Constant($"{def.Name}HitboxHeight", def.HitboxHeight);
                yield return Constant($"{def.Name}BounceSpeedY", def.BounceSpeedY);
            }
        }

        private static IReadOnlyList<StatementSyntax> ProjectilePoolDeclarations(ProjectilePool pool)
        {
            return
            [
                new DeclarationSyntax(
                    ProjectileStructName,
                    pool.HeroArrayName,
                    Maybe.From<ExpressionSyntax>(Constant(pool.HeroCapacity)),
                    Maybe<ExpressionSyntax>.None),
                new DeclarationSyntax(
                    ProjectileStructName,
                    pool.EnemyArrayName,
                    Maybe.From<ExpressionSyntax>(Constant(pool.EnemyCapacity)),
                    Maybe<ExpressionSyntax>.None),
                new DeclarationSyntax(
                    ProjectileSpawnRequestStructName,
                    pool.RequestArrayName,
                    Maybe.From<ExpressionSyntax>(Constant(pool.RequestCapacity)),
                    Maybe<ExpressionSyntax>.None),
                ArrayLoop(
                    pool.HeroArrayName,
                    $"__{pool.Name}_init_hero_i",
                    FieldAssignment(pool.HeroArrayName, $"__{pool.Name}_init_hero_i", "active", "=", Constant(0))),
                ArrayLoop(
                    pool.EnemyArrayName,
                    $"__{pool.Name}_init_enemy_i",
                    FieldAssignment(pool.EnemyArrayName, $"__{pool.Name}_init_enemy_i", "active", "=", Constant(0))),
                ArrayLoop(
                    pool.RequestArrayName,
                    $"__{pool.Name}_init_request_i",
                    FieldAssignment(pool.RequestArrayName, $"__{pool.Name}_init_request_i", "active", "=", Constant(0))),
            ];
        }

        public static IEnumerable<GeneratedName> GeneratedNames(ActorFrameworkState state)
        {
            if (state.Projectiles.Pools.Count != 0)
            {
                yield return new GeneratedName(ProjectileStructName, "framework struct 'Projectile'");
                yield return new GeneratedName(ProjectileSpawnRequestStructName, "framework struct 'ProjectileSpawnRequest'");
            }

            foreach (var team in state.Projectiles.Definitions.Select(def => def.Team).Distinct(StringComparer.Ordinal))
            {
                yield return new GeneratedName(team, $"projectile team '{team}' constant");
            }

            foreach (var behavior in state.Projectiles.Definitions.Select(def => def.Behavior).Distinct(StringComparer.Ordinal))
            {
                yield return new GeneratedName(behavior, $"projectile behavior '{behavior}' constant");
            }

            foreach (var def in state.Projectiles.Definitions)
            {
                yield return new GeneratedName(def.Name, $"Projectiles.Def '{def.Name}' kind constant");
                yield return new GeneratedName($"{def.Name}Team", $"Projectiles.Def '{def.Name}' team constant");
                yield return new GeneratedName($"{def.Name}Behavior", $"Projectiles.Def '{def.Name}' behavior constant");
                yield return new GeneratedName($"{def.Name}TileCollision", $"Projectiles.Def '{def.Name}' tile collision constant");
                yield return new GeneratedName($"{def.Name}SpeedX", $"Projectiles.Def '{def.Name}' speed-x constant");
                yield return new GeneratedName($"{def.Name}SpeedY", $"Projectiles.Def '{def.Name}' speed-y constant");
                yield return new GeneratedName($"{def.Name}Damage", $"Projectiles.Def '{def.Name}' damage constant");
                yield return new GeneratedName($"{def.Name}Lifetime", $"Projectiles.Def '{def.Name}' lifetime constant");
                yield return new GeneratedName($"{def.Name}HitboxWidth", $"Projectiles.Def '{def.Name}' hitbox width constant");
                yield return new GeneratedName($"{def.Name}HitboxHeight", $"Projectiles.Def '{def.Name}' hitbox height constant");
                yield return new GeneratedName($"{def.Name}BounceSpeedY", $"Projectiles.Def '{def.Name}' bounce-speed-y constant");
            }
        }

        private static StructSyntax ProjectileStruct()
        {
            return new StructSyntax(
                ProjectileStructName,
                [
                    new StructFieldSyntax("u8", "kind"),
                    new StructFieldSyntax("u8", "active"),
                    new StructFieldSyntax("u8", "x"),
                    new StructFieldSyntax("u8", "xHi"),
                    new StructFieldSyntax("u8", "y"),
                    new StructFieldSyntax("u8", "yHi"),
                    new StructFieldSyntax("i8", "vx"),
                    new StructFieldSyntax("i8", "vy"),
                    new StructFieldSyntax("u8", "damage"),
                    new StructFieldSyntax("u8", "age"),
                    new StructFieldSyntax("u8", "owner"),
                    new StructFieldSyntax("u8", "direction"),
                ]);
        }

        private static StructSyntax ProjectileSpawnRequestStruct()
        {
            return new StructSyntax(
                ProjectileSpawnRequestStructName,
                [
                    new StructFieldSyntax("u8", "kind"),
                    new StructFieldSyntax("u8", "active"),
                    new StructFieldSyntax("u8", "x"),
                    new StructFieldSyntax("u8", "xHi"),
                    new StructFieldSyntax("u8", "y"),
                    new StructFieldSyntax("u8", "yHi"),
                    new StructFieldSyntax("u8", "direction"),
                    new StructFieldSyntax("u8", "owner"),
                ]);
        }
    }

    private sealed record ProjectilePool(string Name, int HeroCapacity, int EnemyCapacity, int RequestCapacity, int OffscreenMargin, string? EffectPoolName)
    {
        public string HeroArrayName => $"{Name}Hero";
        public string EnemyArrayName => $"{Name}Enemy";
        public string RequestArrayName => $"{Name}Requests";

        public string ArrayNameForTeam(string team)
        {
            return team switch
            {
                "Hero" => HeroArrayName,
                "Enemy" => EnemyArrayName,
                _ => throw new InvalidOperationException($"Unknown projectile team '{team}'."),
            };
        }
    }

    private sealed record ProjectileDef(
        string Name,
        string Team,
        string Sprite,
        string Behavior,
        string TileCollision,
        int SpeedX,
        int SpeedY,
        int Damage,
        int Lifetime,
        int HitboxWidth,
        int HitboxHeight,
        int BounceSpeedY,
        string? SpawnEffect,
        string? ImpactEffect,
        string? ExpireEffect)
    {
        public IEnumerable<string> EffectNames()
        {
            if (SpawnEffect is not null)
            {
                yield return SpawnEffect;
            }

            if (ImpactEffect is not null)
            {
                yield return ImpactEffect;
            }

            if (ExpireEffect is not null)
            {
                yield return ExpireEffect;
            }
        }
    }
}
