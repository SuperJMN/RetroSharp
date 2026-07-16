namespace RetroSharp.GameBoy.Tests;

using System.Buffers.Binary;
using System.IO.Compression;
using RetroSharp.Core.Sdk;
using RetroSharp.Core.Sdk.Tiled;
using RetroSharp.Core.Targeting;
using RetroSharp.GameBoy;
using RetroSharp.Parser;
using RetroSharp.Sdk;
using Xunit;
using static RetroSharp.GameBoy.Tests.GameBoyTestSupport;

public partial class GameBoyRomCompilerTests
{
    [Fact]
    [Trait("RetroSharp.TestOwnership", "FocusedFrontend")]
    public void Projectile_framework_lowers_requests_update_and_draw_as_fixed_pools()
    {
        const string source = """
                              void Main() {
                                  Sprite.Asset(hero_shot, "hero-shot.sprite.json");
                                  Projectiles.Pool(shots, hero: 2, enemy: 3, requests: 4, offscreenMargin: 16);
                                  Projectiles.Def(HeroShot, team: Hero, sprite: hero_shot, speedX: 3, speedY: 0, damage: 1, lifetime: 32, hitboxWidth: 4, hitboxHeight: 4);
                                  u8 queued = 0;
                                  shots.Request(HeroShot, 40, 60, 0, queued);
                                  shots.ProcessRequests();
                                  shots.Update();
                                  shots.Draw();
                              }
                              """;

        var program = ParseGameBoySourceWithPortable2D(source);
        var loweredProgram = ActorFrameworkLowerer.Lower(program, GameBoyTarget.Capabilities, supportsUpdate: true, supportsDraw: true);
        var visitor = new PrintNodeVisitor();
        loweredProgram.Accept(visitor);
        var lowered = visitor.ToString();

        Assert.Contains("Projectile shotsHero[2];", lowered);
        Assert.Contains("Projectile shotsEnemy[3];", lowered);
        Assert.Contains("ProjectileSpawnRequest shotsRequests[4];", lowered);
        Assert.Contains("shotsHero[__shots_init_hero_i].active=0;", lowered);
        Assert.Contains("shotsEnemy[__shots_init_enemy_i].active=0;", lowered);
        Assert.Contains("shotsRequests[__shots_init_request_i].active=0;", lowered);
        Assert.Contains("shotsRequests[__shots_request_call0_i].kind=HeroShot;", lowered);
        Assert.Contains("shotsRequests[__shots_request_call0_i].direction=0;", lowered);
        Assert.Contains("queued=1;", lowered);
        Assert.Contains("queued=0;", lowered);
        Assert.Contains("shotsHero[__shots_process_HeroShot_i].active=1;", lowered);
        Assert.Contains("shotsHero[__shots_process_HeroShot_i].direction=__shots_process_request_direction;", lowered);
        // direction == 0 travels right (+speedX); any non-zero direction travels left (-speedX).
        Assert.Contains("shotsHero[__shots_update_hero_i].direction==0", lowered);
        Assert.Contains("u8 __shots_update_hero_i_vx=(u8)shotsHero[__shots_update_hero_i].vx;", lowered);
        Assert.Contains("shotsHero[__shots_update_hero_i].x+=__shots_update_hero_i_vx;", lowered);
        Assert.Contains("shotsHero[__shots_update_hero_i].x-=__shots_update_hero_i_vx;", lowered);
        Assert.Contains("shotsHero[__shots_update_hero_i].age+=1;", lowered);
        Assert.Contains("RetroSharp_Portable2D_portable2d_sprite_draw(hero_shot, __shots_draw_hero_0_x_HeroShot, __shots_draw_hero_0_y_HeroShot, 0, false, 0);", lowered);
        Assert.Contains("RetroSharp_Portable2D_portable2d_sprite_draw(hero_shot, __shots_draw_hero_1_x_HeroShot, __shots_draw_hero_1_y_HeroShot, 0, false, 0);", lowered);
    }

