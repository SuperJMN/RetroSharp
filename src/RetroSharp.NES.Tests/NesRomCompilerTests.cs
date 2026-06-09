namespace RetroSharp.NES.Tests;

using RetroSharp.GameBoy;
using RetroSharp.NES;
using Xunit;

public class NesRomCompilerTests
{
    [Fact]
    public void Compiles_video_api_calls_to_an_ines_rom()
    {
        const string source = """
                              void main() {
                                  video_init();
                                  palette_set(0, 15);
                                  palette_set(1, 39);
                                  palette_set(2, 22);
                                  palette_set(3, 48);
                                  tilemap_set(14, 12, 1);
                                  tilemap_set(15, 12, 2);
                                  tilemap_set(16, 12, 1);
                                  tilemap_set(15, 13, 3);
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);

        Assert.Equal(24592, rom.Length);
        Assert.Equal((byte)'N', rom[0]);
        Assert.Equal((byte)'E', rom[1]);
        Assert.Equal((byte)'S', rom[2]);
        Assert.Equal(0x1A, rom[3]);
        Assert.Equal(1, rom[4]);
        Assert.Equal(1, rom[5]);

        var prg = rom.Skip(16).Take(16 * 1024).ToArray();
        var chr = rom.Skip(16 + 16 * 1024).Take(8 * 1024).ToArray();

        Assert.Equal(0x00, prg[^4]);
        Assert.Equal(0xC0, prg[^3]);
        Assert.Contains(chr, b => b != 0);
    }

    [Fact]
    public void Compiles_parameterless_user_functions_like_inline_video_blocks()
    {
        const string directSource = """
                                    void main() {
                                        video_init();
                                        palette_set(0, 15);
                                        palette_set(1, 39);
                                        tilemap_set(14, 12, 1);
                                        tilemap_set(15, 12, 2);
                                        video_present();
                                        return;
                                    }
                                    """;

        const string functionSource = """
                                      void setup_palette() {
                                          palette_set(0, 15);
                                          palette_set(1, 39);
                                          return;
                                      }

                                      void draw_mark() {
                                          tilemap_set(14, 12, 1);
                                          tilemap_set(15, 12, 2);
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

        Assert.Equal(NesRomCompiler.CompileSource(directSource), NesRomCompiler.CompileSource(functionSource));
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

        Assert.Equal(NesRomCompiler.CompileSource(directSource), NesRomCompiler.CompileSource(functionSource));
    }

    [Fact]
    public void Compiles_parameterized_runtime_user_functions_like_inline_video_blocks()
    {
        const string directSource = """
                                    void main() {
                                        video_init();
                                        world_column(0, 1, 2);
                                        world_column(1, 3, 4);
                                        world_map(2, 10, 2);
                                        camera_init(2, 10, 2);
                                        camera_set_position(4, 0);
                                        return;
                                    }
                                    """;

        const string functionSource = """
                                      void apply_camera(u8 amount) {
                                          camera_set_position(amount, 0);
                                          return;
                                      }

                                      void main() {
                                          video_init();
                                          world_column(0, 1, 2);
                                          world_column(1, 3, 4);
                                          world_map(2, 10, 2);
                                          camera_init(2, 10, 2);
                                          apply_camera(4);
                                          return;
                                      }
                                      """;

        Assert.Equal(NesRomCompiler.CompileSource(directSource), NesRomCompiler.CompileSource(functionSource));
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
                                       return;
                                   }
                                   """;

        const string expressionSource = """
                                        u8 choose_speed(u8 moving, u8 fast) => moving != 0 ? fast : 0;

                                        void main() {
                                            video_init();
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

                                      void main() {
                                          video_init();
                                          u8 next = step(4, 5);
                                          return;
                                      }
                                      """;

        const string omittedSource = """
                                     u8 step(u8 value, u8 amount = value + 1) => value + amount;

                                     void main() {
                                         video_init();
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

                                      void main() {
                                          video_init();
                                          u8 next = step(4, 5);
                                          return;
                                      }
                                      """;

        const string namedSource = """
                                   u8 step(u8 value, u8 amount = value + 1) => value + amount;

                                   void main() {
                                       video_init();
                                       u8 next = step(amount: 5, value: 4);
                                       return;
                                   }
                                   """;

        const string omittedNamedSource = """
                                          u8 step(u8 value, u8 amount = value + 1) => value + amount;

                                          void main() {
                                              video_init();
                                              u8 next = step(value: 4);
                                              return;
                                          }
                                          """;

        var explicitRom = NesRomCompiler.CompileSource(explicitSource);
        Assert.Equal(explicitRom, NesRomCompiler.CompileSource(namedSource));
        Assert.Equal(explicitRom, NesRomCompiler.CompileSource(omittedNamedSource));
    }

    [Fact]
    public void Nes_drawing_sample_compiles_with_helper_functions()
    {
        var source = File.ReadAllText(RepositoryFile("samples/nes-drawing/drawing.rs"));

        Assert.Contains("void draw_face()", source);

        var rom = NesRomCompiler.CompileSource(source);

        Assert.Equal(24592, rom.Length);
    }

    [Fact]
    public void Cross_target_camera_sample_compiles_for_game_boy_and_nes()
    {
        var sourcePath = RepositoryFile("samples/cross-target-camera/camera.rs");
        var source = File.ReadAllText(sourcePath);
        var baseDirectory = Path.GetDirectoryName(sourcePath);

        Assert.Equal(32768, GameBoyRomCompiler.CompileSource(source, baseDirectory).Length);
        Assert.Equal(24592, NesRomCompiler.CompileSource(source, baseDirectory).Length);
    }

    [Fact]
    public void Rejects_window_hud_mode_through_nes_capability_check()
    {
        const string source = """
                              void main() {
                                  video_init();
                                  hud_set_tile(window, 0, 0, 1);
                                  return;
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => NesRomCompiler.CompileSource(source));

        Assert.Equal(
            "Target 'nes' does not support Window HUD. Use disable HUD for this target.",
            exception.Message);
    }

    [Fact]
    public void Compiles_disabled_hud_mode_as_no_op_for_nes()
    {
        const string source = """
                              void main() {
                                  video_init();
                                  hud_set_tile(none, 0, 0, 1);
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);

        Assert.Equal(24592, rom.Length);
    }

    [Fact]
    public void Compiles_tick_input_helpers_to_nes_controller_state()
    {
        const string source = """
                              void main() {
                                  video_init();
                                  i16 down = 0;
                                  i16 pressed = 0;
                                  i16 released = 0;
                                  i16 held = 0;
                                  while (true) {
                                      video_wait_vblank();
                                      input_poll();
                                      down = button_down(a);
                                      pressed = button_just_pressed(a);
                                      released = button_just_released(a);
                                      held = button_hold_ticks(a);
                                  }
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(16 * 1024).ToArray();

        Assert.Equal(24592, rom.Length);
        Assert.True(ContainsSequence(prg, [0xA5, 0xF0, 0x85, 0xF1]), "input_poll should snapshot previous controller state before reading the current tick.");
        Assert.True(ContainsSequence(prg, [0xA9, 0x01, 0x8D, 0x16, 0x40, 0xA9, 0x00, 0x8D, 0x16, 0x40]), "input_poll should strobe NES controller port $4016.");
        Assert.True(ContainsSequence(prg, [0xAD, 0x16, 0x40, 0x29, 0x01]), "input_poll should read serial button bits from NES controller port $4016.");
        Assert.True(ContainsSequence(prg, [0xA5, 0xF0, 0x29, 0x01]), "button_down(a) should read the current tick mask.");
        Assert.True(ContainsSequence(prg, [0xA5, 0xF1, 0x29, 0x01]), "edge helpers should read the previous tick mask.");
        Assert.True(ContainsSequence(prg, [0xA5, 0xF2]), "button_hold_ticks(a) should read the A-button hold counter.");
    }

    [Fact]
    public void Compiles_struct_member_access_as_adjacent_zero_page_fields()
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
                                  position.y = position.x;
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(16 * 1024).ToArray();

        Assert.Equal(24592, rom.Length);
        Assert.True(ContainsSequence(prg, [0xA9, 0x28, 0x85, 0x00]), "ROM should store position.x at the first zero-page field address.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0x85, 0x01]), "ROM should copy position.x to adjacent position.y with direct zero-page access.");
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
                                          return;
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
                                             return;
                                         }
                                         """;

        Assert.Equal(NesRomCompiler.CompileSource(explicitSource), NesRomCompiler.CompileSource(initializerSource));
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
                                          return;
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
                                             return;
                                         }
                                         """;

        Assert.Equal(NesRomCompiler.CompileSource(explicitSource), NesRomCompiler.CompileSource(initializerSource));
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
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(16 * 1024).ToArray();

        Assert.Equal(24592, rom.Length);
        Assert.True(ContainsSequence(prg, [0xA9, 0x07, 0x85, 0x00]), "Alias ActorIndex should compile as the first byte-backed local.");
        Assert.True(ContainsSequence(prg, [0xA9, 0x00, 0x85, 0x01]), "Alias Position should compile as a struct field at the next local address.");
        Assert.True(ContainsSequence(prg, [0xA9, 0x00, 0x85, 0x02]), "Alias Position should preserve all struct fields with no alias storage.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0x85, 0x01]), "Alias assignments should lower to direct local load/store.");
    }

    [Fact]
    public void Compiles_const_identifiers_as_immediates_without_local_storage()
    {
        const string source = """
                              const u8 StartX = 40;
                              const u8 Copy = StartX - 39;

                              void main() {
                                  video_init();
                                  i16 x = StartX;
                                  i16 y = Copy;
                                  y = x;
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(16 * 1024).ToArray();

        Assert.Equal(24592, rom.Length);
        Assert.True(ContainsSequence(prg, [0xA9, 0x28, 0x85, 0x00]), "Const StartX should compile as an immediate store to the first zero-page local.");
        Assert.True(ContainsSequence(prg, [0xA9, 0x01, 0x85, 0x01]), "Const Copy should compile as an immediate store to the second zero-page local, with no const storage slot.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0x85, 0x01]), "Const declarations should not shift runtime local addresses.");
    }

    [Fact]
    public void Compiles_local_const_identifiers_as_immediates_without_local_storage()
    {
        const string source = """
                              void main() {
                                  video_init();
                                  const u8 StartX = 40;
                                  const u8 Copy = StartX + 1;
                                  i16 x = Copy;
                                  i16 y = 1;
                                  y = x;
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(16 * 1024).ToArray();

        Assert.Equal(24592, rom.Length);
        Assert.True(ContainsSequence(prg, [0xA9, 0x29, 0x85, 0x00]), "Local const Copy should compile its derived value as an immediate store to the first zero-page local.");
        Assert.True(ContainsSequence(prg, [0xA9, 0x01, 0x85, 0x01]), "Local const declarations should not reserve zero-page storage before the second local.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0x85, 0x01]), "Local const declarations should not shift runtime local addresses.");
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
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(16 * 1024).ToArray();

        Assert.Equal(24592, rom.Length);
        Assert.True(ContainsSequence(prg, [0xA9, 0x02, 0x85, 0x00]), "Const conditional expression should fold to one immediate store with no runtime conditional.");
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
                                         return;
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
                                         return;
                                     }
                                     """;

        Assert.Equal(NesRomCompiler.CompileSource(decimalSource), NesRomCompiler.CompileSource(literalSource));
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
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(16 * 1024).ToArray();

        Assert.Equal(24592, rom.Length);
        Assert.True(ContainsSequence(prg, [0xA9, 0x04, 0x85, 0x00]), "sizeof(Actor) should compile as the struct byte size immediate.");
        Assert.True(ContainsSequence(prg, [0xA9, 0x02, 0x85, 0x01]), "sizeof(ptr<u8>) should compile as the pointer byte size immediate.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0x85, 0x01]), "sizeof expressions should not reserve storage or emit helper code.");
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
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(16 * 1024).ToArray();

        Assert.Equal(24592, rom.Length);
        Assert.True(ContainsSequence(prg, [0xA9, 0x01, 0x85, 0x00]), "offsetof(Actor, y) should compile as the field byte offset immediate.");
        Assert.True(ContainsSequence(prg, [0xA9, 0x03, 0x85, 0x01]), "offsetof(Actor, active) should compile as the field byte offset immediate.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0x85, 0x01]), "offsetof expressions should not reserve storage or emit helper code.");
    }

    [Fact]
    public void Compiles_fixed_size_array_constant_indices_as_adjacent_zero_page_bytes()
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

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(16 * 1024).ToArray();

        Assert.Equal(24592, rom.Length);
        Assert.True(ContainsSequence(prg, [0xA9, 0x28, 0x85, 0x00]), "Array index 0 should store to the first zero-page byte.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0x85, 0x01]), "Array index 1 should load from the adjacent zero-page byte with direct addressing.");
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
                                          return;
                                      }
                                      """;

        const string initializerSource = """
                                         void main() {
                                             video_init();
                                             u8 seed = 3;
                                             u8 values[4] = [1, seed, seed + 1];
                                             u8 copy = values[3];
                                             return;
                                         }
                                         """;

        Assert.Equal(NesRomCompiler.CompileSource(explicitSource), NesRomCompiler.CompileSource(initializerSource));
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
                                                return;
                                            }
                                            """;

        const string inferredLengthSource = """
                                            void main() {
                                                video_init();
                                                u8 seed = 3;
                                                u8 values[] = [1, seed, seed + 1];
                                                u8 size = countof(values);
                                                return;
                                            }
                                            """;

        Assert.Equal(NesRomCompiler.CompileSource(explicitLengthSource), NesRomCompiler.CompileSource(inferredLengthSource));
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
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(16 * 1024).ToArray();

        Assert.Equal(24592, rom.Length);
        Assert.True(ContainsSequence(prg, [0xA9, 0x04, 0x85, 0x04]), "countof(values) should compile as an immediate store after the four array bytes.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x04, 0x85, 0x00]), "countof should not emit a helper; subsequent array assignment should remain direct.");
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
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(16 * 1024).ToArray();

        Assert.Equal(24592, rom.Length);
        Assert.True(ContainsSequence(prg, [0xA9, 0x28, 0x85, 0x00]), "Enum Brick should compile as an immediate store to the first zero-page local.");
        Assert.True(ContainsSequence(prg, [0xA9, 0x29, 0x85, 0x01]), "Implicit enum Bonus should compile as the next immediate value with no enum storage slot.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0x85, 0x01]), "Enum declarations should not shift runtime local addresses.");
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
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(16 * 1024).ToArray();

        Assert.Equal(24592, rom.Length);
        Assert.True(ContainsSequence(prg, [0xA5, 0x01, 0x18, 0x65, 0x00, 0x85, 0x00]), "x += y should lower to direct zero-page addition and store.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0x38, 0xE9, 0x01, 0x85, 0x00]), "x -= 1 should lower to direct zero-page subtract/store without a helper call.");
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
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(16 * 1024).ToArray();

        Assert.Equal(24592, rom.Length);
        Assert.True(ContainsSequence(prg, [0xA5, 0x01, 0xC9, 0x03, 0xB0]), "for condition should compare i with 3 and branch out when i >= 3.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x01, 0x18, 0x65, 0x00, 0x85, 0x00]), "for body should use direct x += i zero-page arithmetic.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x01, 0x18, 0x69, 0x01, 0x85, 0x01]), "for increment should use direct i += 1 zero-page arithmetic.");
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
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(16 * 1024).ToArray();

        Assert.Equal(24592, rom.Length);
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0x18, 0x69, 0x01, 0x85, 0x00]), "x++ should lower to direct x += 1 zero-page arithmetic.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0x38, 0xE9, 0x01, 0x85, 0x00]), "x-- should lower to direct x -= 1 zero-page arithmetic.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x01, 0x18, 0x69, 0x01, 0x85, 0x01]), "for i++ should lower to direct i += 1 zero-page arithmetic.");
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
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(16 * 1024).ToArray();

        Assert.Equal(24592, rom.Length);
        Assert.True(ContainsSequence(prg, [0xA9, 0x00, 0x85, 0x01]), "range-for should initialize the loop local once from the range start.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x01, 0xC9, 0x03, 0xB0]), "range-for should compare i with the exclusive upper bound and branch out when i >= end.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x01, 0x18, 0x65, 0x00, 0x85, 0x00]), "range-for body should use direct x += i zero-page arithmetic.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x01, 0x18, 0x69, 0x01, 0x85, 0x01]), "range-for should increment i with direct i++ zero-page arithmetic.");
    }

    [Fact]
    public void Compiles_runtime_indexed_array_access_without_helper_calls()
    {
        const string source = """
                              void main() {
                                  u8 values[4];
                                  u8 i = 1;
                                  values[i] += 1;
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(16 * 1024).ToArray();

        Assert.Equal(24592, rom.Length);
        Assert.True(ContainsSequence(prg, [0xA5, 0x04, 0xAA]), "runtime array indexing should transfer the byte index to X without a helper call or implicit bounds check.");
        Assert.True(ContainsSequence(prg, [0xB5, 0x00, 0x18, 0x69, 0x01, 0x95, 0x00]), "values[i] += 1 should use zero-page indexed load/add/store without a helper call.");
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
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(16 * 1024).ToArray();

        var loopStart = IndexOfSequence(prg, [0xA5, 0x00, 0x18, 0x69, 0x01, 0x85, 0x00]);
        var continueCompare = IndexOfSequence(prg, [0xA5, 0x00, 0xC9, 0x01, 0xD0]);
        var breakCompare = IndexOfSequence(prg, [0xA5, 0x00, 0xC9, 0x03, 0xD0]);

        Assert.Equal(24592, rom.Length);
        Assert.True(loopStart >= 0, "loop body should start with direct x++ zero-page arithmetic.");
        Assert.True(continueCompare >= 0, "continue guard should compare x with 1.");
        Assert.True(breakCompare >= 0, "break guard should compare x with 3.");
        Assert.Equal(0xC000 + loopStart, ReadLittleEndian16(prg, continueCompare + 7));
        Assert.True(ReadLittleEndian16(prg, breakCompare + 7) > 0xC000 + breakCompare, "break should jump beyond the loop body.");
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
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(16 * 1024).ToArray();

        var continueCompare = IndexOfSequence(prg, [0xA5, 0x01, 0xC9, 0x01, 0xD0]);
        var breakCompare = IndexOfSequence(prg, [0xA5, 0x01, 0xC9, 0x03, 0xD0]);
        var increment = IndexOfSequence(prg, [0xA5, 0x01, 0x18, 0x69, 0x01, 0x85, 0x01]);

        Assert.Equal(24592, rom.Length);
        Assert.True(continueCompare >= 0, "continue guard should compare i with 1.");
        Assert.True(breakCompare >= 0, "break guard should compare i with 3.");
        Assert.True(increment >= 0, "for increment should still be emitted as direct i += 1 arithmetic.");

        var continueJumpTarget = ReadLittleEndian16(prg, continueCompare + 7);
        Assert.Equal(0xC000 + increment, continueJumpTarget);

        var breakJumpTarget = ReadLittleEndian16(prg, breakCompare + 7);
        Assert.True(breakJumpTarget > 0xC000 + increment + 7, "break should jump beyond the increment and final loop jump.");
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
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(16 * 1024).ToArray();

        var continueCompare = IndexOfSequence(prg, [0xA5, 0x00, 0xC9, 0x01, 0xD0]);
        var conditionCompare = IndexOfSequence(prg, [0xA5, 0x00, 0xC9, 0x03, 0xB0]);

        Assert.Equal(24592, rom.Length);
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0x18, 0x69, 0x01, 0x85, 0x00]), "do body should emit x++ as direct zero-page arithmetic before the first condition check.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0x18, 0x69, 0x02, 0x85, 0x00]), "do body should emit direct zero-page arithmetic after the continue guard.");
        Assert.True(continueCompare >= 0, "continue guard should compare x with 1.");
        Assert.True(conditionCompare >= 0, "do-while condition should compare x with 3 at the bottom of the loop.");

        var continueJumpTarget = ReadLittleEndian16(prg, continueCompare + 7);
        Assert.Equal(0xC000 + conditionCompare, continueJumpTarget);
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
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(16 * 1024).ToArray();

        var andLeftCompare = IndexOfSequence(prg, [0xA5, 0x00, 0xC9, 0x00, 0xF0]);
        var andRightCompare = IndexOfSequence(prg, [0xA5, 0x01, 0xC9, 0x00, 0xF0]);
        var orLeftCompare = IndexOfSequence(prg, [0xA5, 0x00, 0xC9, 0x00, 0xD0]);
        var orBody = IndexOfSequence(prg, [0xA5, 0x02, 0x18, 0x69, 0x02, 0x85, 0x02]);

        Assert.Equal(24592, rom.Length);
        Assert.True(andLeftCompare >= 0, "&& should test the left condition and branch false before touching the right side.");
        Assert.True(andRightCompare > andLeftCompare, "&& should evaluate the right condition only after the left condition succeeds.");
        Assert.True(orLeftCompare >= 0, "|| should test the left condition with a direct true branch.");
        Assert.True(orBody > orLeftCompare, "|| body should be emitted after the left condition branch.");
        Assert.Equal(0xC000 + orBody, ReadRelativeTarget(prg, orLeftCompare + 4));
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
                                          return;
                                      }
                                      """;

        const string membershipSource = """
                                        void main() {
                                            u8 tile = 2;
                                            u8 hit = 0;
                                            if (tile in 1..4) {
                                                hit = 1;
                                            }
                                            return;
                                        }
                                        """;

        Assert.Equal(NesRomCompiler.CompileSource(explicitSource), NesRomCompiler.CompileSource(membershipSource));
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
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(16 * 1024).ToArray();

        Assert.Equal(24592, rom.Length);
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0xC9, 0x00, 0xD0]), "! should invert x != 0 into a false branch when the inner condition is true.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0x18, 0x69, 0x01, 0x85, 0x00]), "then body should remain direct x += 1 zero-page arithmetic.");
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
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(16 * 1024).ToArray();

        Assert.Equal(24592, rom.Length);
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0xC9, 0x00, 0xD0]), "first if should compare x with 0 and branch to else when false.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0xC9, 0x01, 0xD0]), "else-if should compile as a nested if compare.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0x18, 0x69, 0x01, 0x85, 0x00]), "first body should remain direct x += 1 zero-page arithmetic.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0x18, 0x69, 0x02, 0x85, 0x00]), "else-if body should remain direct x += 2 zero-page arithmetic.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0x18, 0x69, 0x03, 0x85, 0x00]), "else body should remain direct x += 3 zero-page arithmetic.");
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
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(16 * 1024).ToArray();

        Assert.Equal(24592, rom.Length);
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
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(16 * 1024).ToArray();

        Assert.Equal(24592, rom.Length);
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0xC9, 0x00]), "Multi-value switch case should compare the subject with the first literal.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0xC9, 0x01]), "Multi-value switch case should compare the subject with the second literal.");
        Assert.True(ContainsSequence(prg, [0xA9, 0x0A, 0x85, 0x01]), "Multi-value switch case should share one direct branch body.");
        Assert.True(ContainsSequence(prg, [0xA9, 0x1E, 0x85, 0x01]), "Default switch case should remain a direct fallback body.");
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
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(16 * 1024).ToArray();

        Assert.Equal(24592, rom.Length);
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0xC9, 0x01, 0x90]), "Range switch case should compare the subject with the inclusive lower bound and branch out when subject < start.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0xC9, 0x04, 0xB0]), "Range switch case should compare the subject with the exclusive upper bound and branch out when subject >= end.");
        Assert.True(ContainsSequence(prg, [0xA9, 0x0A, 0x85, 0x01]), "Range switch case should share one direct branch body.");
        Assert.True(ContainsSequence(prg, [0xA9, 0x1E, 0x85, 0x01]), "Default switch case should remain a direct fallback body.");
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
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(16 * 1024).ToArray();

        Assert.Equal(24592, rom.Length);
        Assert.True(ContainsSequence(prg, [0xA9, 0x04, 0x85, 0x00]), "Untyped top-level const should fold to the direct literal initializer.");
        Assert.True(ContainsSequence(prg, [0xA9, 0x05, 0x85, 0x00]), "Untyped block-local const should fold to the direct literal assignment.");
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
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(16 * 1024).ToArray();

        Assert.Equal(24592, rom.Length);
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0x09, 0x01, 0x85, 0x00]), "flags |= Solid should lower to LDA/ORA immediate/STA with no helper.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0x29, 0xFD, 0x85, 0x00]), "flags &= ~Hazard should lower to LDA/AND immediate/STA with no helper.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0x49, 0x04, 0x85, 0x00]), "flags ^= Toggle should lower to LDA/EOR immediate/STA with no helper.");
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
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(16 * 1024).ToArray();

        Assert.Equal(24592, rom.Length);
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0x09, 0x01, 0x85, 0x00]), "set_flag(flags, 1) should inline as LDA/ORA immediate/STA with no call ABI.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0x29, 0xFD, 0x85, 0x00]), "clear_flag(flags, 2) should inline as LDA/AND immediate/STA with no call ABI.");
    }

    [Fact]
    public void Compiles_explicit_casts_as_zero_cost_expression_markers()
    {
        const string source = """
                              void main() {
                                  u8 flags = 0;
                                  u16 wide = 1;
                                  flags = (u8)(wide | 2);
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(16 * 1024).ToArray();

        Assert.Equal(24592, rom.Length);
        Assert.True(ContainsSequence(prg, [0xA5, 0x01, 0x09, 0x02, 0x85, 0x00]), "Explicit casts should disappear before NES lowering and leave the direct expression sequence.");
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
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(16 * 1024).ToArray();

        Assert.Equal(24592, rom.Length);
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
                              void main() {
                                  u8 moving = 1;
                                  u8 fast = 2;
                                  u8 speed = moving != 0 ? fast : 0;
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(16 * 1024).ToArray();

        Assert.Equal(24592, rom.Length);
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0xC9, 0x00, 0xF0]), "Conditional expression should branch to the false value when the condition is false.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x01, 0x4C]), "Conditional expression should load the true branch value and jump over the false branch.");
        Assert.True(ContainsSequence(prg, [0xA9, 0x00, 0x85, 0x02]), "Conditional expression should load the false branch value and store one selected byte.");
    }

    [Fact]
    public void Compiles_logical_sprite_draw_to_nes_oam_and_chr_data()
    {
        var baseDirectory = WriteSpriteAsset(
            "hero.nes.json",
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
                              void main() {
                                  video_init();
                                  sprite_asset(hero, "hero.nes.json");
                                  while (true) {
                                      video_wait_vblank();
                                      sprite_draw(hero, 24, 32, 0, 0, 2);
                                  }
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source, baseDirectory);
        var prg = rom.Skip(16).Take(16 * 1024).ToArray();
        var chr = rom.Skip(16 + 16 * 1024).Take(8 * 1024).ToArray();
        var spriteTile = chr.Skip(6 * 16).Take(16).ToArray();

        Assert.Equal(24592, rom.Length);
        Assert.Equal(Enumerable.Repeat((byte)0xFF, 8).Concat(Enumerable.Repeat((byte)0x00, 8)), spriteTile);
        Assert.True(ContainsSequence(prg, [0xA9, 0x20, 0x38, 0xE9, 0x01, 0x8D, 0x00, 0x02]), "sprite_draw should write NES OAM Y as logical y - 1.");
        Assert.True(ContainsSequence(prg, [0xA9, 0x06, 0x8D, 0x01, 0x02]), "sprite_draw should write the first compiled sprite tile to OAM.");
        Assert.True(ContainsSequence(prg, [0xA9, 0x02, 0x8D, 0x02, 0x02]), "sprite_draw should lower portable palette slot 2 to NES OAM attributes.");
        Assert.True(ContainsSequence(prg, [0xA9, 0x18, 0x8D, 0x03, 0x02]), "sprite_draw should write logical x to NES OAM.");
        Assert.True(ContainsSequence(prg, [0xA9, 0x02, 0x8D, 0x14, 0x40]), "sprite_draw should DMA the OAM shadow page after writing logical sprites.");
    }

    [Fact]
    public void Rejects_logical_sprite_palette_slots_outside_nes_capabilities()
    {
        var baseDirectory = WriteSpriteAsset(
            "hero.nes.json",
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
                              void main() {
                                  sprite_asset(hero, "hero.nes.json");
                                  while (true) {
                                      sprite_draw(hero, 24, 32, 0, 0, 4);
                                  }
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => NesRomCompiler.CompileSource(source, baseDirectory));

        Assert.Contains("Target 'nes' supports sprite palette slots 0..3, but slot 4 was requested.", exception.Message);
    }

    [Fact]
    public void Rejects_logical_sprite_assets_that_exceed_nes_sprite_count()
    {
        var rows = Enumerable.Repeat(new string('1', 65 * 8), 8);
        var rowJson = string.Join(",", rows.Select(row => $"\"{row}\""));
        var baseDirectory = WriteSpriteAsset(
            "wide.nes.json",
            "{\"platforms\":{\"nes\":{\"frames\":[[" + rowJson + "]]}}}");

        const string source = """
                              void main() {
                                  sprite_asset(wide, "wide.nes.json");
                                  while (true) {
                                      sprite_draw(wide, 0, 0, 0);
                                  }
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => NesRomCompiler.CompileSource(source, baseDirectory));

        Assert.Contains("NES sprite asset needs 65 hardware sprites, but the hardware limit is 64.", exception.Message);
    }

    [Fact]
    public void Compiles_horizontal_camera_path_from_world_map_to_nes_scroll()
    {
        const string source = """
                              void main() {
                                  video_init();
                                  world_column(0, 1, 2);
                                  world_column(1, 3, 4);
                                  world_map(2, 10, 2);
                                  camera_init(2, 10, 2);
                                  while (true) {
                                      video_wait_vblank();
                                      camera_set_position(8, 0);
                                      camera_apply();
                                  }
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(16 * 1024).ToArray();
        var expectedRows = Enumerable
            .Repeat((byte)0, 64)
            .ToArray();
        expectedRows[0] = 1;
        expectedRows[1] = 3;
        expectedRows[32] = 2;
        expectedRows[33] = 4;

        Assert.Equal(24592, rom.Length);
        Assert.True(ContainsSequence(prg, expectedRows), "world_map should seed the visible NES nametable from world_column data.");
        Assert.True(ContainsSequence(prg, [0xA9, 0x08, 0x85, 0xE0]), "camera_set_position(8, 0) should store the horizontal camera byte.");
        Assert.True(ContainsSequence(prg, [0xAD, 0x02, 0x20, 0xA5, 0xE0, 0x8D, 0x05, 0x20, 0xA9, 0x00, 0x8D, 0x05, 0x20]), "camera_apply should reset the PPU scroll latch and write horizontal then zero vertical scroll.");
    }

    [Fact]
    public void Rejects_vertical_camera_position_in_current_nes_camera_spike()
    {
        const string source = """
                              void main() {
                                  world_column(0, 1, 2);
                                  world_map(1, 10, 2);
                                  camera_init(1, 10, 2);
                                  while (true) {
                                      camera_set_position(0, 1);
                                      camera_apply();
                                  }
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => NesRomCompiler.CompileSource(source));

        Assert.Contains("Target 'nes' supports only horizontal camera_set_position(x, 0) in the current camera spike.", exception.Message);
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

    private static string WriteSpriteAsset(string fileName, string contents)
    {
        var directory = Path.Combine(Path.GetTempPath(), "RetroSharp.NES.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, fileName), contents);
        return directory;
    }

    private static bool ContainsSequence(IReadOnlyList<byte> bytes, IReadOnlyList<byte> sequence)
    {
        return IndexOfSequence(bytes, sequence) >= 0;
    }

    private static int IndexOfSequence(IReadOnlyList<byte> bytes, IReadOnlyList<byte> sequence)
    {
        for (var i = 0; i <= bytes.Count - sequence.Count; i++)
        {
            var match = true;
            for (var j = 0; j < sequence.Count; j++)
            {
                if (bytes[i + j] != sequence[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return i;
            }
        }

        return -1;
    }

    private static int ReadLittleEndian16(IReadOnlyList<byte> bytes, int offset)
    {
        return bytes[offset] | bytes[offset + 1] << 8;
    }

    private static int ReadRelativeTarget(IReadOnlyList<byte> bytes, int branchOffset)
    {
        return 0xC000 + branchOffset + 2 + unchecked((sbyte)bytes[branchOffset + 1]);
    }
}
