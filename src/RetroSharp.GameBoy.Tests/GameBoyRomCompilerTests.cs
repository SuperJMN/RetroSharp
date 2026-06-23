namespace RetroSharp.GameBoy.Tests;

using System.Buffers.Binary;
using System.IO.Compression;
using RetroSharp.Core.Sdk;
using RetroSharp.Core.Targeting;
using RetroSharp.GameBoy;
using RetroSharp.Parser;
using Xunit;

public class GameBoyRomCompilerTests
{
    [Fact]
    public void Compiles_video_api_calls_to_a_game_boy_rom()
    {
        const string source = """
                              void main() {
                                  video_init();
                                  palette_set(0, 0);
                                  palette_set(1, 1);
                                  palette_set(2, 2);
                                  palette_set(3, 3);
                                  tilemap_set(8, 7, 1);
                                  tilemap_set(9, 7, 2);
                                  tilemap_set(10, 7, 1);
                                  tilemap_set(9, 8, 3);
                                  return;
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.Equal(0x00, rom[0x0100]);
        Assert.Equal(0xC3, rom[0x0101]);
        Assert.Equal(0x50, rom[0x0102]);
        Assert.Equal(0x01, rom[0x0103]);
        Assert.Equal("RETROSHARPGB", System.Text.Encoding.ASCII.GetString(rom, 0x0134, 12));
        Assert.Equal(0x00, rom[0x0147]);
        Assert.Equal(0x00, rom[0x0148]);
        Assert.Equal(0x00, rom[0x0149]);
        Assert.Equal(HeaderChecksum(rom), rom[0x014D]);
        Assert.Contains(rom.Skip(0x0150), b => b != 0);
    }

    [Fact]
    public void Logical_palette_declarations_lower_to_game_boy_palette_registers()
    {
        const string source = """
                              void main() {
                                  video.Init();
                                  palette.Background(0, 0, 1, 2, 3);
                                  palette.Sprite(0, 0, 0, 1, 3);
                                  palette.Sprite(1, 0, 3, 2, 1);
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x3E, 0xE4, 0xE0, 0x47]), "palette.Background should lower slot 0 to BGP.");
        Assert.True(ContainsSequence(rom, [0x3E, 0xD0, 0xE0, 0x48]), "palette.Sprite slot 0 should lower to OBP0.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x6C, 0xE0, 0x49]), "palette.Sprite slot 1 should lower to OBP1.");
    }

    [Fact]
    public void Rejects_logical_sprite_palette_slots_outside_game_boy_capabilities()
    {
        const string source = """
                              void main() {
                                  video.Init();
                                  palette.Sprite(2, 0, 1, 2, 3);
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource(source));

        Assert.Equal("Target 'gb' supports sprite palette slots 0..1, but palette slot 2 was requested.", exception.Message);
    }

    [Fact]
    public void Compiles_parameterless_user_functions_like_inline_video_blocks()
    {
        const string directSource = """
                                    void main() {
                                        video_init();
                                        palette_set(0, 0);
                                        palette_set(1, 1);
                                        tilemap_set(8, 7, 1);
                                        tilemap_set(9, 7, 2);
                                        video_present();
                                        return;
                                    }
                                    """;

        const string functionSource = """
                                      void setup_palette() {
                                          palette_set(0, 0);
                                          palette_set(1, 1);
                                          return;
                                      }

                                      void draw_mark() {
                                          tilemap_set(8, 7, 1);
                                          tilemap_set(9, 7, 2);
                                          return;
                                      }

                                      void main() {
                                          video_init();
                                          setup_palette();
                                          draw_mark();
                                          video_present();
                                          return;
                                      }
                                      """;

        Assert.Equal(GameBoyRomCompiler.CompileSource(directSource), GameBoyRomCompiler.CompileSource(functionSource));
    }

    [Fact]
    public void Compiles_parameterized_static_user_functions_like_inline_video_blocks()
    {
        const string directSource = """
                                    void main() {
                                        video_init();
                                        tilemap_set(14, 12, 1);
                                        tilemap_set(15, 12, 2);
                                        video_present();
                                        return;
                                    }
                                    """;

        const string functionSource = """
                                      void draw_tile(u8 x, u8 tile) {
                                          tilemap_set(x, 12, tile);
                                          return;
                                      }

                                      void main() {
                                          video_init();
                                          draw_tile(14, 1);
                                          draw_tile(15, 2);
                                          video_present();
                                          return;
                                      }
                                      """;

        Assert.Equal(GameBoyRomCompiler.CompileSource(directSource), GameBoyRomCompiler.CompileSource(functionSource));
    }

    [Fact]
    public void Compiles_parameterized_runtime_user_functions_like_inline_video_blocks()
    {
        const string directSource = """
                                    void main() {
                                        video_init();
                                        i16 x = 0;
                                        scroll_set(4, 0);
                                        x = 7;
                                    }
                                    """;

        const string functionSource = """
                                      void apply_scroll(u8 amount) {
                                          scroll_set(amount, 0);
                                          return;
                                      }

                                      void main() {
                                          video_init();
                                          i16 x = 0;
                                          apply_scroll(4);
                                          x = 7;
                                      }
                                      """;

        Assert.Equal(GameBoyRomCompiler.CompileSource(directSource), GameBoyRomCompiler.CompileSource(functionSource));
    }

    [Fact]
    public void Compiles_expression_bodied_value_functions_like_single_return_helpers()
    {
        const string blockSource = """
                                   u8 choose_speed(u8 moving, u8 fast) {
                                       return moving != 0 ? fast : 0;
                                   }

                                   void main() {
                                       video_init();
                                       u8 moving = 1;
                                       u8 fast = 2;
                                       u8 speed = choose_speed(moving, fast);
                                   }
                                   """;

        const string expressionSource = """
                                        u8 choose_speed(u8 moving, u8 fast) => moving != 0 ? fast : 0;

                                        void main() {
                                            video_init();
                                            u8 moving = 1;
                                            u8 fast = 2;
                                            u8 speed = choose_speed(moving, fast);
                                        }
                                        """;

        Assert.Equal(GameBoyRomCompiler.CompileSource(blockSource), GameBoyRomCompiler.CompileSource(expressionSource));
    }

    [Fact]
    public void Compiles_default_parameter_value_functions_like_explicit_arguments()
    {
        const string explicitSource = """
                                      u8 step(u8 value, u8 amount = value + 1) => value + amount;

                                      void main() {
                                          video_init();
                                          u8 next = step(4, 5);
                                      }
                                      """;

        const string omittedSource = """
                                     u8 step(u8 value, u8 amount = value + 1) => value + amount;

                                     void main() {
                                         video_init();
                                         u8 next = step(4);
                                     }
                                     """;

        Assert.Equal(GameBoyRomCompiler.CompileSource(explicitSource), GameBoyRomCompiler.CompileSource(omittedSource));
    }

    [Fact]
    public void Compiles_named_argument_value_functions_like_explicit_arguments()
    {
        const string explicitSource = """
                                      u8 step(u8 value, u8 amount = value + 1) => value + amount;

                                      void main() {
                                          video_init();
                                          u8 next = step(4, 5);
                                      }
                                      """;

        const string namedSource = """
                                   u8 step(u8 value, u8 amount = value + 1) => value + amount;

                                   void main() {
                                       video_init();
                                       u8 next = step(amount: 5, value: 4);
                                   }
                                   """;

        const string omittedNamedSource = """
                                          u8 step(u8 value, u8 amount = value + 1) => value + amount;

                                          void main() {
                                              video_init();
                                              u8 next = step(value: 4);
                                          }
                                          """;

        var explicitRom = GameBoyRomCompiler.CompileSource(explicitSource);
        Assert.Equal(explicitRom, GameBoyRomCompiler.CompileSource(namedSource));
        Assert.Equal(explicitRom, GameBoyRomCompiler.CompileSource(omittedNamedSource));
    }

    [Fact]
    public void Compiles_immutable_let_binding_like_equivalent_local_storage()
    {
        const string variableSource = """
                                      void main() {
                                          video_init();
                                          u8 speed = 2;
                                          u8 next = speed + 1;
                                          u8 sink = 0;
                                          sink = next;
                                      }
                                      """;

        const string letSource = """
                                 void main() {
                                     video_init();
                                     let speed = 2;
                                     u8 next = speed + 1;
                                     u8 sink = 0;
                                     sink = next;
                                 }
                                 """;

        Assert.Equal(GameBoyRomCompiler.CompileSource(variableSource), GameBoyRomCompiler.CompileSource(letSource));
    }

    [Fact]
    public void Compiles_inline_pure_helper_contracts_like_equivalent_current_helpers()
    {
        const string implicitSource = """
                                      u8 step(u8 value, u8 amount = 1) => value + amount;

                                      void main() {
                                          video_init();
                                          u8 next = step(4);
                                      }
                                      """;

        const string explicitSource = """
                                      inline pure u8 step(u8 value, u8 amount = 1) => value + amount;

                                      void main() {
                                          video_init();
                                          u8 next = step(4);
                                      }
                                      """;

        Assert.Equal(GameBoyRomCompiler.CompileSource(implicitSource), GameBoyRomCompiler.CompileSource(explicitSource));
    }

    [Fact]
    public void Compiles_switch_expression_like_equivalent_conditional_expression()
    {
        const string conditionalSource = """
                                         void main() {
                                             video_init();
                                             u8 state = 2;
                                             u8 speed = state == 0 ? 0 : state == 1 ? 2 : state >= 2 && state < 5 ? 3 : 1;
                                             u8 sink = 0;
                                             sink = speed;
                                         }
                                         """;

        const string switchSource = """
                                    void main() {
                                        video_init();
                                        u8 state = 2;
                                        u8 speed = state switch { 0 => 0, 1 => 2, 2..5 => 3, _ => 1 };
                                        u8 sink = 0;
                                        sink = speed;
                                    }
                                    """;

        Assert.Equal(GameBoyRomCompiler.CompileSource(conditionalSource), GameBoyRomCompiler.CompileSource(switchSource));
    }

    [Fact]
    public void Rejects_game_boy_switch_expression_that_would_re_evaluate_subject()
    {
        const string source = """
                              u8 next(u8 value) => value + 1;

                              void main() {
                                  video_init();
                                  u8 speed = next(1) switch { 0 => 0, _ => 1 };
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource(source));
        Assert.Equal("switch expression subject must be a simple value expression so lowering cannot re-evaluate a call or side effect.", exception.Message);
    }

    [Fact]
    public void Compiles_sdk_namespaced_dot_calls_like_existing_sdk_functions()
    {
        const string functionSource = """
                                      void main() {
                                          video_init();
                                          world_column(0, 1, 2);
                                          world_column(1, 3, 4);
                                          world_map(2, 10, 2);
                                          camera_init(2, 10, 2);
                                          camera_set_position(4, 0);
                                          video_wait_vblank();
                                          input_poll();
                                      }
                                      """;

        const string dotSource = """
                                 void main() {
                                     video.Init();
                                     world.Column(0, 1, 2);
                                     world.Column(1, 3, 4);
                                     world.Map(2, 10, 2);
                                     camera.Init(2, 10, 2);
                                     camera.SetPosition(4, 0);
                                     video.WaitVBlank();
                                     input.Poll();
                                 }
                                 """;

        Assert.Equal(GameBoyRomCompiler.CompileSource(functionSource), GameBoyRomCompiler.CompileSource(dotSource));
    }

    [Fact]
    public void Compiles_wait_frame_library_helper_over_game_boy_intrinsic_like_sdk_operation()
    {
        const string sdkSource = """
                                 void main() {
                                     video_wait_vblank();
                                 }
                                 """;

        const string intrinsicSource = """
                                       [target("gb")]
                                       [intrinsic("wait_frame")]
                                       extern void gb_wait_frame();

                                       inline void wait_frame() {
                                           gb_wait_frame();
                                       }

                                       void main() {
                                           wait_frame();
                                       }
                                       """;

        Assert.Equal(GameBoyRomCompiler.CompileSource(sdkSource), GameBoyRomCompiler.CompileSource(intrinsicSource));
    }

    [Fact]
    public void Compiles_receiver_method_calls_like_static_helper_calls()
    {
        const string staticSource = """
                                    struct Actor { u8 x; }

                                    inline void Move(this Actor actor, u8 dx) {
                                        actor.x += dx;
                                    }

                                    void main() {
                                        video_init();
                                        Actor actor;
                                        Move(actor, 2);
                                    }
                                    """;

        const string receiverSource = """
                                      struct Actor { u8 x; }

                                      inline void Move(this Actor actor, u8 dx) {
                                          actor.x += dx;
                                      }

                                      void main() {
                                          video_init();
                                          Actor actor;
                                          actor.Move(2);
                                      }
                                      """;

        Assert.Equal(GameBoyRomCompiler.CompileSource(staticSource), GameBoyRomCompiler.CompileSource(receiverSource));
    }

    [Fact]
    public void Compiles_static_class_instance_methods_like_struct_receiver_helpers()
    {
        const string structSource = """
                                    struct Actor { u8 x; }

                                    inline void Move(this Actor actor, u8 dx) {
                                        actor.x += dx;
                                    }

                                    void main() {
                                        video_init();
                                        Actor actor;
                                        actor.Move(2);
                                    }
                                    """;

        const string classSource = """
                                   class Actor {
                                       u8 x;

                                       inline void Move(u8 dx) {
                                           x += dx;
                                       }
                                   }

                                   void main() {
                                       video_init();
                                       Actor actor;
                                       actor.Move(2);
                                   }
                                   """;

        Assert.Equal(GameBoyRomCompiler.CompileSource(structSource), GameBoyRomCompiler.CompileSource(classSource));
    }

    [Fact]
    public void Compiles_static_class_self_calls_like_receiver_helper_calls()
    {
        const string structSource = """
                                    struct Actor { u8 x; }

                                    inline void Nudge(this Actor actor) {
                                        actor.x += 1;
                                    }

                                    inline void Move(this Actor actor) {
                                        Nudge(actor);
                                    }

                                    void main() {
                                        video_init();
                                        Actor actor;
                                        actor.Move();
                                    }
                                    """;

        const string classSource = """
                                   class Actor {
                                       u8 x;

                                       inline void Nudge() {
                                           x += 1;
                                       }

                                       inline void Move() {
                                           Nudge();
                                       }
                                   }

                                   void main() {
                                       video_init();
                                       Actor actor;
                                       actor.Move();
                                   }
                                   """;

        Assert.Equal(GameBoyRomCompiler.CompileSource(structSource), GameBoyRomCompiler.CompileSource(classSource));
    }

    [Fact]
    public void Compiles_static_class_static_methods_constants_and_initializers_like_flat_code()
    {
        const string flatSource = """
                                  const Step = 2;

                                  inline u8 Apply(u8 value) {
                                      return value + Step;
                                  }

                                  struct Actor { u8 x; }

                                  void main() {
                                      video_init();
                                      Actor actor = { x: Step };
                                      actor.x = Apply(actor.x);
                                  }
                                  """;

        const string classSource = """
                                   class Tuning {
                                       static const Step = 2;

                                       static inline u8 Apply(u8 value) {
                                           return value + Tuning.Step;
                                       }
                                   }

                                   class Actor { u8 x; }

                                   void main() {
                                       video_init();
                                       Actor actor = { x: Tuning.Step };
                                       actor.x = Tuning.Apply(actor.x);
                                   }
                                   """;

        Assert.Equal(GameBoyRomCompiler.CompileSource(flatSource), GameBoyRomCompiler.CompileSource(classSource));
    }

    [Fact]
    public void Compiles_receiver_method_calls_inside_nested_blocks_and_sdk_name_shadows()
    {
        const string staticSource = """
                                    struct Actor { u8 x; }

                                    inline void Move(this Actor actor, u8 dx) {
                                        actor.x += dx;
                                    }

                                    void main() {
                                        video_init();
                                        Actor video;
                                        if (true) {
                                            Move(video, 2);
                                        }
                                    }
                                    """;

        const string receiverSource = """
                                      struct Actor { u8 x; }

                                      inline void Move(this Actor actor, u8 dx) {
                                          actor.x += dx;
                                      }

                                      void main() {
                                          video_init();
                                          Actor video;
                                          if (true) {
                                              video.Move(2);
                                          }
                                      }
                                      """;

        Assert.Equal(GameBoyRomCompiler.CompileSource(staticSource), GameBoyRomCompiler.CompileSource(receiverSource));
    }

    [Fact]
    public void Compiles_pipeline_expression_like_nested_helper_calls()
    {
        const string nestedSource = """
                                    u8 Clamp(u8 value, u8 min, u8 max) => value < min ? min : value > max ? max : value;
                                    u8 SnapToTile(u8 value) => value & 0xF8;

                                    void main() {
                                        video_init();
                                        u8 value = 130;
                                        u8 snapped = SnapToTile(Clamp(value, 0, 120));
                                    }
                                    """;

        const string pipelineSource = """
                                      u8 Clamp(u8 value, u8 min, u8 max) => value < min ? min : value > max ? max : value;
                                      u8 SnapToTile(u8 value) => value & 0xF8;

                                      void main() {
                                          video_init();
                                          u8 value = 130;
                                          u8 snapped = value |> Clamp(0, 120) |> SnapToTile();
                                      }
                                      """;

        Assert.Equal(GameBoyRomCompiler.CompileSource(nestedSource), GameBoyRomCompiler.CompileSource(pipelineSource));
    }

    [Fact]
    public void Compiles_pipeline_expression_with_receiver_and_named_default_arguments()
    {
        const string nestedSource = """
                                    struct Actor { u8 x; }
                                    u8 X(this Actor actor) => actor.x;
                                    u8 Clamp(u8 value, u8 min = 0, u8 max = 120) => value < min ? min : value > max ? max : value;

                                    void main() {
                                        video_init();
                                        Actor actor = { x: 130 };
                                        u8 clamped = Clamp(X(actor), max: 120);
                                    }
                                    """;

        const string pipelineSource = """
                                      struct Actor { u8 x; }
                                      u8 X(this Actor actor) => actor.x;
                                      u8 Clamp(u8 value, u8 min = 0, u8 max = 120) => value < min ? min : value > max ? max : value;

                                      void main() {
                                          video_init();
                                          Actor actor = { x: 130 };
                                          u8 clamped = actor.X() |> Clamp(max: 120);
                                      }
                                      """;

        Assert.Equal(GameBoyRomCompiler.CompileSource(nestedSource), GameBoyRomCompiler.CompileSource(pipelineSource));
    }

    [Fact]
    public void Rejects_game_boy_invalid_pure_helper_contracts_before_lowering()
    {
        var statementEffect = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource("""
                                                                                                             pure void draw() {
                                                                                                                 video_init();
                                                                                                             }

                                                                                                             void main() {
                                                                                                                 draw();
                                                                                                             }
                                                                                                             """));
        Assert.Equal("pure helper 'draw' contains side-effecting statements; pure helpers must be a single return expression.", statementEffect.Message);

        var callEffect = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource("""
                                                                                                        u8 next(u8 value) => value + 1;
                                                                                                        pure u8 step(u8 value) => next(value);

                                                                                                        void main() {
                                                                                                            video_init();
                                                                                                            u8 result = step(4);
                                                                                                        }
                                                                                                        """));
        Assert.Equal("pure helper 'step' return expression contains side-effecting operations.", callEffect.Message);
    }

    [Fact]
    public void Rejects_game_boy_explicit_inline_value_helper_when_not_substitutable()
    {
        const string source = """
                              inline u8 step(u8 value) {
                                  u8 next = value + 1;
                                  return next;
                              }

                              void main() {
                                  video_init();
                                  u8 result = step(4);
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource(source));
        Assert.Equal("Game Boy target cannot inline helper 'step' as a value because inline value helpers must be exactly one return expression.", exception.Message);
    }

    [Fact]
    public void Rejects_game_boy_assignment_to_immutable_let_binding()
    {
        const string source = """
                              void main() {
                                  video_init();
                                  let speed = 2;
                                  speed = 3;
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource(source));
        Assert.Equal("Cannot assign to immutable local 'speed'.", exception.Message);
    }

    [Fact]
    public void Rejects_game_boy_compound_and_postfix_mutation_of_immutable_let_binding()
    {
        var compound = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource("""
                                                                                                      void main() {
                                                                                                          video_init();
                                                                                                          let speed = 2;
                                                                                                          speed += 1;
                                                                                                      }
                                                                                                      """));
        Assert.Equal("Cannot assign to immutable local 'speed'.", compound.Message);

        var postfix = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource("""
                                                                                                     void main() {
                                                                                                         video_init();
                                                                                                         let speed = 2;
                                                                                                         speed++;
                                                                                                     }
                                                                                                     """));
        Assert.Equal("Cannot assign to immutable local 'speed'.", postfix.Message);
    }

    [Fact]
    public void GameBoy_drawing_sample_compiles_with_helper_functions()
    {
        var sourcePath = RepositoryFile("samples/gameboy-drawing/drawing.rs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("void draw_face()", source);

        var rom = GameBoyRomCompiler.CompileSource(source, Path.GetDirectoryName(sourcePath));

        Assert.Equal(32768, rom.Length);
    }

    [Fact]
    public void Hud_set_tile_collects_window_operation_and_compiles_to_game_boy_window_tilemap()
    {
        const string source = """
                              void main() {
                                  video_init();
                                  hud_set_tile(window, 1, 0, 5);
                                  return;
                              }
                              """;

        var operations = GameBoyRomCompiler.CollectSdkOperations(source);
        var hud = Assert.IsType<Sdk2DOperation.SetHudTile>(Assert.Single(operations));

        Assert.Equal(HudMode.Window, hud.Mode);
        Assert.Equal(1, hud.X);
        Assert.Equal(0, hud.Y);
        Assert.Equal(5, hud.Tile);

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x3E, 0x00, 0xE0, 0x4A, 0x3E, 0x07, 0xE0, 0x4B]), "Window HUD should position WY=0 and WX=7.");
        Assert.True(ContainsSequence(rom, [0x21, 0x00, 0x9C]), "Window HUD should copy a separate tilemap to $9C00.");
        Assert.True(ContainsSequence(rom, [0x3E, 0xF7, 0xE0, 0x40]), "Window HUD should enable the LCD window layer without disabling existing LCD features.");
    }

    [Fact]
    public void Rejects_split_scroll_hud_mode_through_capability_check()
    {
        const string source = """
                              void main() {
                                  video_init();
                                  hud_set_tile(split_scroll, 0, 0, 1);
                                  return;
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource(source));

        Assert.Equal(
            "Target 'gb' does not support SplitScroll HUD. Use Window HUD, SpriteHud, or disable HUD for this target.",
            exception.Message);
    }

    [Fact]
    public void GameBoy_hud_sample_compiles_with_window_hud()
    {
        var sourcePath = RepositoryFile("samples/gameboy-hud/hud.rs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("hud.SetTile(window", source);

        var rom = GameBoyRomCompiler.CompileSource(source, Path.GetDirectoryName(sourcePath));

        Assert.Equal(32768, rom.Length);
    }

    [Fact]
    public void Compiles_runtime_sprite_loop_to_a_game_boy_rom()
    {
        const string source = """
                              void main() {
                                  video_init();
                                  palette_set(0, 0);
                                  palette_set(1, 1);
                                  palette_set(2, 2);
                                  palette_set(3, 3);
                                  i16 x = 8;
                                  while (true) {
                                      video_wait_vblank();
                                      sprite_set(0, x, 88, 6, 0);
                                      x = x + 1;
                                      if (x == 168) {
                                          x = 0;
                                      }
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x3E, 0x97, 0xE0, 0x40]), "ROM should enable LCD, background, 8x16 sprites, and sprite rendering.");
        Assert.True(ContainsSequence(rom, [0xEA, 0x01, 0xFE]), "ROM should write sprite X into OAM.");
        Assert.True(ContainsSequence(rom, [0xFE, 0xA8]), "ROM should compare x with the wrap coordinate.");
        Assert.True(ContainsSequence(rom, [0x18]), "ROM should contain a relative loop jump.");
    }

    [Fact]
    public void Compiles_sprite_asset_draw_to_a_game_boy_metasprite()
    {
        var baseDirectory = WriteSpriteAsset(
            "player.sprite.json",
            SpriteJson(
                Rows(
                    8,
                    16,
                    "01230123",
                    "32103210")));

        const string source = """
                              void main() {
                                  video_init();
                                  sprite_asset(player_run, "player.sprite.json");
                                  sprite_draw(player_run, 72, 80, 0);
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source, baseDirectory);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x55, 0x33, 0xAA, 0xCC]), "ROM should contain tile data loaded from the editable sprite asset.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x50, 0xC6, 0x10, 0xEA, 0x00, 0xFE]), "sprite_draw should write the logical Y plus the Game Boy sprite offset.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x48, 0xC6, 0x08, 0xEA, 0x01, 0xFE]), "sprite_draw should write the logical X plus the Game Boy sprite offset.");
        Assert.True(ContainsSequence(rom, [0xC6, 0x06, 0xEA, 0x02, 0xFE]), "sprite_draw should use the first generated tile for the first hardware sprite.");
    }

    [Fact]
    public void Sprite_draw_composes_16x32_assets_from_four_hardware_sprites()
    {
        var baseDirectory = WriteSpriteAsset(
            "player.sprite.json",
            SpriteJson(Rows(16, 32)));

        const string source = """
                              void main() {
                                  video_init();
                                  sprite_asset(big_player, "player.sprite.json");
                                  sprite_draw(big_player, 72, 64, 0);
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source, baseDirectory);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x3E, 0x40, 0xC6, 0x10, 0xEA, 0x00, 0xFE]), "Top-left piece should use the logical Y coordinate.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x48, 0xC6, 0x10, 0xEA, 0x05, 0xFE]), "Top-right piece should add the 8 px X offset.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x40, 0xC6, 0x20, 0xEA, 0x08, 0xFE]), "Bottom-left piece should add the 16 px Y offset.");
        Assert.True(ContainsSequence(rom, [0xC6, 0x0C, 0xEA, 0x0E, 0xFE]), "Bottom-right piece should use the fourth generated 8x16 tile pair.");
    }

    [Fact]
    public void Sprite_draw_accepts_logical_flip_x_and_flips_logical_metasprites_horizontally()
    {
        var baseDirectory = WriteSpriteAsset(
            "player.sprite.json",
            SpriteJson(Rows(16, 32)));

        const string source = """
                              void main() {
                                  video_init();
                                  sprite_asset(big_player, "player.sprite.json");
                                  bool flipX = true;
                                  sprite_draw(big_player, 72, 64, 0, flipX);
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source, baseDirectory);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x3E, 0x20]), "sprite_draw should lower logical flipX to the Game Boy OAM X-flip bit.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xFE, 0x00, 0xCA]), "sprite_draw should test the logical flipX boolean before placing metasprite pieces.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x48, 0xC6, 0x10, 0xEA, 0x01, 0xFE]), "X-flipped first logical piece should move to the mirrored top-right hardware X coordinate.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x48, 0xC6, 0x08, 0xEA, 0x05, 0xFE]), "X-flipped second logical piece should move to the mirrored top-left hardware X coordinate.");
        Assert.True(ContainsSequence(rom, [0xC6, 0x06, 0xEA, 0x02, 0xFE]), "X-flipped first logical piece should keep its own tile and rely on the OAM flip bit.");
        Assert.True(ContainsSequence(rom, [0xC6, 0x08, 0xEA, 0x06, 0xFE]), "X-flipped second logical piece should keep its own tile and rely on the OAM flip bit.");
    }

    [Fact]
    public void Sprite_draw_accepts_logical_palette_slot_and_lowers_to_game_boy_object_palette_bit()
    {
        var baseDirectory = WriteSpriteAsset(
            "player.sprite.json",
            SpriteJson(Rows(16, 32)));

        const string source = """
                              void main() {
                                  video_init();
                                  sprite_asset(player, "player.sprite.json");
                                  sprite_draw(player, 72, 64, 0, false, 1);
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source, baseDirectory);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x3E, 0x10, 0xEA, 0x03, 0xFE]), "palette slot 1 should lower to the Game Boy OBP1 OAM attribute bit.");
    }

    [Fact]
    public void Sprite_draw_combines_logical_flip_x_and_palette_slot_in_oam_attributes()
    {
        var baseDirectory = WriteSpriteAsset(
            "player.sprite.json",
            SpriteJson(Rows(16, 32)));

        const string source = """
                              void main() {
                                  video_init();
                                  sprite_asset(player, "player.sprite.json");
                                  sprite_draw(player, 72, 64, 0, true, 1);
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source, baseDirectory);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x3E, 0x30, 0xEA, 0x03, 0xFE]), "flipX and palette slot 1 should combine into OAM attributes without exposing raw flags in source.");
    }

    [Fact]
    public void Animation_frame_maps_constant_ticks_through_looping_clip_data()
    {
        const string source = """
                              void main() {
                                  video_init();
                                  animation_clip(run, 1, 6, 6, 6);
                                  sprite_set(0, 72, 80, animation_frame(run, 0), 0);
                                  sprite_set(1, 80, 80, animation_frame(run, 5), 0);
                                  sprite_set(2, 88, 80, animation_frame(run, 6), 0);
                                  sprite_set(3, 96, 80, animation_frame(run, 17), 0);
                                  sprite_set(4, 104, 80, animation_frame(run, 18), 0);
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x3E, 0x01, 0xEA, 0x02, 0xFE]), "tick 0 should select frame 1.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x01, 0xEA, 0x06, 0xFE]), "tick 5 should still select frame 1.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x02, 0xEA, 0x0A, 0xFE]), "tick 6 should select frame 2.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x03, 0xEA, 0x0E, 0xFE]), "tick 17 should select frame 3.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x01, 0xEA, 0x12, 0xFE]), "tick 18 should loop to frame 1.");
    }

    [Fact]
    public void Animation_frame_lowers_dynamic_ticks_with_predictable_modulo_and_boundary_checks()
    {
        const string source = """
                              void main() {
                                  video_init();
                                  animation_clip(run, 1, 6, 6, 6);
                                  i16 tick = 18;
                                  sprite_set(0, 72, 80, animation_frame(run, tick), 0);
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0xFE, 0x12, 0xDA]), "animation_frame should compare the tick against total clip duration before modulo subtraction.");
        Assert.True(ContainsSequence(rom, [0xD6, 0x12]), "animation_frame should subtract total clip duration while the tick is outside the clip.");
        Assert.True(ContainsSequence(rom, [0xFE, 0x06, 0xDA]), "animation_frame should test the first frame boundary.");
        Assert.True(ContainsSequence(rom, [0xFE, 0x0C, 0xDA]), "animation_frame should test the second frame boundary.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x01]), "animation_frame should be able to return frame 1.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x02]), "animation_frame should be able to return frame 2.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x03]), "animation_frame should be able to return frame 3.");
    }

    [Fact]
    public void Sprite_draw_rejects_palette_slots_outside_game_boy_capabilities()
    {
        var baseDirectory = WriteSpriteAsset(
            "player.sprite.json",
            SpriteJson(Rows(16, 32)));

        const string source = """
                              void main() {
                                  video_init();
                                  sprite_asset(player, "player.sprite.json");
                                  sprite_draw(player, 72, 64, 0, false, 2);
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource(source, baseDirectory));

        Assert.Equal("Target 'gb' supports sprite palette slots 0..1, but slot 2 was requested.", exception.Message);
    }

    [Fact]
    public void Sprite_draw_flips_against_logical_width_before_padding()
    {
        var baseDirectory = WriteSpriteAsset(
            "player.sprite.json",
            SpriteJson(Rows(18, 16)));

        const string source = """
                              void main() {
                                  video_init();
                                  sprite_asset(player, "player.sprite.json");
                                  bool flipX = true;
                                  sprite_draw(player, 72, 64, 0, flipX);
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source, baseDirectory);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x3E, 0x48, 0xC6, 0x12, 0xEA, 0x01, 0xFE]), "The first 8 px piece should move to logical X + 10 when an 18 px sprite is flipped.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x48, 0xC6, 0x0A, 0xEA, 0x05, 0xFE]), "The middle 8 px piece should move to logical X + 2 when an 18 px sprite is flipped.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x48, 0xC6, 0x02, 0xEA, 0x09, 0xFE]), "The padded edge piece should straddle the logical origin instead of adding padded left spacing.");
    }

    [Fact]
    public void Sprite_draw_rejects_raw_oam_attribute_constants_in_portable_flip_argument()
    {
        var baseDirectory = WriteSpriteAsset(
            "player.sprite.json",
            SpriteJson(Rows(16, 32)));

        const string source = """
                              void main() {
                                  video_init();
                                  sprite_asset(player, "player.sprite.json");
                                  sprite_draw(player, 72, 64, 0, 32);
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource(source, baseDirectory));

        Assert.Equal("sprite_draw argument 5 is portable flipX and must be 0, 1, true, false, or a local bool-like value. Use sprite_set for raw Game Boy OAM attributes.", exception.Message);
    }

    [Fact]
    public void Sprite_draw_treats_frame_as_a_logical_frame_index()
    {
        var baseDirectory = WriteSpriteAsset(
            "player.sprite.json",
            SpriteJson(
                Rows(8, 16, "01230123"),
                Rows(8, 16, "32103210")));

        const string source = """
                              void main() {
                                  video_init();
                                  sprite_asset(player_run, "player.sprite.json");
                                  i16 frame = 1;
                                  sprite_draw(player_run, 72, 80, frame);
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source, baseDirectory);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0x47, 0xAF, 0x80, 0x80, 0xC6, 0x06, 0xEA, 0x02, 0xFE]), "sprite_draw should multiply the logical frame by the per-frame tile count.");
    }

    [Fact]
    public void Compiles_png_sprite_sheet_to_a_game_boy_metasprite()
    {
        var baseDirectory = WriteSpritePng(
            "player-run.gb.png",
            16,
            16,
            Rows(
                16,
                16,
                "0231000000000000",
                "3210321000000000"),
            Rows(
                16,
                16,
                "3210321000000000",
                "0123012300000000"));

        const string source = """
                              void main() {
                                  video_init();
                                  sprite_asset(player_run, "player-run.gb.png", 16, 16);
                                  i16 frame = 1;
                                  sprite_draw(player_run, 72, 80, frame);
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source, baseDirectory);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x30, 0x60, 0xAA, 0xCC]), "ROM should contain tile data decoded from the PNG sprite sheet with stable palette indexes.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0x47, 0xAF, 0x80, 0x80, 0x80, 0x80, 0xC6, 0x06, 0xEA, 0x02, 0xFE]), "sprite_draw should use the PNG logical frame index.");
    }

    [Fact]
    public void Compiles_png_sprite_sheet_using_game_boy_platform_variant()
    {
        var baseDirectory = WriteSpritePng(
            "player-run.gb.png",
            8,
            16,
            Rows(8, 16, "11111111"));

        const string source = """
                              void main() {
                                  video_init();
                                  sprite_asset(player_run, "player-run.png", 8, 16);
                                  sprite_draw(player_run, 72, 80, 0);
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source, baseDirectory);
        var program = CompileVideoProgram(source, baseDirectory);
        var asset = program.SpriteAssets["player_run"];

        Assert.Equal(32768, rom.Length);
        Assert.Equal(8, asset.LogicalWidth);
        Assert.Equal(16, asset.LogicalHeight);
        Assert.Equal(1, asset.FrameCount);
    }

    [Fact]
    public void Compiles_png_sprite_sheet_with_non_hardware_height_by_padding()
    {
        var baseDirectory = WriteSpritePng(
            "mario-run.gb.png",
            16,
            27,
            Rows(16, 27, "0231000000000000"));

        const string source = """
                              void main() {
                                  video_init();
                                  sprite_asset(mario_run, "mario-run.gb.png", 16, 27);
                                  sprite_draw(mario_run, 72, 77, 0);
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source, baseDirectory);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x3E, 0x4D, 0xC6, 0x20, 0xEA, 0x08, 0xFE]), "sprite_draw should emit a bottom row hardware sprite after padding 27 px to 32 px.");
        Assert.True(ContainsSequence(rom, [0xC6, 0x0C, 0xEA, 0x0E, 0xFE]), "sprite_draw should allocate the fourth 8x16 tile pair for a padded 16x27 logical sprite.");
    }

    [Fact]
    public void Compiles_grayscale_png_sprite_sheet_with_stable_light_to_dark_mapping()
    {
        var baseDirectory = WriteGrayscaleSpritePng(
            "mario-run.gb.png",
            16,
            27,
            Rows(16, 27, "3210000000000000"));

        const string source = """
                              void main() {
                                  video_init();
                                  sprite_asset(mario_run, "mario-run.gb.png", 16, 27);
                                  sprite_draw(mario_run, 72, 77, 0);
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source, baseDirectory);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0xA0, 0xC0]), "Grayscale PNG should map black to 3, gray to 2, and white to 1 even when black appears first.");
    }

    [Fact]
    public void Compiles_scroll_set_to_game_boy_scroll_register_writes()
    {
        const string source = """
                              void main() {
                                  video_init();
                                  i16 camera = 0;
                                  while (true) {
                                      video_wait_vblank();
                                      scroll_set(camera, 0);
                                      camera = camera + 1;
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xE0, 0x43]), "ROM should load camera from WRAM and write it to SCX.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x00, 0xE0, 0x42]), "ROM should write the constant Y scroll to SCY.");
    }

    [Fact]
    public void Compiles_camera_runtime_to_world_scroll_state_and_streaming()
    {
        const string source = """
                              void main() {
                                  video_init();
                                  map_column(0, 0, 0, 4, 5);
                                  map_column(1, 0, 0, 4, 5);
                                  map_column(2, 0, 0, 4, 5);
                                  map_column(3, 0, 0, 4, 5);
                                  map_column(4, 0, 0, 4, 5);
                                  map_column(5, 0, 0, 4, 5);
                                  map_column(6, 0, 0, 4, 5);
                                  map_column(7, 0, 0, 4, 5);
                                  map_column(8, 0, 0, 4, 5);
                                  map_column(9, 0, 0, 4, 5);
                                  map_column(10, 0, 0, 4, 5);
                                  map_column(11, 0, 0, 4, 5);
                                  map_column(12, 0, 0, 4, 5);
                                  map_column(13, 0, 0, 4, 5);
                                  map_column(14, 0, 0, 4, 5);
                                  map_column(15, 0, 0, 4, 5);
                                  camera_init(16, 11, 4);
                                  while (true) {
                                      video_wait_vblank();
                                      camera_apply();
                                      camera_move_right();
                                      camera_move_left();
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x3E, 0x00, 0xEA, 0xE0, 0xC0, 0xEA, 0xE1, 0xC0, 0xEA, 0xE2, 0xC0]), "camera_init should initialize the 16-bit world X and fine scroll state.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x15, 0xEA, 0xE4, 0xC0, 0x3E, 0x1F, 0xEA, 0xE5, 0xC0]), "camera_init should prefetch one column beyond the 20 full visible columns for fine-scroll partial tiles.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x05, 0xEA, 0xE6, 0xC0]), "camera_init should seed the right source cursor to the fine-scroll partial edge column.");
        Assert.True(ContainsSequence(rom, [0xFA, 0xE0, 0xC0, 0xE0, 0x43, 0xFA, 0xE8, 0xC0, 0xE0, 0x42]), "camera_apply should write the current camera X and Y low bytes to SCX and SCY.");
        Assert.True(ContainsSequence(rom, [0xFA, 0xE0, 0xC0, 0xC6, 0x01, 0xEA, 0xE0, 0xC0]), "camera_move_right should increment the 16-bit camera X low byte.");
        Assert.True(ContainsSequence(rom, [0xFA, 0xE6, 0xC0, 0x5F, 0x16, 0x00]), "camera_move_right should stream from the right source column.");
        Assert.True(ContainsSequence(rom, [0xFA, 0xE5, 0xC0, 0xC6, 0x60, 0x6F, 0x26, 0x99]), "camera_move_left should stream into the left background edge.");
    }

    [Fact]
    public void Camera_set_position_compares_requested_x_before_reusing_camera_steps()
    {
        const string source = """
                              void main() {
                                  video_init();
                                  map_column(0, 0, 0, 4, 5);
                                  map_column(1, 0, 0, 4, 5);
                                  camera_init(2, 11, 4);
                                  i16 requestedX = 1;
                                  camera_set_position(requestedX, 0);
                                  camera_apply();
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0x47, 0xFA, 0xE0, 0xC0, 0x4F, 0x78, 0x91, 0xFE, 0x00, 0xCA]), "camera_set_position should compare modular camera X delta and keep a no-movement path.");
        Assert.True(ContainsSequence(rom, [0x91, 0xFE, 0x00, 0xCA]), "camera_set_position should compute requested-low minus current-low before choosing a step direction.");
        Assert.True(ContainsSequence(rom, [0xFE, 0x80, 0xDA]), "camera_set_position should treat small unsigned deltas as positive movement across byte wrap.");
        Assert.True(ContainsSequence(rom, [0xFA, 0xE0, 0xC0, 0xC6, 0x01, 0xEA, 0xE0, 0xC0]), "camera_set_position should reuse the right-step camera movement path.");
        Assert.True(ContainsSequence(rom, [0xFA, 0xE0, 0xC0, 0xFE, 0x00, 0xC2]), "camera_set_position should reuse the left-step camera movement path.");
    }

    [Fact]
    public void Camera_set_position_tracks_y_state_and_applies_vertical_scroll()
    {
        const string source = """
                              void main() {
                                  video_init();
                                  map_column(0, 0, 0, 4, 5);
                                  map_column(1, 0, 0, 4, 5);
                                  camera_init(2, 11, 4);
                                  i16 cameraY = 1;
                                  camera_set_position(0, cameraY);
                                  camera_apply();
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x3E, 0x00, 0xEA, 0xE8, 0xC0, 0xEA, 0xE9, 0xC0, 0xEA, 0xEA, 0xC0]), "camera_init should initialize the 16-bit world Y and fine scroll state.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0x47, 0xFA, 0xE8, 0xC0, 0x4F, 0x78, 0x91, 0xFE, 0x00, 0xCA]), "camera_set_position should compare modular camera Y delta and keep a no-movement path.");
        Assert.True(ContainsSequence(rom, [0xFA, 0xE8, 0xC0, 0xC6, 0x01, 0xEA, 0xE8, 0xC0]), "camera_set_position should reuse a down-step camera movement path.");
        Assert.True(ContainsSequence(rom, [0xFA, 0xEA, 0xC0, 0xC6, 0x01, 0xEA, 0xEA, 0xC0, 0xFE, 0x08]), "camera_set_position should track fine Y tile-boundary crossings.");
        Assert.True(ContainsSequence(rom, [0xFA, 0xE0, 0xC0, 0xE0, 0x43, 0xFA, 0xE8, 0xC0, 0xE0, 0x42]), "camera_apply should write camera X to SCX and camera Y to SCY.");
    }

    [Fact]
    public void Camera_set_position_streams_bottom_row_when_y_crosses_tile_down()
    {
        const string source = """
                              void main() {
                                  video_init();
                                  world_column(0, 0, 1, 2, 3, 4, 5);
                                  world_column(1, 6, 7, 8, 9, 10, 11);
                                  world_map(2, 11, 6);
                                  camera_init(2, 11, 4);
                                  i16 cameraY = 8;
                                  camera_set_position(0, cameraY);
                                  camera_apply();
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x3E, 0x0B, 0xEA, 0xEB, 0xC0, 0x3E, 0x0F, 0xEA, 0xEC, 0xC0]), "camera_init should seed top and bottom background row cursors.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x00, 0xEA, 0xED, 0xC0, 0x3E, 0x04, 0xEA, 0xEE, 0xC0]), "camera_init should seed top and bottom source row cursors.");
        Assert.True(ContainsSequence(rom, [0xFA, 0xEE, 0xC0, 0xFE, 0x04, 0xC2]), "downward row streaming should select the current bottom source row.");
        Assert.True(ContainsSequence(rom, [0xFA, 0xE5, 0xC0, 0xC6, 0x01, 0xFE, 0x20]), "downward row streaming should fill the visible row from the current background-left column.");
        Assert.True(ContainsSequence(rom, [0xFA, 0xEC, 0xC0, 0xFE, 0x08, 0xDA]), "downward row streaming should compute the target background row address from the bottom row cursor.");
        Assert.True(ContainsSequence(rom, [0xFA, 0xEC, 0xC0, 0xC6, 0x01, 0xEA, 0xEC, 0xC0]), "downward row streaming should advance the bottom background row cursor.");
        Assert.True(ContainsSequence(rom, [0xFA, 0xEE, 0xC0, 0xC6, 0x01, 0xEA, 0xEE, 0xC0]), "downward row streaming should advance the bottom source row cursor.");
    }

    [Fact]
    public void Camera_set_position_streams_top_row_when_y_crosses_tile_up()
    {
        const string source = """
                              void main() {
                                  video_init();
                                  world_column(0, 0, 1, 2, 3, 4, 5);
                                  world_column(1, 6, 7, 8, 9, 10, 11);
                                  world_map(2, 11, 6);
                                  camera_init(2, 11, 4);
                                  i16 cameraY = 255;
                                  camera_set_position(0, cameraY);
                                  camera_apply();
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0xFA, 0xEB, 0xC0, 0xD6, 0x01, 0xEA, 0xEB, 0xC0]), "upward row streaming should move the top background row cursor before streaming.");
        Assert.True(ContainsSequence(rom, [0xFA, 0xED, 0xC0, 0xD6, 0x01, 0xEA, 0xED, 0xC0]), "upward row streaming should move the top source row cursor before streaming.");
        Assert.True(ContainsSequence(rom, [0xFA, 0xED, 0xC0, 0xFE, 0x05, 0xC2]), "upward row streaming should select the wrapped top source row.");
        Assert.True(ContainsSequence(rom, [0xFA, 0xEB, 0xC0, 0xFE, 0x08, 0xDA]), "upward row streaming should compute the target background row address from the top row cursor.");
        Assert.True(ContainsSequence(rom, [0xFA, 0xE5, 0xC0, 0xC6, 0x01, 0xFE, 0x20]), "upward row streaming should fill the visible row from the current background-left column.");
    }

    [Fact]
    public void Compiles_camera_tile_column_at_to_map_width_wrapped_source_column()
    {
        const string source = """
                              void main() {
                                  video_init();
                                  map_column(0, 0, 0, 4, 5);
                                  map_column(1, 0, 0, 4, 5);
                                  map_column(2, 0, 0, 4, 5);
                                  map_column(3, 0, 0, 4, 5);
                                  map_column(4, 0, 0, 4, 5);
                                  map_column(5, 0, 0, 4, 5);
                                  map_column(6, 0, 0, 4, 5);
                                  map_column(7, 0, 0, 4, 5);
                                  map_column(8, 0, 0, 4, 5);
                                  map_column(9, 0, 0, 4, 5);
                                  map_column(10, 0, 0, 4, 5);
                                  map_column(11, 0, 0, 4, 5);
                                  map_column(12, 0, 0, 4, 5);
                                  map_column(13, 0, 0, 4, 5);
                                  map_column(14, 0, 0, 4, 5);
                                  map_column(15, 0, 0, 4, 5);
                                  camera_init(16, 11, 4);
                                  i16 tile = 0;
                                  while (true) {
                                      video_wait_vblank();
                                      tile = map_tile_at(camera_tile_column_at(19), 2);
                                      camera_move_right();
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x3E, 0x13, 0x47, 0xFA, 0xE3, 0xC0, 0x80]), "camera_tile_column_at should add a screen tile column to the camera's source-left column.");
        Assert.True(ContainsSequence(rom, [0xFE, 0x10, 0xDA]), "camera_tile_column_at should branch when the source column is already inside the configured map width.");
        Assert.True(ContainsSequence(rom, [0xD6, 0x10]), "camera_tile_column_at should wrap columns by subtracting the configured map width.");
    }

    [Fact]
    public void Compiles_camera_span_tile_helpers_across_sprite_logical_width()
    {
        var baseDirectory = WriteSpritePng(
            "player-wide.gb.png",
            33,
            16,
            Rows(33, 16, new string('1', 33)));

        const string source = """
                              void main() {
                                  video_init();
                                  sprite_asset(mario_player, "player-wide.gb.png", 33, 16);
                                  map_column(0, 0, 0, 4, 5);
                                  map_column(1, 0, 0, 4, 5);
                                  map_column(2, 0, 0, 4, 5);
                                  map_column(3, 0, 0, 4, 5);
                                  map_column(4, 0, 0, 4, 5);
                                  map_column(5, 0, 0, 4, 5);
                                  map_column(6, 0, 0, 4, 5);
                                  map_column(7, 0, 0, 4, 5);
                                  map_column(8, 0, 0, 4, 5);
                                  map_column(9, 0, 0, 4, 5);
                                  map_column(10, 0, 0, 4, 5);
                                  map_column(11, 0, 0, 4, 5);
                                  map_column(12, 0, 0, 4, 5);
                                  map_column(13, 0, 0, 3, 5);
                                  map_column(14, 0, 0, 4, 5);
                                  map_column(15, 0, 0, 4, 5);
                                  camera_init(16, 11, 4);
                                  i16 logicalWidth = 0;
                                  i16 footTile = 0;
                                  i16 fail = 0;
                                  while (true) {
                                      video_wait_vblank();
                                      logicalWidth = sprite_width(mario_player);
                                      footTile = camera_span_tile_at(72, sprite_width(mario_player), 2);
                                      fail = camera_span_has_tile(72, sprite_width(mario_player), 2, 3);
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source, baseDirectory);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x3E, 0x21]), "sprite_width should compile to the sprite asset's logical width.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x09, 0x47, 0xFA, 0xE3, 0xC0, 0x80]), "Span collision should check the first tile column covered by screen X.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x0D, 0x47, 0xFA, 0xE3, 0xC0, 0x80]), "Span collision should check the last tile column covered by a 33 px logical sprite.");
        Assert.True(ContainsSequence(rom, [0xFE, 0x03, 0xCA]), "camera_span_has_tile should compare each covered source tile against the requested tile id.");
    }

    [Fact]
    public void Video_wait_vblank_waits_for_the_next_vblank_edge()
    {
        const string source = """
                              void main() {
                                  video_init();
                                  while (true) {
                                      video_wait_vblank();
                                      scroll_set(1, 0);
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0xF0, 0x44, 0xFE, 0x90, 0x30]), "ROM should first wait until the previous VBlank has ended.");
        Assert.True(ContainsSequence(rom, [0xF0, 0x44, 0xFE, 0x90, 0x38]), "ROM should then wait until the next VBlank begins.");
    }

    [Fact]
    public void Compiles_tilemap_fill_column_to_runtime_vram_writes()
    {
        const string source = """
                              void main() {
                                  video_init();
                                  i16 column = 20;
                                  while (true) {
                                      video_wait_vblank();
                                      tilemap_fill_column(column, 13, 2, 4);
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x3E, 0x04, 0x47]), "ROM should preserve the tile id in B.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xC6, 0xA0, 0x6F, 0x26, 0x99, 0x78, 0x77]), "ROM should write row 13 at $99A0 + column.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xC6, 0xC0, 0x6F, 0x26, 0x99, 0x78, 0x77]), "ROM should write row 14 at $99C0 + column.");
    }

    [Fact]
    public void Compiles_long_runtime_loop_with_column_streaming()
    {
        const string source = """
                              void main() {
                                  video_init();
                                  i16 camera = 0;
                                  i16 fine = 0;
                                  i16 streamColumn = 20;
                                  i16 marker = 0;
                                  i16 frame = 0;
                                  while (true) {
                                      video_wait_vblank();
                                      scroll_set(camera, 0);
                                      sprite_set(0, 72, 80, 6 + frame, 0);
                                      sprite_set(1, 80, 80, 8 + frame, 0);
                                      camera = camera + 1;
                                      fine = fine + 1;
                                      frame = frame + 4;
                                      if (fine == 8) {
                                          fine = 0;
                                          tilemap_fill_column(streamColumn, 11, 2, 0);
                                          tilemap_fill_column(streamColumn, 13, 1, 4);
                                          tilemap_fill_column(streamColumn, 14, 1, 5);
                                          marker = marker + 1;
                                          if (marker == 4) {
                                              marker = 0;
                                              tilemap_fill_column(streamColumn, 12, 1, 5);
                                          }
                                          streamColumn = streamColumn + 1;
                                          if (streamColumn == 32) {
                                              streamColumn = 0;
                                          }
                                      }
                                      if (frame == 8) {
                                          frame = 0;
                                      }
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.Contains(rom.Skip(0x0150), b => b == 0xC3);
    }

    [Fact]
    public void Compiles_map_columns_to_rom_data_and_streams_them_to_vram()
    {
        const string source = """
                              void main() {
                                  video_init();
                                  map_column(0, 1, 2, 3, 4);
                                  map_column(1, 5, 6, 7, 8);
                                  i16 targetColumn = 20;
                                  i16 mapColumn = 0;
                                  while (true) {
                                      video_wait_vblank();
                                      map_stream_column(targetColumn, mapColumn, 11, 4);
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x01, 0x05]), "ROM should contain map row 0 data.");
        Assert.True(ContainsSequence(rom, [0x02, 0x06]), "ROM should contain map row 1 data.");
        Assert.True(ContainsSequence(rom, [0x03, 0x07]), "ROM should contain map row 2 data.");
        Assert.True(ContainsSequence(rom, [0x04, 0x08]), "ROM should contain map row 3 data.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x01, 0xC0, 0x5F, 0x16, 0x00, 0x21]), "ROM should load the source map column into DE and a row-table address into HL.");
        Assert.True(ContainsSequence(rom, [0x19, 0x7E, 0x47]), "ROM should read a tile from the map row table and preserve it in B.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xC6, 0x60, 0x6F, 0x26, 0x99, 0x78, 0x77]), "ROM should stream row 11 into the target background column.");
    }

    [Fact]
    public void Compiles_map_tile_lookup_as_a_runtime_expression()
    {
        const string source = """
                              void main() {
                                  video_init();
                                  map_column(0, 1, 0);
                                  map_column(1, 0, 2);
                                  i16 column = 1;
                                  i16 grounded = 0;
                                  if (map_tile_at(column, 1) != 0) {
                                      grounded = 1;
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0x5F, 0x16, 0x00, 0x21]), "ROM should load the runtime source map column and the selected row table address.");
        Assert.True(ContainsSequence(rom, [0x19, 0x7E, 0xFE, 0x00]), "ROM should read the tile id into A and compare it with zero.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x01, 0xEA, 0x01, 0xC0]), "ROM should execute the branch body when the tile is non-zero.");
    }

    [Fact]
    public void Compiles_relational_condition_against_a_constant()
    {
        const string source = """
                              void main() {
                                  video_init();
                                  i16 y = 80;
                                  i16 grounded = 0;
                                  if (y >= 78) {
                                      grounded = 1;
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xFE, 0x4E, 0xDA]), "ROM should compare y with 78 and jump over the branch when y is below the threshold.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x01, 0xEA, 0x01, 0xC0]), "ROM should execute the branch body when the relation is true.");
    }

    [Fact]
    public void Compiles_addition_between_runtime_locals()
    {
        const string source = """
                              void main() {
                                  video_init();
                                  i16 y = 40;
                                  i16 velocity = 3;
                                  y = y + velocity;
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0xFA, 0x01, 0xC0, 0x47, 0xFA, 0x00, 0xC0, 0x80, 0xEA, 0x00, 0xC0]), "ROM should add the two byte-backed locals and store the result.");
    }

    [Fact]
    public void Compiles_struct_member_access_as_adjacent_wram_fields()
    {
        const string source = """
                              struct Vec2 {
                                  i16 x;
                                  i16 y;
                              }

                              void main() {
                                  video_init();
                                  Vec2 position;
                                  position.x = 40;
                                  position.y = 3;
                                  position.x = position.x + position.y;
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x3E, 0x28, 0xEA, 0x00, 0xC0]), "ROM should store position.x at the first field address.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x03, 0xEA, 0x01, 0xC0]), "ROM should store position.y at the next field address.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x01, 0xC0, 0x47, 0xFA, 0x00, 0xC0, 0x80, 0xEA, 0x00, 0xC0]), "ROM should use direct loads/stores for member arithmetic with no helper call.");
    }

    [Fact]
    public void Compiles_struct_initializer_like_explicit_member_assignments()
    {
        const string explicitSource = """
                                      struct Vec2 {
                                          u8 x;
                                          u8 y;
                                      }

                                      void main() {
                                          video_init();
                                          u8 seed = 4;
                                          Vec2 position;
                                          position.x = 2;
                                          position.y = seed + 1;
                                      }
                                      """;

        const string initializerSource = """
                                         struct Vec2 {
                                             u8 x;
                                             u8 y;
                                         }

                                         void main() {
                                             video_init();
                                             u8 seed = 4;
                                             Vec2 position = { y: seed + 1, x: 2 };
                                         }
                                         """;

        Assert.Equal(GameBoyRomCompiler.CompileSource(explicitSource), GameBoyRomCompiler.CompileSource(initializerSource));
    }

    [Fact]
    public void Compiles_struct_initializer_shorthand_like_explicit_member_assignments()
    {
        const string explicitSource = """
                                      struct Vec2 {
                                          u8 x;
                                          u8 y;
                                      }

                                      void main() {
                                          video_init();
                                          u8 x = 2;
                                          u8 y = 4;
                                          Vec2 position;
                                          position.x = x;
                                          position.y = y + 1;
                                      }
                                      """;

        const string initializerSource = """
                                         struct Vec2 {
                                             u8 x;
                                             u8 y;
                                         }

                                         void main() {
                                             video_init();
                                             u8 x = 2;
                                             u8 y = 4;
                                             Vec2 position = { x, y: y + 1 };
                                         }
                                         """;

        Assert.Equal(GameBoyRomCompiler.CompileSource(explicitSource), GameBoyRomCompiler.CompileSource(initializerSource));
    }

    [Fact]
    public void Compiles_type_aliases_as_underlying_types_without_runtime_cost()
    {
        const string source = """
                              type ActorIndex = u8;

                              struct Vec2 {
                                  u8 x;
                                  u8 y;
                              }

                              type Position = Vec2;

                              void main() {
                                  video_init();
                                  ActorIndex actor = 7;
                                  Position position;
                                  position.x = actor;
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x3E, 0x07, 0xEA, 0x00, 0xC0]), "Alias ActorIndex should compile as the first byte-backed local.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x00, 0xEA, 0x01, 0xC0]), "Alias Position should compile as a struct field at the next local address.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x00, 0xEA, 0x02, 0xC0]), "Alias Position should preserve all struct fields with no alias storage.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xEA, 0x01, 0xC0]), "Alias assignments should lower to direct local load/store.");
    }

    [Fact]
    public void Compiles_const_identifiers_as_immediates_without_local_storage()
    {
        const string source = """
                              const u8 StartX = 40;
                              const u8 Velocity = StartX - 37;

                              void main() {
                                  video_init();
                                  i16 x = StartX;
                                  i16 velocity = Velocity;
                                  x = x + velocity;
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x3E, 0x28, 0xEA, 0x00, 0xC0]), "Const StartX should compile as an immediate store to the first local.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x03, 0xEA, 0x01, 0xC0]), "Const Velocity should compile as an immediate store to the second local, with no const storage slot.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x01, 0xC0, 0x47, 0xFA, 0x00, 0xC0, 0x80, 0xEA, 0x00, 0xC0]), "Const declarations should not shift runtime local addresses.");
    }

    [Fact]
    public void Compiles_local_const_identifiers_as_immediates_without_local_storage()
    {
        const string source = """
                              void main() {
                                  video_init();
                                  const u8 StartX = 40;
                                  const u8 Velocity = StartX + 1;
                                  i16 x = Velocity;
                                  i16 y = 1;
                                  y = x;
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x3E, 0x29, 0xEA, 0x00, 0xC0]), "Local const Velocity should compile its derived value as an immediate store to the first local.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x01, 0xEA, 0x01, 0xC0]), "Local const declarations should not reserve WRAM before the second local.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xEA, 0x01, 0xC0]), "Local const declarations should not shift runtime local addresses.");
    }

    [Fact]
    public void Compiles_const_conditional_expressions_as_immediates_without_runtime_branch()
    {
        const string source = """
                              const UseFast = true;
                              const Speed = UseFast ? 2 : 0;

                              void main() {
                                  video_init();
                                  u8 speed = Speed;
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x3E, 0x02, 0xEA, 0x00, 0xC0]), "Const conditional expression should fold to one immediate store with no runtime conditional.");
    }

    [Fact]
    public void Compiles_hex_binary_and_separated_literals_like_decimal_immediates()
    {
        const string decimalSource = """
                                     const Mask = 160;
                                     const Tile = 42;

                                     void main() {
                                         video_init();
                                         u8 flags = Mask | 15;
                                         u8 tile = Tile;
                                         u16 distance = 128;
                                     }
                                     """;

        const string literalSource = """
                                     const Mask = 0b1010_0000;
                                     const Tile = 0x2A;

                                     void main() {
                                         video_init();
                                         u8 flags = Mask | 0x0F;
                                         u8 tile = Tile;
                                         u16 distance = 1_28u16;
                                     }
                                     """;

        Assert.Equal(GameBoyRomCompiler.CompileSource(decimalSource), GameBoyRomCompiler.CompileSource(literalSource));
    }

    [Fact]
    public void Compiles_sizeof_type_as_immediate_without_runtime_code()
    {
        const string source = """
                              struct Actor {
                                  u8 x;
                                  u16 y;
                                  bool active;
                              }

                              void main() {
                                  video_init();
                                  u8 actorSize = sizeof(Actor);
                                  u8 pointerSize = sizeof(ptr<u8>);
                                  pointerSize = actorSize;
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x3E, 0x04, 0xEA, 0x00, 0xC0]), "sizeof(Actor) should compile as the struct byte size immediate.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x02, 0xEA, 0x01, 0xC0]), "sizeof(ptr<u8>) should compile as the pointer byte size immediate.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xEA, 0x01, 0xC0]), "sizeof expressions should not reserve storage or emit helper code.");
    }

    [Fact]
    public void Compiles_offsetof_field_as_immediate_without_runtime_code()
    {
        const string source = """
                              struct Actor {
                                  u8 x;
                                  u16 y;
                                  bool active;
                              }

                              void main() {
                                  video_init();
                                  u8 yOffset = offsetof(Actor, y);
                                  u8 activeOffset = offsetof(Actor, active);
                                  activeOffset = yOffset;
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x3E, 0x01, 0xEA, 0x00, 0xC0]), "offsetof(Actor, y) should compile as the field byte offset immediate.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x03, 0xEA, 0x01, 0xC0]), "offsetof(Actor, active) should compile as the field byte offset immediate.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xEA, 0x01, 0xC0]), "offsetof expressions should not reserve storage or emit helper code.");
    }

    [Fact]
    public void Compiles_fixed_size_array_constant_indices_as_adjacent_wram_bytes()
    {
        const string source = """
                              const u8 Count = 4;
                              const u8 First = 0;
                              const u8 Second = 1;

                              void main() {
                                  u8 values[Count];
                                  values[First] = 40;
                                  values[Second] = values[First];
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x3E, 0x28, 0xEA, 0x00, 0xC0]), "Array index 0 should store to the first WRAM byte.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xEA, 0x01, 0xC0]), "Array index 1 should load from the adjacent WRAM byte with direct addressing.");
    }

    [Fact]
    public void Compiles_fixed_size_array_initializer_like_explicit_assignments()
    {
        const string explicitSource = """
                                      void main() {
                                          video_init();
                                          u8 seed = 3;
                                          u8 values[4];
                                          values[0] = 1;
                                          values[1] = seed;
                                          values[2] = seed + 1;
                                          u8 copy = values[3];
                                      }
                                      """;

        const string initializerSource = """
                                         void main() {
                                             video_init();
                                             u8 seed = 3;
                                             u8 values[4] = [1, seed, seed + 1];
                                             u8 copy = values[3];
                                         }
                                         """;

        Assert.Equal(GameBoyRomCompiler.CompileSource(explicitSource), GameBoyRomCompiler.CompileSource(initializerSource));
    }

    [Fact]
    public void Compiles_array_initializer_with_inferred_length_like_explicit_length()
    {
        const string explicitLengthSource = """
                                            void main() {
                                                video_init();
                                                u8 seed = 3;
                                                u8 values[3] = [1, seed, seed + 1];
                                                u8 size = countof(values);
                                            }
                                            """;

        const string inferredLengthSource = """
                                            void main() {
                                                video_init();
                                                u8 seed = 3;
                                                u8 values[] = [1, seed, seed + 1];
                                                u8 size = countof(values);
                                            }
                                            """;

        Assert.Equal(GameBoyRomCompiler.CompileSource(explicitLengthSource), GameBoyRomCompiler.CompileSource(inferredLengthSource));
    }

    [Fact]
    public void Compiles_countof_fixed_size_array_as_immediate_without_runtime_code()
    {
        const string source = """
                              void main() {
                                  video_init();
                                  u8 values[4];
                                  u8 size = countof(values);
                                  values[0] = size;
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x3E, 0x04, 0xEA, 0x04, 0xC0]), "countof(values) should compile as an immediate store after the four array bytes.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x04, 0xC0, 0xEA, 0x00, 0xC0]), "countof should not emit a helper; subsequent array assignment should remain direct.");
    }

    [Fact]
    public void Compiles_enum_members_as_immediates_without_local_storage()
    {
        const string source = """
                              enum Tile {
                                  Empty,
                                  Brick = 40,
                                  Bonus
                              }

                              void main() {
                                  Tile tile = Tile.Brick;
                                  Tile bonus = Tile.Bonus;
                                  bonus = tile;
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x3E, 0x28, 0xEA, 0x00, 0xC0]), "Enum Brick should compile as an immediate store to the first local.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x29, 0xEA, 0x01, 0xC0]), "Implicit enum Bonus should compile as the next immediate value with no enum storage slot.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xEA, 0x01, 0xC0]), "Enum declarations should not shift runtime local addresses.");
    }

    [Fact]
    public void Compiles_compound_assignment_as_direct_lvalue_arithmetic()
    {
        const string source = """
                              void main() {
                                  u8 x = 1;
                                  u8 y = 2;
                                  x += y;
                                  x -= 1;
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0xFA, 0x01, 0xC0, 0x47, 0xFA, 0x00, 0xC0, 0x80, 0xEA, 0x00, 0xC0]), "x += y should lower to the same direct load/add/store sequence as x = x + y.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xD6, 0x01, 0xEA, 0x00, 0xC0]), "x -= 1 should lower to direct subtract/store without a helper call.");
    }

    [Fact]
    public void Compiles_for_loop_to_direct_branching_without_helper_calls()
    {
        const string source = """
                              void main() {
                                  u8 x = 0;
                                  for (u8 i = 0; i < 3; i += 1) {
                                      x += i;
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0xFA, 0x01, 0xC0, 0xFE, 0x03, 0xD2]), "for condition should compare the loop local and jump out when i >= 3.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x01, 0xC0, 0x47, 0xFA, 0x00, 0xC0, 0x80, 0xEA, 0x00, 0xC0]), "for body should use direct x += i arithmetic.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x01, 0xC0, 0xC6, 0x01, 0xEA, 0x01, 0xC0]), "for increment should use direct i += 1 arithmetic.");
    }

    [Fact]
    public void Compiles_increment_decrement_and_for_postfix_increment_as_direct_arithmetic()
    {
        const string source = """
                              void main() {
                                  u8 x = 0;
                                  x++;
                                  x--;
                                  for (u8 i = 0; i < 3; i++) {
                                      x += i;
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xC6, 0x01, 0xEA, 0x00, 0xC0]), "x++ should lower to direct x += 1 arithmetic.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xD6, 0x01, 0xEA, 0x00, 0xC0]), "x-- should lower to direct x -= 1 arithmetic.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x01, 0xC0, 0xC6, 0x01, 0xEA, 0x01, 0xC0]), "for i++ should lower to direct i += 1 arithmetic.");
    }

    [Fact]
    public void Compiles_range_for_loop_as_direct_counted_loop()
    {
        const string source = """
                              void main() {
                                  u8 x = 0;
                                  for (u8 i in 0..3) {
                                      x += i;
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x3E, 0x00, 0xEA, 0x01, 0xC0]), "range-for should initialize the loop local once from the range start.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x01, 0xC0, 0xFE, 0x03, 0xD2]), "range-for should compare i with the exclusive upper bound and branch out when i >= end.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x01, 0xC0, 0x47, 0xFA, 0x00, 0xC0, 0x80, 0xEA, 0x00, 0xC0]), "range-for body should use direct x += i arithmetic.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x01, 0xC0, 0xC6, 0x01, 0xEA, 0x01, 0xC0]), "range-for should increment i with direct i++ arithmetic.");
    }

    [Fact]
    public void Compiles_runtime_indexed_array_access_without_helper_calls()
    {
        const string source = """
                              void main() {
                                  u8 values[4];
                                  u8 i = 1;
                                  values[i] += 1;
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0xFA, 0x04, 0xC0, 0x21, 0x00, 0xC0, 0x5F, 0x16, 0x00, 0x19]), "runtime array indexing should compute HL from the array base and the byte index without a helper call.");
        Assert.True(ContainsSequence(rom, [0x7E, 0xC6, 0x01, 0x77]), "values[i] += 1 should load, add, and store through HL without a helper call.");
    }

    [Fact]
    public void Compiles_bare_loop_break_and_continue_as_direct_jumps()
    {
        const string source = """
                              void main() {
                                  u8 x = 0;
                                  loop {
                                      x++;
                                      if (x == 1) {
                                          continue;
                                      }
                                      if (x == 3) {
                                          break;
                                      }
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        var loopStart = IndexOfSequence(rom, [0xFA, 0x00, 0xC0, 0xC6, 0x01, 0xEA, 0x00, 0xC0]);
        var continueCompare = IndexOfSequence(rom, [0xFA, 0x00, 0xC0, 0xFE, 0x01, 0xC2]);
        var breakCompare = IndexOfSequence(rom, [0xFA, 0x00, 0xC0, 0xFE, 0x03, 0xC2]);

        Assert.Equal(32768, rom.Length);
        Assert.True(loopStart >= 0, "loop body should start with direct x++ arithmetic.");
        Assert.True(continueCompare >= 0, "continue guard should compare x with 1.");
        Assert.True(breakCompare >= 0, "break guard should compare x with 3.");
        Assert.Equal(loopStart, ReadLittleEndian16(rom, continueCompare + 9));
        Assert.True(ReadLittleEndian16(rom, breakCompare + 9) > breakCompare, "break should jump beyond the loop body.");
    }

    [Fact]
    public void Compiles_break_and_continue_as_direct_for_loop_jumps()
    {
        const string source = """
                              void main() {
                                  u8 x = 0;
                                  for (u8 i = 0; i < 4; i += 1) {
                                      if (i == 1) {
                                          continue;
                                      }
                                      if (i == 3) {
                                          break;
                                      }
                                      x += 1;
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        var continueCompare = IndexOfSequence(rom, [0xFA, 0x01, 0xC0, 0xFE, 0x01, 0xC2]);
        var breakCompare = IndexOfSequence(rom, [0xFA, 0x01, 0xC0, 0xFE, 0x03, 0xC2]);
        var increment = IndexOfSequence(rom, [0xFA, 0x01, 0xC0, 0xC6, 0x01, 0xEA, 0x01, 0xC0]);

        Assert.Equal(32768, rom.Length);
        Assert.True(continueCompare >= 0, "continue guard should compare i with 1.");
        Assert.True(breakCompare >= 0, "break guard should compare i with 3.");
        Assert.True(increment >= 0, "for increment should still be emitted as direct i += 1 arithmetic.");

        var continueJumpTarget = ReadLittleEndian16(rom, continueCompare + 9);
        Assert.Equal(increment, continueJumpTarget);

        var breakJumpTarget = ReadLittleEndian16(rom, breakCompare + 9);
        Assert.True(breakJumpTarget > increment + 8, "break should jump beyond the increment and final loop jump.");
    }

    [Fact]
    public void Compiles_do_while_continue_to_condition_check()
    {
        const string source = """
                              void main() {
                                  u8 x = 0;
                                  do {
                                      x++;
                                      if (x == 1) {
                                          continue;
                                      }
                                      x += 2;
                                  } while (x < 3);
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        var continueCompare = IndexOfSequence(rom, [0xFA, 0x00, 0xC0, 0xFE, 0x01, 0xC2]);
        var conditionCompare = IndexOfSequence(rom, [0xFA, 0x00, 0xC0, 0xFE, 0x03, 0xD2]);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xC6, 0x01, 0xEA, 0x00, 0xC0]), "do body should emit x++ as direct arithmetic before the first condition check.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xC6, 0x02, 0xEA, 0x00, 0xC0]), "do body should emit direct arithmetic after the continue guard.");
        Assert.True(continueCompare >= 0, "continue guard should compare x with 1.");
        Assert.True(conditionCompare >= 0, "do-while condition should compare x with 3 at the bottom of the loop.");

        var continueJumpTarget = ReadLittleEndian16(rom, continueCompare + 9);
        Assert.Equal(conditionCompare, continueJumpTarget);
    }

    [Fact]
    public void Compiles_logical_conditions_with_short_circuit_branches()
    {
        const string source = """
                              void main() {
                                  u8 x = 0;
                                  u8 y = 1;
                                  u8 z = 0;
                                  if (x != 0 && y != 0) {
                                      z += 1;
                                  }
                                  if (x != 0 || y != 0) {
                                      z += 2;
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        var andLeftCompare = IndexOfSequence(rom, [0xFA, 0x00, 0xC0, 0xFE, 0x00, 0xCA]);
        var andRightCompare = IndexOfSequence(rom, [0xFA, 0x01, 0xC0, 0xFE, 0x00, 0xCA]);
        var orLeftCompare = IndexOfSequence(rom, [0xFA, 0x00, 0xC0, 0xFE, 0x00, 0xC2]);
        var orBody = IndexOfSequence(rom, [0xFA, 0x02, 0xC0, 0xC6, 0x02, 0xEA, 0x02, 0xC0]);

        Assert.Equal(32768, rom.Length);
        Assert.True(andLeftCompare >= 0, "&& should test the left condition and jump false before touching the right side.");
        Assert.True(andRightCompare > andLeftCompare, "&& should evaluate the right condition only after the left condition succeeds.");
        Assert.True(orLeftCompare >= 0, "|| should test the left condition with a direct true branch.");
        Assert.True(orBody > orLeftCompare, "|| body should be emitted after the left condition branch.");
        Assert.Equal(orBody, ReadLittleEndian16(rom, orLeftCompare + 6));
    }

    [Fact]
    public void Compiles_range_membership_condition_like_explicit_bounds_checks()
    {
        const string explicitSource = """
                                      void main() {
                                          u8 tile = 2;
                                          u8 hit = 0;
                                          if (tile >= 1 && tile < 4) {
                                              hit = 1;
                                          }
                                      }
                                      """;

        const string membershipSource = """
                                        void main() {
                                            u8 tile = 2;
                                            u8 hit = 0;
                                            if (tile in 1..4) {
                                                hit = 1;
                                            }
                                        }
                                        """;

        Assert.Equal(GameBoyRomCompiler.CompileSource(explicitSource), GameBoyRomCompiler.CompileSource(membershipSource));
    }

    [Fact]
    public void Compiles_unary_not_condition_by_inverting_the_branch()
    {
        const string source = """
                              void main() {
                                  u8 x = 0;
                                  if (!(x != 0)) {
                                      x += 1;
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xFE, 0x00, 0xC2]), "! should invert x != 0 into a false jump when the inner condition is true.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xC6, 0x01, 0xEA, 0x00, 0xC0]), "then body should remain direct x += 1 arithmetic.");
    }

    [Fact]
    public void Compiles_else_if_chain_as_nested_direct_branches()
    {
        const string source = """
                              void main() {
                                  u8 x = 0;
                                  if (x == 0) {
                                      x += 1;
                                  } else if (x == 1) {
                                      x += 2;
                                  } else {
                                      x += 3;
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xFE, 0x00, 0xC2]), "first if should compare x with 0 and jump to the else branch when false.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xFE, 0x01, 0xC2]), "else-if should compile as a nested if compare.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xC6, 0x01, 0xEA, 0x00, 0xC0]), "first body should remain direct x += 1 arithmetic.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xC6, 0x02, 0xEA, 0x00, 0xC0]), "else-if body should remain direct x += 2 arithmetic.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xC6, 0x03, 0xEA, 0x00, 0xC0]), "else body should remain direct x += 3 arithmetic.");
    }

    [Fact]
    public void Compiles_switch_as_direct_compare_branches_without_runtime_helper()
    {
        const string source = """
                              void main() {
                                  video_init();
                                  u8 state = 1;
                                  u8 value;
                                  switch (state) {
                                      case 0 {
                                          value = 10;
                                      }
                                      case 1 {
                                          value = 20;
                                      }
                                      default {
                                          value = 30;
                                      }
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x3E, 0x01, 0xEA, 0x00, 0xC0]), "Switch subject should stay a normal byte-backed local.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xFE, 0x00]), "First switch case should compare the subject directly to the case literal.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xFE, 0x01]), "Second switch case should compare the subject directly to the case literal.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x0A, 0xEA, 0x01, 0xC0]), "First switch case should lower to the direct branch body.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x14, 0xEA, 0x01, 0xC0]), "Second switch case should lower to the direct branch body.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x1E, 0xEA, 0x01, 0xC0]), "Default switch case should lower to the direct fallback body.");
    }

    [Fact]
    public void Compiles_switch_case_with_multiple_values_as_direct_compares()
    {
        const string source = """
                              void main() {
                                  u8 state = 1;
                                  u8 value;
                                  switch (state) {
                                      case 0, 1 {
                                          value = 10;
                                      }
                                      default {
                                          value = 30;
                                      }
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xFE, 0x00]), "Multi-value switch case should compare the subject with the first literal.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xFE, 0x01]), "Multi-value switch case should compare the subject with the second literal.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x0A, 0xEA, 0x01, 0xC0]), "Multi-value switch case should share one direct branch body.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x1E, 0xEA, 0x01, 0xC0]), "Default switch case should remain a direct fallback body.");
    }

    [Fact]
    public void Compiles_switch_case_with_half_open_range_as_direct_bounds_checks()
    {
        const string source = """
                              void main() {
                                  u8 state = 2;
                                  u8 value;
                                  switch (state) {
                                      case 1..4 {
                                          value = 10;
                                      }
                                      default {
                                          value = 30;
                                      }
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xFE, 0x01, 0xDA]), "Range switch case should compare the subject with the inclusive lower bound and jump out when subject < start.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xFE, 0x04, 0xD2]), "Range switch case should compare the subject with the exclusive upper bound and jump out when subject >= end.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x0A, 0xEA, 0x01, 0xC0]), "Range switch case should share one direct branch body.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x1E, 0xEA, 0x01, 0xC0]), "Default switch case should remain a direct fallback body.");
    }

    [Fact]
    public void Compiles_untyped_constants_as_folded_literals()
    {
        const string source = """
                              const BaseValue = 4;
                              void main() {
                                  u8 value = BaseValue;
                                  const NextValue = BaseValue + 1;
                                  value = NextValue;
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x3E, 0x04, 0xEA, 0x00, 0xC0]), "Untyped top-level const should fold to the direct literal initializer.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x05, 0xEA, 0x00, 0xC0]), "Untyped block-local const should fold to the direct literal assignment.");
    }

    [Fact]
    public void Compiles_bitwise_compound_assignment_as_direct_mask_operations()
    {
        const string source = """
                              const Solid = 1;
                              const Hazard = 2;
                              const Toggle = 4;
                              void main() {
                                  u8 flags = 0;
                                  flags |= Solid;
                                  flags &= ~Hazard;
                                  flags ^= Toggle;
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xF6, 0x01, 0xEA, 0x00, 0xC0]), "flags |= Solid should lower to LD/OR immediate/store with no helper.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xE6, 0xFD, 0xEA, 0x00, 0xC0]), "flags &= ~Hazard should lower to LD/AND immediate/store with no helper.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xEE, 0x04, 0xEA, 0x00, 0xC0]), "flags ^= Toggle should lower to LD/XOR immediate/store with no helper.");
    }

    [Fact]
    public void Compiles_value_returning_user_functions_like_inline_expressions()
    {
        const string source = """
                              u8 set_flag(u8 flags, u8 mask) {
                                  return flags | mask;
                              }

                              u8 clear_flag(u8 flags, u8 mask) {
                                  return flags & ~mask;
                              }

                              void main() {
                                  u8 flags = 0;
                                  flags = set_flag(flags, 1);
                                  flags = clear_flag(flags, 2);
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xF6, 0x01, 0xEA, 0x00, 0xC0]), "set_flag(flags, 1) should inline as LD/OR immediate/store with no call ABI.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xE6, 0xFD, 0xEA, 0x00, 0xC0]), "clear_flag(flags, 2) should inline as LD/AND immediate/store with no call ABI.");
    }

    [Fact]
    public void Compiles_explicit_casts_as_zero_cost_expression_markers()
    {
        const string source = """
                              void main() {
                                  u8 flags = 0;
                                  u16 wide = 1;
                                  flags = (u8)(wide | 2);
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0xFA, 0x01, 0xC0, 0xF6, 0x02, 0xEA, 0x00, 0xC0]), "Explicit casts should disappear before Game Boy lowering and leave the direct expression sequence.");
    }

    [Fact]
    public void Compiles_logical_value_expressions_as_direct_boolean_materialization()
    {
        const string source = """
                              void main() {
                                  u8 x = 1;
                                  u8 y = 0;
                                  u8 both = x != 0 && y != 0;
                                  u8 either = x != 0 || y != 0;
                                  u8 notX = !(x != 0);
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xFE, 0x00, 0xCA]), "&& should false-branch after the left operand.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x01, 0xC0, 0xFE, 0x00, 0xCA]), "&& should false-branch after the right operand.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x00, 0xEA, 0x02, 0xC0]), "&& should materialize false as 0 in the destination byte.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xFE, 0x00, 0xC2]), "|| and ! should true-branch using direct compares.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x00, 0xEA, 0x03, 0xC0]), "|| should materialize false as 0 in the destination byte.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x00, 0xEA, 0x04, 0xC0]), "! should materialize false as 0 in the destination byte.");
    }

    [Fact]
    public void Compiles_conditional_value_expressions_as_direct_branches()
    {
        const string source = """
                              void main() {
                                  u8 moving = 1;
                                  u8 fast = 2;
                                  u8 speed = moving != 0 ? fast : 0;
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xFE, 0x00, 0xCA]), "Conditional expression should branch to the false value when the condition is false.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x01, 0xC0, 0xC3]), "Conditional expression should load the true branch value and jump over the false branch.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x00, 0xEA, 0x02, 0xC0]), "Conditional expression should load the false branch value and store one selected byte.");
    }

    [Fact]
    public void Compiles_button_pressed_as_a_runtime_expression()
    {
        const string source = """
                              void main() {
                                  video_init();
                                  i16 jumped = 0;
                                  if (button_pressed(a) != 0) {
                                      jumped = 1;
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x3E, 0x10, 0xE0, 0x00, 0xF0, 0x00, 0xF0, 0x00, 0xF0, 0x00, 0xF0, 0x00, 0x2F, 0xE6, 0x0F, 0xE6, 0x01, 0xFE, 0x00]), "ROM should settle and read the Game Boy action-button register before returning 1 when A is pressed.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x01, 0xEA, 0x00, 0xC0]), "ROM should execute the branch body when the button is pressed.");
    }

    [Fact]
    public void Compiles_tick_input_api_for_variable_jump()
    {
        const string source = """
                              void main() {
                                  video_init();
                                  i16 grounded = 1;
                                  i16 velocityY = 0;
                                  i16 jumping = 0;
                                  i16 jumpTicks = 0;
                                  while (true) {
                                      video_wait_vblank();
                                      input_poll();
                                      if (button_just_pressed(a) != 0) {
                                          if (grounded != 0) {
                                              velocityY = 252;
                                              jumping = 1;
                                          }
                                      }
                                      if (jumping != 0) {
                                          jumpTicks = button_hold_ticks(a);
                                          if (button_down(a) != 0) {
                                              if (jumpTicks < 12) {
                                                  velocityY = velocityY - 1;
                                              }
                                          }
                                          if (button_just_released(a) != 0) {
                                              jumping = 0;
                                          }
                                      }
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0xFA, 0xF0, 0xC0, 0xEA, 0xF1, 0xC0]), "input_poll should snapshot the previous button mask before reading the current tick.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x10, 0xE0, 0x00, 0xF0, 0x00, 0xF0, 0x00, 0xF0, 0x00, 0xF0, 0x00, 0x2F, 0xE6, 0x0F, 0x47]), "input_poll should read the settled action-button group into the current tick mask.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x20, 0xE0, 0x00, 0xF0, 0x00, 0xF0, 0x00, 0xF0, 0x00, 0xF0, 0x00, 0x2F, 0xE6, 0x0F, 0xCB, 0x37, 0xB0, 0xEA, 0xF0, 0xC0]), "input_poll should read the settled direction-button group and store a combined button mask.");
        Assert.True(ContainsSequence(rom, [0xFA, 0xF2, 0xC0, 0xEA, 0x03, 0xC0]), "button_hold_ticks(a) should read the A-button hold counter into a game variable.");
        Assert.True(ContainsSequence(rom, [0xFA, 0xF0, 0xC0, 0xE6, 0x01, 0xFE, 0x00, 0xCA]), "button_down(a) and button_just_pressed(a) should test the current tick mask.");
        Assert.True(ContainsSequence(rom, [0xFA, 0xF1, 0xC0, 0xE6, 0x01, 0xFE, 0x00, 0xC2]), "button_just_pressed(a) should reject buttons that were already down in the previous tick.");
        Assert.True(ContainsSequence(rom, [0xFA, 0xF0, 0xC0, 0xE6, 0x01, 0xFE, 0x00, 0xC2]), "button_just_released(a) should require the button to be up in the current tick.");
        Assert.True(ContainsSequence(rom, [0xFA, 0xF1, 0xC0, 0xE6, 0x01, 0xFE, 0x00, 0xCA]), "button_just_released(a) should require the button to have been down in the previous tick.");
    }

    [Fact]
    public void Input_poll_settles_joypad_rows_before_latching_buttons()
    {
        const string source = """
                              void main() {
                                  video_init();
                                  while (true) {
                                      video_wait_vblank();
                                      input_poll();
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x3E, 0x10, 0xE0, 0x00, 0xF0, 0x00, 0xF0, 0x00, 0xF0, 0x00, 0xF0, 0x00, 0x2F, 0xE6, 0x0F, 0x47]), "input_poll should discard early action-row reads so DMG hardware has time to settle.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x20, 0xE0, 0x00, 0xF0, 0x00, 0xF0, 0x00, 0xF0, 0x00, 0xF0, 0x00, 0x2F, 0xE6, 0x0F, 0xCB, 0x37]), "input_poll should discard early d-pad-row reads so DMG hardware has time to settle.");
        Assert.True(ContainsSequence(rom, [0xEA, 0xF0, 0xC0, 0x3E, 0x30, 0xE0, 0x00]), "input_poll should deselect both joypad rows after latching the snapshot.");
    }

    [Fact]
    public void GameBoy_runner_drives_scroll_and_run_animation_from_dpad()
    {
        var sourcePath = RepositoryFile("samples/runner/runner.rs");
        var source = File.ReadAllText(sourcePath);

        var movementStart = source.IndexOf("inline void HandleHorizontalInput(PlayerState player, Pixel footWorldY)", StringComparison.Ordinal);
        var movementEnd = source.IndexOf("class FrameState", movementStart, StringComparison.Ordinal);
        Assert.True(movementStart >= 0);
        Assert.True(movementEnd > movementStart);

        var movementBlock = source[movementStart..movementEnd];
        var rightStart = movementBlock.IndexOf("if (button_down(right) != 0)", StringComparison.Ordinal);
        Assert.True(rightStart >= 0, "Runner should gate forward movement with the D-pad right button.");

        var leftStart = movementBlock.IndexOf("if (button_down(left) != 0)", StringComparison.Ordinal);
        Assert.True(leftStart >= 0, "Runner should gate backward movement with the D-pad left button.");

        var movementCall = source.IndexOf("view.HandleHorizontalInput(player, movementFootWorldY);", StringComparison.Ordinal);
        var animationCall = source.IndexOf("player.UpdateRunAnimation(view);", StringComparison.Ordinal);
        Assert.True(movementCall >= 0);
        Assert.True(animationCall > movementCall, "Runner should update movement before animation state.");

        Assert.Contains("type Pixel = i16;", source);
        Assert.Contains("CameraState view;", source);
        Assert.DoesNotContain("Pixel cameraX = 0;", source);

        Assert.Contains("x += 1;", movementBlock);
        Assert.Contains("moving = 1;", movementBlock);
        Assert.Contains("x -= 1;", movementBlock);
        Assert.Contains("camera.SetPosition(x, 0);", movementBlock);
        Assert.DoesNotContain("if (view.x > 0)", movementBlock);
        Assert.DoesNotContain("camera_move_right();", source);
        Assert.DoesNotContain("camera_move_left();", source);
        Assert.Contains("if (moving != 0)", movementBlock);
        Assert.Contains("animTick++;", source);
        Assert.Contains("animation.Frame(run, animTick)", source);
        Assert.DoesNotContain("i16 frame = 0;", source);
        Assert.DoesNotContain("frame = frame + 1;", source);
        Assert.DoesNotContain("if (frame == 3)", source);
        Assert.DoesNotContain("displayFrame = frame + 1;", source);
        Assert.Equal(1, CountOccurrences(source, "camera.SetPosition(x, 0);"));
        Assert.Equal(1, CountOccurrences(source, "animTick++;"));

        var rom = GameBoyRomCompiler.CompileSource(source, Path.GetDirectoryName(sourcePath));
        Assert.Equal(32768, rom.Length);
    }

    [Fact]
    public void Compiles_constant_groups_like_flat_constants()
    {
        const string flatSource = """
                                  const WorldWidth = 16;
                                  const WorldStreamY = 9;
                                  const WorldHeight = 5;
                                  const PlayerScreenX = 72;

                                  void main() {
                                      video_init();
                                      world_column(0, 1, 2, 3, 4, 5);
                                      world_flags(0, 0, 0, 1, 1, 1);
                                      world_map(WorldWidth, WorldStreamY, WorldHeight);
                                      camera_init(WorldWidth, WorldStreamY, WorldHeight);
                                      i16 cameraX = 0;
                                      i16 playerWorldX = cameraX + PlayerScreenX;
                                      camera_set_position(cameraX, 0);
                                  }
                                  """;

        const string groupedSource = """
                                     enum World { Width = 16, StreamY = 9, Height = 5 }
                                     enum Player { ScreenX = 72 }

                                     void main() {
                                         video_init();
                                         world_column(0, 1, 2, 3, 4, 5);
                                         world_flags(0, 0, 0, 1, 1, 1);
                                         world_map(World.Width, World.StreamY, World.Height);
                                         camera_init(World.Width, World.StreamY, World.Height);
                                         i16 cameraX = 0;
                                         i16 playerWorldX = cameraX + Player.ScreenX;
                                         camera_set_position(cameraX, 0);
                                     }
                                     """;

        Assert.Equal(GameBoyRomCompiler.CompileSource(flatSource), GameBoyRomCompiler.CompileSource(groupedSource));
    }

    [Fact]
    public void Compiles_pixel_struct_receiver_helpers_like_flat_state_updates()
    {
        const string flatSource = """
                                  type Pixel = i16;
                                  enum Player { StartY = 73 }

                                  void main() {
                                      video_init();
                                      Pixel playerY = 0;
                                      Pixel velocityY = 0;
                                      Pixel grounded = 0;
                                      Pixel jumping = 0;
                                      Pixel displayFrame = 0;
                                      playerY = Player.StartY;
                                      velocityY = 0;
                                      grounded = 1;
                                      displayFrame = 0;
                                      jumping = 0;
                                      velocityY += 1;
                                      playerY += velocityY;
                                  }
                                  """;

        const string receiverSource = """
                                      type Pixel = i16;
                                      enum Player { StartY = 73 }

                                      struct PlayerState {
                                          Pixel y;
                                          Pixel velocityY;
                                          Pixel grounded;
                                          Pixel jumping;
                                          Pixel displayFrame;
                                      }

                                      inline void Reset(this PlayerState player) {
                                          player.y = Player.StartY;
                                          player.velocityY = 0;
                                          player.grounded = 1;
                                          player.displayFrame = 0;
                                          player.jumping = 0;
                                      }

                                      inline void ApplyGravity(this PlayerState player) {
                                          player.velocityY += 1;
                                          player.y += player.velocityY;
                                      }

                                      void main() {
                                          video_init();
                                          PlayerState player;
                                          player.Reset();
                                          player.ApplyGravity();
                                      }
                                      """;

        Assert.Equal(GameBoyRomCompiler.CompileSource(flatSource), GameBoyRomCompiler.CompileSource(receiverSource));
    }

    [Fact]
    public void Compiles_camera_state_receiver_helper_like_flat_horizontal_input()
    {
        const string flatSource = """
                                  type Pixel = i16;

                                  void main() {
                                      video_init();
                                      world_column(0, 1, 2);
                                      world_flags(0, 0, 1);
                                      world_map(1, 10, 2);
                                      camera_init(1, 10, 2);
                                      input_poll();
                                      Pixel cameraX = 0;
                                      Pixel moving = 0;
                                      bool displayFlipX = false;
                                      moving = 0;
                                      if (button_down(right) != 0) {
                                          moving = 1;
                                          displayFlipX = false;
                                          cameraX += 1;
                                      }
                                      if (button_down(left) != 0) {
                                          moving = 1;
                                          displayFlipX = true;
                                          cameraX -= 1;
                                      }
                                      if (moving != 0) {
                                          camera_set_position(cameraX, 0);
                                      }
                                  }
                                  """;

        const string receiverSource = """
                                      type Pixel = i16;

                                      struct CameraState {
                                          Pixel x;
                                          Pixel moving;
                                      }

                                      struct PlayerState {
                                          bool displayFlipX;
                                      }

                                      inline void HandleHorizontalInput(this CameraState view, PlayerState player) {
                                          view.moving = 0;
                                          if (button_down(right) != 0) {
                                              view.moving = 1;
                                              player.displayFlipX = false;
                                              view.x += 1;
                                          }
                                          if (button_down(left) != 0) {
                                              view.moving = 1;
                                              player.displayFlipX = true;
                                              view.x -= 1;
                                          }
                                          if (view.moving != 0) {
                                              camera_set_position(view.x, 0);
                                          }
                                      }

                                      void main() {
                                          video_init();
                                          world_column(0, 1, 2);
                                          world_flags(0, 0, 1);
                                          world_map(1, 10, 2);
                                          camera_init(1, 10, 2);
                                          input_poll();
                                          CameraState view;
                                          PlayerState player;
                                          view.HandleHorizontalInput(player);
                                      }
                                      """;

        Assert.Equal(GameBoyRomCompiler.CompileSource(flatSource), GameBoyRomCompiler.CompileSource(receiverSource));
    }

    [Fact]
    public void GameBoy_runner_uses_constant_groups_and_lightweight_player_state()
    {
        var source = File.ReadAllText(RepositoryFile("samples/runner/runner.rs"));

        Assert.Contains("enum World", source);
        Assert.Contains("Width = 68", source);
        Assert.Contains("Height = 14", source);
        Assert.Contains("SignedVelocityWrap = 128", source);
        Assert.Contains("PixelWidth = 544", source);
        Assert.Contains("enum Player", source);
        Assert.Contains("ScreenX = 72", source);
        Assert.Contains("class PlayerState", source);
        Assert.Contains("inline void Reset()", source);
        Assert.Contains("inline void ApplyGravity()", source);
        Assert.Contains("PlayerState player;", source);
        Assert.Contains("player.Reset();", source);
        Assert.Contains("player.ApplyGravity();", source);
        Assert.Contains("""world.Load("maps/runner.tmj");""", source);
        Assert.Contains("load_world();", source);
        Assert.Contains("sprite.Draw(mario_player, Player.ScreenX, player.y", source);
        Assert.DoesNotContain("const WorldWidth", source);
        Assert.DoesNotContain("const PlayerScreenX", source);
        Assert.DoesNotContain("Pixel playerY =", source);
        Assert.DoesNotContain("Pixel velocityY =", source);
    }

    [Fact]
    public void GameBoy_runner_extracts_frame_loop_into_named_inline_helpers()
    {
        var source = File.ReadAllText(RepositoryFile("samples/runner/runner.rs"));

        Assert.Contains("class CameraState", source);
        Assert.Contains("class FrameState", source);
        Assert.Contains("inline void PresentFrame(PlayerState player)", source);
        Assert.Contains("inline void HandleJumpInput()", source);
        Assert.Contains("inline void HandleHorizontalInput(PlayerState player, Pixel footWorldY)", source);
        Assert.Contains("inline void ResolveSolidLanding(PlayerState player", source);
        Assert.Contains("inline void ResolveFall(PlayerState player)", source);
        Assert.Contains("inline void ResolveReset(PlayerState player)", source);
        Assert.Contains("inline void UpdateRunAnimation(CameraState view)", source);
        Assert.Contains("PresentFrame(player);", source);
        Assert.Contains("frame.Begin();", source);
        Assert.Contains("frame.ResolveSolidLanding(player, footWorldY);", source);
        Assert.Contains("frame.ResolveFall(player);", source);
        Assert.Contains("frame.ResolveReset(player);", source);
        Assert.Contains("player.HandleJumpInput();", source);
        Assert.Contains("let movementFootWorldY = player.y - Player.WorldOriginY;", source);
        Assert.Contains("view.HandleHorizontalInput(player, movementFootWorldY);", source);
        Assert.Contains("player.UpdateRunAnimation(view);", source);
        Assert.DoesNotContain("Pixel cameraX = 0;", source);
        Assert.DoesNotContain("Pixel moving = 0;", source);
        Assert.DoesNotContain("Pixel resetRequested = 0;", source);
    }

    [Fact]
    public void GameBoy_runner_uses_player_spritesheet_for_playable_scene()
    {
        var sourcePath = RepositoryFile("samples/runner/runner.rs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("""sprite.Asset(mario_player, "assets/mario-player.png", 18, 32);""", source);
        Assert.Contains("animation.Clip(run, 1, 6, 6, 6);", source);
        Assert.DoesNotContain("animation.Clip(enemy_walk", source);
        Assert.DoesNotContain("sprites_clear();", source);
        Assert.Contains("displayFrame = grounded switch", source);
        Assert.Contains("0 => 4", source);
        Assert.Contains("_ => animation.Frame(run, animTick)", source);
        Assert.Contains("0 => 0", source);
        Assert.Contains("bool displayFlipX;", source);
        Assert.Contains("player.displayFlipX = true;", source);
        Assert.Contains("player.displayFlipX = false;", source);
        Assert.DoesNotContain("displayFlags = 32;", source);
        Assert.Contains("sprite.Draw(mario_player, Player.ScreenX, player.y, player.displayFrame, player.displayFlipX, 0);", source);
        Assert.DoesNotContain("enemy_slug", source);
        Assert.Equal(1, CountOccurrences(source, "sprite.Draw("));

        var rom = GameBoyRomCompiler.CompileSource(source, Path.GetDirectoryName(sourcePath));
        Assert.Equal(32768, rom.Length);
    }

    [Fact]
    public void GameBoy_runner_sprite_asset_preserves_portable_metadata()
    {
        var sourcePath = RepositoryFile("samples/runner/runner.rs");
        var source = File.ReadAllText(sourcePath);

        var program = CompileVideoProgram(source, Path.GetDirectoryName(sourcePath));
        var asset = program.SpriteAssets["mario_player"];

        Assert.Equal("mario_player", asset.Metadata.Id);
        Assert.Equal(new Size2D(18, 32), asset.Metadata.LogicalSize);
        Assert.Equal(new Point2D(0, 0), asset.Metadata.Origin);
        Assert.Equal(new Rect2D(0, 0, 18, 32), asset.Metadata.Hitbox);
        Assert.Equal(1, asset.Metadata.PaletteSlots);

        var clip = Assert.Single(asset.Metadata.AnimationClips);
        Assert.Equal("default", clip.Name);
        Assert.Equal(0, clip.FirstFrame);
        Assert.Equal(asset.FrameCount, clip.FrameCount);
    }

    [Fact]
    public void GameBoy_runner_uses_lighter_object_palette_for_player_sprite()
    {
        var sourcePath = RepositoryFile("samples/runner/runner.rs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("palette.Background(0, 0, 1, 2, 3);", source);
        Assert.Contains("palette.Sprite(0, 0, 0, 1, 3);", source);
        Assert.DoesNotContain("palette.Set(", source);
        Assert.DoesNotContain("objectPalette.Set(", source);
        Assert.DoesNotContain("objectPalette.Set(1, 1);", source);
        Assert.DoesNotContain("objectPalette.Set(2, 2);", source);
        Assert.DoesNotContain("objectPalette.Set(1, 2);", source);
        Assert.DoesNotContain("objectPalette.Set(2, 3);", source);

        var rom = GameBoyRomCompiler.CompileSource(source, Path.GetDirectoryName(sourcePath));

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x3E, 0xD0, 0xE0, 0x48]), "Runner should map sprite tones to OBP0 as 0, 0, 1, 3.");
    }

    [Fact]
    public void GameBoy_runner_keeps_layout_readable_and_gives_hit_feedback()
    {
        var sourcePath = RepositoryFile("samples/runner/runner.rs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("Player.ScreenX", source);
        Assert.Contains("Player.WorldOriginY", source);
        Assert.Contains("CollisionProbe.LandingSearchHeight", source);
        Assert.Contains("CollisionProbe.NoTileHit", source);
        Assert.DoesNotContain("EnemyState", source);
        Assert.DoesNotContain("hitFlashTicks", source);

        var program = CompileVideoProgram(source, Path.GetDirectoryName(sourcePath));
        Assert.NotEqual(0, program.TileMap[9 * 32 + 4]);
        Assert.Equal(program.TileMap[9 * 32 + 4], program.TileMap[10 * 32 + 14]);
        Assert.NotEqual(0, program.TileMap[17 * 32 + 10]);
        Assert.NotEqual(program.TileMap[9 * 32 + 4], program.TileMap[17 * 32 + 10]);
        Assert.Equal(program.TileMap[17 * 32 + 0], program.TileMap[17 * 32 + 10]);
    }

    [Fact]
    public void GameBoy_runner_presents_sprites_immediately_after_vblank()
    {
        var source = File.ReadAllText(RepositoryFile("samples/runner/runner.rs"));

        Assert.Contains("PlayerState player;", source);
        Assert.Contains("player.Reset();", source);

        var vblankStart = source.IndexOf("video.WaitVBlank();", StringComparison.Ordinal);
        var inputPoll = source.IndexOf("input.Poll();", StringComparison.Ordinal);
        var gravity = source.IndexOf("player.ApplyGravity();", StringComparison.Ordinal);
        var draw = source.IndexOf("sprite.Draw(mario_player, Player.ScreenX, player.y, player.displayFrame, player.displayFlipX, 0);", StringComparison.Ordinal);

        Assert.True(vblankStart >= 0);
        Assert.True(draw > vblankStart, "Runner should draw the active state immediately after entering VBlank.");
        Assert.True(inputPoll > draw, "Runner should finish sprite presentation before input and gameplay updates consume VBlank time.");
        Assert.True(gravity > inputPoll, "Runner should update gameplay after the VBlank presentation block.");
    }

    [Fact]
    public void GameBoy_runner_declares_and_ticks_background_music()
    {
        var sourcePath = RepositoryFile("samples/runner/runner.rs");
        var baseDirectory = Path.GetDirectoryName(sourcePath);
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("""music.Asset(runner_theme, "music/free_06_delight.uge");""", source);
        Assert.Contains("audio.Init();", source);
        Assert.Contains("music.Play(runner_theme);", source);

        var vblankStart = source.IndexOf("video.WaitVBlank();", StringComparison.Ordinal);
        var audioUpdate = source.IndexOf("audio.Update();", StringComparison.Ordinal);
        var cameraApply = source.IndexOf("camera.Apply();", StringComparison.Ordinal);
        Assert.True(vblankStart >= 0);
        Assert.True(audioUpdate > vblankStart, "Runner should tick the music runtime once after VBlank starts.");
        Assert.True(cameraApply > audioUpdate, "Runner should tick music before camera/sprite presentation work consumes VBlank time.");

        var operations = GameBoyRomCompiler.CollectSdkAudioOperations(source, baseDirectory);
        Assert.Contains(operations, operation => operation is SdkAudioOperation.InitializeAudio);
        Assert.Contains(operations, operation => operation is SdkAudioOperation.PlayMusic { ThemeId: "runner_theme" });
        Assert.Contains(operations, operation => operation is SdkAudioOperation.UpdateAudio);

        var rom = GameBoyRomCompiler.CompileSource(source, baseDirectory);
        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x3E, 0x80, 0xE0, 0x26]), "Runner BGM should enable NR52.");
        Assert.True(ContainsSequence(rom, [0xE0, 0x12]), "Runner BGM should write CH1 envelope data during audio.Update.");
        Assert.True(ContainsSequence(rom, [0xE0, 0x14]), "Runner BGM should trigger CH1 notes during audio.Update.");
    }

    [Fact]
    public void World_map_generates_initial_visible_tilemap_from_map_columns()
    {
        const string source = """
                              void define_level_columns() {
                                  map_column(0, 0, 0, 4, 5);
                                  map_column(1, 0, 0, 4, 5);
                                  map_column(2, 0, 5, 4, 5);
                                  map_column(3, 0, 0, 4, 5);
                                  map_column(4, 0, 0, 4, 5);
                                  map_column(5, 0, 0, 4, 5);
                                  map_column(6, 0, 0, 4, 5);
                                  map_column(7, 0, 0, 3, 5);
                                  map_column(8, 0, 0, 4, 5);
                                  map_column(9, 0, 0, 4, 5);
                                  map_column(10, 5, 0, 4, 5);
                                  map_column(11, 0, 0, 4, 5);
                                  map_column(12, 0, 0, 4, 5);
                                  map_column(13, 0, 0, 0, 0);
                                  map_column(14, 0, 0, 0, 0);
                                  map_column(15, 0, 0, 0, 0);
                                  return;
                              }

                              void main() {
                                  define_level_columns();
                                  world_map(16, 11, 4);
                                  return;
                              }
                              """;

        var program = CompileVideoProgram(source);
        var worldMap = Assert.IsType<WorldMap2D>(program.WorldMap);

        Assert.Equal(16, worldMap.Width);
        Assert.Equal(4, worldMap.Height);
        Assert.Equal(3, worldMap.TileIdAt(7, 2));
        Assert.Equal(WorldTileFlags.Empty, worldMap.FlagsAt(7, 2));
        Assert.Equal(4, program.TileMap[13 * 32]);
        Assert.Equal(5, program.TileMap[14 * 32]);
        Assert.Equal(5, program.TileMap[12 * 32 + 2]);
        Assert.Equal(5, program.TileMap[11 * 32 + 10]);
        Assert.Equal(3, program.TileMap[13 * 32 + 7]);
        Assert.Equal(0, program.TileMap[13 * 32 + 13]);
        Assert.Equal(0, program.TileMap[14 * 32 + 13]);
        Assert.Equal(5, program.TileMap[12 * 32 + 18]);
        Assert.Equal(3, program.TileMap[13 * 32 + 23]);
        Assert.Equal(0, program.TileMap[13 * 32 + 29]);
    }

    [Fact]
    public void World_map_generates_streaming_columns_from_world_columns()
    {
        const string source = """
                              void define_world() {
                                  world_column(0, 0, 0, 4, 5);
                                  world_column(1, 0, 0, 4, 5);
                                  world_column(2, 0, 5, 4, 5);
                                  world_column(3, 0, 0, 4, 5);
                                  world_column(4, 0, 0, 4, 5);
                                  world_column(5, 0, 0, 4, 5);
                                  world_column(6, 0, 0, 4, 5);
                                  world_column(7, 0, 0, 3, 5);
                                  world_column(8, 0, 0, 4, 5);
                                  world_column(9, 0, 0, 4, 5);
                                  world_column(10, 5, 0, 4, 5);
                                  world_column(11, 0, 0, 4, 5);
                                  world_column(12, 0, 0, 4, 5);
                                  world_column(13, 0, 0, 0, 0);
                                  world_column(14, 0, 0, 0, 0);
                                  world_column(15, 0, 0, 0, 0);
                                  return;
                              }

                              void main() {
                                  define_world();
                                  world_map(16, 11, 4);
                                  camera_init(16, 11, 4);
                                  return;
                              }
                              """;

        var program = CompileVideoProgram(source);
        var worldMap = Assert.IsType<WorldMap2D>(program.WorldMap);

        Assert.Equal(4, program.MapColumnHeight);
        Assert.Equal(3, worldMap.TileIdAt(7, 2));
        Assert.Equal(3, program.MapColumns[7][2]);
        Assert.Equal(0, program.MapColumns[13][2]);
        Assert.Equal(5, program.TileMap[14 * 32]);
        Assert.Equal(3, program.TileMap[13 * 32 + 23]);
        Assert.Equal(32768, GameBoyRomCompiler.CompileSource(source).Length);
    }

    [Fact]
    public void World_load_imports_tiled_json_map_layers()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RetroSharp.GameBoy.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        WriteTiledTilesheetPng(directory, "runner.png", 8, 8, 1, 2, 3, 1, 2, 3);
        File.WriteAllText(
            Path.Combine(directory, "runner.tsj"),
            """
            {
              "type": "tileset",
              "version": "1.10",
              "tiledversion": "1.12.2",
              "name": "runner",
              "tilewidth": 8,
              "tileheight": 8,
              "spacing": 0,
              "margin": 0,
              "tilecount": 6,
              "columns": 6,
              "image": "runner.png",
              "imagewidth": 48,
              "imageheight": 8
            }
            """);
        File.WriteAllText(
            Path.Combine(directory, "level.tmj"),
            """
            {
              "type": "map",
              "version": "1.10",
              "tiledversion": "1.10.2",
              "orientation": "orthogonal",
              "renderorder": "right-down",
              "width": 3,
              "height": 4,
              "tilewidth": 8,
              "tileheight": 8,
              "infinite": false,
              "properties": [
                { "name": "retrosharpStreamY", "type": "int", "value": 5 },
                { "name": "retrosharpWorldY", "type": "int", "value": 2 },
                { "name": "retrosharpWorldHeight", "type": "int", "value": 2 }
              ],
              "layers": [
                {
                  "id": 1,
                  "name": "background",
                  "type": "tilelayer",
                  "width": 3,
                  "height": 4,
                  "visible": true,
                  "opacity": 1,
                  "x": 0,
                  "y": 0,
                  "data": [2, 0, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0]
                },
                {
                  "id": 2,
                  "name": "world",
                  "type": "tilelayer",
                  "width": 3,
                  "height": 4,
                  "visible": true,
                  "opacity": 1,
                  "x": 0,
                  "y": 0,
                  "data": [0, 0, 0, 0, 0, 0, 5, 0, 6, 4, 5, 0]
                },
                {
                  "id": 3,
                  "name": "collision",
                  "type": "tilelayer",
                  "width": 3,
                  "height": 4,
                  "visible": true,
                  "opacity": 1,
                  "x": 0,
                  "y": 0,
                  "data": [0, 0, 0, 0, 0, 0, 1, 0, 4, 2, 1, 0]
                }
              ],
              "tilesets": [
                { "firstgid": 1, "source": "runner.tsj" }
              ]
            }
            """);

        const string source = """
                              void main() {
                                  world.Load("level.tmj");
                                  camera.Init(3, 5, 2);
                                  return;
                              }
                              """;

        var program = CompileVideoProgram(source, directory);
        var worldMap = Assert.IsType<WorldMap2D>(program.WorldMap);

        Assert.Equal(3, worldMap.Width);
        Assert.Equal(2, worldMap.Height);
        Assert.Equal(6, worldMap.TileIdAt(0, 0));
        Assert.Equal(7, worldMap.TileIdAt(2, 0));
        Assert.Equal(8, worldMap.TileIdAt(0, 1));
        Assert.Equal(WorldTileFlags.Solid, worldMap.FlagsAt(0, 0));
        Assert.Equal(WorldTileFlags.Platform, worldMap.FlagsAt(2, 0));
        Assert.Equal(WorldTileFlags.Hazard, worldMap.FlagsAt(0, 1));
        Assert.Equal(6, program.TileMap[3 * 32]);
        Assert.Equal(7, program.TileMap[3 * 32 + 2]);
        Assert.Equal(6, program.TileMap[3 * 32 + 3]);
        Assert.Equal(6, program.TileMap[5 * 32]);
        Assert.Equal(7, program.TileMap[5 * 32 + 2]);
        Assert.Equal(6, program.TileMap[5 * 32 + 3]);
        Assert.Equal(8, program.TileMap[6 * 32]);
        Assert.Equal(32768, GameBoyRomCompiler.CompileSource(source, directory).Length);
    }

    [Fact]
    public void World_load_composes_tiled_background_under_empty_world_tiles_with_world_y_alignment()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RetroSharp.GameBoy.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        WriteTiledTilesheetPng(directory, "runner.png", 8, 8, 1, 2, 3);
        File.WriteAllText(
            Path.Combine(directory, "runner.tsj"),
            """
            {
              "type": "tileset",
              "version": "1.10",
              "tiledversion": "1.12.2",
              "name": "runner",
              "tilewidth": 8,
              "tileheight": 8,
              "spacing": 0,
              "margin": 0,
              "tilecount": 3,
              "columns": 3,
              "image": "runner.png",
              "imagewidth": 24,
              "imageheight": 8
            }
            """);
        File.WriteAllText(
            Path.Combine(directory, "level.tmj"),
            """
            {
              "type": "map",
              "version": "1.10",
              "tiledversion": "1.10.2",
              "orientation": "orthogonal",
              "renderorder": "right-down",
              "width": 3,
              "height": 6,
              "tilewidth": 8,
              "tileheight": 8,
              "infinite": false,
              "properties": [
                { "name": "retrosharpStreamY", "type": "int", "value": 2 },
                { "name": "retrosharpWorldY", "type": "int", "value": 3 },
                { "name": "retrosharpWorldHeight", "type": "int", "value": 2 }
              ],
              "layers": [
                {
                  "id": 1,
                  "name": "background",
                  "type": "tilelayer",
                  "width": 3,
                  "height": 6,
                  "visible": true,
                  "opacity": 1,
                  "x": 0,
                  "y": 0,
                  "data": [0, 0, 0, 0, 0, 0, 1, 2, 3, 3, 3, 3, 0, 0, 0, 0, 0, 0]
                },
                {
                  "id": 2,
                  "name": "world",
                  "type": "tilelayer",
                  "width": 3,
                  "height": 6,
                  "visible": true,
                  "opacity": 1,
                  "x": 0,
                  "y": 0,
                  "data": [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 0, 0, 0, 0, 0]
                },
                {
                  "id": 3,
                  "name": "collision",
                  "type": "tilelayer",
                  "width": 3,
                  "height": 6,
                  "visible": true,
                  "opacity": 1,
                  "x": 0,
                  "y": 0,
                  "data": [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0]
                }
              ],
              "tilesets": [
                { "firstgid": 1, "source": "runner.tsj" }
              ]
            }
            """);

        const string source = """
                              void main() {
                                  world.Load("level.tmj");
                                  camera.Init(3, 2, 2);
                                  return;
                              }
                              """;

        var program = CompileVideoProgram(source, directory);
        var worldMap = Assert.IsType<WorldMap2D>(program.WorldMap);

        Assert.Equal(8, worldMap.TileIdAt(0, 0));
        Assert.Equal(8, worldMap.TileIdAt(1, 0));
        Assert.Equal(8, worldMap.TileIdAt(2, 0));
        Assert.Equal(WorldTileFlags.Empty, worldMap.FlagsAt(0, 0));
        Assert.Equal(7, worldMap.TileIdAt(0, 1));
        Assert.Equal(WorldTileFlags.Solid, worldMap.FlagsAt(0, 1));
        Assert.Equal(0, worldMap.TileIdAt(1, 1));
        Assert.Equal(WorldTileFlags.Empty, worldMap.FlagsAt(1, 1));
        Assert.Equal(6, program.TileMap[1 * 32]);
        Assert.Equal(7, program.TileMap[1 * 32 + 1]);
        Assert.Equal(8, program.TileMap[1 * 32 + 2]);
        Assert.Equal(8, program.TileMap[2 * 32]);
        Assert.Equal(8, program.TileMap[2 * 32 + 1]);
        Assert.Equal(8, program.TileMap[2 * 32 + 2]);
        Assert.Equal(7, program.TileMap[3 * 32]);
        Assert.Equal(0, program.TileMap[3 * 32 + 1]);
        Assert.Equal(7, program.MapColumns[0][1]);
        Assert.Equal(0, program.MapColumns[1][1]);

        Assert.Equal(2, program.BackgroundStreamHeight);
        for (var column = 0; column < 3; column++)
        {
            for (var row = 0; row < program.BackgroundStreamHeight; row++)
            {
                Assert.Equal(program.TileMap[row * 32 + column], program.BackgroundColumns[column][row]);
            }
        }
    }

    [Fact]
    public void Camera_streams_background_rows_above_the_world_band_when_scrolling_horizontally()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RetroSharp.GameBoy.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        WriteTiledTilesheetPng(directory, "runner.png", 8, 8, 1, 2, 3);
        File.WriteAllText(
            Path.Combine(directory, "runner.tsj"),
            """
            {
              "type": "tileset",
              "version": "1.10",
              "tiledversion": "1.12.2",
              "name": "runner",
              "tilewidth": 8,
              "tileheight": 8,
              "spacing": 0,
              "margin": 0,
              "tilecount": 3,
              "columns": 3,
              "image": "runner.png",
              "imagewidth": 24,
              "imageheight": 8
            }
            """);
        File.WriteAllText(
            Path.Combine(directory, "level.tmj"),
            """
            {
              "type": "map",
              "version": "1.10",
              "tiledversion": "1.10.2",
              "orientation": "orthogonal",
              "renderorder": "right-down",
              "width": 3,
              "height": 6,
              "tilewidth": 8,
              "tileheight": 8,
              "infinite": false,
              "properties": [
                { "name": "retrosharpStreamY", "type": "int", "value": 2 },
                { "name": "retrosharpWorldY", "type": "int", "value": 3 },
                { "name": "retrosharpWorldHeight", "type": "int", "value": 2 }
              ],
              "layers": [
                {
                  "id": 1,
                  "name": "background",
                  "type": "tilelayer",
                  "width": 3,
                  "height": 6,
                  "visible": true,
                  "opacity": 1,
                  "x": 0,
                  "y": 0,
                  "data": [0, 0, 0, 0, 0, 0, 1, 2, 3, 3, 3, 3, 0, 0, 0, 0, 0, 0]
                },
                {
                  "id": 2,
                  "name": "world",
                  "type": "tilelayer",
                  "width": 3,
                  "height": 6,
                  "visible": true,
                  "opacity": 1,
                  "x": 0,
                  "y": 0,
                  "data": [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 0, 0, 0, 0, 0]
                }
              ],
              "tilesets": [
                { "firstgid": 1, "source": "runner.tsj" }
              ]
            }
            """);

        const string source = """
                              void main() {
                                  world.Load("level.tmj");
                                  camera.Init(3, 2, 2);
                                  loop {
                                      camera.SetPosition(1, 0);
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source, directory);

        // The world band starts at GB row 2 (0x9840). The background region above the band
        // (GB rows 0..1) must also stream when scrolling, writing GB row 0 at 0x9800.
        Assert.True(
            ContainsSequence(rom, [0xFA, 0xE4, 0xC0, 0xC6, 0x00, 0x6F, 0x26, 0x98]),
            "camera_move_right should also stream the background row above the band into GB row 0 (0x9800).");
        Assert.True(
            ContainsSequence(rom, [0xFA, 0xE5, 0xC0, 0xC6, 0x00, 0x6F, 0x26, 0x98]),
            "camera_move_left should also stream the background row above the band into GB row 0 (0x9800).");

        // The streaming runs late in the frame (after input, physics, collision), so it must
        // wait for a fresh VBlank edge before touching the background tilemap, otherwise
        // a jump/landing-heavy frame can enter this path late in the current VBlank and tear
        // the top background rows while scrolling. The edge wait
        // (wait while LY >= 144, then wait while LY < 144) must sit immediately before the
        // band column load (LD A,($C0E6) for the right step, LD A,($C0E7) for the left step).
        Assert.True(
            ContainsSequence(rom, [0xF0, 0x44, 0xFE, 0x90, 0x30, 0xFA, 0xF0, 0x44, 0xFE, 0x90, 0x38, 0xFA, 0xFA, 0xE6, 0xC0]),
            "camera_move_right should wait for a fresh VBlank edge before streaming the next column into the background tilemap.");
        Assert.True(
            ContainsSequence(rom, [0xF0, 0x44, 0xFE, 0x90, 0x30, 0xFA, 0xF0, 0x44, 0xFE, 0x90, 0x38, 0xFA, 0xFA, 0xE7, 0xC0]),
            "camera_move_left should wait for a fresh VBlank edge before streaming the next column into the background tilemap.");
    }

    [Fact]
    public void Camera_horizontal_streaming_skips_world_rows_below_the_visible_screen()
    {
        const string source = """
                              void define_world() {
                                  world_column(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14);
                                  world_column(1, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28);
                                  return;
                              }

                              void main() {
                                  define_world();
                                  world_map(2, 9, 14);
                                  camera.Init(2, 9, 14);
                                  loop {
                                      camera.SetPosition(8, 0);
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.True(
            ContainsSequence(rom, [0xC6, 0x20, 0x6F, 0x26, 0x9A, 0x78, 0x77]),
            "camera horizontal streaming should still update the last visible world row (GB row 17).");
        Assert.False(
            ContainsSequence(rom, [0xC6, 0x40, 0x6F, 0x26, 0x9A, 0x78, 0x77]),
            "camera horizontal streaming should not spend VBlank time on off-screen world row 18.");
    }

    [Fact]
    public void World_load_imports_tiled_external_tilesets_images_and_object_collisions()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RetroSharp.GameBoy.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        WriteTiledTilesheetPng(directory, "tiles.png", 16, 16, 0, 2, 3);
        File.WriteAllText(
            Path.Combine(directory, "level.tsx"),
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <tileset version="1.10" tiledversion="1.12.2" name="Level" tilewidth="16" tileheight="16" tilecount="3" columns="3">
             <image source="tiles.png" width="48" height="16"/>
             <tile id="1">
              <objectgroup draworder="index" id="2">
               <object id="1" x="0" y="0" width="16" height="16"/>
              </objectgroup>
             </tile>
            </tileset>
            """);
        File.WriteAllText(
            Path.Combine(directory, "level.tmj"),
            """
            {
              "type": "map",
              "version": "1.10",
              "tiledversion": "1.12.2",
              "orientation": "orthogonal",
              "renderorder": "right-down",
              "width": 3,
              "height": 3,
              "tilewidth": 16,
              "tileheight": 16,
              "infinite": false,
              "properties": [
                { "name": "retrosharpStreamY", "type": "int", "value": 4 },
                { "name": "retrosharpWorldY", "type": "int", "value": 1 },
                { "name": "retrosharpWorldHeight", "type": "int", "value": 2 }
              ],
              "layers": [
                {
                  "id": 1,
                  "name": "background",
                  "type": "tilelayer",
                  "width": 3,
                  "height": 3,
                  "visible": true,
                  "opacity": 1,
                  "x": 0,
                  "y": 0,
                  "data": [1, 2, 3, 0, 0, 0, 0, 0, 0]
                },
                {
                  "id": 2,
                  "name": "world",
                  "type": "tilelayer",
                  "width": 3,
                  "height": 3,
                  "visible": true,
                  "opacity": 1,
                  "x": 0,
                  "y": 0,
                  "data": [0, 0, 0, 2, 3, 0, 0, 0, 0]
                }
              ],
              "tilesets": [
                { "firstgid": 1, "source": "level.tsx" }
              ]
            }
            """);

        const string source = """
                              void main() {
                                  world.Load("level.tmj");
                                  camera.Init(6, 4, 4);
                                  return;
                              }
                              """;

        var program = CompileVideoProgram(source, directory);
        var worldMap = Assert.IsType<WorldMap2D>(program.WorldMap);

        Assert.Equal(6, worldMap.Width);
        Assert.Equal(4, worldMap.Height);
        Assert.Equal(6, worldMap.TileIdAt(0, 0));
        Assert.Equal(6, worldMap.TileIdAt(1, 0));
        Assert.Equal(7, worldMap.TileIdAt(2, 0));
        Assert.Equal(7, worldMap.TileIdAt(3, 0));
        Assert.Equal(6, worldMap.TileIdAt(0, 1));
        Assert.Equal(6, worldMap.TileIdAt(1, 1));
        Assert.Equal(WorldTileFlags.Solid, worldMap.FlagsAt(0, 0));
        Assert.Equal(WorldTileFlags.Solid, worldMap.FlagsAt(1, 0));
        Assert.Equal(WorldTileFlags.Solid, worldMap.FlagsAt(0, 1));
        Assert.Equal(WorldTileFlags.Solid, worldMap.FlagsAt(1, 1));
        Assert.Equal(WorldTileFlags.Empty, worldMap.FlagsAt(2, 0));
        Assert.Equal(0, program.TileMap[0]);
        Assert.Equal(0, program.TileMap[1]);
        Assert.Equal(6, program.TileMap[2 * 32 + 2]);
        Assert.Equal(6, program.TileMap[2 * 32 + 3]);
        Assert.Equal(7, program.TileMap[2 * 32 + 4]);
        Assert.Equal(7, program.TileMap[2 * 32 + 5]);
        Assert.Equal(6, program.TileMap[4 * 32]);
        Assert.Equal(6, program.TileMap[4 * 32 + 1]);
        Assert.Equal(7, program.TileMap[4 * 32 + 2]);
        Assert.Equal(7, program.TileMap[4 * 32 + 3]);
        Assert.Equal(6, program.TileMap[5 * 32]);
        Assert.Equal(6, program.TileMap[5 * 32 + 1]);
        Assert.Equal(32768, GameBoyRomCompiler.CompileSource(source, directory).Length);
    }

    [Fact]
    public void World_load_reserves_generated_background_tiles_before_sprite_assets()
    {
        var directory = WriteSpriteAsset(
            "player.sprite.json",
            SpriteJson(Rows(8, 16, "01230123")));
        WriteTiledTilesheetPng(directory, "tiles.png", 16, 16, 3);
        File.WriteAllText(
            Path.Combine(directory, "level.tsx"),
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <tileset version="1.10" tiledversion="1.12.2" name="Level" tilewidth="16" tileheight="16" tilecount="1" columns="1">
             <image source="tiles.png" width="16" height="16"/>
            </tileset>
            """);
        File.WriteAllText(
            Path.Combine(directory, "level.tmj"),
            """
            {
              "type": "map",
              "version": "1.10",
              "tiledversion": "1.12.2",
              "orientation": "orthogonal",
              "renderorder": "right-down",
              "width": 1,
              "height": 1,
              "tilewidth": 16,
              "tileheight": 16,
              "infinite": false,
              "properties": [
                { "name": "retrosharpStreamY", "type": "int", "value": 4 },
                { "name": "retrosharpWorldY", "type": "int", "value": 0 },
                { "name": "retrosharpWorldHeight", "type": "int", "value": 1 }
              ],
              "layers": [
                {
                  "id": 1,
                  "name": "world",
                  "type": "tilelayer",
                  "width": 1,
                  "height": 1,
                  "visible": true,
                  "opacity": 1,
                  "x": 0,
                  "y": 0,
                  "data": [1]
                }
              ],
              "tilesets": [
                { "firstgid": 1, "source": "level.tsx" }
              ]
            }
            """);

        const string source = """
                              void main() {
                                  video.Init();
                                  sprite.Asset(player, "player.sprite.json");
                                  world.Load("level.tmj");
                                  sprite.Draw(player, 8, 8, 0);
                                  return;
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source, directory);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0xC6, 0x08, 0xEA, 0x02, 0xFE]), "Generated background tiles should leave the first 8x16 sprite tile on an even tile after them.");
    }

    [Fact]
    public void World_map_generates_collision_flags_and_lowers_flag_queries()
    {
        const string source = """
                              void define_world() {
                                  world_column(0, 0, 4);
                                  world_column(1, 3, 5);
                                  world_flags(0, 0, 1);
                                  world_flags(1, 2, 1);
                                  return;
                              }

                              void main() {
                                  define_world();
                                  world_map(2, 11, 2);
                                  camera_init(2, 11, 2);
                                  i16 column = 1;
                                  i16 hazard = 0;
                                  i16 solid = 0;
                                  if (map_flags_at(column, 0) != 0) {
                                      hazard = 1;
                                  }
                                  if (camera_span_has_flags(0, 8, 1, 1) != 0) {
                                      solid = 1;
                                  }
                                  if (camera_span_has_flags(8, 8, 0, 2) != 0) {
                                      hazard = 2;
                                  }
                                  return;
                              }
                              """;

        var program = CompileVideoProgram(source);
        var worldMap = Assert.IsType<WorldMap2D>(program.WorldMap);

        Assert.Equal(WorldTileFlags.Solid, worldMap.FlagsAt(0, 1));
        Assert.Equal(WorldTileFlags.Hazard, worldMap.FlagsAt(1, 0));
        Assert.Equal(WorldTileFlags.Solid, worldMap.FlagsAt(1, 1));

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x00, 0x02]), "ROM should contain world flag row 0 data.");
        Assert.True(ContainsSequence(rom, [0x01, 0x01]), "ROM should contain world flag row 1 data.");
        Assert.True(ContainsSequence(rom, [0xE6, 0x01, 0xFE, 0x00, 0xC2]), "Solid flag queries should mask bit 0 independently.");
        Assert.True(ContainsSequence(rom, [0xE6, 0x02, 0xFE, 0x00, 0xC2]), "Hazard flag queries should mask bit 1 independently.");
    }

    [Fact]
    public void World_tile_flags_at_reads_world_pixel_coordinates_and_bounds_to_empty()
    {
        const string source = """
                              void define_world() {
                                  world_column(0, 0, 4);
                                  world_column(1, 3, 5);
                                  world_flags(0, 0, 1);
                                  world_flags(1, 2, 1);
                                  return;
                              }

                              void main() {
                                  define_world();
                                  world_map(2, 11, 2);
                                  i16 worldX = 8;
                                  i16 worldY = 0;
                                  i16 hazard = 0;
                                  i16 solid = 0;
                                  i16 empty = 0;
                                  if (world_tile_flags_at(worldX, worldY) != 0) {
                                      hazard = 1;
                                  }
                                  if (world_tile_flags_at(0, 8) != 0) {
                                      solid = 1;
                                  }
                                  if (world_tile_flags_at(0, 0) != 0) {
                                      empty = 3;
                                  }
                                  if (world_tile_flags_at(16, 0) != 0) {
                                      empty = 1;
                                  }
                                  if (world_tile_flags_at(0, 16) != 0) {
                                      empty = 2;
                                  }
                                  return;
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x00, 0x02]), "ROM should contain world flag row 0 data.");
        Assert.True(ContainsSequence(rom, [0x01, 0x01]), "ROM should contain world flag row 1 data.");
        Assert.True(ContainsSequence(rom, [0xCB, 0x3F, 0xCB, 0x3F, 0xCB, 0x3F]), "world_tile_flags_at should convert world pixels to tile coordinates by dividing by 8.");
        Assert.True(ContainsSequence(rom, [0xFE, 0x02, 0xD2]), "world_tile_flags_at should guard map bounds before reading flag rows.");
    }

    [Fact]
    public void Collision_aabb_tiles_checks_each_overlapped_world_tile()
    {
        const string source = """
                              void define_world() {
                                  world_column(0, 0, 4, 0);
                                  world_column(1, 3, 5, 0);
                                  world_column(2, 0, 0, 6);
                                  world_flags(0, 0, 1, 0);
                                  world_flags(1, 2, 1, 0);
                                  world_flags(2, 0, 0, 4);
                                  return;
                              }

                              void main() {
                                  define_world();
                                  world_map(3, 11, 3);
                                  i16 x = 7;
                                  i16 y = 8;
                                  i16 oneTile = collision_aabb_tiles(8, 0, 8, 8, 2);
                                  i16 horizontalSpan = collision_aabb_tiles(x, y, 2, 1, 1);
                                  i16 verticalSpan = collision_aabb_tiles(0, 7, 1, 2, 1);
                                  i16 empty = collision_aabb_tiles(0, 0, 8, 8, 1);
                                  i16 platform = collision_aabb_tiles(16, 16, 8, 8, 4);
                                  i16 zeroWidth = collision_aabb_tiles(0, 8, 0, 8, 1);
                                  return;
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0xE6, 0x01, 0xFE, 0x00, 0xC2]), "AABB collision should mask solid flags.");
        Assert.True(ContainsSequence(rom, [0xE6, 0x02, 0xFE, 0x00, 0xC2]), "AABB collision should mask hazard flags.");
        Assert.True(ContainsSequence(rom, [0xE6, 0x04, 0xFE, 0x00, 0xC2]), "AABB collision should mask platform flags.");
    }

    [Fact]
    public void Camera_aabb_tiles_checks_each_overlapped_tile_against_visible_camera_columns()
    {
        const string source = """
                              void define_world() {
                                  world_column(0, 0, 4);
                                  world_column(1, 0, 5);
                                  world_column(2, 0, 6);
                                  world_column(3, 0, 7);
                                  world_flags(0, 0, 1);
                                  world_flags(1, 0, 0);
                                  world_flags(2, 0, 1);
                                  world_flags(3, 0, 0);
                                  return;
                              }

                              void main() {
                                  define_world();
                                  world_map(4, 11, 2);
                                  camera_init(4, 11, 2);
                                  i16 y = 8;
                                  i16 hit = camera.AabbTiles(72, y, 18, 8, 1);
                                  return;
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(
            ContainsSequence(rom, [0xFA, 0xE2, 0xC0, 0xC6, 0x48, 0xCB, 0x3F, 0xCB, 0x3F, 0xCB, 0x3F, 0x47, 0xFA, 0xE3, 0xC0, 0x80]),
            "camera.AabbTiles should derive the source column from camera fine X plus the visible screen X.");
        Assert.True(
            ContainsSequence(rom, [0xFA, 0xE2, 0xC0, 0xC6, 0x59, 0xCB, 0x3F, 0xCB, 0x3F, 0xCB, 0x3F]),
            "camera.AabbTiles should include the far edge of the sprite-width span.");
        Assert.True(ContainsSequence(rom, [0xE6, 0x01, 0xFE, 0x00, 0xC2]), "camera.AabbTiles should mask requested collision flags.");
    }

    [Fact]
    public void Camera_aabb_hit_top_returns_top_edge_of_first_overlapped_tile()
    {
        const string source = """
                              void define_world() {
                                  world_column(0, 0, 4);
                                  world_column(1, 0, 4);
                                  world_column(2, 0, 4);
                                  world_flags(0, 0, 1);
                                  world_flags(1, 0, 1);
                                  world_flags(2, 0, 1);
                                  world_map(3, 11, 2);
                                  camera_init(3, 11, 2);
                              }

                              void main() {
                                  define_world();
                                  i16 footY = 16;
                                  i16 hitTop = camera.AabbHitTop(72, footY - 8, 16, 16, 1);
                                  return;
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x3E, 0xFF]), "camera.AabbHitTop should return 255 when no overlapped tile has the requested flags.");
        Assert.True(ContainsSequence(rom, [0xC6, 0xF8, 0xE6, 0xF8]), "camera.AabbHitTop should apply the search offset and return the hit tile's top world Y.");
    }

    [Fact]
    public void GameBoy_runner_uses_dynamic_world_y_for_tiled_solid_landing()
    {
        var sourcePath = RepositoryFile("samples/runner/runner.rs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("LandingSearchTopOffset = 32", source);
        Assert.Contains("LandingSearchHeight = 40", source);
        Assert.Contains("NoTileHit = 255", source);
        Assert.Contains("inline void ResolveSolidLanding(PlayerState player, Pixel footWorldY)", source);
        Assert.Contains("let footWorldY = player.y - Player.WorldOriginY;", source);
        Assert.Contains("footTile = camera.AabbHitTop(Player.ScreenX, footWorldY - CollisionProbe.LandingSearchTopOffset, sprite_width(mario_player), CollisionProbe.LandingSearchHeight, CollisionFlag.Solid);", source);
        Assert.Contains("if (footTile != CollisionProbe.NoTileHit)", source);
        Assert.Contains("player.Land(footTile + Player.WorldOriginY);", source);
        Assert.DoesNotContain("CollisionProbe.TileSize2", source);
        Assert.DoesNotContain("CollisionProbe.TileSize3", source);
        Assert.DoesNotContain("CollisionProbe.TileSize4", source);
        Assert.DoesNotContain("landedWorldY", source);
        Assert.Contains("frame.ResolveSolidLanding(player, footWorldY);", source);
        Assert.DoesNotContain("collision_aabb_tiles(footLeftX, 0", source);
        Assert.DoesNotContain("playerWorldX", source);
        Assert.DoesNotContain("WrapWorldX", source);
        Assert.DoesNotContain("CollisionProbe.GroundY", source);
    }

    [Fact]
    public void GameBoy_runner_blocks_horizontal_camera_motion_against_tall_solids()
    {
        var sourcePath = RepositoryFile("samples/runner/runner.rs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("LeftWallProbeX = 71", source);
        Assert.Contains("RightWallProbeX = 73", source);
        Assert.Contains("WallProbeHeight = 8", source);
        Assert.Contains("inline void HandleHorizontalInput(PlayerState player, Pixel footWorldY)", source);
        Assert.Contains("let wallProbeY = footWorldY - CollisionProbe.WallProbeHeight;", source);
        Assert.Contains("camera.AabbTiles(Player.RightWallProbeX, wallProbeY, sprite_width(mario_player), CollisionProbe.WallProbeHeight, CollisionFlag.Solid) == 0", source);
        Assert.Contains("camera.AabbTiles(Player.LeftWallProbeX, wallProbeY, sprite_width(mario_player), CollisionProbe.WallProbeHeight, CollisionFlag.Solid) == 0", source);
        Assert.Contains("let movementFootWorldY = player.y - Player.WorldOriginY;", source);
        Assert.Contains("view.HandleHorizontalInput(player, movementFootWorldY);", source);
        Assert.DoesNotContain("view.HandleHorizontalInput(player);", source);

        var rom = GameBoyRomCompiler.CompileSource(source, Path.GetDirectoryName(sourcePath));
        Assert.Equal(32768, rom.Length);
    }

    [Fact]
    public void GameBoy_runner_uses_lower_gravity_with_compensated_jump_height()
    {
        var sourcePath = RepositoryFile("samples/runner/runner.rs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("Velocity = 253", source);
        Assert.Contains("BoostTicks = 12", source);
        Assert.Contains("GravityFrames = 2", source);
        Assert.Contains("BoostTickMask = 1", source);
        Assert.Contains("Pixel gravityTick;", source);
        Assert.Contains("gravityTick = 0;", source);

        var gravityStart = source.IndexOf("inline void ApplyGravity()", StringComparison.Ordinal);
        var landStart = source.IndexOf("inline void Land(Pixel targetY)", StringComparison.Ordinal);
        Assert.True(gravityStart >= 0);
        Assert.True(landStart > gravityStart);
        var gravityBlock = source[gravityStart..landStart];
        Assert.Contains("gravityTick++;", gravityBlock);
        Assert.Contains("if (gravityTick >= Jump.GravityFrames)", gravityBlock);
        Assert.Contains("velocityY += 1;", gravityBlock);
        Assert.Contains("if (velocityY != 0)", gravityBlock);
        Assert.Contains("grounded = 0;", gravityBlock);
        Assert.Contains("y += velocityY;", gravityBlock);
        Assert.DoesNotContain("grounded = 0;\n        gravityTick++;", gravityBlock);

        var jumpStart = source.IndexOf("inline void HandleJumpInput()", StringComparison.Ordinal);
        var animationStart = source.IndexOf("inline void UpdateRunAnimation(CameraState view)", StringComparison.Ordinal);
        Assert.True(jumpStart >= 0);
        Assert.True(animationStart > jumpStart);
        var jumpBlock = source[jumpStart..animationStart];
        Assert.Contains("if ((jumpTicks & Jump.BoostTickMask) != 0)", jumpBlock);
        Assert.Contains("velocityY -= 1;", jumpBlock);

        var rom = GameBoyRomCompiler.CompileSource(source, Path.GetDirectoryName(sourcePath));
        Assert.Equal(32768, rom.Length);
    }

    [Fact]
    public void GameBoy_runner_uses_actor_feet_holes_failure_tiles_and_reset_state()
    {
        var sourcePath = RepositoryFile("samples/runner/runner.rs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("Pixel footTile;", source);
        Assert.Contains("let footWorldY = player.y - Player.WorldOriginY;", source);
        Assert.Contains("Pixel resetRequested;", source);

        Assert.Contains("""world.Load("maps/runner.tmj");""", source);
        Assert.DoesNotContain("world.Column(", source);
        Assert.DoesNotContain("world.Flags(", source);
        Assert.DoesNotContain("world.Map(", source);
        Assert.DoesNotContain("map_column(", source);
        Assert.DoesNotContain("inline pure Pixel WrapWorldX(Pixel x) => x;", source);
        Assert.DoesNotContain("playerWorldX", source);
        Assert.Contains("let footWorldY = player.y - Player.WorldOriginY;", source);
        Assert.Contains("if (velocityY >= World.SignedVelocityWrap)", source);
        Assert.Contains("y = 0;", source);
        Assert.Contains("player.velocityY < World.SignedVelocityWrap", source);
        Assert.Contains("player.velocityY != 0", source);
        Assert.Contains("footTile = camera.AabbHitTop(Player.ScreenX, footWorldY - CollisionProbe.LandingSearchTopOffset, sprite_width(mario_player), CollisionProbe.LandingSearchHeight, CollisionFlag.Solid);", source);
        Assert.Contains("player.Land(footTile + Player.WorldOriginY);", source);
        Assert.DoesNotContain("camera_span_has_flags(", source);
        Assert.DoesNotContain("camera_span_has_tile(", source);
        Assert.DoesNotContain("camera_span_tile_at(", source);
        Assert.DoesNotContain("footLeftX", source);
        Assert.DoesNotContain("footCenterX", source);
        Assert.DoesNotContain("footRightX", source);
        Assert.DoesNotContain("map_tile_at(player", source);
        Assert.DoesNotContain("failTile", source);
        Assert.DoesNotContain("hazardHit", source);
        Assert.DoesNotContain("BounceFromHazard", source);
        Assert.DoesNotContain("EnemyState", source);
        Assert.DoesNotContain("if (footTile != 3)", source);
        Assert.Contains("if (player.grounded == 0)", source);
        Assert.Contains("if (player.y >= Player.FallResetY)", source);
        Assert.Contains("if (resetRequested != 0)", source);
        Assert.Contains("player.Reset();", source);
        Assert.Contains("velocityY = 0;", source);
        Assert.Contains("jumping = 0;", source);

        var rom = GameBoyRomCompiler.CompileSource(source, Path.GetDirectoryName(sourcePath));
        Assert.Equal(32768, rom.Length);
    }

    [Fact]
    public void GameBoy_runner_keeps_visible_map_collision_and_streaming_cursors_in_sync()
    {
        var sourcePath = RepositoryFile("samples/runner/runner.rs");
        var source = File.ReadAllText(sourcePath);

        Assert.DoesNotContain("void draw_starting_scene()", source);
        Assert.DoesNotContain("tilemap_fill(", source);
        Assert.DoesNotContain("void draw_background()", source);
        Assert.DoesNotContain("tilemap.Set(", source);
        Assert.Contains("void load_world()", source);
        Assert.Contains("""world.Load("maps/runner.tmj");""", source);
        Assert.True(File.Exists(RepositoryFile("samples/runner/maps/runner.tmj")));
        Assert.True(File.Exists(RepositoryFile("samples/runner/maps/runner-tiles.tsj")));
        Assert.True(File.Exists(RepositoryFile("samples/runner/maps/runner-tiles.png")));
        Assert.DoesNotContain("world.Column(", source);
        Assert.DoesNotContain("world.Flags(", source);
        Assert.DoesNotContain("world.Map(", source);
        Assert.DoesNotContain("map_column(", source);
        Assert.Contains("Height = 14", source);

        Assert.Contains("camera.Init(World.Width, World.StreamY, World.Height);", source);
        Assert.True(
            source.IndexOf("camera.Init(World.Width, World.StreamY, World.Height);", StringComparison.Ordinal) >
            source.IndexOf("load_world();", StringComparison.Ordinal));
        Assert.Contains("camera.Apply();", source);
        Assert.Contains("let footWorldY = player.y - Player.WorldOriginY;", source);
        var topClampStart = source.IndexOf("if (velocityY >= World.SignedVelocityWrap)", StringComparison.Ordinal);
        Assert.True(topClampStart >= 0);
        var footProbeStart = source.IndexOf("let footWorldY = player.y - Player.WorldOriginY;", StringComparison.Ordinal);
        Assert.True(footProbeStart > topClampStart, "Runner should clamp upward Y wrap before collision probes and reset checks.");
        var topClampBlock = source[topClampStart..footProbeStart];
        Assert.Contains("if (y >= World.SignedVelocityWrap)", topClampBlock);
        Assert.Contains("y = 0;", topClampBlock);
        Assert.Contains("velocityY = 0;", topClampBlock);
        Assert.Contains("jumping = 0;", topClampBlock);
        var solidLandingStart = source.IndexOf("inline void ResolveSolidLanding", StringComparison.Ordinal);
        var fallStart = source.IndexOf("inline void ResolveFall", StringComparison.Ordinal);
        Assert.True(solidLandingStart >= 0);
        Assert.True(fallStart > solidLandingStart);
        var solidLandingBlock = source[solidLandingStart..fallStart];
        Assert.Contains("player.velocityY < World.SignedVelocityWrap", solidLandingBlock);
        Assert.Contains("player.velocityY != 0", solidLandingBlock);
        Assert.Contains("camera.AabbHitTop(Player.ScreenX, footWorldY - CollisionProbe.LandingSearchTopOffset, sprite_width(mario_player), CollisionProbe.LandingSearchHeight, CollisionFlag.Solid)", solidLandingBlock);
        Assert.Contains("player.Land(footTile + Player.WorldOriginY);", solidLandingBlock);
        Assert.DoesNotContain("camera_span_has_flags(", source);
        Assert.DoesNotContain("camera_span_has_tile(", source);
        Assert.DoesNotContain("camera_span_tile_at(", source);
        Assert.Contains("camera.SetPosition(x, 0);", source);
        Assert.DoesNotContain("camera_move_right();", source);
        Assert.DoesNotContain("camera_move_left();", source);
        Assert.DoesNotContain("i16 screenLeftColumn = 0;", source);
        Assert.DoesNotContain("i16 rightSourceColumn = 4;", source);
        Assert.DoesNotContain("i16 leftSourceColumn = 15;", source);
        Assert.DoesNotContain("i16 leftStreamColumn = 31;", source);
        Assert.DoesNotContain("map_stream_column(streamColumn, rightSourceColumn, 11, 4);", source);
        Assert.DoesNotContain("map_stream_column(leftStreamColumn, leftSourceColumn, 11, 4);", source);

        var resetStart = source.IndexOf("inline void ResolveReset(PlayerState player)", StringComparison.Ordinal);
        Assert.True(resetStart >= 0);
        var resetEnd = source.IndexOf("void setup_video()", resetStart, StringComparison.Ordinal);
        Assert.True(resetEnd > resetStart);
        var resetBlock = source[resetStart..resetEnd];
        Assert.Contains("if (resetRequested != 0)", resetBlock);
        Assert.DoesNotContain("camera = 0;", resetBlock);
        Assert.DoesNotContain("camera.Init(", resetBlock);
        Assert.DoesNotContain("streamColumn = 20;", resetBlock);
        Assert.DoesNotContain("screenLeftColumn = 0;", resetBlock);
        Assert.DoesNotContain("rightSourceColumn = 4;", resetBlock);
        Assert.DoesNotContain("leftSourceColumn = 15;", resetBlock);

        var program = CompileVideoProgram(source, Path.GetDirectoryName(sourcePath));
        var worldMap = Assert.IsType<WorldMap2D>(program.WorldMap);

        Assert.Equal(68, worldMap.Width);
        Assert.Equal(14, worldMap.Height);
        Assert.NotEqual(0, program.TileMap[9 * 32 + 4]);
        Assert.Equal(program.TileMap[9 * 32 + 4], program.TileMap[10 * 32 + 14]);
        Assert.NotEqual(0, program.TileMap[17 * 32 + 10]);
        Assert.NotEqual(0, worldMap.TileIdAt(4, 0));
        Assert.Equal(WorldTileFlags.Empty, worldMap.FlagsAt(4, 0));
        Assert.NotEqual(0, worldMap.TileIdAt(14, 0));
        Assert.Equal(WorldTileFlags.Empty, worldMap.FlagsAt(14, 0));
        Assert.NotEqual(0, worldMap.TileIdAt(22, 4));
        Assert.Equal(WorldTileFlags.Empty, worldMap.FlagsAt(22, 4));
        Assert.NotEqual(0, worldMap.TileIdAt(0, 8));
        Assert.Equal(WorldTileFlags.Solid, worldMap.FlagsAt(0, 8));
        Assert.NotEqual(0, worldMap.TileIdAt(26, 8));
        Assert.Equal(WorldTileFlags.Solid, worldMap.FlagsAt(26, 8));
        Assert.NotEqual(0, worldMap.TileIdAt(16, 0));
        Assert.Equal(WorldTileFlags.Empty, worldMap.FlagsAt(16, 0));

        var rom = GameBoyRomCompiler.CompileSource(source, Path.GetDirectoryName(sourcePath));
        Assert.Equal(32768, rom.Length);
    }

    [Fact]
    public void GameBoy_runner_applies_reset_before_consuming_jump_input()
    {
        var sourcePath = RepositoryFile("samples/runner/runner.rs");
        var source = File.ReadAllText(sourcePath);

        var resetStart = source.IndexOf("frame.ResolveReset(player);", StringComparison.Ordinal);
        var jumpStart = source.IndexOf("player.HandleJumpInput();", StringComparison.Ordinal);
        var movementStart = source.IndexOf("view.HandleHorizontalInput(player, movementFootWorldY);", StringComparison.Ordinal);

        Assert.True(resetStart >= 0);
        Assert.True(jumpStart > resetStart, "Reset should restore safe actor state before jump input is consumed.");
        Assert.True(movementStart > resetStart, "Reset should not discard horizontal movement input for the same frame.");

        var rom = GameBoyRomCompiler.CompileSource(source, Path.GetDirectoryName(sourcePath));
        Assert.Equal(32768, rom.Length);
    }

    [Fact]
    public void GameBoy_runner_keeps_ground_alignment_and_reset_animation_state()
    {
        var sourcePath = RepositoryFile("samples/runner/runner.rs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("StartY = 105", source);
        Assert.Contains("WorldOriginY = 41", source);
        Assert.Contains("y = Player.StartY;", source);
        Assert.Equal(1, CountOccurrences(source, "y = Player.StartY;"));
        Assert.Equal(1, CountOccurrences(source, "player.Land(footTile + Player.WorldOriginY);"));
        Assert.DoesNotContain("player.y = 77;", source);
        Assert.Contains("""world.Load("maps/runner.tmj");""", source);
        Assert.DoesNotContain("world.Column(", source);
        Assert.DoesNotContain("world.Flags(", source);
        Assert.DoesNotContain("world.Map(", source);
        Assert.DoesNotContain("map_column(", source);
        Assert.DoesNotContain("tilemap.Set(", source);
        Assert.Contains("footTile = camera.AabbHitTop(Player.ScreenX, footWorldY - CollisionProbe.LandingSearchTopOffset, sprite_width(mario_player), CollisionProbe.LandingSearchHeight, CollisionFlag.Solid);", source);
        Assert.Contains("player.velocityY < World.SignedVelocityWrap", source);
        Assert.Contains("player.velocityY != 0", source);
        Assert.DoesNotContain("camera_span_has_flags(", source);
        Assert.DoesNotContain("failTile", source);
        Assert.DoesNotContain("hazardHit", source);
        Assert.DoesNotContain("if (footTile != 3)", source);

        var resetStart = source.IndexOf("inline void ResolveReset(PlayerState player)", StringComparison.Ordinal);
        Assert.True(resetStart >= 0);
        var resetEnd = source.IndexOf("void setup_video()", resetStart, StringComparison.Ordinal);
        Assert.True(resetEnd > resetStart);
        var resetBlock = source[resetStart..resetEnd];
        Assert.DoesNotContain("frame = 0;", resetBlock);
        Assert.DoesNotContain("displayFlipX = false;", resetBlock);
        Assert.DoesNotContain("animTick = 0;", resetBlock);
        Assert.DoesNotContain("view.moving = 0;", resetBlock);

        var resetCall = source.IndexOf("frame.ResolveReset(player);", StringComparison.Ordinal);
        var jumpCall = source.IndexOf("player.HandleJumpInput();", StringComparison.Ordinal);
        Assert.True(jumpCall > resetCall);

        var rom = GameBoyRomCompiler.CompileSource(source, Path.GetDirectoryName(sourcePath));
        Assert.Equal(32768, rom.Length);
    }

    [Fact]
    public void Compiles_long_if_body_with_map_streaming()
    {
        const string source = """
                              void main() {
                                  video_init();
                                  map_column(0, 0, 0, 3, 5);
                                  map_column(1, 0, 5, 3, 5);
                                  i16 fine = 0;
                                  i16 streamColumn = 20;
                                  i16 mapColumn = 0;
                                  while (true) {
                                      video_wait_vblank();
                                      fine = fine + 1;
                                      if (fine == 8) {
                                          fine = 0;
                                          map_stream_column(streamColumn, mapColumn, 11, 4);
                                          streamColumn = streamColumn + 1;
                                          if (streamColumn == 32) {
                                              streamColumn = 0;
                                          }
                                          mapColumn = mapColumn + 1;
                                          if (mapColumn == 2) {
                                              mapColumn = 0;
                                          }
                                      }
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0xC2]), "ROM should use an absolute conditional JP for long if bodies.");
    }

    private static byte HeaderChecksum(byte[] rom)
    {
        var checksum = 0;
        for (var i = 0x0134; i <= 0x014C; i++)
        {
            checksum = checksum - rom[i] - 1;
        }

        return (byte)checksum;
    }

    private static string WriteSpriteAsset(string fileName, string json)
    {
        var directory = Path.Combine(Path.GetTempPath(), "RetroSharp.GameBoy.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, fileName), json);
        return directory;
    }

    private static string WriteSpritePng(string fileName, int frameWidth, int frameHeight, params string[][] frames)
    {
        var directory = Path.Combine(Path.GetTempPath(), "RetroSharp.GameBoy.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        var width = frameWidth * frames.Length;
        var height = frameHeight;
        var rgba = new byte[width * height * 4];
        var palette = new[]
        {
            (R: (byte)0x00, G: (byte)0x00, B: (byte)0x00, A: (byte)0x00),
            (R: (byte)0xE0, G: (byte)0xF8, B: (byte)0xD0, A: (byte)0xFF),
            (R: (byte)0x88, G: (byte)0xC0, B: (byte)0x70, A: (byte)0xFF),
            (R: (byte)0x34, G: (byte)0x68, B: (byte)0x56, A: (byte)0xFF),
        };

        for (var frameIndex = 0; frameIndex < frames.Length; frameIndex++)
        {
            var frame = frames[frameIndex];
            for (var y = 0; y < frameHeight; y++)
            {
                for (var x = 0; x < frameWidth; x++)
                {
                    var color = palette[frame[y][x] - '0'];
                    var targetX = frameIndex * frameWidth + x;
                    var offset = (y * width + targetX) * 4;
                    rgba[offset] = color.R;
                    rgba[offset + 1] = color.G;
                    rgba[offset + 2] = color.B;
                    rgba[offset + 3] = color.A;
                }
            }
        }

        File.WriteAllBytes(Path.Combine(directory, fileName), EncodeRgbaPng(width, height, rgba));
        return directory;
    }

    private static string WriteGrayscaleSpritePng(string fileName, int frameWidth, int frameHeight, params string[][] frames)
    {
        var directory = Path.Combine(Path.GetTempPath(), "RetroSharp.GameBoy.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        var width = frameWidth * frames.Length;
        var height = frameHeight;
        var rgba = new byte[width * height * 4];
        var palette = new[]
        {
            (R: (byte)0x00, G: (byte)0x00, B: (byte)0x00, A: (byte)0x00),
            (R: (byte)0xFF, G: (byte)0xFF, B: (byte)0xFF, A: (byte)0xFF),
            (R: (byte)0xB8, G: (byte)0xB8, B: (byte)0xB8, A: (byte)0xFF),
            (R: (byte)0x00, G: (byte)0x00, B: (byte)0x00, A: (byte)0xFF),
        };

        for (var frameIndex = 0; frameIndex < frames.Length; frameIndex++)
        {
            var frame = frames[frameIndex];
            for (var y = 0; y < frameHeight; y++)
            {
                for (var x = 0; x < frameWidth; x++)
                {
                    var color = palette[frame[y][x] - '0'];
                    var targetX = frameIndex * frameWidth + x;
                    var offset = (y * width + targetX) * 4;
                    rgba[offset] = color.R;
                    rgba[offset + 1] = color.G;
                    rgba[offset + 2] = color.B;
                    rgba[offset + 3] = color.A;
                }
            }
        }

        File.WriteAllBytes(Path.Combine(directory, fileName), EncodeRgbaPng(width, height, rgba));
        return directory;
    }

    private static void WriteTiledTilesheetPng(string directory, string fileName, int tileWidth, int tileHeight, params byte[] tileColors)
    {
        var width = tileWidth * tileColors.Length;
        var height = tileHeight;
        var rgba = new byte[width * height * 4];
        var palette = new[]
        {
            (R: (byte)0xFF, G: (byte)0xFF, B: (byte)0xFF, A: (byte)0xFF),
            (R: (byte)0xB8, G: (byte)0xB8, B: (byte)0xB8, A: (byte)0xFF),
            (R: (byte)0x68, G: (byte)0x68, B: (byte)0x68, A: (byte)0xFF),
            (R: (byte)0x00, G: (byte)0x00, B: (byte)0x00, A: (byte)0xFF),
        };

        for (var tile = 0; tile < tileColors.Length; tile++)
        {
            var color = palette[tileColors[tile]];
            for (var y = 0; y < tileHeight; y++)
            {
                for (var x = 0; x < tileWidth; x++)
                {
                    var offset = (y * width + tile * tileWidth + x) * 4;
                    rgba[offset] = color.R;
                    rgba[offset + 1] = color.G;
                    rgba[offset + 2] = color.B;
                    rgba[offset + 3] = color.A;
                }
            }
        }

        File.WriteAllBytes(Path.Combine(directory, fileName), EncodeRgbaPng(width, height, rgba));
    }

    private static string SpriteJson(params string[][] frames)
    {
        var frameJson = string.Join(
            ",",
            frames.Select(frame => "[" + string.Join(",", frame.Select(row => $"\"{row}\"")) + "]"));

        return $$"""
                 {
                   "platforms": {
                     "gb": {
                       "frames": [{{frameJson}}]
                     }
                   }
                 }
                 """;
    }

    private static string[] Rows(int width, int height, params string[] overrides)
    {
        var rows = Enumerable.Repeat(new string('0', width), height).ToArray();
        for (var i = 0; i < overrides.Length; i++)
        {
            rows[i] = overrides[i];
        }

        return rows;
    }

    private static byte[] EncodeRgbaPng(int width, int height, byte[] rgba)
    {
        using var output = new MemoryStream();
        output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr[0..4], width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr[4..8], height);
        ihdr[8] = 8;
        ihdr[9] = 6;
        WritePngChunk(output, "IHDR", ihdr);

        using var raw = new MemoryStream();
        for (var y = 0; y < height; y++)
        {
            raw.WriteByte(0);
            raw.Write(rgba, y * width * 4, width * 4);
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            raw.Position = 0;
            raw.CopyTo(zlib);
        }

        WritePngChunk(output, "IDAT", compressed.ToArray());
        WritePngChunk(output, "IEND", []);
        return output.ToArray();
    }

    private static void WritePngChunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);

        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);

        var crc = Crc32(typeBytes, data);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        output.Write(crcBytes);
    }

    private static uint Crc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in type)
        {
            crc = UpdateCrc32(crc, value);
        }

        foreach (var value in data)
        {
            crc = UpdateCrc32(crc, value);
        }

        return crc ^ 0xFFFFFFFFu;
    }

    private static uint UpdateCrc32(uint crc, byte value)
    {
        crc ^= value;
        for (var i = 0; i < 8; i++)
        {
            crc = (crc & 1) == 0 ? crc >> 1 : 0xEDB88320u ^ (crc >> 1);
        }

        return crc;
    }

    private static bool ContainsSequence(byte[] bytes, byte[] sequence)
    {
        return IndexOfSequence(bytes, sequence) >= 0;
    }

    private static int IndexOfSequence(byte[] bytes, byte[] sequence)
    {
        for (var i = 0; i <= bytes.Length - sequence.Length; i++)
        {
            var matches = true;
            for (var j = 0; j < sequence.Length; j++)
            {
                if (bytes[i + j] != sequence[j])
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                return i;
            }
        }

        return -1;
    }

    private static int ReadLittleEndian16(byte[] bytes, int offset)
    {
        return bytes[offset] | bytes[offset + 1] << 8;
    }

    private static string RepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"Could not find repository file '{relativePath}'.");
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static GameBoyVideoProgram CompileVideoProgram(string source)
    {
        return CompileVideoProgram(source, null);
    }

    private static GameBoyVideoProgram CompileVideoProgram(string source, string? baseDirectory)
    {
        var parse = new SomeParser().Parse(source);
        if (parse.IsFailure)
        {
            throw new InvalidOperationException(parse.Error);
        }

        return GameBoyVideoProgram.FromProgram(parse.Value, baseDirectory);
    }
}
