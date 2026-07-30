namespace RetroSharp.NES.Tests;

using RetroSharp.GameBoy;
using RetroSharp.NES;
using RetroSharp.Parser;
using RetroSharp.Sdk;
using Xunit;
using static RetroSharp.NES.Tests.NesTestAssets;

public partial class NesRomCompilerTests
{
    [Fact]
    public void Compiles_parameterless_user_functions_like_inline_video_blocks()
    {
        const string directSource = """
                                    void Main() {
                                        Video.Init();
                                        Palette.Set(0, 15);
                                        Palette.Set(1, 39);
                                        Tilemap.Set(14, 12, 1);
                                        Tilemap.Set(15, 12, 2);
                                        Video.Present();
                                        return;
                                    }
                                    """;

        const string functionSource = """
                                      void setup_palette() {
                                          Palette.Set(0, 15);
                                          Palette.Set(1, 39);
                                          return;
                                      }

                                      void draw_mark() {
                                          Tilemap.Set(14, 12, 1);
                                          Tilemap.Set(15, 12, 2);
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

        Assert.Equal(NesRomCompiler.CompileSource(directSource), NesRomCompiler.CompileSource(functionSource));
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

        Assert.Equal(NesRomCompiler.CompileSource(directSource), NesRomCompiler.CompileSource(functionSource));
    }

    [Fact]
    public void Compiles_parameterized_runtime_user_functions_like_inline_video_blocks()
    {
        const string directSource = """
                                    void Main() {
                                        Video.Init();
                                        World.Column(0, 1, 2);
                                        World.Column(1, 3, 4);
                                        World.Map(2, 10, 2);
                                        Camera.Init(2, 10, 2);
                                        Camera.SetPosition(4, 0);
                                        return;
                                    }
                                    """;

        const string functionSource = """
                                      void apply_camera(u8 amount) {
                                          Camera.SetPosition(amount, 0);
                                          return;
                                      }

                                      void Main() {
                                          Video.Init();
                                          World.Column(0, 1, 2);
                                          World.Column(1, 3, 4);
                                          World.Map(2, 10, 2);
                                          Camera.Init(2, 10, 2);
                                          apply_camera(4);
                                          return;
                                      }
                                      """;

        Assert.Equal(NesRomCompiler.CompileSource(directSource), NesRomCompiler.CompileSource(functionSource));
    }

    [Fact]
    public void Inlined_user_function_locals_are_unique_per_call()
    {
        const string source = """
                              void apply_camera(u8 x) {
                                  u8 scratch = x;
                                  Camera.SetPosition(scratch, 0);
                                  return;
                              }

                              void Main() {
                                  Video.Init();
                                  World.Column(0, 1, 2);
                                  World.Column(1, 3, 4);
                                  World.Map(2, 10, 2);
                                  Camera.Init(2, 10, 2);
                                  apply_camera(14);
                                  apply_camera(15);
                                  return;
                              }
                              """;

        _ = NesRomCompiler.CompileSource(source);
    }

    [Fact]
    public void Compiles_static_class_const_groups_like_enum_groups()
    {
        const string enumSource = """
                                  enum World { Width = 2, StreamY = 10, Height = 2 }

                                  void Main() {
                                      Video.Init();
                                      World.Column(0, 1, 2);
                                      World.Column(1, 3, 4);
                                      World.Map(World.Width, World.StreamY, World.Height);
                                      Camera.Init(World.Width, World.StreamY, World.Height);
                                      Camera.SetPosition(4, 0);
                                      return;
                                  }
                                  """;

        const string staticClassSource = """
                                          static class World { const i16 Width = 2; const i16 StreamY = 10; const i16 Height = 2; }

                                          void Main() {
                                              Video.Init();
                                              World.Column(0, 1, 2);
                                              World.Column(1, 3, 4);
                                              World.Map(World.Width, World.StreamY, World.Height);
                                              Camera.Init(World.Width, World.StreamY, World.Height);
                                              Camera.SetPosition(4, 0);
                                              return;
                                          }
                                          """;

        Assert.Equal(NesRomCompiler.CompileSource(enumSource), NesRomCompiler.CompileSource(staticClassSource));
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
                                       return;
                                   }
                                   """;

        const string expressionSource = """
                                        u8 choose_speed(u8 moving, u8 fast) => moving != 0 ? fast : 0;

                                        void Main() {
                                            Video.Init();
                                            u8 moving = 1;
                                            u8 fast = 2;
                                            u8 speed = choose_speed(moving, fast);
                                            return;
                                        }
                                        """;

        Assert.Equal(NesRomCompiler.CompileSource(blockSource), NesRomCompiler.CompileSource(expressionSource));
    }

