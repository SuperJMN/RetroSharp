using System.Globalization;
using CSharpFunctionalExtensions;
using RetroSharp.Core;
using RetroSharp.Parser;

namespace RetroSharp.Sdk;

public static partial class ActorFrameworkLowerer
{
    private static partial class Projectiles
    {
        private static string? OptionalIdentifier(
            IReadOnlyDictionary<string, ExpressionSyntax> arguments,
            string name,
            string ownerName,
            string context)
        {
            if (!arguments.TryGetValue(name, out var expression))
            {
                return null;
            }

            if (expression is IdentifierSyntax identifier)
            {
                return identifier.Identifier;
            }

            throw new InvalidOperationException($"{context} for '{ownerName}' requires '{name}' to be an identifier.");
        }

        private static LValue ExpressionToLValue(ExpressionSyntax expression, string context)
        {
            return expression switch
            {
                IdentifierSyntax identifier => new IdentifierLValue(identifier.Identifier),
                MemberAccessSyntax memberAccess => new MemberAccessLValue(memberAccess),
                _ => throw new InvalidOperationException($"{context} must be an assignable identifier or field."),
            };
        }

        private static string RequiredProjectileIdentifier(
            IReadOnlyDictionary<string, ExpressionSyntax> arguments,
            string name,
            string projectileName)
        {
            if (!arguments.TryGetValue(name, out var expression))
            {
                throw new InvalidOperationException($"Projectiles.Def for '{projectileName}' requires '{name}'.");
            }

            if (expression is IdentifierSyntax identifier)
            {
                return identifier.Identifier;
            }

            throw new InvalidOperationException($"Projectiles.Def for '{projectileName}' requires '{name}' to be an identifier.");
        }

        private static string OptionalProjectileIdentifier(
            IReadOnlyDictionary<string, ExpressionSyntax> arguments,
            string name,
            string defaultValue,
            string projectileName)
        {
            if (!arguments.TryGetValue(name, out var expression))
            {
                return defaultValue;
            }

            if (expression is IdentifierSyntax identifier)
            {
                return identifier.Identifier;
            }

            throw new InvalidOperationException($"Projectiles.Def for '{projectileName}' requires '{name}' to be an identifier.");
        }

        private static string? OptionalProjectileIdentifier(
            IReadOnlyDictionary<string, ExpressionSyntax> arguments,
            string name,
            string projectileName)
        {
            if (!arguments.TryGetValue(name, out var expression))
            {
                return null;
            }

            if (expression is IdentifierSyntax identifier)
            {
                return identifier.Identifier;
            }

            throw new InvalidOperationException($"Projectiles.Def for '{projectileName}' requires '{name}' to be an identifier.");
        }

        private static int RequiredProjectileLiteralByte(
            IReadOnlyDictionary<string, ExpressionSyntax> arguments,
            string name,
            string projectileName)
        {
            if (!arguments.TryGetValue(name, out var expression))
            {
                throw new InvalidOperationException($"Projectiles.Def for '{projectileName}' requires '{name}'.");
            }

            if (TryLiteralByte(expression, out var value))
            {
                return value;
            }

            throw new InvalidOperationException($"Projectiles.Def for '{projectileName}' requires '{name}' to be a literal byte value.");
        }

        private static int OptionalProjectileDefLiteralByte(
            IReadOnlyDictionary<string, ExpressionSyntax> arguments,
            string name,
            int defaultValue,
            string projectileName)
        {
            if (!arguments.TryGetValue(name, out var expression))
            {
                return defaultValue;
            }

            if (TryLiteralByte(expression, out var value))
            {
                return value;
            }

            throw new InvalidOperationException($"Projectiles.Def for '{projectileName}' requires '{name}' to be a literal byte value.");
        }

        private static int OptionalProjectileLiteralByte(
            IReadOnlyDictionary<string, ExpressionSyntax> arguments,
            string name,
            int defaultValue,
            string poolName)
        {
            if (!arguments.TryGetValue(name, out var expression))
            {
                return defaultValue;
            }

            if (TryLiteralByte(expression, out var value))
            {
                return value;
            }

            throw new InvalidOperationException($"Projectiles.Pool for '{poolName}' requires literal byte limits for hero, enemy, requests, and offscreenMargin.");
        }

