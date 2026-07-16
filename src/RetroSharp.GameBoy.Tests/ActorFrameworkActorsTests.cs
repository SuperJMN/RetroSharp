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
    public void Compiles_actor_pool_and_enemy_definition_like_fixed_storage_and_constants()
    {
        const string manualSource = """
                                    struct Actor {
                                        u8 kind;
                                        u8 active;
                                        u8 x;
                                        u8 xHi;
                                        u8 y;
                                        u8 yHi;
                                        i8 vx;
                                        i8 vy;
                                        u8 state;
                                        u8 timer;
                                        u8 facing;
                                        u8 animTick;
                                        u8 health;
                                    }

                                    const Walker = 1;
                                    const Goomba = 1;
                                    const GoombaBehavior = Walker;
                                    const GoombaSpeed = 1;
                                    const GoombaHp = 1;
                                    const GoombaCooldown = 60;
                                    const GoombaContactDamage = 0;
                                    const GoombaHitboxWidth = 0;
                                    const GoombaHitboxHeight = 0;

                                    inline u8 enemy_behavior(u8 kind) => kind == Goomba ? GoombaBehavior : 0;
                                    inline u8 enemy_speed(u8 kind) => kind == Goomba ? GoombaSpeed : 0;
                                    inline u8 enemy_hp(u8 kind) => kind == Goomba ? GoombaHp : 0;
                                    inline u8 enemy_cooldown(u8 kind) => kind == Goomba ? GoombaCooldown : 0;

                                    void Main() {
                                        Video.Init();
                                        Actor enemies[2];
                                        enemies[0].active = 1;
                                        enemies[0].kind = Goomba;
                                        enemies[0].health = enemy_hp(enemies[0].kind);
                                        enemies[0].vx = (i8)enemy_speed(enemies[0].kind);
                                        enemies[0].timer = enemy_cooldown(enemies[0].kind);
                                    }
                                    """;

        const string actorSource = """
                                   void Main() {
                                       Video.Init();
                                       Actors.Pool(enemies, 0b10);
                                       Enemies.Def(Goomba, behavior: Walker, speed: 0x01u8, hp: 0b1, cooldown: 0x3Cu8);
                                       enemies[0].active = 1;
                                       enemies[0].kind = Goomba;
                                       enemies[0].health = Enemies.Hp(enemies[0].kind);
                                       enemies[0].vx = (i8)Enemies.Speed(enemies[0].kind);
                                       enemies[0].timer = Enemies.Cooldown(enemies[0].kind);
                                   }
                                   """;

        var manualRom = GameBoyRomCompiler.CompileSource(manualSource);
        var actorRom = GameBoyRomCompiler.CompileSource(actorSource);

        Assert.True(
            manualRom.SequenceEqual(actorRom),
            "Game Boy actor framework should lower to the same fixed Actor storage and direct enemy lookup constants as the manual source.");
        Assert.Equal(manualRom, actorRom);
    }

    [Fact]
    public void Actor_framework_directives_can_come_from_renamed_sdk_role_metadata_gb()
    {
        const string manualSource = """
                                    struct Actor {
                                        u8 kind;
                                        u8 active;
                                        u8 x;
                                        u8 xHi;
                                        u8 y;
                                        u8 yHi;
                                        i8 vx;
                                        i8 vy;
                                        u8 state;
                                        u8 timer;
                                        u8 facing;
                                        u8 animTick;
                                        u8 health;
                                    }

                                    const Slime = 1;
                                    const SlimeSpeed = 3;

                                    inline u8 enemy_speed(u8 kind) => kind == Slime ? SlimeSpeed : 0;

                                    void Main() {
                                        Actor mobs[1];
                                        mobs[0].active = 1;
                                        mobs[0].kind = Slime;
                                        mobs[0].vx = (i8)enemy_speed(mobs[0].kind);
                                    }
                                    """;

        const string roleBackedSource = """
                                        class EncounterKit {
                                            static inline [sdk_role("actor_pool")] void Reserve(i16 name, i16 capacity) {
                                            }

                                            static inline [sdk_role("actor_enemy_def")] void Enemy(i16 name) {
                                            }

                                            static inline [sdk_role("actor_enemy_speed")] i16 Pace(i16 kind) => 0;
                                        }

                                        void Main() {
                                            EncounterKit.Reserve(mobs, 1);
                                            EncounterKit.Enemy(Slime, behavior: Walker, speed: 3);
                                            mobs[0].active = 1;
                                            mobs[0].kind = Slime;
                                            mobs[0].vx = (i8)EncounterKit.Pace(mobs[0].kind);
                                        }
                                        """;

        Assert.Equal(GameBoyRomCompiler.CompileSource(manualSource), GameBoyRomCompiler.CompileSource(roleBackedSource));
    }

    [Fact]
    public void Rejects_actor_generated_name_collision_with_user_symbol()
    {
        const string source = """
                              const GoombaSpeed = 7;

                              void Main() {
                                  Actors.Pool(enemies, 1);
                                  Enemies.Def(Goomba, behavior: Walker, speed: 1);
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource(source));

        Assert.Equal("actor framework cannot generate Enemies.Def 'Goomba' speed constant named 'GoombaSpeed' because user constant 'GoombaSpeed' is already declared.", exception.Message);
    }

    [Fact]
    public void Rejects_actor_generated_name_collision_between_directives()
    {
        const string source = """
                              void Main() {
                                  Actors.Pool(enemies, 1);
                                  Enemies.Def(Goomba, behavior: Walker, speed: 1);
                                  Enemies.Def(GoombaSpeed, behavior: Flyer);
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource(source));

        Assert.Equal("actor framework cannot generate Enemies.Def 'GoombaSpeed' kind constant named 'GoombaSpeed' because Enemies.Def 'Goomba' speed constant also generates 'GoombaSpeed'.", exception.Message);
    }

    [Fact]
    [Trait("RetroSharp.TestOwnership", "FocusedFrontend")]
    public void Prunes_unused_enemy_lookup_helpers_until_author_calls_metadata_api()
    {
        const string unusedSource = """
                                    void Main() {
                                        Actors.Pool(enemies, 1);
                                        Enemies.Def(Goomba, behavior: Walker, speed: 1, hp: 2);
                                        enemies[0].kind = Goomba;
                                    }
                                    """;

        const string usedSource = """
                                  void Main() {
                                      Actors.Pool(enemies, 1);
                                      Enemies.Def(Goomba, behavior: Walker, speed: 1, hp: 2);
                                      enemies[0].kind = Goomba;
                                      enemies[0].vx = (i8)Enemies.Speed(enemies[0].kind);
                                  }
                                  """;

        var unusedProgram = ParseGameBoySourceWithPortable2D(unusedSource);
        var unusedLowered = ActorFrameworkLowerer.Lower(unusedProgram, GameBoyTarget.Capabilities, supportsUpdate: true, supportsDraw: true);

        var usedProgram = ParseGameBoySourceWithPortable2D(usedSource);
        var usedLowered = ActorFrameworkLowerer.Lower(usedProgram, GameBoyTarget.Capabilities, supportsUpdate: true, supportsDraw: true);

        Assert.DoesNotContain(unusedLowered.Functions, function => function.Name.StartsWith("enemy_", StringComparison.Ordinal));
        Assert.Contains(usedLowered.Functions, function => function.Name == "enemy_speed");
        Assert.DoesNotContain(usedLowered.Functions, function => function.Name == "enemy_hp");
    }

    [Fact]
    [Trait("RetroSharp.TestOwnership", "FocusedFrontend")]
    public void Actor_draw_hoists_camera_x_projection_once_per_phase_loop()
    {
        const string source = """
                              void Main() {
                                  Sprite.Asset(goomba, "goomba.sprite.json");
                                  Sprite.Asset(bat, "bat.sprite.json");
                                  Actors.Pool(enemies, 2);
                                  Enemies.Def(Goomba, sprite: goomba, behavior: Walker, speed: 1);
                                  Enemies.Def(Bat, sprite: bat, behavior: Flyer, speed: 1);
                                  enemies[0].active = 1;
                                  enemies[0].kind = Goomba;
                                  enemies[1].active = 1;
                                  enemies[1].kind = Bat;
                                  enemies.Draw();
                              }
                              """;

        var program = ParseGameBoySourceWithPortable2D(source);
        var loweredProgram = ActorFrameworkLowerer.Lower(program, GameBoyTarget.Capabilities, supportsUpdate: true, supportsDraw: true);
        var visitor = new PrintNodeVisitor();
        loweredProgram.Accept(visitor);
        var lowered = visitor.ToString();

        Assert.Equal(1, CountOccurrences(lowered, "u8 __enemies_draw_camera_x_lo=__rs_actor_camera_x_lo();"));
        Assert.Equal(1, CountOccurrences(lowered, "u8 __enemies_draw_camera_x_hi=__rs_actor_camera_x_hi();"));
        Assert.Equal(1, CountOccurrences(lowered, "u8 __enemies_draw_screen_x=enemies[__enemies_draw_i].x-__enemies_draw_camera_x_lo;"));
        Assert.DoesNotContain("__enemies_draw_camera_x_lo_Goomba", lowered);
        Assert.DoesNotContain("__enemies_draw_camera_x_hi_Goomba", lowered);
        Assert.DoesNotContain("__enemies_draw_camera_x_lo_Bat", lowered);
        Assert.DoesNotContain("__enemies_draw_camera_x_hi_Bat", lowered);
        Assert.DoesNotContain("__enemies_draw_screen_x_Goomba", lowered);
        Assert.DoesNotContain("__enemies_draw_screen_x_Bat", lowered);
        Assert.True(
            lowered.IndexOf("u8 __enemies_draw_camera_x_lo=__rs_actor_camera_x_lo();", StringComparison.Ordinal) <
            lowered.IndexOf("for(u8 __enemies_draw_i=0;", StringComparison.Ordinal),
            "camera X should be read before the draw slot loop.");
    }

    [Fact]
    [Trait("RetroSharp.TestOwnership", "FocusedFrontend")]
    public void Actor_draw_hides_offscreen_slots_without_skipping_the_sprite_call()
    {
        const string source = """
                              void Main() {
                                  Sprite.Asset(goomba, "goomba.sprite.json");
                                  Actors.Pool(enemies, 1);
                                  Enemies.Def(Goomba, sprite: goomba, behavior: Walker, speed: 1);
                                  enemies[0].active = 1;
                                  enemies[0].kind = Goomba;
                                  enemies.Draw();
                              }
                              """;

        var program = ParseGameBoySourceWithPortable2D(source);
        var loweredProgram = ActorFrameworkLowerer.Lower(program, GameBoyTarget.Capabilities, supportsUpdate: true, supportsDraw: true);
        var visitor = new PrintNodeVisitor();
        loweredProgram.Accept(visitor);
        var lowered = visitor.ToString();

        Assert.Contains("u8 __enemies_draw_x_Goomba=0;", lowered);
        Assert.Contains("u8 __enemies_draw_y_Goomba=144;", lowered);
        Assert.Contains("__enemies_draw_x_Goomba=__enemies_draw_screen_x;", lowered);
        Assert.Contains("__enemies_draw_y_Goomba=__enemies_draw_screen_y;", lowered);
        Assert.Contains("RetroSharp_Portable2D_portable2d_sprite_draw(goomba, __enemies_draw_x_Goomba, __enemies_draw_y_Goomba, 0, false, 0);", lowered);
        Assert.Equal(1, CountOccurrences(lowered, "RetroSharp_Portable2D_portable2d_sprite_draw(goomba"));
    }

    [Fact]
    [Trait("RetroSharp.TestOwnership", "FocusedFrontend")]
    public void Actor_framework_projects_wide_y_spawns_for_draw_and_collision_on_game_boy()
    {
        var baseDirectory = WriteActorSpawnMap(
            """
            [
              { "id": 1, "type": "Goomba", "x": 24, "y": 261 }
            ]
            """);
        const string source = """
                              void Main() {
                                  World.Column(0, 0, 0);
                                  World.Map(40, 10, 40);
                                  Camera.Init(40, 10, 18);
                                  Sprite.Asset(goomba, "goomba.sprite.json");
                                  Actors.Pool(enemies, 2);
                                  Enemies.Def(Goomba, sprite: goomba, behavior: Walker, hitboxWidth: 8, hitboxHeight: 8);
                                  Camera.SetPosition(0, 160);
                                  Actors.SpawnLayer(enemies, "level.tmj", "actors");
                                  enemies.TouchTiles(0, 1);
                                  enemies.LandOnTiles(4, 12, 1);
                                  enemies.Draw();
                              }
                              """;

        var program = ParseGameBoySourceWithPortable2D(source);
        var loweredProgram = ActorFrameworkLowerer.Lower(program, GameBoyTarget.Capabilities, supportsUpdate: true, supportsDraw: true, baseDirectory);
        var visitor = new PrintNodeVisitor();
        loweredProgram.Accept(visitor);
        var lowered = visitor.ToString();

        Assert.Contains("u8 yHi;", lowered);
        Assert.Contains("inline u8 __enemies_spawn_0_y(u8 index)=>5;", lowered);
        Assert.Contains("inline u8 __enemies_spawn_0_yHi(u8 index)=>1;", lowered);
        Assert.Contains("u8 __enemies_touch_screen_y=enemies[__enemies_touch_i].y-__enemies_touch_camera_y_lo;", lowered);
        Assert.Contains("RetroSharp_Portable2D_portable2d_camera_screen_aabb_tiles(\"default\", __enemies_touch_screen_x, __enemies_touch_screen_y, 8, 8, 1)", lowered);
        Assert.Contains("RetroSharp_Portable2D_portable2d_camera_screen_aabb_hit_top(\"default\", __enemies_land_screen_x, __enemies_land_screen_y-4", lowered);
        Assert.Contains("u8 __enemies_draw_x_Goomba=0;", lowered);
        Assert.Contains("u8 __enemies_draw_y_Goomba=144;", lowered);
        Assert.Contains("__enemies_draw_x_Goomba=__enemies_draw_screen_x;", lowered);
        Assert.Contains("__enemies_draw_y_Goomba=__enemies_draw_screen_y;", lowered);
        Assert.Contains("RetroSharp_Portable2D_portable2d_sprite_draw(goomba, __enemies_draw_x_Goomba, __enemies_draw_y_Goomba, 0, false, 0);", lowered);
        Assert.Equal(1, CountOccurrences(lowered, "RetroSharp_Portable2D_portable2d_sprite_draw(goomba"));
    }

    [Fact]
    public void Actor_framework_runtime_uses_wide_y_for_game_boy_draw_and_collision()
    {
        var baseDirectory = WriteActorSpawnMap(
            """
            [
              { "id": 1, "type": "Goomba", "x": 24, "y": 261 }
            ]
            """);
        File.WriteAllText(Path.Combine(baseDirectory, "goomba.sprite.json"), SpriteJson(Rows(8, 16, "11111111")));
        const string source = """
                              void Main() {
                                  Video.Init();
                                  World.Column(0,
                                      0, 0, 0, 0, 0, 0, 0, 0,
                                      0, 0, 0, 0, 0, 0, 0, 0,
                                      0, 0, 0, 0, 0, 0, 0, 0,
                                      0, 0, 0, 0, 0, 0, 0, 0,
                                      1, 0, 0, 0, 0, 0, 0, 0);
                                  World.Flags(0,
                                      0, 0, 0, 0, 0, 0, 0, 0,
                                      0, 0, 0, 0, 0, 0, 0, 0,
                                      0, 0, 0, 0, 0, 0, 0, 0,
                                      0, 0, 0, 0, 0, 0, 0, 0,
                                      1, 0, 0, 0, 0, 0, 0, 0);
                                  World.Map(1, 0, 40);
                                  Camera.Init(1, 0, 40);
                                  Sprite.Asset(goomba, "goomba.sprite.json");
                                  Actors.Pool(enemies, 1);
                                  Enemies.Def(Goomba, sprite: goomba, behavior: Walker, hitboxWidth: 8, hitboxHeight: 8);
                                  u8 cameraY = 0;
                                  for (u8 i = 0; i < 160; i += 1) {
                                      cameraY += 1;
                                      Camera.SetPosition(0, cameraY);
                                      Video.WaitVBlank();
                                      Camera.Apply();
                                  }
                                  Actors.SpawnLayer(enemies, "level.tmj", "actors");
                                  enemies.TouchTiles(0, 1);
                                  if (enemies[0].state == 1) {
                                      enemies[0].x = 32;
                                  } else {
                                      enemies[0].x = 16;
                                  }
                                  while (true) {
                                      Video.WaitVBlank();
                                      enemies.Draw();
                                      Camera.Apply();
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source, baseDirectory);
        var cpu = new GameBoyTestCpu(rom) { CycleAccurateLy = true };
        cpu.RunFrames(180);

        Assert.Equal(117, cpu.Oam(0xFE00));
        Assert.Equal(40, cpu.Oam(0xFE01));
    }

    [Fact]
    public void Rejects_actor_pool_without_literal_capacity()
    {
        const string source = """
                              void Main() {
                                  u8 capacity = 2;
                                  Actors.Pool(enemies, capacity);
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource(source));

        Assert.Equal("Actors.Pool for 'enemies' requires a literal capacity from 1 to 255.", exception.Message);
    }

    [Fact]
    public void Rejects_actor_pool_above_game_boy_struct_array_storage_capacity()
    {
        const string source = """
                              void Main() {
                                  Actors.Pool(enemies, 41);
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource(source));

        Assert.Equal("Game Boy target struct array 'enemies' uses 533 byte slot(s), but runtime indexed struct arrays are limited to 255 byte slots.", exception.Message);
    }

    [Fact]
    public void Rejects_unknown_actor_behavior_with_named_diagnostic_on_game_boy()
    {
        const string source = """
                              void Main() {
                                  Actors.Pool(enemies, 1);
                                  Enemies.Def(Goomba, behavior: Ghost);
                                  enemies.Update();
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource(source));

        Assert.Equal("Unknown actor behavior 'Ghost'.", exception.Message);
    }

    [Fact]
    public void Rejects_actor_draw_loop_that_exceeds_game_boy_hardware_sprite_budget()
    {
        var baseDirectory = WriteSpriteJsonAsset(
            "wide-goomba.sprite.json",
            SpriteJson(Rows(16, 16, "1111111111111111")));

        const string source = """
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(goomba, "wide-goomba.sprite.json");
                                  Actors.Pool(enemies, 21);
                                  Enemies.Def(Goomba, sprite: goomba, behavior: Walker, speed: 1);
                                  Video.WaitVBlank();
                                  enemies.Draw();
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource(source, baseDirectory));

        Assert.Equal(
            "Target 'gb' supports 40 hardware sprites per frame, but Actors.Pool for 'enemies' can draw up to 42 because capacity 21 times Enemies.Def 'Goomba' sprite 'goomba' uses 2 hardware sprites.",
            exception.Message);
    }

    [Fact]
    public void Compiles_actor_draw_loop_when_multi_sprite_png_pool_fits_game_boy_budget()
    {
        var baseDirectory = WriteSpritePng(
            "goomba.png",
            8,
            32,
            Rows(8, 32, Enumerable.Repeat("11111111", 32).ToArray()));

        const string source = """
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(goomba, "goomba.png", 8, 32);
                                  Actors.Pool(enemies, 10);
                                  Enemies.Def(Goomba, sprite: goomba, behavior: Walker, speed: 1);
                                  Video.WaitVBlank();
                                  enemies.Draw();
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source, baseDirectory);

        Assert.NotEmpty(rom);
    }

    [Fact]
    public void Rejects_actor_draw_loop_that_can_exceed_game_boy_scanline_budget()
    {
        var baseDirectory = WriteSpriteJsonAsset(
            "goomba.sprite.json",
            SpriteJson(Rows(8, 16, "11111111")));

        const string source = """
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(goomba, "goomba.sprite.json");
                                  Actors.Pool(enemies, 11);
                                  Enemies.Def(Goomba, sprite: goomba, behavior: Walker, speed: 1);
                                  Video.WaitVBlank();
                                  enemies.Draw();
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource(source, baseDirectory));

        Assert.Equal(
            "Target 'gb' supports 10 hardware sprites per scanline, but Actors.Pool for 'enemies' can draw up to 11 on one scanline because capacity 11 times Enemies.Def 'Goomba' sprite 'goomba' uses 1 hardware sprite on its busiest scanline.",
            exception.Message);
    }

    [Fact]
    public void Compiles_actor_update_and_draw_like_grouped_pool_loops()
    {
        var baseDirectory = WriteSpriteJsonAsset(
            "goomba.sprite.json",
            SpriteJson(Rows(8, 16, "11111111")));

        const string manualSource = """
                                    struct Actor {
                                        u8 kind;
                                        u8 active;
                                        u8 x;
                                        u8 xHi;
                                        u8 y;
                                        u8 yHi;
                                        i8 vx;
                                        i8 vy;
                                        u8 state;
                                        u8 timer;
                                        u8 facing;
                                        u8 animTick;
                                        u8 health;
                                    }

                                    const Walker = 1;
                                    const Goomba = 1;
                                    const GoombaSpeed = 1;

                                    void Main() {
                                        Video.Init();
                                        Sprite.Asset(goomba, "goomba.sprite.json");
                                        Actor enemies[1];
                                        enemies[0].active = 1;
                                        enemies[0].kind = Goomba;
                                        enemies[0].x = 24;
                                        enemies[0].y = 48;
                                        Video.WaitVBlank();

                                        for (u8 __enemies_update_i = 0; __enemies_update_i < countof(enemies); __enemies_update_i += 1) {
                                            if (enemies[__enemies_update_i].active != 0) {
                                                if (enemies[__enemies_update_i].kind == Goomba) {
                                                    enemies[__enemies_update_i].x += GoombaSpeed;
                                                    if (enemies[__enemies_update_i].x < GoombaSpeed) {
                                                        enemies[__enemies_update_i].xHi += 1;
                                                    }
                                                }
                                            }
                                        }

                                        u8 __enemies_draw_camera_x_lo = __rs_actor_camera_x_lo();
                                        u8 __enemies_draw_camera_x_hi = __rs_actor_camera_x_hi();
                                        u8 __enemies_draw_camera_y_lo = __rs_actor_camera_y_lo();
                                        u8 __enemies_draw_camera_y_hi = __rs_actor_camera_y_hi();
                                        for (u8 __enemies_draw_i = 0; __enemies_draw_i < countof(enemies); __enemies_draw_i += 1) {
                                            u8 __enemies_draw_screen_x = enemies[__enemies_draw_i].x - __enemies_draw_camera_x_lo;
                                            u8 __enemies_draw_screen_y = enemies[__enemies_draw_i].y - __enemies_draw_camera_y_lo;
                                            u8 __enemies_draw_visible_x = 0;
                                            u8 __enemies_draw_visible_y = 0;
                                            if ((((enemies[__enemies_draw_i].xHi == __enemies_draw_camera_x_hi) && (enemies[__enemies_draw_i].x >= __enemies_draw_camera_x_lo)) || ((enemies[__enemies_draw_i].xHi == __enemies_draw_camera_x_hi + 1) && (enemies[__enemies_draw_i].x < __enemies_draw_camera_x_lo))) && (__enemies_draw_screen_x < 160)) {
                                                __enemies_draw_visible_x = 1;
                                            }
                                            if ((((enemies[__enemies_draw_i].yHi == __enemies_draw_camera_y_hi) && (enemies[__enemies_draw_i].y >= __enemies_draw_camera_y_lo)) || ((enemies[__enemies_draw_i].yHi == __enemies_draw_camera_y_hi + 1) && (enemies[__enemies_draw_i].y < __enemies_draw_camera_y_lo))) && (__enemies_draw_screen_y < 144)) {
                                                __enemies_draw_visible_y = 1;
                                            }
                                            if (enemies[__enemies_draw_i].kind == Goomba) {
                                                u8 __enemies_draw_x_Goomba = 0;
                                                u8 __enemies_draw_y_Goomba = 144;
                                                if (enemies[__enemies_draw_i].active != 0) {
                                                    if ((__enemies_draw_visible_x != 0) && (__enemies_draw_visible_y != 0)) {
                                                        __enemies_draw_x_Goomba = __enemies_draw_screen_x;
                                                        __enemies_draw_y_Goomba = __enemies_draw_screen_y;
                                                    }
                                                }
                                                Sprite.Draw(goomba, __enemies_draw_x_Goomba, __enemies_draw_y_Goomba, 0, false, 0);
                                            }
                                        }
                                    }
                                    """;

        const string actorSource = """
                                   void Main() {
                                       Video.Init();
                                       Sprite.Asset(goomba, "goomba.sprite.json");
                                       Actors.Pool(enemies, 1);
                                       Enemies.Def(Goomba, sprite: goomba, behavior: Walker, speed: 1, hp: 1);
                                       enemies[0].active = 1;
                                       enemies[0].kind = Goomba;
                                       enemies[0].x = 24;
                                       enemies[0].y = 48;
                                       Video.WaitVBlank();
                                       enemies.Update();
                                       enemies.Draw();
                                   }
                                   """;

        Assert.Equal(GameBoyRomCompiler.CompileSource(manualSource, baseDirectory), GameBoyRomCompiler.CompileSource(actorSource, baseDirectory));
    }

    [Fact]
    public void Actor_draw_uses_world_x_minus_camera_x_and_culls_offscreen_on_game_boy()
    {
        var baseDirectory = WriteSpriteJsonAsset(
            "goomba.sprite.json",
            SpriteJson(Rows(8, 16, "11111111")));

        const string source = """
                              void Main() {
                                  Video.Init();
                                  World.Column(0, 0, 0);
                                  World.Map(1, 10, 2);
                                  Camera.Init(1, 10, 2);
                                  Sprite.Asset(goomba, "goomba.sprite.json");
                                  Actors.Pool(enemies, 1);
                                  Enemies.Def(Goomba, sprite: goomba, behavior: Walker, speed: 1, hp: 1);
                                  enemies[0].active = 1;
                                  enemies[0].kind = Goomba;
                                  enemies[0].x = 20;
                                  enemies[0].xHi = 0;
                                  enemies[0].y = 48;
                                  Camera.SetPosition(4, 0);
                                  Video.WaitVBlank();
                                  enemies.Draw();
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source, baseDirectory);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0xFA, 0xE0, 0xC0]), "actor draw should read the camera X low byte.");
        Assert.True(ContainsSequence(rom, [0xFA, 0xE1, 0xC0]), "actor draw should read the camera X high byte.");
        Assert.True(ContainsSequence(rom, [0xFE, 0xA0, 0xD2]), "actor draw should cull screen X values at or beyond the 160px Game Boy viewport.");
    }

    [Fact]
    public void Actor_tile_collision_uses_per_actor_camera_relative_x_on_game_boy()
    {
        const string manualSource = """
                                    struct Actor {
                                        u8 kind;
                                        u8 active;
                                        u8 x;
                                        u8 xHi;
                                        u8 y;
                                        u8 yHi;
                                        i8 vx;
                                        i8 vy;
                                        u8 state;
                                        u8 timer;
                                        u8 facing;
                                        u8 animTick;
                                        u8 health;
                                    }

                                    const Walker = 1;
                                    const Goomba = 1;

                                    void Main() {
                                        World.Column(0, 0, 0);
                                        World.Flags(0, 0, 1);
                                        World.Column(1, 0, 0);
                                        World.Flags(1, 0, 2);
                                        World.Map(2, 10, 2);
                                        Camera.Init(2, 10, 2);
                                        Actor enemies[2];
                                        enemies[0].active = 1;
                                        enemies[0].kind = Goomba;
                                        enemies[0].x = 24;
                                        enemies[0].xHi = 0;
                                        enemies[0].y = 0;
                                        enemies[1].active = 1;
                                        enemies[1].kind = Goomba;
                                        enemies[1].x = 104;
                                        enemies[1].xHi = 0;
                                        enemies[1].y = 0;
                                        Camera.SetPosition(0, 0);

                                        u8 __enemies_touch_camera_x_lo = __rs_actor_camera_x_lo();
                                        u8 __enemies_touch_camera_x_hi = __rs_actor_camera_x_hi();
                                        u8 __enemies_touch_camera_y_lo = __rs_actor_camera_y_lo();
                                        u8 __enemies_touch_camera_y_hi = __rs_actor_camera_y_hi();
                                        for (u8 __enemies_touch_i = 0; __enemies_touch_i < countof(enemies); __enemies_touch_i += 1) {
                                            if (enemies[__enemies_touch_i].active != 0) {
                                                u8 __enemies_touch_screen_x = enemies[__enemies_touch_i].x - __enemies_touch_camera_x_lo;
                                                u8 __enemies_touch_screen_y = enemies[__enemies_touch_i].y - __enemies_touch_camera_y_lo;
                                                u8 __enemies_touch_visible_x = 0;
                                                u8 __enemies_touch_visible_y = 0;
                                                if ((((enemies[__enemies_touch_i].xHi == __enemies_touch_camera_x_hi) && (enemies[__enemies_touch_i].x >= __enemies_touch_camera_x_lo)) || ((enemies[__enemies_touch_i].xHi == __enemies_touch_camera_x_hi + 1) && (enemies[__enemies_touch_i].x < __enemies_touch_camera_x_lo))) && (__enemies_touch_screen_x < 160)) {
                                                    __enemies_touch_visible_x = 1;
                                                }
                                                if ((((enemies[__enemies_touch_i].yHi == __enemies_touch_camera_y_hi) && (enemies[__enemies_touch_i].y >= __enemies_touch_camera_y_lo)) || ((enemies[__enemies_touch_i].yHi == __enemies_touch_camera_y_hi + 1) && (enemies[__enemies_touch_i].y < __enemies_touch_camera_y_lo))) && (__enemies_touch_screen_y < 144)) {
                                                    __enemies_touch_visible_y = 1;
                                                }
                                                if (enemies[__enemies_touch_i].kind == Goomba) {
                                                    if ((__enemies_touch_visible_x != 0) && (__enemies_touch_visible_y != 0)) {
                                                        if (Camera.ScreenAabbTiles(__enemies_touch_screen_x, __enemies_touch_screen_y, 8, 8, 1) != 0) {
                                                            enemies[__enemies_touch_i].state = 1;
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    """;

        const string actorSource = """
                                   void Main() {
                                       World.Column(0, 0, 0);
                                       World.Flags(0, 0, 1);
                                       World.Column(1, 0, 0);
                                       World.Flags(1, 0, 2);
                                       World.Map(2, 10, 2);
                                       Camera.Init(2, 10, 2);
                                       Actors.Pool(enemies, 2);
                                       Enemies.Def(Goomba, behavior: Walker, hitboxWidth: 8, hitboxHeight: 8);
                                       enemies[0].active = 1;
                                       enemies[0].kind = Goomba;
                                       enemies[0].x = 24;
                                       enemies[0].xHi = 0;
                                       enemies[0].y = 0;
                                       enemies[1].active = 1;
                                       enemies[1].kind = Goomba;
                                       enemies[1].x = 104;
                                       enemies[1].xHi = 0;
                                       enemies[1].y = 0;
                                       Camera.SetPosition(0, 0);
                                       enemies.TouchTiles(0, 1);
                                   }
                                   """;

        Assert.Equal(GameBoyRomCompiler.CompileSource(manualSource), GameBoyRomCompiler.CompileSource(actorSource));
    }

    [Fact]
    public void Compiles_actor_update_animation_tick_like_explicit_field_increment()
    {
        const string manualSource = """
                                    struct Actor {
                                        u8 kind;
                                        u8 active;
                                        u8 x;
                                        u8 xHi;
                                        u8 y;
                                        u8 yHi;
                                        i8 vx;
                                        i8 vy;
                                        u8 state;
                                        u8 timer;
                                        u8 facing;
                                        u8 animTick;
                                        u8 health;
                                    }

                                    const Walker = 1;
                                    const Goomba = 1;
                                    const GoombaSpeed = 1;

                                    void Main() {
                                        Video.Init();
                                        Actor enemies[1];
                                        enemies[0].active = 1;
                                        enemies[0].kind = Goomba;

                                        for (u8 __enemies_update_i = 0; __enemies_update_i < countof(enemies); __enemies_update_i += 1) {
                                            if (enemies[__enemies_update_i].active != 0) {
                                                if (enemies[__enemies_update_i].kind == Goomba) {
                                                    enemies[__enemies_update_i].x += GoombaSpeed;
                                                    if (enemies[__enemies_update_i].x < GoombaSpeed) {
                                                        enemies[__enemies_update_i].xHi += 1;
                                                    }
                                                    enemies[__enemies_update_i].animTick += 1;
                                                }
                                            }
                                        }
                                    }
                                    """;

        const string actorSource = """
                                   void Main() {
                                       Video.Init();
                                       Actors.Pool(enemies, 1);
                                       Enemies.Def(Goomba, behavior: Walker, speed: 1, animation: walk);
                                       enemies[0].active = 1;
                                       enemies[0].kind = Goomba;
                                       enemies.Update();
                                   }
                                   """;

        Assert.Equal(GameBoyRomCompiler.CompileSource(manualSource), GameBoyRomCompiler.CompileSource(actorSource));
    }

    [Fact]
    public void Compiles_actor_spawn_layer_into_runtime_activation_table()
    {
        var baseDirectory = WriteActorSpawnMap(
            """
            [
              { "id": 1, "type": "Goomba", "x": 24, "y": 40, "properties": [ { "name": "facing", "type": "int", "value": 1 } ] },
              { "id": 2, "type": "Bat", "x": 72, "y": 32 }
            ]
            """);

        const string actorSource = """
                                   void Main() {
                                       World.Column(0, 0, 0);
                                       World.Map(40, 10, 2);
                                       Camera.Init(40, 10, 2);
                                       Actors.Pool(enemies, 2);
                                       Enemies.Def(Goomba, behavior: Walker);
                                       Enemies.Def(Bat, behavior: Flyer);
                                       Camera.SetPosition(0, 0);
                                       Actors.SpawnLayer(enemies, "level.tmj", "actors");
                                   }
                                   """;

        var rom = GameBoyRomCompiler.CompileSource(actorSource, baseDirectory);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0xFA, 0xE0, 0xC0]), "runtime spawn activation should read camera X low instead of materializing all slots at compile time.");
    }

    [Fact]
    public void Compiles_actor_spawn_layer_as_runtime_camera_window_activation_on_game_boy()
    {
        var baseDirectory = WriteActorSpawnMap(
            """
            [
              { "id": 1, "type": "Goomba", "x": 24, "y": 40 },
              { "id": 2, "type": "Bat", "x": 280, "y": 32, "properties": [ { "name": "facing", "type": "int", "value": 1 } ] }
            ]
            """);

        var manualSource = $$"""
                             struct Actor {
                                 u8 kind;
                                 u8 active;
                                 u8 x;
                                 u8 xHi;
                                 u8 y;
                                 u8 yHi;
                                 i8 vx;
                                 i8 vy;
                                 u8 state;
                                 u8 timer;
                                 u8 facing;
                                 u8 animTick;
                                 u8 health;
                             }

                             const Goomba = 1;
                             const Bat = 2;

                             inline u8 __enemies_spawn_0_kind(u8 index) => index == 0 ? Goomba : Bat;
                             inline u8 __enemies_spawn_0_x(u8 index) => 24;
                             inline u8 __enemies_spawn_0_xHi(u8 index) => index == 0 ? 0 : 1;
                             inline u8 __enemies_spawn_0_y(u8 index) => index == 0 ? 40 : 32;
                             inline u8 __enemies_spawn_0_yHi(u8 index) => 0;
                             inline u8 __enemies_spawn_0_active(u8 index) => 1;
                             inline u8 __enemies_spawn_0_vx(u8 index) => 0;
                             inline u8 __enemies_spawn_0_vy(u8 index) => 0;
                             inline u8 __enemies_spawn_0_state(u8 index) => 0;
                             inline u8 __enemies_spawn_0_timer(u8 index) => 0;
                             inline u8 __enemies_spawn_0_facing(u8 index) => index == 0 ? 0 : 1;
                             inline u8 __enemies_spawn_0_animTick(u8 index) => 0;
                             inline u8 __enemies_spawn_0_health(u8 index) => 0;

                             void Main() {
                                 World.Column(0, 0, 0);
                                 World.Map(40, 10, 2);
                                 Camera.Init(40, 10, 2);
                                 Actor enemies[1];
                                 u8 __enemies_spawn_0_used[2];

                                 Camera.SetPosition(0, 0);
                             {{RuntimeSpawnActivationBlock("__enemies_spawn_0_call0", 160)}}

                                 Camera.SetPosition(128, 0);
                             {{RuntimeSpawnActivationBlock("__enemies_spawn_0_call1", 160)}}
                             }
                             """;

        const string actorSource = """
                                   void Main() {
                                       World.Column(0, 0, 0);
                                       World.Map(40, 10, 2);
                                       Camera.Init(40, 10, 2);
                                       Actors.Pool(enemies, 1);
                                       Enemies.Def(Goomba, behavior: Walker);
                                       Enemies.Def(Bat, behavior: Flyer);
                                       Camera.SetPosition(0, 0);
                                       Actors.SpawnLayer(enemies, "level.tmj", "actors");
                                       Camera.SetPosition(128, 0);
                                       Actors.SpawnLayer(enemies, "level.tmj", "actors");
                                   }
                                   """;

        var expected = GameBoyRomCompiler.CompileSource(manualSource);
        var actual = GameBoyRomCompiler.CompileSource(actorSource, baseDirectory);
        var firstDifference = Enumerable.Range(0x150, expected.Length - 0x150).FirstOrDefault(index => expected[index] != actual[index], -1);

        Assert.True(
            expected.SequenceEqual(actual),
            firstDifference < 0
                ? "Runtime actor activation ROMs differ only in header checksum bytes."
                : $"Runtime actor activation ROMs differ at 0x{firstDifference:X4}: expected {BytesAround(expected, firstDifference)}, actual {BytesAround(actual, firstDifference)}.");
    }

    [Fact]
    public void Rejects_actor_spawn_layer_that_exceeds_pool_capacity()
    {
        var baseDirectory = WriteActorSpawnMap(
            """
            [
              { "id": 1, "type": "Goomba", "x": 24, "y": 40 },
              { "id": 2, "type": "Bat", "x": 72, "y": 32 }
            ]
            """);

        const string source = """
                              void Main() {
                                  Actors.Pool(enemies, 1);
                                  Enemies.Def(Goomba, behavior: Walker);
                                  Enemies.Def(Bat, behavior: Flyer);
                                  Actors.SpawnLayer(enemies, "level.tmj", "actors");
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource(source, baseDirectory));

        Assert.Equal("Actors.SpawnLayer for pool 'enemies' can activate 2 spawn(s) in one camera window from layer 'actors', exceeding the declared capacity 1.", exception.Message);
    }

    [Fact]
    public void Compiles_actor_spawn_window_as_runtime_camera_relative_window()
    {
        var baseDirectory = WriteActorSpawnMap(
            """
            [
              { "id": 1, "type": "Goomba", "x": 24, "y": 40 },
              { "id": 2, "type": "Bat", "x": 72, "y": 32 }
            ]
            """);

        const string actorSource = """
                                   void Main() {
                                       World.Column(0, 0, 0);
                                       World.Map(40, 10, 2);
                                       Camera.Init(40, 10, 2);
                                       Actors.Pool(enemies, 1);
                                       Enemies.Def(Goomba, behavior: Walker);
                                       Enemies.Def(Bat, behavior: Flyer);
                                       Camera.SetPosition(0, 0);
                                       Actors.SpawnWindow(enemies, "level.tmj", "actors", 0, 48);
                                   }
                                   """;

        var rom = GameBoyRomCompiler.CompileSource(actorSource, baseDirectory);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0xFE, 0x30]), "SpawnWindow width 48 should lower to a runtime screen-X window check.");
    }

    [Fact]
    public void Compiles_actor_touch_player_helper_for_player_contact()
    {
        const string source = """
                              void Main() {
                                  Actors.Pool(enemies, 1);
                                  Enemies.Def(Goomba, behavior: Walker, contactDamage: 2, hitboxWidth: 8, hitboxHeight: 8);
                                  enemies[0].active = 1;
                                  enemies[0].kind = Goomba;
                                  enemies[0].x = 72;
                                  enemies[0].y = 40;
                                  enemies.TouchPlayer(72, 40, 16, 16);
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
    }

    [Fact]
    public void Actor_touch_player_detects_overlap_when_actor_right_edge_wraps()
    {
        const string source = """
                              void Main() {
                                  Actors.Pool(enemies, 1);
                                  Enemies.Def(Goomba, behavior: Walker, contactDamage: 7, hitboxWidth: 128, hitboxHeight: 8);
                                  enemies[0].active = 1;
                                  enemies[0].kind = Goomba;
                                  enemies[0].x = 150;
                                  enemies[0].y = 40;
                                  enemies.TouchPlayer(140, 40, 16, 16);
                                  while (true) {
                                      Video.WaitVBlank();
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);
        var cpu = new GameBoyTestCpu(rom);
        cpu.RunFrames(2);

        Assert.Equal(7, cpu.Wram(0xC008));
    }

    [Fact]
    public void Rejects_actor_spawn_window_that_exceeds_pool_capacity()
    {
        var baseDirectory = WriteActorSpawnMap(
            """
            [
              { "id": 1, "type": "Goomba", "x": 24, "y": 40 },
              { "id": 2, "type": "Bat", "x": 72, "y": 32 }
            ]
            """);

        const string source = """
                              void Main() {
                                  Actors.Pool(enemies, 1);
                                  Enemies.Def(Goomba, behavior: Walker);
                                  Enemies.Def(Bat, behavior: Flyer);
                                  Actors.SpawnWindow(enemies, "level.tmj", "actors", 0, 128);
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource(source, baseDirectory));

        Assert.Equal("Actors.SpawnWindow for pool 'enemies' can activate 2 spawn(s) in one camera window from layer 'actors', exceeding the declared capacity 1.", exception.Message);
    }

    [Fact]
    public void Compiles_multiple_actor_behaviors_like_direct_kind_branches()
    {
        const string manualSource = """
                                    struct Actor {
                                        u8 kind;
                                        u8 active;
                                        u8 x;
                                        u8 xHi;
                                        u8 y;
                                        u8 yHi;
                                        i8 vx;
                                        i8 vy;
                                        u8 state;
                                        u8 timer;
                                        u8 facing;
                                        u8 animTick;
                                        u8 health;
                                    }

                                    const Walker = 1;
                                    const Flyer = 2;
                                    const Patrol = 3;
                                    const Shooter = 4;
                                    const Chaser = 5;
                                    const Hazard = 6;

                                    const Goomba = 1;
                                    const GoombaSpeed = 1;
                                    const Bat = 2;
                                    const BatSpeed = 2;
                                    const Koopa = 3;
                                    const KoopaSpeed = 1;
                                    const KoopaCooldown = 3;
                                    const Turret = 4;
                                    const TurretCooldown = 5;
                                    const Seeker = 5;
                                    const SeekerSpeed = 1;
                                    const Spike = 6;
                                    const SpikeContactDamage = 2;

                                    void Main() {
                                        Video.Init();
                                        Actor enemies[6];
                                        enemies[0].active = 1;
                                        enemies[0].kind = Goomba;
                                        enemies[1].active = 1;
                                        enemies[1].kind = Bat;
                                        enemies[2].active = 1;
                                        enemies[2].kind = Koopa;
                                        enemies[2].timer = 2;
                                        enemies[3].active = 1;
                                        enemies[3].kind = Turret;
                                        enemies[3].timer = 4;
                                        enemies[4].active = 1;
                                        enemies[4].kind = Seeker;
                                        enemies[4].facing = 1;
                                        enemies[5].active = 1;
                                        enemies[5].kind = Spike;

                                        for (u8 __enemies_update_i = 0; __enemies_update_i < countof(enemies); __enemies_update_i += 1) {
                                            if (enemies[__enemies_update_i].active != 0) {
                                                if (enemies[__enemies_update_i].kind == Goomba) {
                                                    enemies[__enemies_update_i].x += GoombaSpeed;
                                                    if (enemies[__enemies_update_i].x < GoombaSpeed) {
                                                        enemies[__enemies_update_i].xHi += 1;
                                                    }
                                                } else {
                                                    if (enemies[__enemies_update_i].kind == Bat) {
                                                        enemies[__enemies_update_i].y += BatSpeed;
                                                        if (enemies[__enemies_update_i].y < BatSpeed) {
                                                            enemies[__enemies_update_i].yHi += 1;
                                                        }
                                                    } else {
                                                        if (enemies[__enemies_update_i].kind == Koopa) {
                                                            if (enemies[__enemies_update_i].facing == 0) {
                                                                enemies[__enemies_update_i].x += KoopaSpeed;
                                                                if (enemies[__enemies_update_i].x < KoopaSpeed) {
                                                                    enemies[__enemies_update_i].xHi += 1;
                                                                }
                                                            } else {
                                                                if (enemies[__enemies_update_i].x < KoopaSpeed) {
                                                                    enemies[__enemies_update_i].xHi -= 1;
                                                                }
                                                                enemies[__enemies_update_i].x -= KoopaSpeed;
                                                            }
                                                            enemies[__enemies_update_i].timer += 1;
                                                            if (enemies[__enemies_update_i].timer == KoopaCooldown) {
                                                                enemies[__enemies_update_i].timer = 0;
                                                                enemies[__enemies_update_i].facing ^= 1;
                                                            }
                                                        } else {
                                                            if (enemies[__enemies_update_i].kind == Turret) {
                                                                enemies[__enemies_update_i].timer += 1;
                                                                if (enemies[__enemies_update_i].timer == TurretCooldown) {
                                                                    enemies[__enemies_update_i].timer = 0;
                                                                    enemies[__enemies_update_i].state = 1;
                                                                } else {
                                                                    enemies[__enemies_update_i].state = 0;
                                                                }
                                                            } else {
                                                                if (enemies[__enemies_update_i].kind == Seeker) {
                                                                    if (enemies[__enemies_update_i].facing == 0) {
                                                                        enemies[__enemies_update_i].x += SeekerSpeed;
                                                                        if (enemies[__enemies_update_i].x < SeekerSpeed) {
                                                                            enemies[__enemies_update_i].xHi += 1;
                                                                        }
                                                                    } else {
                                                                        if (enemies[__enemies_update_i].x < SeekerSpeed) {
                                                                            enemies[__enemies_update_i].xHi -= 1;
                                                                        }
                                                                        enemies[__enemies_update_i].x -= SeekerSpeed;
                                                                    }
                                                                } else {
                                                                    if (enemies[__enemies_update_i].kind == Spike) {
                                                                        enemies[__enemies_update_i].state = SpikeContactDamage;
                                                                    }
                                                                }
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    """;

        const string actorSource = """
                                   void Main() {
                                       Video.Init();
                                       Actors.Pool(enemies, 6);
                                       Enemies.Def(Goomba, behavior: Walker, speed: 1);
                                       Enemies.Def(Bat, behavior: Flyer, speed: 2);
                                       Enemies.Def(Koopa, behavior: Patrol, speed: 1, cooldown: 3);
                                       Enemies.Def(Turret, behavior: Shooter, cooldown: 5);
                                       Enemies.Def(Seeker, behavior: Chaser, speed: 1);
                                       Enemies.Def(Spike, behavior: Hazard, contactDamage: 2);
                                       enemies[0].active = 1;
                                       enemies[0].kind = Goomba;
                                       enemies[1].active = 1;
                                       enemies[1].kind = Bat;
                                       enemies[2].active = 1;
                                       enemies[2].kind = Koopa;
                                       enemies[2].timer = 2;
                                       enemies[3].active = 1;
                                       enemies[3].kind = Turret;
                                       enemies[3].timer = 4;
                                       enemies[4].active = 1;
                                       enemies[4].kind = Seeker;
                                       enemies[4].facing = 1;
                                       enemies[5].active = 1;
                                       enemies[5].kind = Spike;
                                       enemies.Update();
                                   }
                                   """;

        Assert.Equal(GameBoyRomCompiler.CompileSource(manualSource), GameBoyRomCompiler.CompileSource(actorSource));
    }

    private static string WriteActorSpawnMap(string objectsJson)
    {
        var directory = Path.Combine(Path.GetTempPath(), "RetroSharp.GameBoy.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, "level.tmj"),
            $$"""
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
                  { "type": "objectgroup", "name": "actors", "objects": {{objectsJson}} }
                ]
              }
              """);
        return directory;
    }

    private static string RuntimeSpawnActivationBlock(string prefix, int windowWidth)
    {
        return $$"""
                     u8 {{prefix}}_recycle_camera_x_lo = __rs_actor_camera_x_lo();
                     u8 {{prefix}}_recycle_camera_x_hi = __rs_actor_camera_x_hi();
                     for (u8 {{prefix}}_recycle_i = 0; {{prefix}}_recycle_i < countof(enemies); {{prefix}}_recycle_i += 1) {
                         if (enemies[{{prefix}}_recycle_i].active != 0) {
                             u8 {{prefix}}_recycle_screen_x = enemies[{{prefix}}_recycle_i].x - {{prefix}}_recycle_camera_x_lo;
                             if (!((((enemies[{{prefix}}_recycle_i].xHi == {{prefix}}_recycle_camera_x_hi) && (enemies[{{prefix}}_recycle_i].x >= {{prefix}}_recycle_camera_x_lo)) || ((enemies[{{prefix}}_recycle_i].xHi == {{prefix}}_recycle_camera_x_hi + 1) && (enemies[{{prefix}}_recycle_i].x < {{prefix}}_recycle_camera_x_lo))) && ({{prefix}}_recycle_screen_x < {{windowWidth}}))) {
                                 enemies[{{prefix}}_recycle_i].active = 0;
                             }
                         }
                     }
                     u8 {{prefix}}_camera_x_lo = __rs_actor_camera_x_lo();
                     u8 {{prefix}}_camera_x_hi = __rs_actor_camera_x_hi();
                     for (u8 {{prefix}}_i = 0; {{prefix}}_i < 2; {{prefix}}_i += 1) {
                         if (__enemies_spawn_0_used[{{prefix}}_i] == 0) {
                             u8 {{prefix}}_kind_value = __enemies_spawn_0_kind({{prefix}}_i);
                             u8 {{prefix}}_x_value = __enemies_spawn_0_x({{prefix}}_i);
                             u8 {{prefix}}_xHi_value = __enemies_spawn_0_xHi({{prefix}}_i);
                             u8 {{prefix}}_y_value = __enemies_spawn_0_y({{prefix}}_i);
                             u8 {{prefix}}_yHi_value = __enemies_spawn_0_yHi({{prefix}}_i);
                             u8 {{prefix}}_active_value = __enemies_spawn_0_active({{prefix}}_i);
                             u8 {{prefix}}_vx_value = __enemies_spawn_0_vx({{prefix}}_i);
                             u8 {{prefix}}_vy_value = __enemies_spawn_0_vy({{prefix}}_i);
                             u8 {{prefix}}_state_value = __enemies_spawn_0_state({{prefix}}_i);
                             u8 {{prefix}}_timer_value = __enemies_spawn_0_timer({{prefix}}_i);
                             u8 {{prefix}}_facing_value = __enemies_spawn_0_facing({{prefix}}_i);
                             u8 {{prefix}}_animTick_value = __enemies_spawn_0_animTick({{prefix}}_i);
                             u8 {{prefix}}_health_value = __enemies_spawn_0_health({{prefix}}_i);
                             u8 {{prefix}}_screen_x = {{prefix}}_x_value - {{prefix}}_camera_x_lo;
                             if (((({{prefix}}_xHi_value == {{prefix}}_camera_x_hi) && ({{prefix}}_x_value >= {{prefix}}_camera_x_lo)) || (({{prefix}}_xHi_value == {{prefix}}_camera_x_hi + 1) && ({{prefix}}_x_value < {{prefix}}_camera_x_lo))) && ({{prefix}}_screen_x < {{windowWidth}})) {
                                 u8 {{prefix}}_assigned = 0;
                                 for (u8 {{prefix}}_slot = 0; {{prefix}}_slot < countof(enemies); {{prefix}}_slot += 1) {
                                     if ({{prefix}}_assigned == 0) {
                                         if (enemies[{{prefix}}_slot].active == 0) {
                                             enemies[{{prefix}}_slot].kind = {{prefix}}_kind_value;
                                             enemies[{{prefix}}_slot].x = {{prefix}}_x_value;
                                             enemies[{{prefix}}_slot].xHi = {{prefix}}_xHi_value;
                                             enemies[{{prefix}}_slot].y = {{prefix}}_y_value;
                                             enemies[{{prefix}}_slot].yHi = {{prefix}}_yHi_value;
                                             enemies[{{prefix}}_slot].vx = {{prefix}}_vx_value;
                                             enemies[{{prefix}}_slot].vy = {{prefix}}_vy_value;
                                             enemies[{{prefix}}_slot].state = {{prefix}}_state_value;
                                             enemies[{{prefix}}_slot].timer = {{prefix}}_timer_value;
                                             enemies[{{prefix}}_slot].facing = {{prefix}}_facing_value;
                                             enemies[{{prefix}}_slot].animTick = {{prefix}}_animTick_value;
                                             enemies[{{prefix}}_slot].health = {{prefix}}_health_value;
                                             enemies[{{prefix}}_slot].active = {{prefix}}_active_value;
                                             __enemies_spawn_0_used[{{prefix}}_i] = 1;
                                             {{prefix}}_assigned = 1;
                                         }
                                     }
                                 }
                             }
                         }
                     }
                 """;
    }

}
