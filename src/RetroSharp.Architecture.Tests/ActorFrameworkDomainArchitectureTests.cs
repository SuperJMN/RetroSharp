using System.Text.RegularExpressions;

namespace RetroSharp.Architecture.Tests;

public sealed class ActorFrameworkDomainArchitectureTests
{
    private const string LowererPath = "src/RetroSharp.Sdk.Frontend/ActorFrameworkLowerer.cs";
    private const string GeneratedProgramPath = "src/RetroSharp.Sdk.Frontend/ActorFrameworkLowerer.GeneratedProgram.cs";
    private const string SharedGenerationPath = "src/RetroSharp.Sdk.Frontend/ActorFrameworkLowerer.SharedGeneration.cs";
    private const string ForbiddenParallelHelperPattern = @"\b(?:ActorCameraDeclarations|BuildActorScreenProjection|KindDispatch)\s*\(";

    private static readonly (string Path, string Module, string[] OwnedMembers)[] FeatureModules =
    [
        ("src/RetroSharp.Sdk.Frontend/ActorFrameworkLowerer.Actors.cs", "Actors", ["ReadPool(", "ReadSpawnDirective(", "TryRewriteExpression(", "PoolUpdateLoop(", "ValidateSpawnLayers(", "GeneratedNames("]),
        ("src/RetroSharp.Sdk.Frontend/ActorFrameworkLowerer.Projectiles.cs", "Projectiles", ["ReadPool(", "ReadDefinition(", "TryRewriteExpression(", "UpdateStatements(", "ValidateEffects(", "GeneratedNames("]),
        ("src/RetroSharp.Sdk.Frontend/ActorFrameworkLowerer.Effects.cs", "Effects", ["ReadPool(", "ReadDefinition(", "TryRewriteExpression(", "UpdateStatements(", "DrawStatements(", "GeneratedNames("]),
        ("src/RetroSharp.Sdk.Frontend/ActorFrameworkLowerer.GeneratedProgram.cs", "GeneratedProgramArtifacts", ["Build(", "ValidateNameCollisions(", "GeneratedNames("]),
    ];

    private static readonly (string Path, string[] OwnedMembers)[] FeatureGenerationFiles =
    [
        ("src/RetroSharp.Sdk.Frontend/ActorFrameworkLowerer.Actors.Generation.cs", ["ValidatePoolSpriteBudgets(", "PoolDrawStatements(", "RuntimeSpawnActivationStatements("]),
        ("src/RetroSharp.Sdk.Frontend/ActorFrameworkLowerer.Projectiles.Generation.cs", ["ProjectileRequestStatements(", "ProjectileUpdateBlock(", "ProjectileTouchActorsStatements("]),
        ("src/RetroSharp.Sdk.Frontend/ActorFrameworkLowerer.Effects.Generation.cs", ["EffectRequestStatements(", "EffectProcessRequestStatements(", "EffectSpawnFromRequestStatements("]),
    ];

    private static readonly (string Path, string[] OwnedDeclarations)[] DomainDeclarations =
    [
        ("src/RetroSharp.Sdk.Frontend/ActorFrameworkLowerer.Actors.cs",
        [
            "private const string SdkRoleAttribute",
            "RolesByMetadata =",
            "private enum ActorFrameworkRole",
            "private sealed record ActorFrameworkCall(",
            "private sealed class ActorFrameworkRoleIndex",
            "private static string DisplayName(",
            "private sealed record ActorPool(",
            "private sealed record ActorSpawnLayer(",
            "private sealed record ActorSpawnLayerKey(",
            "private sealed record EnemyDef(",
        ]),
        ("src/RetroSharp.Sdk.Frontend/ActorFrameworkLowerer.Projectiles.cs",
        [
            "private sealed record ProjectilePool(",
            "private sealed record ProjectileDef(",
        ]),
        ("src/RetroSharp.Sdk.Frontend/ActorFrameworkLowerer.Effects.cs",
        [
            "private sealed record EffectPool(",
            "private sealed record EffectDef(",
        ]),
        ("src/RetroSharp.Sdk.Frontend/ActorFrameworkLowerer.GeneratedProgram.cs",
        [
            "private sealed record GeneratedName(",
        ]),
        ("src/RetroSharp.Sdk.Frontend/ActorFrameworkLowerer.SharedGeneration.cs",
        [
            "private sealed record ActorScreenProjection(",
            "private sealed record KindBranch(",
        ]),
    ];