        private static IReadOnlyList<StatementSyntax> ProjectileRequestStatements(ProjectilePool pool, QualifiedCallSyntax call, ActorFrameworkState state)
        {
            var parameters = call.Parameters.Select(parameter => RewriteExpression(parameter, state)).ToList();
            if (parameters.Count is < 4 or > 6)
            {
                throw new InvalidOperationException($"{pool.Name}.Request expects kind, x, y, direction, optional result, and optional owner.");
            }

            if (parameters[0] is not IdentifierSyntax kind)
            {
                throw new InvalidOperationException($"{pool.Name}.Request kind must be a projectile identifier.");
            }

            state.Projectiles.Definition(kind.Identifier);
            var prefix = state.GeneratedCalls.NextProjectileRequestPrefix(pool);
            var indexName = $"{prefix}_i";
            var writtenName = $"{prefix}_written";
            var result = parameters.Count >= 5 ? ExpressionToLValue(parameters[4], $"{pool.Name}.Request result") : null;
            var owner = parameters.Count == 6 ? parameters[5] : Constant(0);
            var requestWrites = new List<StatementSyntax>
            {
                FieldAssignment(pool.RequestArrayName, indexName, "active", "=", Constant(1)),
                FieldAssignment(pool.RequestArrayName, indexName, "kind", "=", kind),
                FieldAssignment(pool.RequestArrayName, indexName, "x", "=", parameters[1]),
                FieldAssignment(pool.RequestArrayName, indexName, "xHi", "=", Constant(0)),
                FieldAssignment(pool.RequestArrayName, indexName, "y", "=", parameters[2]),
                FieldAssignment(pool.RequestArrayName, indexName, "yHi", "=", Constant(0)),
                FieldAssignment(pool.RequestArrayName, indexName, "direction", "=", parameters[3]),
                FieldAssignment(pool.RequestArrayName, indexName, "owner", "=", owner),
                Assign(new IdentifierLValue(writtenName), Constant(1)),
            };

            if (result is not null)
            {
                requestWrites.Add(Assign(result, Constant(1)));
            }

            var statements = new List<StatementSyntax>
            {
                new DeclarationSyntax("u8", writtenName, Maybe<ExpressionSyntax>.None, Maybe.From<ExpressionSyntax>(Constant(0))),
            };

            if (result is not null)
            {
                statements.Add(Assign(result, Constant(0)));
            }

            statements.Add(ArrayLoop(
                pool.RequestArrayName,
                indexName,
                new IfElseSyntax(
                    And(
                        new BinaryExpressionSyntax(new IdentifierSyntax(writtenName), Constant(0), Operator.Equal),
                        new BinaryExpressionSyntax(PoolField(pool.RequestArrayName, indexName, "active"), Constant(0), Operator.Equal)),
                    new BlockSyntax(requestWrites),
                    Maybe<BlockSyntax>.None)));

            return statements;
        }

        private static IReadOnlyList<StatementSyntax> ProjectileProcessRequestStatements(ProjectilePool pool, QualifiedCallSyntax call, ActorFrameworkState state)
        {
            RequireNoArguments(call);
            RequireProjectileDefs(state, $"{pool.Name}.ProcessRequests");

            var requestIndex = $"__{pool.Name}_process_request_i";
            var branches = state.Projectiles.Definitions
                .Select(def => new KindBranch(def.Name, new BlockSyntax(ProjectileSpawnFromRequestStatements(pool, requestIndex, def, state).ToList())))
                .ToList();

            return
            [
                ArrayLoop(
                    pool.RequestArrayName,
                    requestIndex,
                    new IfElseSyntax(
                        new BinaryExpressionSyntax(PoolField(pool.RequestArrayName, requestIndex, "active"), Constant(0), Operator.NotEqual),
                        new BlockSyntax([
                            new DeclarationSyntax("u8", $"__{pool.Name}_process_request_x", Maybe<ExpressionSyntax>.None, Maybe.From<ExpressionSyntax>(PoolField(pool.RequestArrayName, requestIndex, "x"))),
                            new DeclarationSyntax("u8", $"__{pool.Name}_process_request_xHi", Maybe<ExpressionSyntax>.None, Maybe.From<ExpressionSyntax>(PoolField(pool.RequestArrayName, requestIndex, "xHi"))),
                            new DeclarationSyntax("u8", $"__{pool.Name}_process_request_y", Maybe<ExpressionSyntax>.None, Maybe.From<ExpressionSyntax>(PoolField(pool.RequestArrayName, requestIndex, "y"))),
                            new DeclarationSyntax("u8", $"__{pool.Name}_process_request_yHi", Maybe<ExpressionSyntax>.None, Maybe.From<ExpressionSyntax>(PoolField(pool.RequestArrayName, requestIndex, "yHi"))),
                            new DeclarationSyntax("u8", $"__{pool.Name}_process_request_owner", Maybe<ExpressionSyntax>.None, Maybe.From<ExpressionSyntax>(PoolField(pool.RequestArrayName, requestIndex, "owner"))),
                            new DeclarationSyntax("u8", $"__{pool.Name}_process_request_direction", Maybe<ExpressionSyntax>.None, Maybe.From<ExpressionSyntax>(PoolField(pool.RequestArrayName, requestIndex, "direction"))),
                            PooledKindDispatch(pool.RequestArrayName, requestIndex, branches, "projectile request projectile dispatch requires at least one Projectiles.Def declaration."),
                            FieldAssignment(pool.RequestArrayName, requestIndex, "active", "=", Constant(0)),
                        ]),
                        Maybe<BlockSyntax>.None)),
            ];
        }