    [Fact]
    [Trait("RetroSharp.TestOwnership", "FocusedFrontend")]
    public void Projectile_framework_draw_hides_inactive_or_offscreen_slots_without_skipping_the_sprite_call()
    {
        const string source = """
                              void Main() {
                                  Sprite.Asset(hero_shot, "hero-shot.sprite.json");
                                  Projectiles.Pool(shots, hero: 1, enemy: 1, requests: 1, offscreenMargin: 16);
                                  Projectiles.Def(HeroShot, team: Hero, sprite: hero_shot, speedX: 3, speedY: 0, damage: 1, lifetime: 32, hitboxWidth: 4, hitboxHeight: 4);
                                  shotsHero[0].kind = HeroShot;
                                  shots.Draw();
                              }
                              """;

        var program = ParseGameBoySourceWithPortable2D(source);
        var loweredProgram = ActorFrameworkLowerer.Lower(program, GameBoyTarget.Capabilities, supportsUpdate: true, supportsDraw: true);
        var visitor = new PrintNodeVisitor();
        loweredProgram.Accept(visitor);
        var lowered = visitor.ToString();

        Assert.Contains("u8 __shots_draw_hero_0_x_HeroShot=0;", lowered);
        Assert.Contains("u8 __shots_draw_hero_0_y_HeroShot=144;", lowered);
        Assert.Contains("__shots_draw_hero_0_x_HeroShot=__shots_draw_hero_0_screen_x;", lowered);
        Assert.Contains("__shots_draw_hero_0_y_HeroShot=__shots_draw_hero_0_screen_y;", lowered);
        Assert.Contains("RetroSharp_Portable2D_portable2d_sprite_draw(hero_shot, __shots_draw_hero_0_x_HeroShot, __shots_draw_hero_0_y_HeroShot, 0, false, 0);", lowered);
        Assert.Equal(1, CountOccurrences(lowered, "RetroSharp_Portable2D_portable2d_sprite_draw(hero_shot"));
    }

    [Fact]
    [Trait("RetroSharp.TestOwnership", "FocusedFrontend")]
    public void Projectile_framework_lowers_gravity_arc_through_instance_velocity()
    {
        const string source = """
                              void Main() {
                                  Sprite.Asset(hero_shot, "hero-shot.sprite.json");
                                  Projectiles.Pool(shots, hero: 1, enemy: 1, requests: 1, offscreenMargin: 16);
                                  Projectiles.Def(ArcShot, team: Hero, sprite: hero_shot, speedX: 2, speedY: 1, damage: 1, lifetime: 32, hitboxWidth: 4, hitboxHeight: 4, behavior: GravityArc);
                                  shots.Update();
                              }
                              """;

        var program = ParseGameBoySourceWithPortable2D(source);
        var loweredProgram = ActorFrameworkLowerer.Lower(program, GameBoyTarget.Capabilities, supportsUpdate: true, supportsDraw: true);
        var visitor = new PrintNodeVisitor();
        loweredProgram.Accept(visitor);
        var lowered = visitor.ToString();

        Assert.Contains("u8 __shots_update_hero_i_vx=(u8)shotsHero[__shots_update_hero_i].vx;", lowered);
        Assert.Contains("i8 __shots_update_hero_i_vy=shotsHero[__shots_update_hero_i].vy;", lowered);
        Assert.Contains("shotsHero[__shots_update_hero_i].x+=__shots_update_hero_i_vx;", lowered);
        Assert.Contains("__shots_update_hero_i_vy<0", lowered);
        Assert.Contains("u8 __shots_update_hero_i_vy_up=(u8)(0-__shots_update_hero_i_vy);", lowered);
        Assert.Contains("u8 __shots_update_hero_i_vy_down=(u8)__shots_update_hero_i_vy;", lowered);
        Assert.Contains("shotsHero[__shots_update_hero_i].y-=__shots_update_hero_i_vy_up;", lowered);
        Assert.Contains("shotsHero[__shots_update_hero_i].y+=__shots_update_hero_i_vy_down;", lowered);
        Assert.Contains("shotsHero[__shots_update_hero_i].vy+=1;", lowered);
        Assert.DoesNotContain("shotsHero[__shots_update_hero_i].y+=ArcShotSpeedY;", lowered);
    }

