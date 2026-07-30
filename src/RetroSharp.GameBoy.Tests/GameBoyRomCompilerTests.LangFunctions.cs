namespace RetroSharp.GameBoy.Tests;

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
    public void Compiles_parameterless_user_functions_like_inline_video_blocks()
    {
        const string directSource = """
                                    void Main() {
                                        Video.Init();
                                        Palette.Set(0, 0);
                                        Palette.Set(1, 1);
                                        Tilemap.Set(8, 7, 1);
                                        Tilemap.Set(9, 7, 2);
                                        Video.Present();
                                        return;
                                    }
                                    """;

        const string functionSource = """
                                      void setup_palette() {
                                          Palette.Set(0, 0);
                                          Palette.Set(1, 1);
                                          return;
                                      }

                                      void draw_mark() {
                                          Tilemap.Set(8, 7, 1);
                                          Tilemap.Set(9, 7, 2);
                                          return;
                                      }

                                      void Main() {
                                          Video.Init();
                                          setup_palette();
                                          draw_mark();
                                          Video.Present();
                                          return;
                                      }
                                      """;

        Assert.Equal(GameBoyRomCompiler.CompileSource(directSource), GameBoyRomCompiler.CompileSource(functionSource));
    }

    [Fact]
    public void Compiles_parameterized_static_user_functions_like_inline_video_blocks()
    {
        const string directSource = """
                                    void Main() {
                                        Video.Init();
                                        Tilemap.Set(14, 12, 1);
                                        Tilemap.Set(15, 12, 2);
                                        Video.Present();
                                        return;
                                    }
                                    """;

        const string functionSource = """
                                      void draw_tile(u8 x, u8 tile) {
                                          Tilemap.Set(x, 12, tile);
                                          return;
                                      }

                                      void Main() {
                                          Video.Init();
                                          draw_tile(14, 1);
                                          draw_tile(15, 2);
                                          Video.Present();
                                          return;
                                      }
                                      """;

        Assert.Equal(GameBoyRomCompiler.CompileSource(directSource), GameBoyRomCompiler.CompileSource(functionSource));
    }

    [Fact]
    public void Compiles_parameterized_runtime_user_functions_like_inline_video_blocks()
    {
        const string directSource = """
                                    void Main() {
                                        Video.Init();
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

                                      void Main() {
                                          Video.Init();
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

                                   void Main() {
                                       Video.Init();
                                       u8 moving = 1;
                                       u8 fast = 2;
                                       u8 speed = choose_speed(moving, fast);
                                   }
                                   """;

        const string expressionSource = """
                                        u8 choose_speed(u8 moving, u8 fast) => moving != 0 ? fast : 0;

                                        void Main() {
                                            Video.Init();
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

                                      void Main() {
                                          Video.Init();
                                          u8 next = step(4, 5);
                                      }
                                      """;

        const string omittedSource = """
                                     u8 step(u8 value, u8 amount = value + 1) => value + amount;

                                     void Main() {
                                         Video.Init();
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

                                      void Main() {
                                          Video.Init();
                                          u8 next = step(4, 5);
                                      }
                                      """;

        const string namedSource = """
                                   u8 step(u8 value, u8 amount = value + 1) => value + amount;

                                   void Main() {
                                       Video.Init();
                                       u8 next = step(amount: 5, value: 4);
                                   }
                                   """;

        const string omittedNamedSource = """
                                          u8 step(u8 value, u8 amount = value + 1) => value + amount;

                                          void Main() {
                                              Video.Init();
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
                                      void Main() {
                                          Video.Init();
                                          u8 speed = 2;
                                          u8 next = speed + 1;
                                          u8 sink = 0;
                                          sink = next;
                                      }
                                      """;

        const string letSource = """
                                 void Main() {
                                     Video.Init();
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

                                      void Main() {
                                          Video.Init();
                                          u8 next = step(4);
                                      }
                                      """;

        const string explicitSource = """
                                      inline pure u8 step(u8 value, u8 amount = 1) => value + amount;

                                      void Main() {
                                          Video.Init();
                                          u8 next = step(4);
                                      }
                                      """;

        Assert.Equal(GameBoyRomCompiler.CompileSource(implicitSource), GameBoyRomCompiler.CompileSource(explicitSource));
    }

    [Fact]
    public void Compiles_switch_expression_like_equivalent_conditional_expression()
    {
        const string conditionalSource = """
                                         void Main() {
                                             Video.Init();
                                             u8 state = 2;
                                             u8 speed = state == 0 ? 0 : state == 1 ? 2 : state >= 2 && state < 5 ? 3 : 1;
                                             u8 sink = 0;
                                             sink = speed;
                                         }
                                         """;

        const string switchSource = """
                                    void Main() {
                                        Video.Init();
                                        u8 state = 2;
                                        u8 speed = state switch { 0 => 0, 1 => 2, 2..5 => 3, _ => 1 };
                                        u8 sink = 0;
                                        sink = speed;
                                    }
                                    """;

        Assert.Equal(GameBoyRomCompiler.CompileSource(conditionalSource), GameBoyRomCompiler.CompileSource(switchSource));
    }

    [Fact]
    public void Compiles_receiver_method_calls_like_static_helper_calls()
    {
        const string staticSource = """
                                    struct Actor { u8 x; }

                                    inline void Move(this Actor actor, u8 dx) {
                                        actor.x += dx;
                                    }

                                    void Main() {
                                        Video.Init();
                                        Actor actor;
                                        Move(actor, 2);
                                    }
                                    """;

        const string receiverSource = """
                                      struct Actor { u8 x; }

                                      inline void Move(this Actor actor, u8 dx) {
                                          actor.x += dx;
                                      }

                                      void Main() {
                                          Video.Init();
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

                                    void Main() {
                                        Video.Init();
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

                                   void Main() {
                                       Video.Init();
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

                                    void Main() {
                                        Video.Init();
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

                                   void Main() {
                                       Video.Init();
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

                                  void Main() {
                                      Video.Init();
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

                                   void Main() {
                                       Video.Init();
                                       Actor actor = { x: Tuning.Step };
                                       actor.x = Tuning.Apply(actor.x);
                                   }
                                   """;

        Assert.Equal(GameBoyRomCompiler.CompileSource(flatSource), GameBoyRomCompiler.CompileSource(classSource));
    }

    [Fact]
    public void Compiles_pipeline_expression_like_nested_helper_calls()
    {
        const string nestedSource = """
                                    u8 Clamp(u8 value, u8 min, u8 max) => value < min ? min : value > max ? max : value;
                                    u8 SnapToTile(u8 value) => value & 0xF8;

                                    void Main() {
                                        Video.Init();
                                        u8 value = 130;
                                        u8 snapped = SnapToTile(Clamp(value, 0, 120));
                                    }
                                    """;

        const string pipelineSource = """
                                      u8 Clamp(u8 value, u8 min, u8 max) => value < min ? min : value > max ? max : value;
                                      u8 SnapToTile(u8 value) => value & 0xF8;

                                      void Main() {
                                          Video.Init();
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

                                    void Main() {
                                        Video.Init();
                                        Actor actor = { x: 130 };
                                        u8 clamped = Clamp(X(actor), max: 120);
                                    }
                                    """;

        const string pipelineSource = """
                                      struct Actor { u8 x; }
                                      u8 X(this Actor actor) => actor.x;
                                      u8 Clamp(u8 value, u8 min = 0, u8 max = 120) => value < min ? min : value > max ? max : value;

                                      void Main() {
                                          Video.Init();
                                          Actor actor = { x: 130 };
                                          u8 clamped = actor.X() |> Clamp(max: 120);
                                      }
                                      """;

        Assert.Equal(GameBoyRomCompiler.CompileSource(nestedSource), GameBoyRomCompiler.CompileSource(pipelineSource));
    }

    [Fact]
    public void GameBoy_drawing_sample_compiles_with_helper_functions()
    {
        var sourcePath = RepositoryFile("samples/static-drawing/drawing.rs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("void DrawFace()", source);

        _ = GameBoyRomCompiler.CompileSource(source, Path.GetDirectoryName(sourcePath));
    }

    [Fact]
    public void Let_word_inference_matches_explicit_storage_and_lowering_on_game_boy()
    {
        const string declarations = """
                                    const i16 FootOffset = 31;
                                    const u16 DistanceStep = 4u16;
                                    struct State { i16 y; i16 hit; u16 distance; u8 frame; }
                                    """;
        const string setup = """
                              Video.Init();
                              State state;
                              state.y = 273;
                              state.hit = -1;
                              state.distance = 300u16;
                              state.frame = 7;
                              """;
        var inferredSource = $$"""
                               {{declarations}}
                               void Main() {
                                   {{setup}}
                                   let footWorldY = state.y + FootOffset;
                                   let noHit = state.hit;
                                   let farDistance = state.distance + DistanceStep;
                                   let nextFrame = state.frame + 1;
                               }
                               """;
        var explicitSource = $$"""
                               {{declarations}}
                               void Main() {
                                   {{setup}}
                                   i16 footWorldY = state.y + FootOffset;
                                   i16 noHit = state.hit;
                                   u16 farDistance = state.distance + DistanceStep;
                                   u8 nextFrame = state.frame + 1;
                               }
                               """;

        Assert.Equal(
            GameBoyRomCompiler.CompileSource(explicitSource),
            GameBoyRomCompiler.CompileSource(inferredSource));
    }

    [Fact]
    public void Compiles_const_conditional_expressions_as_immediates_without_runtime_branch()
    {
        const string source = """
                              const UseFast = true;
                              const Speed = UseFast ? 2 : 0;

                              void Main() {
                                  Video.Init();
                                  u8 speed = Speed;
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.True(ContainsSequence(rom, [0x3E, 0x02, 0xEA, 0x00, 0xC0]), "Const conditional expression should fold to one immediate store with no runtime conditional.");
    }

    [Fact]
    public void Compiles_nested_variable_subtraction_without_reusing_expression_scratch()
    {
        const string source = """
                              void Main() {
                                  u8 a = 10;
                                  u8 b = 7;
                                  u8 c = 2;
                                  u8 result = a - (b - c);
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xF5, 0xFA, 0x01, 0xC0, 0xF5, 0xFA, 0x02, 0xC0, 0x47, 0xF1, 0x90, 0x47, 0xF1, 0x90, 0xEA, 0x03, 0xC0]), "Nested subtraction should preserve each left operand on the CPU stack while evaluating the right operand.");
        Assert.False(ContainsSequence(rom, [0xEA, 0x1C, 0xC1]), "Nested subtraction must not store operands in the shared expression scratch address.");
    }

    [Fact]
    public void Compiles_nested_variable_relational_compare_without_reusing_expression_scratch()
    {
        const string source = """
                              void Main() {
                                  u8 a = 9;
                                  u8 b = 4;
                                  u8 c = 8;
                                  u8 d = 2;
                                  u8 result = 0;
                                  if ((a - b) < (c - d)) {
                                      result = 1;
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xF5, 0xFA, 0x01, 0xC0, 0x47, 0xF1, 0x90, 0xF5, 0xFA, 0x02, 0xC0, 0xF5, 0xFA, 0x03, 0xC0, 0x47, 0xF1, 0x90, 0x47, 0xF1, 0xB8]), "Nested relational compares should preserve the left expression on the CPU stack while evaluating the right expression.");
        Assert.False(ContainsSequence(rom, [0xEA, 0x1C, 0xC1]), "Nested relational compare must not store operands in the shared expression scratch address.");
    }

    [Fact]
    public void Compiles_nested_variable_equality_compare_without_reusing_expression_scratch()
    {
        const string source = """
                              void Main() {
                                  u8 a = 9;
                                  u8 b = 4;
                                  u8 c = 8;
                                  u8 d = 3;
                                  u8 result = 0;
                                  if ((a - b) == (c - d)) {
                                      result = 1;
                                  }
                                  if ((a - b) != (c - d)) {
                                      result = 2;
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xF5, 0xFA, 0x01, 0xC0, 0x47, 0xF1, 0x90, 0xF5, 0xFA, 0x02, 0xC0, 0xF5, 0xFA, 0x03, 0xC0, 0x47, 0xF1, 0x90, 0x47, 0xF1, 0xB8, 0xC2]), "Nested == should preserve the left expression on the CPU stack before comparing.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xF5, 0xFA, 0x01, 0xC0, 0x47, 0xF1, 0x90, 0xF5, 0xFA, 0x02, 0xC0, 0xF5, 0xFA, 0x03, 0xC0, 0x47, 0xF1, 0x90, 0x47, 0xF1, 0xB8, 0xCA]), "Nested != should preserve the left expression on the CPU stack before comparing.");
        Assert.False(ContainsSequence(rom, [0xEA, 0x1C, 0xC1]), "Nested equality compares must not store operands in the shared expression scratch address.");
    }

    [Fact]
    public void Compiles_for_loop_to_direct_branching_without_helper_calls()
    {
        const string source = """
                              void Main() {
                                  u8 x = 0;
                                  for (u8 i = 0; i < 3; i += 1) {
                                      x += i;
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.True(ContainsSequence(rom, [0xFA, 0x01, 0xC0, 0xFE, 0x03, 0xD2]), "for condition should compare the loop local and jump out when i >= 3.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x01, 0xC0, 0x47, 0xFA, 0x00, 0xC0, 0x80, 0xEA, 0x00, 0xC0]), "for body should use direct x += i arithmetic.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x01, 0xC0, 0xC6, 0x01, 0xEA, 0x01, 0xC0]), "for increment should use direct i += 1 arithmetic.");
    }

    [Fact]
    public void Compiles_runtime_indexed_array_access_without_helper_calls()
    {
        const string source = """
                              void Main() {
                                  u8 values[4];
                                  u8 i = 1;
                                  values[i] += 1;
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.True(ContainsSequence(rom, [0xFA, 0x04, 0xC0, 0x21, 0x00, 0xC0, 0x5F, 0x16, 0x00, 0x19]), "runtime array indexing should compute HL from the array base and the byte index without a helper call.");
        Assert.True(ContainsSequence(rom, [0x7E, 0xC6, 0x01, 0x77]), "values[i] += 1 should load, add, and store through HL without a helper call.");
    }

    [Fact]
    public void Compiles_switch_as_direct_compare_branches_without_runtime_helper()
    {
        const string source = """
                              void Main() {
                                  Video.Init();
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
                              void Main() {
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

        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xFE, 0x00]), "Multi-value switch case should compare the subject with the first literal.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xFE, 0x01]), "Multi-value switch case should compare the subject with the second literal.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x0A, 0xEA, 0x01, 0xC0]), "Multi-value switch case should share one direct branch body.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x1E, 0xEA, 0x01, 0xC0]), "Default switch case should remain a direct fallback body.");
    }

    [Fact]
    public void Compiles_switch_case_with_half_open_range_as_direct_bounds_checks()
    {
        const string source = """
                              void Main() {
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

        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xFE, 0x01, 0xDA]), "Range switch case should compare the subject with the inclusive lower bound and jump out when subject < start.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xFE, 0x04, 0xD2]), "Range switch case should compare the subject with the exclusive upper bound and jump out when subject >= end.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x0A, 0xEA, 0x01, 0xC0]), "Range switch case should share one direct branch body.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x1E, 0xEA, 0x01, 0xC0]), "Default switch case should remain a direct fallback body.");
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

                              void Main() {
                                  u8 flags = 0;
                                  flags = set_flag(flags, 1);
                                  flags = clear_flag(flags, 2);
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xF6, 0x01, 0xEA, 0x00, 0xC0]), "set_flag(flags, 1) should inline as LD/OR immediate/store with no call ABI.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xE6, 0xFD, 0xEA, 0x00, 0xC0]), "clear_flag(flags, 2) should inline as LD/AND immediate/store with no call ABI.");
    }

    [Fact]
    public void Compiles_explicit_casts_as_zero_cost_expression_markers()
    {
        const string source = """
                              void Main() {
                                  u8 flags = 0;
                                  u16 wide = 1;
                                  flags = (u8)(wide | 2);
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.True(ContainsSequence(rom, [0xFA, 0x01, 0xC0, 0xF6, 0x02, 0xEA, 0x00, 0xC0]), "Explicit casts should disappear before Game Boy lowering and leave the direct expression sequence.");
    }

    [Fact]
    public void Compiles_logical_value_expressions_as_direct_boolean_materialization()
    {
        const string source = """
                              void Main() {
                                  u8 x = 1;
                                  u8 y = 0;
                                  u8 both = x != 0 && y != 0;
                                  u8 either = x != 0 || y != 0;
                                  u8 notX = !(x != 0);
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

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
                              void Main() {
                                  u8 moving = 1;
                                  u8 fast = 2;
                                  u8 speed = moving != 0 ? fast : 0;
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xFE, 0x00, 0xCA]), "Conditional expression should branch to the false value when the condition is false.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x01, 0xC0, 0xC3]), "Conditional expression should load the true branch value and jump over the false branch.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x00, 0xEA, 0x02, 0xC0]), "Conditional expression should load the false branch value and store one selected byte.");
    }

    [Fact]
    public void Compiles_static_class_const_groups_like_enum_groups()
    {
        const string enumSource = """
                                  enum World { Width = 16, StreamY = 9, Height = 5 }
                                  enum Player { ScreenX = 72 }

                                  void Main() {
                                      Video.Init();
                                      World.Column(0, 1, 2, 3, 4, 5);
                                      World.Flags(0, 0, 0, 1, 1, 1);
                                      World.Map(World.Width, World.StreamY, World.Height);
                                      Camera.Init(World.Width, World.StreamY, World.Height);
                                      i16 cameraX = 0;
                                      i16 playerWorldX = cameraX + Player.ScreenX;
                                      Camera.SetPosition(cameraX, 0);
                                  }
                                  """;

        const string staticClassSource = """
                                          static class Level { const i16 Width = 16; const i16 StreamY = 9; const i16 Height = 5; }
                                          static class Player { const i16 ScreenX = 72; }

                                          void Main() {
                                              Video.Init();
                                              World.Column(0, 1, 2, 3, 4, 5);
                                              World.Flags(0, 0, 0, 1, 1, 1);
                                              World.Map(Level.Width, Level.StreamY, Level.Height);
                                              Camera.Init(Level.Width, Level.StreamY, Level.Height);
                                              i16 cameraX = 0;
                                              i16 playerWorldX = cameraX + Player.ScreenX;
                                              Camera.SetPosition(cameraX, 0);
                                          }
                                          """;

        Assert.Equal(GameBoyRomCompiler.CompileSource(enumSource), GameBoyRomCompiler.CompileSource(staticClassSource));
    }

    [Fact]
    public void Compiles_pixel_struct_receiver_helpers_like_flat_state_updates()
    {
        const string flatSource = """
                                  type Pixel = i16;
                                  enum Player { StartY = 73 }

                                  void Main() {
                                      Video.Init();
                                      Pixel playerY;
                                      Pixel velocityY;
                                      Pixel grounded;
                                      Pixel jumping;
                                      Pixel displayFrame;
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

                                      void Main() {
                                          Video.Init();
                                          PlayerState player;
                                          player.Reset();
                                          player.ApplyGravity();
                                      }
                                      """;

        Assert.Equal(GameBoyRomCompiler.CompileSource(flatSource), GameBoyRomCompiler.CompileSource(receiverSource));
    }

}
