namespace RetroSharp.NES.Tests;

using System.Buffers.Binary;
using System.IO.Compression;
using RetroSharp.GameBoy;
using RetroSharp.NES;
using RetroSharp.Parser;
using RetroSharp.Sdk;
using Xunit;
using static RetroSharp.NES.Tests.NesTestAssets;

public partial class NesRomCompilerTests
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
                                        return;
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
                                       return;
                                   }
                                   """;

        Assert.Equal(NesRomCompiler.CompileSource(manualSource), NesRomCompiler.CompileSource(actorSource));
    }

    [Fact]
    public void Rejects_actor_pool_without_literal_capacity()
    {
        const string source = """
                              void Main() {
                                  u8 capacity = 2;
                                  Actors.Pool(enemies, capacity);
                                  return;
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => NesRomCompiler.CompileSource(source));

        Assert.Equal("Actors.Pool for 'enemies' requires a literal capacity from 1 to 255.", exception.Message);
    }

    [Fact]
    public void Rejects_actor_pool_above_nes_struct_array_storage_capacity()
    {
        const string source = """
                              void Main() {
                                  Actors.Pool(enemies, 65);
                                  return;
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => NesRomCompiler.CompileSource(source));

        Assert.Equal("NES target struct array 'enemies' uses 845 byte slot(s), but runtime indexed struct arrays are limited to 255 byte slots.", exception.Message);
    }

    [Fact]
    [Trait("RetroSharp.TestOwnership", "FocusedFrontend")]
    public void Actor_framework_projects_wide_y_spawns_for_draw_and_collision_on_nes()
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
                                  Camera.Init(40, 10, 40);
                                  Sprite.Asset(goomba, "goomba.nes.json");
                                  Actors.Pool(enemies, 2);
                                  Enemies.Def(Goomba, sprite: goomba, behavior: Walker, hitboxWidth: 8, hitboxHeight: 8);
                                  Camera.SetPosition(0, 160);
                                  Actors.SpawnLayer(enemies, "level.tmj", "actors");
                                  enemies.TouchTiles(0, 1);
                                  enemies.LandOnTiles(4, 12, 1);
                                  enemies.Draw();
                                  return;
                              }
                              """;

        var parse = new SomeParser().Parse(
            SdkLibrarySource.Merge(
                NesTarget.Intrinsics,
                source,
                libraryImportPaths: [SdkImportResolver.Portable2D]));
        Assert.True(parse.IsSuccess, parse.IsFailure ? parse.Error : string.Empty);
        var targetProgram = TargetProgramSelector.Select(parse.Value, NesTarget.Intrinsics);

        var loweredProgram = ActorFrameworkLowerer.Lower(targetProgram, NesTarget.Capabilities, supportsUpdate: true, supportsDraw: true, baseDirectory);
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
        Assert.Contains("u8 __enemies_draw_y_Goomba=240;", lowered);
        Assert.Contains("__enemies_draw_x_Goomba=__enemies_draw_screen_x;", lowered);
        Assert.Contains("__enemies_draw_y_Goomba=__enemies_draw_screen_y;", lowered);
        Assert.Contains("RetroSharp_Portable2D_portable2d_sprite_draw(goomba, __enemies_draw_x_Goomba, __enemies_draw_y_Goomba, 0, false, 0);", lowered);
    }

    [Fact]
    public void Rejects_unknown_actor_behavior_with_named_diagnostic_on_nes()
    {
        const string source = """
                              void Main() {
                                  Actors.Pool(enemies, 1);
                                  Enemies.Def(Goomba, behavior: Ghost);
                                  enemies.Update();
                                  return;
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => NesRomCompiler.CompileSource(source));

        Assert.Equal("Unknown actor behavior 'Ghost'.", exception.Message);
    }

    [Fact]
    public void Rejects_actor_draw_loop_that_exceeds_nes_hardware_sprite_budget()
    {
        var rows = Enumerable.Repeat(new string('1', 24), 8);
        var rowJson = string.Join(",", rows.Select(row => $"\"{row}\""));
        var baseDirectory = WriteSpriteAsset(
            "wide-goomba.nes.json",
            "{\"platforms\":{\"nes\":{\"frames\":[[" + rowJson + "]]}}}");

        const string source = """
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(goomba, "wide-goomba.nes.json");
                                  Actors.Pool(enemies, 23);
                                  Enemies.Def(Goomba, sprite: goomba, behavior: Walker, speed: 1);
                                  Video.WaitVBlank();
                                  enemies.Draw();
                                  return;
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => NesRomCompiler.CompileSource(source, baseDirectory));

        Assert.Equal(
            "Target 'nes' supports 64 hardware sprites per frame, but Actors.Pool for 'enemies' can draw up to 69 because capacity 23 times Enemies.Def 'Goomba' sprite 'goomba' uses 3 hardware sprites.",
            exception.Message);
    }

    [Fact]
    public void Nes_collectors_do_not_run_compile_only_actor_sprite_budget_validation()
    {
        var rows = Enumerable.Repeat(new string('1', 24), 8);
        var rowJson = string.Join(",", rows.Select(row => $"\"{row}\""));
        var baseDirectory = WriteSpriteAsset(
            "wide-goomba.nes.json",
            "{\"platforms\":{\"nes\":{\"frames\":[[" + rowJson + "]]}}}");

        const string source = """
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(goomba, "wide-goomba.nes.json");
                                  Actors.Pool(enemies, 23);
                                  Enemies.Def(Goomba, sprite: goomba, behavior: Walker, speed: 1);
                                  Video.WaitVBlank();
                                  enemies.Draw();
                                  return;
                              }
                              """;

        Assert.NotEmpty(NesRomCompiler.CollectSdkOperations(source, baseDirectory));
        Assert.Empty(NesRomCompiler.CollectSdkAudioOperations(source, baseDirectory));
    }

    [Fact]
    public void Compiles_actor_draw_loop_when_multi_sprite_png_pool_fits_nes_budget()
    {
        var baseDirectory = WriteSpritePng(
            "goomba.png",
            8,
            16,
            Rows(8, 16, Enumerable.Repeat("11111111", 16).ToArray()));

        const string source = """
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(goomba, "goomba.png", 8, 16);
                                  Actors.Pool(enemies, 8);
                                  Enemies.Def(Goomba, sprite: goomba, behavior: Walker, speed: 1);
                                  Video.WaitVBlank();
                                  enemies.Draw();
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source, baseDirectory);

        Assert.NotEmpty(rom);
    }

    [Fact]
    public void Rejects_actor_draw_loop_that_can_exceed_nes_scanline_budget()
    {
        var baseDirectory = WriteSpriteAsset(
            "goomba.nes.json",
            """
            {
              "platforms": {
                "nes": {
                  "frames": [
                    [
                      "11111111",
                      "11111111",
                      "11111111",
                      "11111111",
                      "11111111",
                      "11111111",
                      "11111111",
                      "11111111"
                    ]
                  ]
                }
              }
            }
            """);

        const string source = """
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(goomba, "goomba.nes.json");
                                  Actors.Pool(enemies, 9);
                                  Enemies.Def(Goomba, sprite: goomba, behavior: Walker, speed: 1);
                                  Video.WaitVBlank();
                                  enemies.Draw();
                                  return;
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => NesRomCompiler.CompileSource(source, baseDirectory));

        Assert.Equal(
            "Target 'nes' supports 8 hardware sprites per scanline, but Actors.Pool for 'enemies' can draw up to 9 on one scanline because capacity 9 times Enemies.Def 'Goomba' sprite 'goomba' uses 1 hardware sprite on its busiest scanline.",
            exception.Message);
    }

    [Fact]
    public void Compiles_actor_update_like_grouped_pool_loop_for_nes()
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
                                        enemies[0].x = 24;
                                        enemies[0].y = 48;

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

                                        return;
                                    }
                                    """;

        const string actorSource = """
                                   void Main() {
                                       Video.Init();
                                       Actors.Pool(enemies, 1);
                                       Enemies.Def(Goomba, behavior: Walker, speed: 1, hp: 1);
                                       enemies[0].active = 1;
                                       enemies[0].kind = Goomba;
                                       enemies[0].x = 24;
                                       enemies[0].y = 48;
                                       enemies.Update();
                                       return;
                                   }
                                   """;

        Assert.Equal(NesRomCompiler.CompileSource(manualSource), NesRomCompiler.CompileSource(actorSource));
    }

    [Fact]
    public void Compiles_actor_draw_like_grouped_pool_loop_for_nes()
    {
        var baseDirectory = WriteSpriteAsset(
            "goomba.nes.json",
            """
            {
              "platforms": {
                "nes": {
                  "frames": [
                    [
                      "11111111",
                      "11111111",
                      "11111111",
                      "11111111",
                      "11111111",
                      "11111111",
                      "11111111",
                      "11111111"
                    ]
                  ]
                }
              }
            }
            """);

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
                                        Video.Init();
                                        Sprite.Asset(goomba, "goomba.nes.json");
                                        Actor enemies[1];
                                        enemies[0].active = 1;
                                        enemies[0].kind = Goomba;
                                        enemies[0].x = 24;
                                        enemies[0].y = 48;
                                        Video.WaitVBlank();

                                        u8 __enemies_draw_camera_x_lo = __rs_actor_camera_x_lo();
                                        u8 __enemies_draw_camera_x_hi = __rs_actor_camera_x_hi();
                                        u8 __enemies_draw_camera_y_lo = __rs_actor_camera_y_lo();
                                        u8 __enemies_draw_camera_y_hi = __rs_actor_camera_y_hi();
                                        for (u8 __enemies_draw_i = 0; __enemies_draw_i < countof(enemies); __enemies_draw_i += 1) {
                                            u8 __enemies_draw_screen_x = enemies[__enemies_draw_i].x - __enemies_draw_camera_x_lo;
                                            u8 __enemies_draw_screen_y = enemies[__enemies_draw_i].y - __enemies_draw_camera_y_lo;
                                            u8 __enemies_draw_visible_x = 0;
                                            u8 __enemies_draw_visible_y = 0;
                                            if (((enemies[__enemies_draw_i].xHi == __enemies_draw_camera_x_hi) && (enemies[__enemies_draw_i].x >= __enemies_draw_camera_x_lo)) || ((enemies[__enemies_draw_i].xHi == __enemies_draw_camera_x_hi + 1) && (enemies[__enemies_draw_i].x < __enemies_draw_camera_x_lo))) {
                                                __enemies_draw_visible_x = 1;
                                            }
                                            if ((((enemies[__enemies_draw_i].yHi == __enemies_draw_camera_y_hi) && (enemies[__enemies_draw_i].y >= __enemies_draw_camera_y_lo)) || ((enemies[__enemies_draw_i].yHi == __enemies_draw_camera_y_hi + 1) && (enemies[__enemies_draw_i].y < __enemies_draw_camera_y_lo))) && (__enemies_draw_screen_y < 240)) {
                                                __enemies_draw_visible_y = 1;
                                            }
                                            if (enemies[__enemies_draw_i].kind == Goomba) {
                                                u8 __enemies_draw_x_Goomba = 0;
                                                u8 __enemies_draw_y_Goomba = 240;
                                                if (enemies[__enemies_draw_i].active != 0) {
                                                    if ((__enemies_draw_visible_x != 0) && (__enemies_draw_visible_y != 0)) {
                                                        __enemies_draw_x_Goomba = __enemies_draw_screen_x;
                                                        __enemies_draw_y_Goomba = __enemies_draw_screen_y;
                                                    }
                                                }
                                                Sprite.Draw(goomba, __enemies_draw_x_Goomba, __enemies_draw_y_Goomba, 0, false, 0);
                                            }
                                        }

                                        return;
                                    }
                                    """;

        const string actorSource = """
                                   void Main() {
                                       Video.Init();
                                       Sprite.Asset(goomba, "goomba.nes.json");
                                       Actors.Pool(enemies, 1);
                                       Enemies.Def(Goomba, sprite: goomba, behavior: Walker, speed: 1, hp: 1);
                                       enemies[0].active = 1;
                                       enemies[0].kind = Goomba;
                                       enemies[0].x = 24;
                                       enemies[0].y = 48;
                                       Video.WaitVBlank();
                                       enemies.Draw();
                                       return;
                                   }
                                   """;

        Assert.Equal(NesRomCompiler.CompileSource(manualSource, baseDirectory), NesRomCompiler.CompileSource(actorSource, baseDirectory));
    }

    [Fact]
    public void Actor_draw_uses_world_x_minus_camera_x_and_culls_offscreen_on_nes()
    {
        var baseDirectory = WriteSpriteAsset(
            "goomba.nes.json",
            """
            {
              "platforms": {
                "nes": {
                  "frames": [
                    [
                      "11111111",
                      "11111111",
                      "11111111",
                      "11111111",
                      "11111111",
                      "11111111",
                      "11111111",
                      "11111111"
                    ]
                  ]
                }
              }
            }
            """);

        const string source = """
                              void Main() {
                                  Video.Init();
                                  World.Column(0, 0, 0);
                                  World.Map(1, 10, 2);
                                  Camera.Init(1, 10, 2);
                                  Sprite.Asset(goomba, "goomba.nes.json");
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
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source, baseDirectory);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.Equal(40976, rom.Length);
        Assert.True(ContainsSequence(prg, [0xA5, 0xE0]), "actor draw should read the camera X low byte.");
        Assert.True(ContainsSequence(prg, [0xA9, 0x00]), "actor draw should use a compile-time known-zero camera X high byte when the configured camera extent fits one byte.");
        Assert.True(ContainsSequence(prg, [0xC5, 0xE9, 0xD0]), "actor draw should compare actor and camera world pages and branch around sprite drawing when outside the camera window.");
    }

    [Fact]
    public void Actor_tile_collision_uses_per_actor_camera_relative_x_on_nes()
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
                                                if (((enemies[__enemies_touch_i].xHi == __enemies_touch_camera_x_hi) && (enemies[__enemies_touch_i].x >= __enemies_touch_camera_x_lo)) || ((enemies[__enemies_touch_i].xHi == __enemies_touch_camera_x_hi + 1) && (enemies[__enemies_touch_i].x < __enemies_touch_camera_x_lo))) {
                                                    __enemies_touch_visible_x = 1;
                                                }
                                                if ((((enemies[__enemies_touch_i].yHi == __enemies_touch_camera_y_hi) && (enemies[__enemies_touch_i].y >= __enemies_touch_camera_y_lo)) || ((enemies[__enemies_touch_i].yHi == __enemies_touch_camera_y_hi + 1) && (enemies[__enemies_touch_i].y < __enemies_touch_camera_y_lo))) && (__enemies_touch_screen_y < 240)) {
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

                                        return;
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
                                       return;
                                   }
                                   """;

        var manualRom = NesRomCompiler.CompileSource(manualSource);
        var actorRom = NesRomCompiler.CompileSource(actorSource);

        Assert.True(
            manualRom.SequenceEqual(actorRom),
            "NES actor TouchTiles should match the manual loop that passes __enemies_touch_screen_x computed from enemies[__enemies_touch_i].x, so actor slots at X=24 and X=104 do not share one fixed collision column.");
        Assert.Equal(manualRom, actorRom);
    }

    [Fact]
    public void Compiles_actor_spawn_layer_as_runtime_camera_window_activation_on_nes()
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
                             {{RuntimeSpawnActivationBlockForNes("__enemies_spawn_0_call0")}}

                                 Camera.SetPosition(128, 0);
                             {{RuntimeSpawnActivationBlockForNes("__enemies_spawn_0_call1")}}
                                 return;
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
                                       return;
                                   }
                                   """;

        var expected = NesRomCompiler.CompileSource(manualSource);
        var actual = NesRomCompiler.CompileSource(actorSource, baseDirectory);
        var firstDifference = Enumerable.Range(16, expected.Length - 16).FirstOrDefault(index => expected[index] != actual[index], -1);

        Assert.True(
            expected.SequenceEqual(actual),
            firstDifference < 0
                ? "NES runtime actor activation ROMs differ only in iNES header bytes."
                : $"NES runtime actor activation ROMs differ at 0x{firstDifference:X4}: expected {BytesAround(expected, firstDifference)}, actual {BytesAround(actual, firstDifference)}.");
    }

    private static string WriteActorSpawnMap(string objectsJson)
    {
        var directory = Path.Combine(Path.GetTempPath(), "RetroSharp.NES.Tests", Guid.NewGuid().ToString("N"));
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

    private static string RuntimeSpawnActivationBlockForNes(string prefix)
    {
        return $$"""
                     u8 {{prefix}}_recycle_camera_x_lo = __rs_actor_camera_x_lo();
                     u8 {{prefix}}_recycle_camera_x_hi = __rs_actor_camera_x_hi();
                     for (u8 {{prefix}}_recycle_i = 0; {{prefix}}_recycle_i < countof(enemies); {{prefix}}_recycle_i += 1) {
                         if (enemies[{{prefix}}_recycle_i].active != 0) {
                             u8 {{prefix}}_recycle_screen_x = enemies[{{prefix}}_recycle_i].x - {{prefix}}_recycle_camera_x_lo;
                             if (!((enemies[{{prefix}}_recycle_i].xHi == {{prefix}}_recycle_camera_x_hi) && (enemies[{{prefix}}_recycle_i].x >= {{prefix}}_recycle_camera_x_lo) || (enemies[{{prefix}}_recycle_i].xHi == {{prefix}}_recycle_camera_x_hi + 1) && (enemies[{{prefix}}_recycle_i].x < {{prefix}}_recycle_camera_x_lo))) {
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
                             if ((({{prefix}}_xHi_value == {{prefix}}_camera_x_hi) && ({{prefix}}_x_value >= {{prefix}}_camera_x_lo)) || (({{prefix}}_xHi_value == {{prefix}}_camera_x_hi + 1) && ({{prefix}}_x_value < {{prefix}}_camera_x_lo))) {
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