    [Fact]
    [Trait("RetroSharp.TestOwnership", "FocusedFrontend")]
    public void Projectile_framework_lowers_bouncing_tile_collision()
    {
        const string source = """
                              void Main() {
                                  Sprite.Asset(fireball, "fireball.sprite.json");
                                  World.Column(0, 0, 4);
                                  World.Flags(0, 0, 1);
                                  World.Map(1, 10, 2);
                                  Camera.Init(1, 10, 2);
                                  Projectiles.Pool(shots, hero: 1, enemy: 1, requests: 1, offscreenMargin: 16);
                                  Projectiles.Def(Fireball, team: Hero, sprite: fireball, speedX: 3, speedY: 0, damage: 1, lifetime: 48, hitboxWidth: 8, hitboxHeight: 8, behavior: GravityArc, tileCollision: Bounce, bounceSpeedY: 4);
                                  shots.TouchTiles(0, 1);
                              }
                              """;

        var program = ParseGameBoySourceWithPortable2D(source);
        var loweredProgram = ActorFrameworkLowerer.Lower(program, GameBoyTarget.Capabilities, supportsUpdate: true, supportsDraw: true);
        var visitor = new PrintNodeVisitor();
        loweredProgram.Accept(visitor);
        var lowered = visitor.ToString();

        Assert.Contains("RetroSharp_Portable2D_portable2d_camera_screen_aabb_tiles(\"default\", __shots_tiles_hero_screen_x, __shots_tiles_hero_screen_y, 8, 8, 1)", lowered);
        Assert.Contains("shotsHero[__shots_tiles_hero_i].vy=0-FireballBounceSpeedY;", lowered);
        Assert.DoesNotContain("shotsHero[__shots_tiles_hero_i].active=0;", lowered);
    }

    [Fact]
    [Trait("RetroSharp.TestOwnership", "FocusedFrontend")]
    public void Projectile_framework_lowers_expiring_tile_collision_with_impact_effect()
    {
        const string source = """
                              void Main() {
                                  Sprite.Asset(bomb, "bomb.sprite.json");
                                  Sprite.Asset(spark, "spark.sprite.json");
                                  World.Column(0, 0, 4);
                                  World.Flags(0, 0, 1);
                                  World.Map(1, 10, 2);
                                  Camera.Init(1, 10, 2);
                                  Effects.Pool(fx, capacity: 2, requests: 2);
                                  Effects.Def(Spark, sprite: spark, lifetime: 6);
                                  Projectiles.Pool(shots, hero: 1, enemy: 1, requests: 1, offscreenMargin: 16, effects: fx);
                                  Projectiles.Def(Bomb, team: Hero, sprite: bomb, speedX: 2, speedY: 0, damage: 1, lifetime: 32, hitboxWidth: 8, hitboxHeight: 8, tileCollision: Expire, impactEffect: Spark);
                                  shots.TouchTiles(0, 1);
                              }
                              """;

        var program = ParseGameBoySourceWithPortable2D(source);
        var loweredProgram = ActorFrameworkLowerer.Lower(program, GameBoyTarget.Capabilities, supportsUpdate: true, supportsDraw: true);
        var visitor = new PrintNodeVisitor();
        loweredProgram.Accept(visitor);
        var lowered = visitor.ToString();

        Assert.Contains("RetroSharp_Portable2D_portable2d_camera_screen_aabb_tiles(\"default\", __shots_tiles_hero_screen_x, __shots_tiles_hero_screen_y, 8, 8, 1)", lowered);
        Assert.Contains(".kind=Spark;", lowered);
        Assert.Contains("shotsHero[__shots_tiles_hero_i].active=0;", lowered);
    }