    private static readonly string[] DomainMembersForbiddenInRoot =
    [
        "BehaviorIds =",
        "ProjectileTeamIds =",
        "ProjectileBehaviorIds =",
        "ProjectileTileCollisionIds =",
        "EnemyLookupFunctions =",
        "SpawnInitialFieldNames =",
        "EnemyLookupDescriptor",
        "EnemySpriteBudget",
        "Pool(ActorFrameworkCall",
        "ProjectilePool(QualifiedCallSyntax",
        "EffectPool(QualifiedCallSyntax",
        "SpawnLayer(ActorFrameworkCall",
        "OptionalIdentifier(",
        "ValidatePoolSpriteBudget(",
        "ProjectileRequestStatements(",
        "ProjectileProcessRequestStatements(",
        "ProjectileSpawnFromRequestStatements(",
        "ProjectileUpdateTeamStatements(",
        "ProjectileUpdateBlock(",
        "ProjectileDrawStatements(",
        "ProjectileTouchTilesStatements(",
        "ProjectileTouchActorsStatements(",
        "ProjectileTouchHeroStatements(",
        "ProjectileEffectRequestStatements(",
        "BuildProjectileScreenProjection(",
        "CameraComponent(",
        "ExpressionToLValue(",
        "EffectRequestStatements(",
        "EffectEnqueueStatements(",
        "EffectProcessRequestStatements(",
        "EffectSpawnFromRequestStatements(",
        "UpdateStatementsFor(",
        "FacingMove(",
        "PoolDrawStatements(",
        "PoolTouchTilesStatements(",
        "PoolLandOnTilesStatements(",
        "PoolTouchPlayerStatements(",
        "BuildActorScreenProjection(",
        "RuntimeSpawnActivationStatements(",
        "SpawnRecycleStatements(",
        "SpawnActivationStatements(",
        "SpawnSlotAssignments(",
        "BuildSpawnScreenProjection(",
        "RequiredProjectileIdentifier(",
        "RequiredEffectIdentifier(",
        "SpawnLookupFieldNames(",
    ];

    private static readonly string[] DomainQualifiedCallTokensForbiddenInRoot =
    [
        "QualifiedCallSyntax { Qualifier: \"Projectiles\"",
        "QualifiedCallSyntax { Qualifier: \"Effects\"",
    ];