    [Fact]
    public void Compiles_default_parameter_value_functions_like_explicit_arguments()
    {
        const string explicitSource = """
                                      u8 step(u8 value, u8 amount = value + 1) => value + amount;

                                      void Main() {
                                          Video.Init();
                                          u8 next = step(4, 5);
                                          return;
                                      }
                                      """;

        const string omittedSource = """
                                     u8 step(u8 value, u8 amount = value + 1) => value + amount;

                                     void Main() {
                                         Video.Init();
                                         u8 next = step(4);
                                         return;
                                     }
                                     """;

        Assert.Equal(NesRomCompiler.CompileSource(explicitSource), NesRomCompiler.CompileSource(omittedSource));
    }

    [Fact]
    public void Compiles_named_argument_value_functions_like_explicit_arguments()
    {
        const string explicitSource = """
                                      u8 step(u8 value, u8 amount = value + 1) => value + amount;

                                      void Main() {
                                          Video.Init();
                                          u8 next = step(4, 5);
                                          return;
                                      }
                                      """;

        const string namedSource = """
                                   u8 step(u8 value, u8 amount = value + 1) => value + amount;

                                   void Main() {
                                       Video.Init();
                                       u8 next = step(amount: 5, value: 4);
                                       return;
                                   }
                                   """;

        const string omittedNamedSource = """
                                          u8 step(u8 value, u8 amount = value + 1) => value + amount;

                                          void Main() {
                                              Video.Init();
                                              u8 next = step(value: 4);
                                              return;
                                          }
                                          """;

        var explicitRom = NesRomCompiler.CompileSource(explicitSource);
        Assert.Equal(explicitRom, NesRomCompiler.CompileSource(namedSource));
        Assert.Equal(explicitRom, NesRomCompiler.CompileSource(omittedNamedSource));
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
                                          return;
                                      }
                                      """;

        const string letSource = """
                                 void Main() {
                                     Video.Init();
                                     let speed = 2;
                                     u8 next = speed + 1;
                                     u8 sink = 0;
                                     sink = next;
                                     return;
                                 }
                                 """;

        Assert.Equal(NesRomCompiler.CompileSource(variableSource), NesRomCompiler.CompileSource(letSource));
    }

    [Fact]
    public void Compiles_inline_pure_helper_contracts_like_equivalent_current_helpers()
    {
        const string implicitSource = """
                                      u8 step(u8 value, u8 amount = 1) => value + amount;

                                      void Main() {
                                          Video.Init();
                                          u8 next = step(4);
                                          return;
                                      }
                                      """;

        const string explicitSource = """
                                      inline pure u8 step(u8 value, u8 amount = 1) => value + amount;

                                      void Main() {
                                          Video.Init();
                                          u8 next = step(4);
                                          return;
                                      }
                                      """;

        Assert.Equal(NesRomCompiler.CompileSource(implicitSource), NesRomCompiler.CompileSource(explicitSource));
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
                                             return;
                                         }
                                         """;

        const string switchSource = """
                                    void Main() {
                                        Video.Init();
                                        u8 state = 2;
                                        u8 speed = state switch { 0 => 0, 1 => 2, 2..5 => 3, _ => 1 };
                                        u8 sink = 0;
                                        sink = speed;
                                        return;
                                    }
                                    """;

