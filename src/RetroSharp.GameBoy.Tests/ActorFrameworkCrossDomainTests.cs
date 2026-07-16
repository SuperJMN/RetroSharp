namespace RetroSharp.GameBoy.Tests;

using RetroSharp.GameBoy;
using RetroSharp.Parser;
using RetroSharp.Sdk;
using Xunit;

public partial class GameBoyRomCompilerTests
{
    [Fact]
    [Trait("RetroSharp.TestOwnership", "FocusedFrontend")]
    public void Actor_pool_ownership_wins_over_homonymous_projectile_pool_calls()
    {
        const string source = """
                              void Main() {
                                  Actors.Pool(shared, 1);
                                  Enemies.Def(Goomba, behavior: Walker);
                                  Projectiles.Pool(shared, hero: 1, enemy: 1, requests: 1, offscreenMargin: 16);
                                  Projectiles.Def(HeroShot, team: Hero, sprite: hero_shot, speedX: 3, speedY: 0, damage: 1, lifetime: 32, hitboxWidth: 4, hitboxHeight: 4);
                                  shared.TouchActors(shared);
                              }
                              """;

        var program = ParseGameBoySourceWithPortable2D(source);
        var loweredProgram = ActorFrameworkLowerer.Lower(program, GameBoyTarget.Capabilities, supportsUpdate: true, supportsDraw: true);
        var visitor = new PrintNodeVisitor();
        loweredProgram.Accept(visitor);
        var lowered = visitor.ToString();

        Assert.Contains("shared.TouchActors(shared);", lowered, StringComparison.Ordinal);
        Assert.DoesNotContain("__shared_actor_hero_i", lowered, StringComparison.Ordinal);
    }

    [Fact]
    public void Projectile_pool_ownership_wins_over_homonymous_effect_pool_calls()
    {
        const string source = """
                              void Main() {
                                  Projectiles.Pool(shared, hero: 1, enemy: 1, requests: 1, offscreenMargin: 16);
                                  Effects.Pool(shared, capacity: 1, requests: 1);
                                  Effects.Def(Spark, sprite: spark_sprite, lifetime: 4);
                                  shared.Request(Spark, 40, 60);
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            var program = ParseGameBoySourceWithPortable2D(source);
            ActorFrameworkLowerer.Lower(program, GameBoyTarget.Capabilities, supportsUpdate: true, supportsDraw: true);
        });

        Assert.Equal(
            "shared.Request expects kind, x, y, direction, optional result, and optional owner.",
            exception.Message);
    }
}