        private static IReadOnlyList<StatementSyntax> ProjectileSpawnFromRequestStatements(ProjectilePool pool, string requestIndex, ProjectileDef def, ActorFrameworkState state)
        {
            var arrayName = pool.ArrayNameForTeam(def.Team);
            var indexName = $"__{pool.Name}_process_{def.Name}_i";
            var writtenName = $"__{pool.Name}_process_{def.Name}_written";
            var spawnStatements = new List<StatementSyntax>
            {
                FieldAssignment(arrayName, indexName, "active", "=", Constant(1)),
                FieldAssignment(arrayName, indexName, "kind", "=", new IdentifierSyntax(def.Name)),
                FieldAssignment(arrayName, indexName, "x", "=", new IdentifierSyntax($"__{pool.Name}_process_request_x")),
                FieldAssignment(arrayName, indexName, "xHi", "=", new IdentifierSyntax($"__{pool.Name}_process_request_xHi")),
                FieldAssignment(arrayName, indexName, "y", "=", new IdentifierSyntax($"__{pool.Name}_process_request_y")),
                FieldAssignment(arrayName, indexName, "yHi", "=", new IdentifierSyntax($"__{pool.Name}_process_request_yHi")),
                FieldAssignment(arrayName, indexName, "vx", "=", new IdentifierSyntax($"{def.Name}SpeedX")),
                FieldAssignment(arrayName, indexName, "vy", "=", new IdentifierSyntax($"{def.Name}SpeedY")),
                FieldAssignment(arrayName, indexName, "damage", "=", new IdentifierSyntax($"{def.Name}Damage")),
                FieldAssignment(arrayName, indexName, "age", "=", Constant(0)),
                FieldAssignment(arrayName, indexName, "owner", "=", new IdentifierSyntax($"__{pool.Name}_process_request_owner")),
                FieldAssignment(arrayName, indexName, "direction", "=", new IdentifierSyntax($"__{pool.Name}_process_request_direction")),
            };
            spawnStatements.AddRange(ProjectileEffectRequestStatements(
                pool,
                def,
                def.SpawnEffect,
                "projectile_spawn",
                new IdentifierSyntax($"__{pool.Name}_process_request_x"),
                new IdentifierSyntax($"__{pool.Name}_process_request_xHi"),
                new IdentifierSyntax($"__{pool.Name}_process_request_y"),
                new IdentifierSyntax($"__{pool.Name}_process_request_yHi"),
                state));
            spawnStatements.Add(Assign(new IdentifierLValue(writtenName), Constant(1)));

            return
            [
                new DeclarationSyntax("u8", writtenName, Maybe<ExpressionSyntax>.None, Maybe.From<ExpressionSyntax>(Constant(0))),
                ArrayLoop(
                    arrayName,
                    indexName,
                    new IfElseSyntax(
                        And(
                            new BinaryExpressionSyntax(new IdentifierSyntax(writtenName), Constant(0), Operator.Equal),
                            new BinaryExpressionSyntax(PoolField(arrayName, indexName, "active"), Constant(0), Operator.Equal)),
                        new BlockSyntax(spawnStatements),
                        Maybe<BlockSyntax>.None)),
            ];
        }


        private static IReadOnlyList<StatementSyntax> ProjectileUpdateTeamStatements(ProjectilePool pool, string team, ActorFrameworkState state)
        {
            var defs = state.Projectiles.Definitions.Where(def => def.Team == team).ToList();
            if (defs.Count == 0)
            {
                return [];
            }

            var arrayName = pool.ArrayNameForTeam(team);
            var phase = team == "Hero" ? "update_hero" : "update_enemy";
            var indexName = $"__{pool.Name}_{phase}_i";
            var projection = BuildPooledScreenProjection(arrayName, indexName, pool.Name, phase, state.ScreenWidth, state.ScreenHeight, pool.OffscreenMargin);
            var branches = defs
                .Select(def => new KindBranch(def.Name, new BlockSyntax(ProjectileUpdateBlock(pool, arrayName, indexName, def, projection, state).ToList())))
                .ToList();

            return PooledCameraDeclarations(pool.Name, phase, state.ConfiguresCamera)
                .Append(ArrayLoop(
                    arrayName,
                    indexName,
                    new IfElseSyntax(
                        new BinaryExpressionSyntax(PoolField(arrayName, indexName, "active"), Constant(0), Operator.NotEqual),
                        new BlockSyntax(projection.Declarations
                            .Append(PooledKindDispatch(arrayName, indexName, branches, $"{pool.Name}.Update projectile dispatch requires at least one Projectiles.Def declaration."))
                            .ToList()),
                        Maybe<BlockSyntax>.None)))
                .ToList();
        }

