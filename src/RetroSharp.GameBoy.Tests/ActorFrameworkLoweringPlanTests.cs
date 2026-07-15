namespace RetroSharp.GameBoy.Tests;

using System.Reflection;
using RetroSharp.Core.Sdk;
using RetroSharp.GameBoy;
using RetroSharp.Parser;
using RetroSharp.Sdk;
using Xunit;

public sealed class ActorFrameworkLoweringPlanTests
{
    [Fact]
    public void Plan_discovers_actor_directives_and_drawn_pools()
    {
        const string source = """
                              void Main() {
                                  Actors.Pool(enemies, 2);
                                  Enemies.Def(Goomba, behavior: Walker, speed: 1, hp: 2);
                                  u8 speed = Enemies.Speed(Goomba);
                                  enemies.Draw();
                              }
                              """;

        var plan = Analyze(source);

        Assert.True(plan.HasDirectives);
        Assert.Equal(1, plan.ActorPoolCount);
        Assert.Equal(1, plan.EnemyDefinitionCount);
        Assert.Equal(new[] { "enemies" }, plan.DrawnActorPoolNames);
        Assert.Contains("Actor", plan.GeneratedNames);
        Assert.Contains("Goomba", plan.GeneratedNames);
        Assert.Contains("enemy_speed", plan.GeneratedNames);
    }

    [Fact]
    public void Plan_discovers_and_validates_spawn_layers()
    {
        var baseDirectory = WriteActorSpawnMap();
        const string source = """
                              void Main() {
                                  Actors.Pool(enemies, 2);
                                  Enemies.Def(Goomba, behavior: Walker);
                                  Actors.SpawnLayer(enemies, "level.tmj", "actors");
                              }
                              """;

        var plan = Analyze(source, baseDirectory);

        Assert.Equal(1, plan.SpawnLayerCount);
        Assert.Contains("__enemies_spawn_0_used", plan.GeneratedNames);
    }

    [Fact]
    public void Plan_discovers_projectile_directives()
    {
        const string source = """
                              void Main() {
                                  Projectiles.Pool(shots, hero: 1, enemy: 2, requests: 3, offscreenMargin: 16);
                                  Projectiles.Def(HeroShot, team: Hero, sprite: hero_shot, speedX: 3, speedY: 0, damage: 1, lifetime: 32, hitboxWidth: 4, hitboxHeight: 4);
                              }
                              """;

        var plan = Analyze(source);

        Assert.Equal(1, plan.ProjectilePoolCount);
        Assert.Equal(1, plan.ProjectileDefinitionCount);
        Assert.Contains("Projectile", plan.GeneratedNames);
        Assert.Contains("HeroShot", plan.GeneratedNames);
    }

    [Fact]
    public void Plan_discovers_effect_directives()
    {
        const string source = """
                              void Main() {
                                  Effects.Pool(fx, capacity: 2, requests: 3);
                                  Effects.Def(Spark, sprite: spark, lifetime: 8);
                              }
                              """;

        var plan = Analyze(source);

        Assert.Equal(1, plan.EffectPoolCount);
        Assert.Equal(1, plan.EffectDefinitionCount);
        Assert.Contains("Effect", plan.GeneratedNames);
        Assert.Contains("Spark", plan.GeneratedNames);
    }

    [Fact]
    public void Plan_for_a_program_without_directives_is_empty()
    {
        var plan = Analyze("void Main() { u8 value = 1; }");

        Assert.False(plan.HasDirectives);
        Assert.Equal(0, plan.ActorPoolCount);
        Assert.Equal(0, plan.EnemyDefinitionCount);
        Assert.Equal(0, plan.SpawnLayerCount);
        Assert.Equal(0, plan.ProjectilePoolCount);
        Assert.Equal(0, plan.ProjectileDefinitionCount);
        Assert.Equal(0, plan.EffectPoolCount);
        Assert.Equal(0, plan.EffectDefinitionCount);
        Assert.Empty(plan.DrawnActorPoolNames);
        Assert.Empty(plan.GeneratedNames);
    }

    private static ActorFrameworkLowerer.ActorFrameworkLoweringPlan Analyze(string source, string? baseDirectory = null)
    {
        var program = ParseGameBoySourceWithPortable2D(source);
        Assert.Null(typeof(ActorFrameworkLowerer).GetMethod(
            "Analyze",
            BindingFlags.Static | BindingFlags.Public));
        return ActorFrameworkLowerer.Analyze(
            program,
            GameBoyTarget.Capabilities,
            supportsUpdate: true,
            supportsDraw: true,
            baseDirectory);
    }

    private static ProgramSyntax ParseGameBoySourceWithPortable2D(string source)
    {
        var parse = new SomeParser().Parse(
            SdkLibrarySource.Merge(
                GameBoyTarget.Intrinsics,
                source,
                libraryImportPaths: [SdkImportResolver.Portable2D]));
        if (parse.IsFailure)
        {
            throw new InvalidOperationException(parse.Error);
        }

        return TargetProgramSelector.Select(parse.Value, GameBoyTarget.Intrinsics);
    }

    private static string WriteActorSpawnMap()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RetroSharp.GameBoy.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, "level.tmj"),
            """
            {
              "type": "map",
              "orientation": "orthogonal",
              "infinite": false,
              "width": 1,
              "height": 1,
              "tilewidth": 8,
              "tileheight": 8,
              "properties": [
                { "name": "retrosharpStreamY", "type": "int", "value": 0 }
              ],
              "layers": [
                { "type": "tilelayer", "name": "world", "width": 1, "height": 1, "data": [0] },
                {
                  "type": "objectgroup",
                  "name": "actors",
                  "objects": [
                    { "id": 1, "type": "Goomba", "x": 24, "y": 16 }
                  ]
                }
              ]
            }
            """);
        return directory;
    }
}