    [Fact]
    [Trait("RetroSharp.TestOwnership", "FocusedFrontend")]
    public void Projectile_framework_culls_update_against_camera_bounds_plus_offscreen_margin()
    {
        const string source = """
                              void Main() {
                                  Sprite.Asset(hero_shot, "hero-shot.sprite.json");
                                  Camera.Init(40, 18, 1);
                                  Camera.SetPosition(32, 8);
                                  Projectiles.Pool(shots, hero: 1, enemy: 1, requests: 1, offscreenMargin: 16);
                                  Projectiles.Def(HeroShot, team: Hero, sprite: hero_shot, speedX: 3, speedY: 0, damage: 1, lifetime: 32, hitboxWidth: 4, hitboxHeight: 4);
                                  shots.Update();
                              }
                              """;

        var program = ParseGameBoySourceWithPortable2D(source);
        var loweredProgram = ActorFrameworkLowerer.Lower(program, GameBoyTarget.Capabilities, supportsUpdate: true, supportsDraw: true);
        var visitor = new PrintNodeVisitor();
        loweredProgram.Accept(visitor);
        var lowered = visitor.ToString();

        Assert.Contains("u8 __shots_update_hero_visible_x=0;", lowered);
        Assert.Contains("__shots_update_hero_screen_x<176", lowered);
        Assert.Contains("__shots_update_hero_screen_x>=240", lowered);
        Assert.Contains("!(__shots_update_hero_visible_x!=0&&__shots_update_hero_visible_y!=0)", lowered);
        Assert.Contains("shotsHero[__shots_update_hero_i].active=0;", lowered);
    }

    [Fact]
    [Trait("RetroSharp.TestOwnership", "FocusedFrontend")]
    public void Projectile_framework_uniquifies_request_temporaries_per_call_site()
    {
        const string source = """
                              void Main() {
                                  Sprite.Asset(hero_shot, "hero-shot.sprite.json");
                                  Projectiles.Pool(shots, hero: 2, enemy: 1, requests: 2, offscreenMargin: 16);
                                  Projectiles.Def(HeroShot, team: Hero, sprite: hero_shot, speedX: 3, speedY: 0, damage: 1, lifetime: 32, hitboxWidth: 4, hitboxHeight: 4);
                                  u8 queuedRight = 0;
                                  u8 queuedLeft = 0;
                                  shots.Request(HeroShot, 40, 60, 0, queuedRight);
                                  shots.Request(HeroShot, 20, 60, 1, queuedLeft);
                              }
                              """;

        var program = ParseGameBoySourceWithPortable2D(source);
        var loweredProgram = ActorFrameworkLowerer.Lower(program, GameBoyTarget.Capabilities, supportsUpdate: true, supportsDraw: true);
        var visitor = new PrintNodeVisitor();
        loweredProgram.Accept(visitor);
        var lowered = visitor.ToString();

        Assert.Contains("u8 __shots_request_call0_written=0;", lowered);
        Assert.Contains("u8 __shots_request_call1_written=0;", lowered);
        Assert.Contains("for(u8 __shots_request_call0_i=0;__shots_request_call0_i<countof(shotsRequests);__shots_request_call0_i+=1)", lowered);
        Assert.Contains("for(u8 __shots_request_call1_i=0;__shots_request_call1_i<countof(shotsRequests);__shots_request_call1_i+=1)", lowered);
    }