        Assert.Equal(NesRomCompiler.CompileSource(conditionalSource), NesRomCompiler.CompileSource(switchSource));
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
                                        return;
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
                                          return;
                                      }
                                      """;

        Assert.Equal(NesRomCompiler.CompileSource(staticSource), NesRomCompiler.CompileSource(receiverSource));
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
                                        return;
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
                                       return;
                                   }
                                   """;

        Assert.Equal(NesRomCompiler.CompileSource(structSource), NesRomCompiler.CompileSource(classSource));
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
                                        return;
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
                                       return;
                                   }
                                   """;

        Assert.Equal(NesRomCompiler.CompileSource(structSource), NesRomCompiler.CompileSource(classSource));
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
                                      return;
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
                                       return;
                                   }
                                   """;

        Assert.Equal(NesRomCompiler.CompileSource(flatSource), NesRomCompiler.CompileSource(classSource));
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
                                        return;
                                    }
                                    """;

        const string pipelineSource = """
                                      u8 Clamp(u8 value, u8 min, u8 max) => value < min ? min : value > max ? max : value;
                                      u8 SnapToTile(u8 value) => value & 0xF8;

                                      void Main() {
                                          Video.Init();
                                          u8 value = 130;
                                          u8 snapped = value |> Clamp(0, 120) |> SnapToTile();
                                          return;
                                      }
                                      """;

        Assert.Equal(NesRomCompiler.CompileSource(nestedSource), NesRomCompiler.CompileSource(pipelineSource));
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
                                        return;
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
                                          return;
                                      }
                                      """;

        Assert.Equal(NesRomCompiler.CompileSource(nestedSource), NesRomCompiler.CompileSource(pipelineSource));
    }

    [Fact]
    public void Nes_drawing_sample_compiles_with_helper_functions()
    {
        var source = File.ReadAllText(RepositoryFile("samples/static-drawing/drawing.rs"));

        Assert.Contains("void DrawFace()", source);

        _ = NesRomCompiler.CompileSource(source);
    }

    [Fact]
    public void Let_word_inference_matches_explicit_storage_and_lowering_on_nes()
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
                                   return;
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
                                   return;
                               }
                               """;

        Assert.Equal(
            NesRomCompiler.CompileSource(explicitSource),
            NesRomCompiler.CompileSource(inferredSource));
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
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.True(ContainsSequence(prg, [0xA9, 0x02, 0x85, 0x00]), "Const conditional expression should fold to one immediate store with no runtime conditional.");
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
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0x48, 0xA5, 0x01, 0x38, 0xE5, 0x02, 0x85, 0xE9, 0x68, 0x38, 0xE5, 0xE9, 0x85, 0x03]), "Nested subtraction should preserve the outer left operand on the CPU stack while evaluating the right operand.");
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
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0x38, 0xE5, 0x01, 0x48, 0xA5, 0x02, 0x38, 0xE5, 0x03, 0x85, 0xE9, 0x68, 0xC5, 0xE9]), "Nested relational compares should preserve the left expression on the CPU stack while evaluating the right expression.");
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
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0x38, 0xE5, 0x01, 0x48, 0xA5, 0x02, 0x38, 0xE5, 0x03, 0x85, 0xE9, 0x68, 0xC5, 0xE9, 0xD0]), "Nested == should preserve the left expression on the CPU stack before comparing.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0x38, 0xE5, 0x01, 0x48, 0xA5, 0x02, 0x38, 0xE5, 0x03, 0x85, 0xE9, 0x68, 0xC5, 0xE9, 0xF0]), "Nested != should preserve the left expression on the CPU stack before comparing.");
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
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.True(ContainsSequence(prg, [0xA5, 0x01, 0xC9, 0x03, 0xB0]), "for condition should compare i with 3 and branch out when i >= 3.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x01, 0x18, 0x65, 0x00, 0x85, 0x00]), "for body should use direct x += i zero-page arithmetic.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x01, 0x18, 0x69, 0x01, 0x85, 0x01]), "for increment should use direct i += 1 zero-page arithmetic.");
    }

    [Fact]
    public void Compiles_runtime_indexed_array_access_without_helper_calls()
    {
        const string source = """
                              void Main() {
                                  u8 values[4];
                                  u8 i = 1;
                                  values[i] += 1;
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.True(ContainsSequence(prg, [0xA5, 0x04, 0xAA]), "runtime array indexing should transfer the byte index to X without a helper call or implicit bounds check.");
        Assert.True(ContainsSequence(prg, [0xB5, 0x00, 0x18, 0x69, 0x01, 0x95, 0x00]), "values[i] += 1 should use zero-page indexed load/add/store without a helper call.");
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
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.True(ContainsSequence(prg, [0xA9, 0x01, 0x85, 0x00]), "Switch subject should stay a normal byte-backed local.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0xC9, 0x00]), "First switch case should compare the subject directly to the case literal.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0xC9, 0x01]), "Second switch case should compare the subject directly to the case literal.");
        Assert.True(ContainsSequence(prg, [0xA9, 0x0A, 0x85, 0x01]), "First switch case should lower to the direct branch body.");
        Assert.True(ContainsSequence(prg, [0xA9, 0x14, 0x85, 0x01]), "Second switch case should lower to the direct branch body.");
        Assert.True(ContainsSequence(prg, [0xA9, 0x1E, 0x85, 0x01]), "Default switch case should lower to the direct fallback body.");
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
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0xC9, 0x00]), "Multi-value switch case should compare the subject with the first literal.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0xC9, 0x01]), "Multi-value switch case should compare the subject with the second literal.");
        Assert.True(ContainsSequence(prg, [0xA9, 0x0A, 0x85, 0x01]), "Multi-value switch case should share one direct branch body.");
        Assert.True(ContainsSequence(prg, [0xA9, 0x1E, 0x85, 0x01]), "Default switch case should remain a direct fallback body.");
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
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0xC9, 0x01, 0x90]), "Range switch case should compare the subject with the inclusive lower bound and branch out when subject < start.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0xC9, 0x04, 0xB0]), "Range switch case should compare the subject with the exclusive upper bound and branch out when subject >= end.");
        Assert.True(ContainsSequence(prg, [0xA9, 0x0A, 0x85, 0x01]), "Range switch case should share one direct branch body.");
        Assert.True(ContainsSequence(prg, [0xA9, 0x1E, 0x85, 0x01]), "Default switch case should remain a direct fallback body.");
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
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0x09, 0x01, 0x85, 0x00]), "set_flag(flags, 1) should inline as LDA/ORA immediate/STA with no call ABI.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0x29, 0xFD, 0x85, 0x00]), "clear_flag(flags, 2) should inline as LDA/AND immediate/STA with no call ABI.");
    }

    [Fact]
    public void Compiles_explicit_casts_as_zero_cost_expression_markers()
    {
        const string source = """
                              void Main() {
                                  u8 flags = 0;
                                  u16 wide = 1;
                                  flags = (u8)(wide | 2);
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.True(ContainsSequence(prg, [0xA5, 0x01, 0x09, 0x02, 0x85, 0x00]), "Explicit casts should disappear before NES lowering and leave the direct expression sequence.");
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
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0xC9, 0x00, 0xF0]), "&& should false-branch after the left operand.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x01, 0xC9, 0x00, 0xF0]), "&& should false-branch after the right operand.");
        Assert.True(ContainsSequence(prg, [0xA9, 0x00, 0x85, 0x02]), "&& should materialize false as 0 in the destination byte.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0xC9, 0x00, 0xD0]), "|| and ! should true-branch using direct compares.");
        Assert.True(ContainsSequence(prg, [0xA9, 0x00, 0x85, 0x03]), "|| should materialize false as 0 in the destination byte.");
        Assert.True(ContainsSequence(prg, [0xA9, 0x00, 0x85, 0x04]), "! should materialize false as 0 in the destination byte.");
    }

    [Fact]
    public void Compiles_conditional_value_expressions_as_direct_branches()
    {
        const string source = """
                              void Main() {
                                  u8 moving = 1;
                                  u8 fast = 2;
                                  u8 speed = moving != 0 ? fast : 0;
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0xC9, 0x00, 0xF0]), "Conditional expression should branch to the false value when the condition is false.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x01, 0x4C]), "Conditional expression should load the true branch value and jump over the false branch.");
        Assert.True(ContainsSequence(prg, [0xA9, 0x00, 0x85, 0x02]), "Conditional expression should load the false branch value and store one selected byte.");
    }








}