    [Fact]
    public void Actor_framework_generation_is_partitioned_into_feature_owned_modules()
    {
        var root = RepositoryRoot();
        var lowererSource = File.ReadAllText(Path.Combine(root, LowererPath));

        Assert.Contains("partial class ActorFrameworkLowerer", lowererSource, StringComparison.Ordinal);
        foreach (var (path, module, ownedMembers) in FeatureModules)
        {
            var fullPath = Path.Combine(root, path);
            Assert.True(File.Exists(fullPath), $"Actor Framework feature module '{path}' must exist.");

            var source = File.ReadAllText(fullPath);
            var declaration = module == "GeneratedProgramArtifacts"
                ? $"static class {module}"
                : $"static partial class {module}";
            Assert.Contains(declaration, source, StringComparison.Ordinal);
            Assert.All(ownedMembers, member => Assert.Contains(member, source, StringComparison.Ordinal));
        }

        foreach (var (path, ownedMembers) in FeatureGenerationFiles)
        {
            var source = File.ReadAllText(Path.Combine(root, path));
            Assert.All(ownedMembers, member => Assert.Contains(member, source, StringComparison.Ordinal));
        }

        Assert.Contains("GeneratedProgramArtifacts.Build(", lowererSource, StringComparison.Ordinal);
        Assert.Contains("Actors.CollectDirectives(", lowererSource, StringComparison.Ordinal);
        Assert.Contains("Projectiles.CollectDirectives(", lowererSource, StringComparison.Ordinal);
        Assert.Contains("Effects.CollectDirectives(", lowererSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Cross_domain_generation_primitives_are_neutral_and_shared()
    {
        var root = RepositoryRoot();
        var sharedSource = File.ReadAllText(Path.Combine(root, SharedGenerationPath));
        var actorsSource = File.ReadAllText(Path.Combine(root, "src/RetroSharp.Sdk.Frontend/ActorFrameworkLowerer.Actors.cs"));
        var actorsGenerationSource = File.ReadAllText(Path.Combine(root, "src/RetroSharp.Sdk.Frontend/ActorFrameworkLowerer.Actors.Generation.cs"));
        var projectilesGenerationSource = File.ReadAllText(Path.Combine(root, "src/RetroSharp.Sdk.Frontend/ActorFrameworkLowerer.Projectiles.Generation.cs"));
        var effectsSource = File.ReadAllText(Path.Combine(root, "src/RetroSharp.Sdk.Frontend/ActorFrameworkLowerer.Effects.cs"))
            + File.ReadAllText(Path.Combine(root, "src/RetroSharp.Sdk.Frontend/ActorFrameworkLowerer.Effects.Generation.cs"));

        Assert.Contains("PooledKindDispatch(", sharedSource, StringComparison.Ordinal);
        Assert.Contains("PooledCameraDeclarations(", sharedSource, StringComparison.Ordinal);
        Assert.Contains("BuildPooledScreenProjection(", sharedSource, StringComparison.Ordinal);
        Assert.Contains("StableSpriteDrawBlock(", sharedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectileKindDispatch(", sharedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildProjectileScreenProjection(", sharedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Projectiles.", sharedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Projectiles.Pooled", effectsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Projectiles.Build", effectsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Projectiles.Stable", effectsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Projectiles.Def", effectsSource, StringComparison.Ordinal);

        var lowererSources = Directory
            .GetFiles(Path.Combine(root, "src/RetroSharp.Sdk.Frontend"), "ActorFrameworkLowerer*.cs", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => (Path: path, Source: File.ReadAllText(path)))
            .ToList();
        Assert.NotEmpty(lowererSources);
        Assert.All(
            lowererSources,
            lowererSource => Assert.False(
                Regex.IsMatch(lowererSource.Source, ForbiddenParallelHelperPattern, RegexOptions.CultureInvariant),
                $"Actor Framework lowerer source '{Path.GetRelativePath(root, lowererSource.Path)}' must consume the shared pooled generation helpers instead of declaring or calling a parallel helper."));
        Assert.DoesNotContain("Actors.KindDispatch(", projectilesGenerationSource, StringComparison.Ordinal);

        foreach (var phase in new[] { "draw", "touch", "land", "player" })
        {
            Assert.Contains(
                $"BuildPooledScreenProjection(pool.Name, indexName, pool.Name, \"{phase}\", state.ScreenWidth, state.ScreenHeight, margin: 0)",
                actorsGenerationSource,
                StringComparison.Ordinal);
            Assert.Contains(
                $"PooledCameraDeclarations(pool.Name, \"{phase}\", configuresCamera: true)",
                actorsGenerationSource,
                StringComparison.Ordinal);
        }

        const string actorDispatchMessage = "actor dispatch requires at least one Enemies.Def declaration.";
        Assert.Contains("PooledKindDispatch(", actorsSource, StringComparison.Ordinal);
        Assert.Contains("PooledKindDispatch(", actorsGenerationSource, StringComparison.Ordinal);
        Assert.Contains("PooledKindDispatch(", projectilesGenerationSource, StringComparison.Ordinal);
        Assert.Contains(actorDispatchMessage, actorsSource, StringComparison.Ordinal);
        Assert.Contains(actorDispatchMessage, actorsGenerationSource, StringComparison.Ordinal);
        Assert.Contains(actorDispatchMessage, projectilesGenerationSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Root_lowerer_contains_only_shared_orchestration_plan_and_syntax_primitives()
    {
        var root = RepositoryRoot();
        var lowererSource = File.ReadAllText(Path.Combine(root, LowererPath));

        Assert.All(
            DomainMembersForbiddenInRoot,
            member => Assert.DoesNotContain(member, lowererSource, StringComparison.Ordinal));
        Assert.All(
            DomainQualifiedCallTokensForbiddenInRoot,
            token => Assert.DoesNotContain(token, lowererSource, StringComparison.Ordinal));
    }

    [Fact]
    public void Domain_declarations_are_owned_by_their_feature_files_and_absent_from_root()
    {
        var root = RepositoryRoot();
        var lowererSource = File.ReadAllText(Path.Combine(root, LowererPath));

        foreach (var (path, ownedDeclarations) in DomainDeclarations)
        {
            var ownerSource = File.ReadAllText(Path.Combine(root, path));
            Assert.All(ownedDeclarations, declaration =>
            {
                Assert.Contains(declaration, ownerSource, StringComparison.Ordinal);
                Assert.DoesNotContain(declaration, lowererSource, StringComparison.Ordinal);
            });
        }
    }

    [Fact]
    public void Generated_name_facts_are_aggregated_from_the_feature_modules()
    {
        var root = RepositoryRoot();
        var generatedSource = File.ReadAllText(Path.Combine(root, GeneratedProgramPath));

        Assert.Contains("Actors.GeneratedNames(state)", generatedSource, StringComparison.Ordinal);
        Assert.Contains("Projectiles.GeneratedNames(state)", generatedSource, StringComparison.Ordinal);
        Assert.Contains("Effects.GeneratedNames(state)", generatedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new GeneratedName(", generatedSource, StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RetroSharp.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }
}