    [Fact]
    [Trait("RetroSharp.TestOwnership", "FocusedFrontend")]
    public void Projectile_framework_emits_effect_requests_for_spawn_impact_and_expiration()
    {
        const string source = """
                              void Main() {
                                  Sprite.Asset(hero_shot, "hero-shot.sprite.json");
                                  Sprite.Asset(spark_sprite, "spark.sprite.json");
                                  Effects.Pool(fx, capacity: 4, requests: 4);
                                  Effects.Def(Muzzle, sprite: spark_sprite, lifetime: 4);
                                  Effects.Def(Spark, sprite: spark_sprite, lifetime: 8);
                                  Effects.Def(Puff, sprite: spark_sprite, lifetime: 6);
                                  Projectiles.Pool(shots, hero: 1, enemy: 1, requests: 1, offscreenMargin: 16, effects: fx);
                                  Projectiles.Def(HeroShot, team: Hero, sprite: hero_shot, speedX: 3, speedY: 0, damage: 1, lifetime: 32, hitboxWidth: 4, hitboxHeight: 4, spawnEffect: Muzzle, impactEffect: Spark, expireEffect: Puff);
                                  Actors.Pool(enemies, 1);
                                  Enemies.Def(Goomba, behavior: Walker, hp: 2, hitboxWidth: 8, hitboxHeight: 8);
                                  u8 queued = 0;
                                  shots.Request(HeroShot, 40, 60, 0, queued);
                                  shots.ProcessRequests();
                                  shots.Update();
                                  shots.TouchActors(enemies);
                              }
                              """;

        var program = ParseGameBoySourceWithPortable2D(source);
        var loweredProgram = ActorFrameworkLowerer.Lower(program, GameBoyTarget.Capabilities, supportsUpdate: true, supportsDraw: true);
        var visitor = new PrintNodeVisitor();
        loweredProgram.Accept(visitor);
        var lowered = visitor.ToString();

        Assert.Contains("fxRequests", lowered);
        Assert.Contains(".kind=Muzzle;", lowered);
        Assert.Contains(".kind=Spark;", lowered);
        Assert.Contains(".kind=Puff;", lowered);
        Assert.Contains("fxRequests[", lowered);
        Assert.Contains("shotsHero[__shots_actor_hero_i].active=0;", lowered);
    }

    [Fact]
    [Trait("RetroSharp.TestOwnership", "FocusedFrontend")]
    public void Projectile_framework_lowers_actor_and_hero_collision_hooks()
    {
        const string source = """
                              void Main() {
                                  Sprite.Asset(hero_shot, "hero-shot.sprite.json");
                                  Sprite.Asset(enemy_bullet, "enemy-bullet.sprite.json");
                                  Projectiles.Pool(shots, hero: 1, enemy: 1, requests: 2, offscreenMargin: 16);
                                  Projectiles.Def(HeroShot, team: Hero, sprite: hero_shot, speedX: 3, speedY: 0, damage: 1, lifetime: 32, hitboxWidth: 4, hitboxHeight: 4);
                                  Projectiles.Def(EnemyBullet, team: Enemy, sprite: enemy_bullet, speedX: 1, speedY: 0, damage: 2, lifetime: 64, hitboxWidth: 4, hitboxHeight: 4);
                                  Actors.Pool(enemies, 1);
                                  Enemies.Def(Goomba, behavior: Walker, hp: 2, hitboxWidth: 8, hitboxHeight: 8);
                                  u8 heroDamage = 0;
                                  shots.TouchActors(enemies);
                                  shots.TouchHero(72, 40, 16, 16, heroDamage);
                              }
                              """;

        var program = ParseGameBoySourceWithPortable2D(source);
        var loweredProgram = ActorFrameworkLowerer.Lower(program, GameBoyTarget.Capabilities, supportsUpdate: true, supportsDraw: true);
        var visitor = new PrintNodeVisitor();
        loweredProgram.Accept(visitor);
        var lowered = visitor.ToString();

        Assert.Contains("for(u8 __shots_actor_hero_i=0;__shots_actor_hero_i<countof(shotsHero);__shots_actor_hero_i+=1)", lowered);
        Assert.Contains("for(u8 __shots_actor_enemies_i=0;__shots_actor_enemies_i<countof(enemies);__shots_actor_enemies_i+=1)", lowered);
        Assert.Contains("shotsHero[__shots_actor_hero_i].xHi", lowered);
        Assert.Contains("enemies[__shots_actor_enemies_i].xHi", lowered);
        Assert.Contains("shotsHero[__shots_actor_hero_i].yHi", lowered);
        Assert.Contains("enemies[__shots_actor_enemies_i].yHi", lowered);
        Assert.Contains("shotsHero[__shots_actor_hero_i].active!=0&&", lowered);
        Assert.Contains("enemies[__shots_actor_enemies_i].health-=HeroShotDamage;", lowered);
        Assert.Contains("shotsHero[__shots_actor_hero_i].active=0;", lowered);
        Assert.Contains("for(u8 __shots_hero_enemy_i=0;__shots_hero_enemy_i<countof(shotsEnemy);__shots_hero_enemy_i+=1)", lowered);
        Assert.Contains("heroDamage+=EnemyBulletDamage;", lowered);
        Assert.Contains("shotsEnemy[__shots_hero_enemy_i].active=0;", lowered);
    }