        private static IReadOnlyList<StatementSyntax> ProjectileUpdateBlock(
            ProjectilePool pool,
            string arrayName,
            string indexName,
            ProjectileDef def,
            ActorScreenProjection projection,
            ActorFrameworkState state)
        {
            var horizontalVelocityName = $"{indexName}_vx";
            var verticalVelocityName = $"{indexName}_vy";
            var upwardSpeedName = $"{indexName}_vy_up";
            var downwardSpeedName = $"{indexName}_vy_down";
            var horizontalVelocity = new IdentifierSyntax(horizontalVelocityName);
            var verticalVelocity = new IdentifierSyntax(verticalVelocityName);
            var upwardSpeed = new IdentifierSyntax(upwardSpeedName);
            var downwardSpeed = new IdentifierSyntax(downwardSpeedName);
            var statements = new List<StatementSyntax>
            {
                new DeclarationSyntax("u8", horizontalVelocityName, Maybe<ExpressionSyntax>.None, Maybe.From<ExpressionSyntax>(new CastSyntax("u8", PoolField(arrayName, indexName, "vx")))),
                new DeclarationSyntax("i8", verticalVelocityName, Maybe<ExpressionSyntax>.None, Maybe.From<ExpressionSyntax>(PoolField(arrayName, indexName, "vy"))),
                new IfElseSyntax(
                    new BinaryExpressionSyntax(PoolField(arrayName, indexName, "direction"), Constant(0), Operator.Equal),
                    new BlockSyntax(ProjectileAddWorldX(arrayName, indexName, horizontalVelocity).ToList()),
                    Maybe.From(new BlockSyntax(ProjectileSubtractWorldX(arrayName, indexName, horizontalVelocity).ToList()))),
                new IfElseSyntax(
                    new BinaryExpressionSyntax(verticalVelocity, Constant(0), Operator.LessThan),
                    new BlockSyntax(new StatementSyntax[]
                        {
                            new DeclarationSyntax("u8", upwardSpeedName, Maybe<ExpressionSyntax>.None, Maybe.From<ExpressionSyntax>(new CastSyntax("u8", new BinaryExpressionSyntax(Constant(0), verticalVelocity, Operator.Subtraction)))),
                        }
                        .Concat(ProjectileSubtractWorldY(arrayName, indexName, upwardSpeed))
                        .ToList()),
                    Maybe.From(new BlockSyntax(new StatementSyntax[]
                        {
                            new DeclarationSyntax("u8", downwardSpeedName, Maybe<ExpressionSyntax>.None, Maybe.From<ExpressionSyntax>(new CastSyntax("u8", verticalVelocity))),
                        }
                        .Concat(ProjectileAddWorldY(arrayName, indexName, downwardSpeed))
                        .ToList()))),
            };

            if (def.Behavior == "GravityArc")
            {
                statements.Add(FieldAssignment(arrayName, indexName, "vy", "+=", Constant(1)));
            }

            statements.Add(FieldAssignment(arrayName, indexName, "age", "+=", Constant(1)));
            statements.Add(new IfElseSyntax(
                new BinaryExpressionSyntax(PoolField(arrayName, indexName, "age"), new IdentifierSyntax($"{def.Name}Lifetime"), Operator.Equal),
                new BlockSyntax(ProjectileEffectRequestStatements(
                        pool,
                        def,
                        def.ExpireEffect,
                        "projectile_expire",
                        PoolField(arrayName, indexName, "x"),
                        PoolField(arrayName, indexName, "xHi"),
                        PoolField(arrayName, indexName, "y"),
                        PoolField(arrayName, indexName, "yHi"),
                        state)
                    .Append(FieldAssignment(arrayName, indexName, "active", "=", Constant(0)))
                    .ToList()),
                Maybe<BlockSyntax>.None));
            statements.Add(new IfElseSyntax(
                new UnaryExpressionSyntax("!", projection.Visible),
                new BlockSyntax([FieldAssignment(arrayName, indexName, "active", "=", Constant(0))]),
                Maybe<BlockSyntax>.None));

            return statements;
        }

        // Rightward step: advance the 16-bit world X (low byte + page) by the projectile speed, carrying
        // into xHi on wrap. Mirrors the actor AddWorldX helper for the projectile pool arrays.
        private static IReadOnlyList<StatementSyntax> ProjectileAddWorldX(string arrayName, string indexName, ExpressionSyntax amount)
        {
            return
            [
                FieldAssignment(arrayName, indexName, "x", "+=", amount),
                new IfElseSyntax(
                    new BinaryExpressionSyntax(PoolField(arrayName, indexName, "x"), amount, Operator.LessThan),
                    new BlockSyntax([FieldAssignment(arrayName, indexName, "xHi", "+=", Constant(1))]),
                    Maybe<BlockSyntax>.None),
            ];
        }