    [Fact]
    public void Compiles_projectile_draw_through_game_boy_backend()
    {
        var baseDirectory = WriteSpriteJsonAsset(
            "hero-shot.sprite.json",
            SpriteJson(Rows(8, 16, "11111111")));
        const string source = """
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(hero_shot, "hero-shot.sprite.json");
                                  Projectiles.Pool(shots, hero: 1, enemy: 1, requests: 1, offscreenMargin: 16);
                                  Projectiles.Def(HeroShot, team: Hero, sprite: hero_shot, speedX: 3, speedY: 0, damage: 1, lifetime: 32, hitboxWidth: 4, hitboxHeight: 4);
                                  u8 queued = 0;
                                  shots.Request(HeroShot, 40, 60, 0, queued);
                                  shots.ProcessRequests();
                                  Video.WaitVBlank();
                                  shots.Draw();
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source, baseDirectory);

        Assert.Equal(32768, rom.Length);
    }

    [Fact]
    public void Projectile_draw_runtime_uses_distinct_oam_slots_for_pool_slots()
    {
        var baseDirectory = WriteSpriteJsonAsset(
            "hero-shot.sprite.json",
            SpriteJson(Rows(8, 16, "11111111")));
        const string source = """
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(hero_shot, "hero-shot.sprite.json");
                                  Projectiles.Pool(shots, hero: 2, enemy: 1, requests: 2, offscreenMargin: 16);
                                  Projectiles.Def(HeroShot, team: Hero, sprite: hero_shot, speedX: 0, speedY: 0, damage: 1, lifetime: 32, hitboxWidth: 4, hitboxHeight: 4);
                                  u8 queuedA = 0;
                                  u8 queuedB = 0;
                                  shots.Request(HeroShot, 24, 40, 0, queuedA);
                                  shots.Request(HeroShot, 64, 60, 0, queuedB);
                                  shots.ProcessRequests();
                                  while (true) {
                                      Video.WaitVBlank();
                                      shots.Draw();
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source, baseDirectory);
        var cpu = new GameBoyTestCpu(rom) { CycleAccurateLy = true };
        cpu.RunFrames(4);

        Assert.Equal(56, cpu.Oam(0xFE00));
        Assert.Equal(32, cpu.Oam(0xFE01));
        Assert.Equal(76, cpu.Oam(0xFE04));
        Assert.Equal(72, cpu.Oam(0xFE05));
    }

    [Fact]
    public void Rejects_projectile_pool_without_literal_limits()
    {
        const string source = """
                              void Main() {
                                  u8 heroLimit = 2;
                                  Projectiles.Pool(shots, hero: heroLimit, enemy: 3, requests: 4, offscreenMargin: 16);
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource(source));

        Assert.Equal("Projectiles.Pool for 'shots' requires literal byte limits for hero, enemy, requests, and offscreenMargin.", exception.Message);
    }

    [Fact]
    public void Rejects_unknown_projectile_team()
    {
        const string source = """
                              void Main() {
                                  Projectiles.Pool(shots, hero: 1, enemy: 1, requests: 1, offscreenMargin: 16);
                                  Projectiles.Def(Shot, team: Neutral, sprite: shot, speedX: 1, speedY: 0, damage: 1, lifetime: 16, hitboxWidth: 4, hitboxHeight: 4);
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource(source));

        Assert.Equal("Unknown projectile team 'Neutral'.", exception.Message);
    }

}