        // Leftward step: borrow from xHi before the low byte underflows, then subtract the projectile
        // speed. Mirrors the actor SubtractWorldX helper for the projectile pool arrays.
        private static IReadOnlyList<StatementSyntax> ProjectileSubtractWorldX(string arrayName, string indexName, ExpressionSyntax amount)
        {
            return
            [
                new IfElseSyntax(
                    new BinaryExpressionSyntax(PoolField(arrayName, indexName, "x"), amount, Operator.LessThan),
                    new BlockSyntax([FieldAssignment(arrayName, indexName, "xHi", "-=", Constant(1))]),
                    Maybe<BlockSyntax>.None),
                FieldAssignment(arrayName, indexName, "x", "-=", amount),
            ];
        }

        private static IReadOnlyList<StatementSyntax> ProjectileAddWorldY(string arrayName, string indexName, ExpressionSyntax amount)
        {
            return
            [
                FieldAssignment(arrayName, indexName, "y", "+=", amount),
                new IfElseSyntax(
                    new BinaryExpressionSyntax(PoolField(arrayName, indexName, "y"), amount, Operator.LessThan),
                    new BlockSyntax([FieldAssignment(arrayName, indexName, "yHi", "+=", Constant(1))]),
                    Maybe<BlockSyntax>.None),
            ];
        }

        private static IReadOnlyList<StatementSyntax> ProjectileSubtractWorldY(string arrayName, string indexName, ExpressionSyntax amount)
        {
            return
            [
                new IfElseSyntax(
                    new BinaryExpressionSyntax(PoolField(arrayName, indexName, "y"), amount, Operator.LessThan),
                    new BlockSyntax([FieldAssignment(arrayName, indexName, "yHi", "-=", Constant(1))]),
                    Maybe<BlockSyntax>.None),
                FieldAssignment(arrayName, indexName, "y", "-=", amount),
            ];
        }

        private static IReadOnlyList<StatementSyntax> ProjectileDrawStatements(ProjectilePool pool, QualifiedCallSyntax call, ActorFrameworkState state)
        {
            RequireNoArguments(call);
            if (!state.SupportsDraw)
            {
                throw new InvalidOperationException($"Target '{state.TargetName}' does not support projectile Draw yet.");
            }

            RequireProjectileDefs(state, $"{pool.Name}.Draw");
            return ProjectileDrawTeamStatements(pool, "Hero", state)
                .Concat(ProjectileDrawTeamStatements(pool, "Enemy", state))
                .ToList();
        }

        private static IReadOnlyList<StatementSyntax> ProjectileDrawTeamStatements(ProjectilePool pool, string team, ActorFrameworkState state)
        {
            var defs = state.Projectiles.Definitions.Where(def => def.Team == team).ToList();
            if (defs.Count == 0)
            {
                return [];
            }

            var arrayName = pool.ArrayNameForTeam(team);
            var phase = team == "Hero" ? "draw_hero" : "draw_enemy";
            var capacity = team == "Hero" ? pool.HeroCapacity : pool.EnemyCapacity;
            var statements = PooledCameraDeclarations(pool.Name, phase, state.ConfiguresCamera).ToList();
            for (var slot = 0; slot < capacity; slot++)
            {
                var slotPhase = $"{phase}_{slot.ToString(CultureInfo.InvariantCulture)}";
                var indexName = $"__{pool.Name}_{slotPhase}_i";
                var variablePrefix = $"__{pool.Name}_{slotPhase}";
                var projection = BuildPooledScreenProjection(
                    arrayName,
                    indexName,
                    pool.Name,
                    slotPhase,
                    state.ScreenWidth,
                    state.ScreenHeight,
                    cameraPhase: phase);
                var branches = defs
                    .Select(def => new KindBranch(def.Name, ProjectileDrawBlock(arrayName, indexName, variablePrefix, def, projection, state)))
                    .ToList();

                statements.Add(new DeclarationSyntax(
                    "u8",
                    indexName,
                    Maybe<ExpressionSyntax>.None,
                    Maybe.From<ExpressionSyntax>(Constant(slot))));
                statements.AddRange(projection.Declarations);
                statements.Add(PooledKindDispatch(arrayName, indexName, branches, $"{pool.Name}.Draw projectile dispatch requires at least one Projectiles.Def declaration."));
            }

            return statements;
        }

        private static BlockSyntax ProjectileDrawBlock(
            string arrayName,
            string indexName,
            string variablePrefix,
            ProjectileDef def,
            ActorScreenProjection projection,
            ActorFrameworkState state)
        {
            return StableSpriteDrawBlock(
                arrayName,
                indexName,
                variablePrefix,
                def.Name,
                def.Sprite,
                projection,
                state.ScreenHeight,
                state);
        }

        private static IReadOnlyList<StatementSyntax> ProjectileTouchTilesStatements(ProjectilePool pool, QualifiedCallSyntax call, ActorFrameworkState state)
        {
            var parameters = RequireArguments(call, 2);
            RequireProjectileDefs(state, $"{pool.Name}.TouchTiles");

            var yOffset = RequiredLiteralByte(parameters[0], $"{pool.Name}.TouchTiles argument 1");
            var flags = RewriteExpression(parameters[1], state);

            return ProjectileTouchTilesTeamStatements(pool, "Hero", yOffset, flags, state)
                .Concat(ProjectileTouchTilesTeamStatements(pool, "Enemy", yOffset, flags, state))
                .ToList();
        }

        private static IReadOnlyList<StatementSyntax> ProjectileTouchTilesTeamStatements(
            ProjectilePool pool,
            string team,
            int yOffset,
            ExpressionSyntax flags,
            ActorFrameworkState state)
        {
            var defs = state.Projectiles.Definitions.Where(def => def.Team == team && def.TileCollision != "None").ToList();
            if (defs.Count == 0)
            {
                return [];
            }

            var arrayName = pool.ArrayNameForTeam(team);
            var phase = team == "Hero" ? "tiles_hero" : "tiles_enemy";
            var indexName = $"__{pool.Name}_{phase}_i";
            var projection = BuildPooledScreenProjection(arrayName, indexName, pool.Name, phase, state.ScreenWidth, state.ScreenHeight);
            var branches = defs
                .Select(def => new KindBranch(def.Name, ProjectileTouchTilesBlock(pool, arrayName, indexName, def, yOffset, flags, projection, state)))
                .ToList();

            return PooledCameraDeclarations(pool.Name, phase, state.ConfiguresCamera)
                .Append(ArrayLoop(
                    arrayName,
                    indexName,
                    new IfElseSyntax(
                        new BinaryExpressionSyntax(PoolField(arrayName, indexName, "active"), Constant(0), Operator.NotEqual),
                        new BlockSyntax(projection.Declarations
                            .Append(PooledKindDispatch(arrayName, indexName, branches, $"{pool.Name}.TouchTiles projectile dispatch requires at least one Projectiles.Def declaration."))
                            .ToList()),
                        Maybe<BlockSyntax>.None)))
                .ToList();
        }

        private static BlockSyntax ProjectileTouchTilesBlock(
            ProjectilePool pool,
            string arrayName,
            string indexName,
            ProjectileDef def,
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
                            Constant(def.HitboxWidth),
                            Constant(def.HitboxHeight),
                            flags,
                        ]),
                    Constant(0),
                    Operator.NotEqual),
                new BlockSyntax(ProjectileTileCollisionStatements(pool, arrayName, indexName, def, state).ToList()),
                Maybe<BlockSyntax>.None);

            return new BlockSyntax([
                new IfElseSyntax(projection.Visible, new BlockSyntax([collision]), Maybe<BlockSyntax>.None),
            ]);
        }

        private static IReadOnlyList<StatementSyntax> ProjectileTileCollisionStatements(
            ProjectilePool pool,
            string arrayName,
            string indexName,
            ProjectileDef def,
            ActorFrameworkState state)
        {
            var statements = ProjectileEffectRequestStatements(
                pool,
                def,
                def.ImpactEffect,
                "projectile_tile",
                PoolField(arrayName, indexName, "x"),
                PoolField(arrayName, indexName, "xHi"),
                PoolField(arrayName, indexName, "y"),
                PoolField(arrayName, indexName, "yHi"),
                state).ToList();

            statements.AddRange(def.TileCollision switch
            {
                "Expire" => [FieldAssignment(arrayName, indexName, "active", "=", Constant(0))],
                "Bounce" => [FieldAssignment(arrayName, indexName, "vy", "=", new BinaryExpressionSyntax(Constant(0), new IdentifierSyntax($"{def.Name}BounceSpeedY"), Operator.Subtraction))],
                _ => throw new InvalidOperationException($"Unknown projectile tile collision '{def.TileCollision}'."),
            });

            return statements;
        }

        private static IReadOnlyList<StatementSyntax> ProjectileTouchActorsStatements(ProjectilePool pool, QualifiedCallSyntax call, ActorFrameworkState state)
        {
            var parameters = RequireArguments(call, 1);
            if (parameters[0] is not IdentifierSyntax actorPoolName || !state.Actors.TryPool(actorPoolName.Identifier, out var actorPool))
            {
                throw new InvalidOperationException($"{pool.Name}.TouchActors expects an actor pool identifier.");
            }

            RequireProjectileDefs(state, $"{pool.Name}.TouchActors");
            Actors.RequireEnemyDefs(state, $"{pool.Name}.TouchActors");

            var heroDefs = state.Projectiles.Definitions.Where(def => def.Team == "Hero").ToList();
            if (heroDefs.Count == 0)
            {
                return [];
            }

            var projectileIndex = $"__{pool.Name}_actor_hero_i";
            var actorIndex = $"__{pool.Name}_actor_{actorPool.Name}_i";
            var projectileProjection = BuildPooledScreenProjection(pool.HeroArrayName, projectileIndex, pool.Name, "actor_hero", state.ScreenWidth, state.ScreenHeight);
            var actorProjection = BuildPooledScreenProjection(actorPool.Name, actorIndex, pool.Name, "actor_target", state.ScreenWidth, state.ScreenHeight);
            var projectileBranches = heroDefs
                .Select(def => new KindBranch(def.Name, ProjectileTouchActorsForProjectileBlock(pool, projectileIndex, actorPool, actorIndex, def, projectileProjection, actorProjection, state)))
                .ToList();

            return PooledCameraDeclarations(pool.Name, "actor_hero", state.ConfiguresCamera)
                .Concat(PooledCameraDeclarations(pool.Name, "actor_target", state.ConfiguresCamera))
                .Append(ArrayLoop(
                    pool.HeroArrayName,
                    projectileIndex,
                    new IfElseSyntax(
                        new BinaryExpressionSyntax(PoolField(pool.HeroArrayName, projectileIndex, "active"), Constant(0), Operator.NotEqual),
                        new BlockSyntax(projectileProjection.Declarations
                            .Append(PooledKindDispatch(pool.HeroArrayName, projectileIndex, projectileBranches, $"{pool.Name}.TouchActors projectile dispatch requires at least one Projectiles.Def declaration."))
                            .ToList()),
                        Maybe<BlockSyntax>.None)))
                .ToList();
        }

        private static BlockSyntax ProjectileTouchActorsForProjectileBlock(
            ProjectilePool pool,
            string projectileIndex,
            ActorPool actorPool,
            string actorIndex,
            ProjectileDef projectileDef,
            ActorScreenProjection projectileProjection,
            ActorScreenProjection actorProjection,
            ActorFrameworkState state)
        {
            var actorBranches = state.Actors.EnemyDefs
                .Select(enemyDef => new KindBranch(enemyDef.Name, ProjectileTouchActorKindBlock(pool, projectileIndex, actorPool, actorIndex, projectileDef, enemyDef, projectileProjection, actorProjection, state)))
                .ToList();

            return new BlockSyntax([
                ArrayLoop(
                    actorPool.Name,
                    actorIndex,
                    new IfElseSyntax(
                        new BinaryExpressionSyntax(PoolField(actorPool.Name, actorIndex, "active"), Constant(0), Operator.NotEqual),
                        new BlockSyntax(actorProjection.Declarations
                            .Append(PooledKindDispatch(actorPool.Name, actorIndex, actorBranches, $"{actorPool.Name} actor dispatch requires at least one Enemies.Def declaration."))
                            .ToList()),
                        Maybe<BlockSyntax>.None)),
            ]);
        }

        private static BlockSyntax ProjectileTouchActorKindBlock(
            ProjectilePool pool,
            string projectileIndex,
            ActorPool actorPool,
            string actorIndex,
            ProjectileDef projectileDef,
            EnemyDef enemyDef,
            ActorScreenProjection projectileProjection,
            ActorScreenProjection actorProjection,
            ActorFrameworkState state)
        {
            var overlapsX = AabbOverlaps(
                projectileProjection.ScreenX,
                projectileDef.HitboxWidth,
                actorProjection.ScreenX,
                enemyDef.HitboxWidth);
            var overlapsY = AabbOverlaps(
                projectileProjection.ScreenY,
                projectileDef.HitboxHeight,
                actorProjection.ScreenY,
                enemyDef.HitboxHeight);

            return new BlockSyntax([
                new IfElseSyntax(
                    And(
                        new BinaryExpressionSyntax(PoolField(pool.HeroArrayName, projectileIndex, "active"), Constant(0), Operator.NotEqual),
                        And(projectileProjection.Visible, And(actorProjection.Visible, And(overlapsX, overlapsY)))),
                    new BlockSyntax(new StatementSyntax[]
                        {
                            FieldAssignment(actorPool.Name, actorIndex, "health", "-=", new IdentifierSyntax($"{projectileDef.Name}Damage")),
                            FieldAssignment(actorPool.Name, actorIndex, "state", "=", new IdentifierSyntax($"{projectileDef.Name}Damage")),
                        }
                        .Concat(ProjectileEffectRequestStatements(
                            pool,
                            projectileDef,
                            projectileDef.ImpactEffect,
                            "projectile_impact",
                            PoolField(pool.HeroArrayName, projectileIndex, "x"),
                            PoolField(pool.HeroArrayName, projectileIndex, "xHi"),
                            PoolField(pool.HeroArrayName, projectileIndex, "y"),
                            PoolField(pool.HeroArrayName, projectileIndex, "yHi"),
                            state))
                        .Append(FieldAssignment(pool.HeroArrayName, projectileIndex, "active", "=", Constant(0)))
                        .ToList()),
                    Maybe<BlockSyntax>.None),
            ]);
        }

        private static IReadOnlyList<StatementSyntax> ProjectileTouchHeroStatements(ProjectilePool pool, QualifiedCallSyntax call, ActorFrameworkState state)
        {
            var parameters = RequireArguments(call, 5)
                .Select(parameter => RewriteExpression(parameter, state))
                .ToList();
            var damageTarget = ExpressionToLValue(parameters[4], $"{pool.Name}.TouchHero damage target");
            RequireProjectileDefs(state, $"{pool.Name}.TouchHero");

            var enemyDefs = state.Projectiles.Definitions.Where(def => def.Team == "Enemy").ToList();
            if (enemyDefs.Count == 0)
            {
                return [];
            }

            var indexName = $"__{pool.Name}_hero_enemy_i";
            var projection = BuildPooledScreenProjection(pool.EnemyArrayName, indexName, pool.Name, "hero_enemy", state.ScreenWidth, state.ScreenHeight);
            var branches = enemyDefs
                .Select(def => new KindBranch(def.Name, ProjectileTouchHeroBlock(pool, indexName, def, projection, parameters, damageTarget, state)))
                .ToList();

            return PooledCameraDeclarations(pool.Name, "hero_enemy", state.ConfiguresCamera)
                .Append(ArrayLoop(
                    pool.EnemyArrayName,
                    indexName,
                    new IfElseSyntax(
                        new BinaryExpressionSyntax(PoolField(pool.EnemyArrayName, indexName, "active"), Constant(0), Operator.NotEqual),
                        new BlockSyntax(projection.Declarations
                            .Append(PooledKindDispatch(pool.EnemyArrayName, indexName, branches, $"{pool.Name}.TouchHero projectile dispatch requires at least one Projectiles.Def declaration."))
                            .ToList()),
                        Maybe<BlockSyntax>.None)))
                .ToList();
        }

        private static BlockSyntax ProjectileTouchHeroBlock(
            ProjectilePool pool,
            string indexName,
            ProjectileDef def,
            ActorScreenProjection projection,
            IReadOnlyList<ExpressionSyntax> parameters,
            LValue damageTarget,
            ActorFrameworkState state)
        {
            var overlapsX = AabbOverlaps(projection.ScreenX, def.HitboxWidth, parameters[0], RequiredLiteralByte(parameters[2], $"{pool.Name}.TouchHero argument 3"));
            var overlapsY = AabbOverlaps(projection.ScreenY, def.HitboxHeight, parameters[1], RequiredLiteralByte(parameters[3], $"{pool.Name}.TouchHero argument 4"));

            return new BlockSyntax([
                new IfElseSyntax(
                    And(projection.Visible, And(overlapsX, overlapsY)),
                    new BlockSyntax(new StatementSyntax[]
                        {
                            new ExpressionStatementSyntax(new AssignmentSyntax(damageTarget, "+=", new IdentifierSyntax($"{def.Name}Damage"))),
                        }
                        .Concat(ProjectileEffectRequestStatements(
                            pool,
                            def,
                            def.ImpactEffect,
                            "projectile_impact",
                            PoolField(pool.EnemyArrayName, indexName, "x"),
                            PoolField(pool.EnemyArrayName, indexName, "xHi"),
                            PoolField(pool.EnemyArrayName, indexName, "y"),
                            PoolField(pool.EnemyArrayName, indexName, "yHi"),
                            state))
                        .Append(FieldAssignment(pool.EnemyArrayName, indexName, "active", "=", Constant(0)))
                        .ToList()),
                    Maybe<BlockSyntax>.None),
            ]);
        }

        private static IReadOnlyList<StatementSyntax> ProjectileEffectRequestStatements(
            ProjectilePool projectilePool,
            ProjectileDef projectileDef,
            string? effectName,
            string purpose,
            ExpressionSyntax x,
            ExpressionSyntax xHi,
            ExpressionSyntax y,
            ExpressionSyntax yHi,
            ActorFrameworkState state)
        {
            if (effectName is null)
            {
                return [];
            }

            if (projectilePool.EffectPoolName is null)
            {
                throw new InvalidOperationException($"Projectiles.Pool for '{projectilePool.Name}' must declare an effects pool before Projectiles.Def '{projectileDef.Name}' can emit effect '{effectName}'.");
            }

            state.Effects.Definition(effectName);
            var effectPool = state.Effects.Pool(projectilePool.EffectPoolName);
            return Effects.EnqueueStatements(
                effectPool,
                new IdentifierSyntax(effectName),
                x,
                xHi,
                y,
                yHi,
                state.GeneratedCalls.NextEffectRequestPrefix(effectPool, $"{purpose}_{projectileDef.Name}"));
        }

        private static void RequireProjectileDefs(ActorFrameworkState state, string callName)
        {
            if (state.Projectiles.Definitions.Count == 0)
            {
                throw new InvalidOperationException($"{callName} requires at least one Projectiles.Def declaration.");
            }
        }

        private static BinaryExpressionSyntax AabbOverlaps(ExpressionSyntax left, int leftWidth, ExpressionSyntax right, int rightWidth)
        {
            return And(
                new BinaryExpressionSyntax(left, OffsetExpression(right, rightWidth, subtract: false), Operator.LessThan),
                new BinaryExpressionSyntax(OffsetExpression(left, leftWidth, subtract: false), right, Operator.Get(">")));
        }


    }
}
