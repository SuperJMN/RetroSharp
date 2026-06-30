namespace RetroSharp.NES.Tests;

using System.Buffers.Binary;
using System.IO.Compression;
using RetroSharp.GameBoy;
using RetroSharp.NES;
using RetroSharp.Parser;
using RetroSharp.Sdk;
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

        Assert.Equal(40976, rom.Length);
        Assert.Equal((byte)'N', rom[0]);
        Assert.Equal((byte)'E', rom[1]);
        Assert.Equal((byte)'S', rom[2]);
        Assert.Equal(0x1A, rom[3]);
        Assert.Equal(2, rom[4]);
        Assert.Equal(1, rom[5]);

        var prg = rom.Skip(16).Take(32 * 1024).ToArray();
        var chr = rom.Skip(16 + 32 * 1024).Take(8 * 1024).ToArray();

        Assert.Equal(0x00, prg[^4]);
        Assert.Equal(0x80, prg[^3]);
        Assert.Contains(chr, b => b != 0);
    }

    [Fact]
    public void Logical_palette_declarations_map_tones_to_nes_grayscale_palette_slots()
    {
        const string source = """
                              void main() {
                                  video.Init();
                                  palette.Background(2, 0, 1, 2, 3);
                                  palette.Sprite(3, 0, 0, 1, 3);
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);

        Assert.Equal(40976, rom.Length);
        Assert.True(ContainsSequence(rom, [0x30, 0x10, 0x00, 0x0F]), "palette.Background should map logical light-to-dark tones to NES grayscale colors.");
        Assert.True(ContainsSequence(rom, [0x30, 0x30, 0x10, 0x0F]), "palette.Sprite should map the runner's logical sprite tones to NES grayscale colors.");
    }

    [Fact]
    public void Rejects_logical_palette_sprite_slots_outside_nes_capabilities()
    {
        const string source = """
                              void main() {
                                  video.Init();
                                  palette.Sprite(4, 15, 17, 34, 51);
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => NesRomCompiler.CompileSource(source));

        Assert.Equal("Target 'nes' supports sprite palette slots 0..3, but palette slot 4 was requested.", exception.Message);
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
    public void Bool_flags_lower_like_int_flags_with_explicit_comparisons()
    {
        const string intSource = """
                                 type Pixel = i16;
                                 struct S { Pixel grounded; Pixel moving; Pixel x; }
                                 inline void step(this S s, Pixel grounded) {
                                     if (grounded != 0) { s.x += 1; }
                                     if (s.grounded == 0) { s.x += 1; }
                                 }
                                 void main() {
                                     video_init();
                                     S s; s.grounded = 1; s.moving = 0; s.x = 0;
                                     s.step(s.grounded);
                                     Pixel frame = s.grounded switch { 0 => 4, _ => s.moving switch { 0 => 0, _ => 7 } };
                                     i16 sink = frame + s.x;
                                     if (sink != 0) { video_present(); }
                                     return;
                                 }
                                 """;

        const string boolSource = """
                                  type Pixel = i16;
                                  struct S { bool grounded; bool moving; Pixel x; }
                                  inline void step(this S s, bool grounded) {
                                      if (grounded) { s.x += 1; }
                                      if (!s.grounded) { s.x += 1; }
                                  }
                                  void main() {
                                      video_init();
                                      S s; s.grounded = true; s.moving = false; s.x = 0;
                                      s.step(s.grounded);
                                      Pixel frame = s.grounded switch { false => 4, _ => s.moving switch { false => 0, _ => 7 } };
                                      i16 sink = frame + s.x;
                                      if (sink != 0) { video_present(); }
                                      return;
                                  }
                                  """;

        Assert.Equal(NesRomCompiler.CompileSource(intSource), NesRomCompiler.CompileSource(boolSource));
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
    public void Compiles_immutable_let_binding_like_equivalent_local_storage()
    {
        const string variableSource = """
                                      void main() {
                                          video_init();
                                          u8 speed = 2;
                                          u8 next = speed + 1;
                                          u8 sink = 0;
                                          sink = next;
                                          return;
                                      }
                                      """;

        const string letSource = """
                                 void main() {
                                     video_init();
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

                                      void main() {
                                          video_init();
                                          u8 next = step(4);
                                          return;
                                      }
                                      """;

        const string explicitSource = """
                                      inline pure u8 step(u8 value, u8 amount = 1) => value + amount;

                                      void main() {
                                          video_init();
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
                                         void main() {
                                             video_init();
                                             u8 state = 2;
                                             u8 speed = state == 0 ? 0 : state == 1 ? 2 : state >= 2 && state < 5 ? 3 : 1;
                                             u8 sink = 0;
                                             sink = speed;
                                             return;
                                         }
                                         """;

        const string switchSource = """
                                    void main() {
                                        video_init();
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
    public void Rejects_nes_switch_expression_that_would_re_evaluate_subject()
    {
        const string source = """
                              u8 next(u8 value) => value + 1;

                              void main() {
                                  video_init();
                                  u8 speed = next(1) switch { 0 => 0, _ => 1 };
                                  return;
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => NesRomCompiler.CompileSource(source));
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
                                          return;
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
                                     return;
                                 }
                                 """;

        Assert.Equal(NesRomCompiler.CompileSource(functionSource), NesRomCompiler.CompileSource(dotSource));
    }

    [Fact]
    public void Compiles_wait_frame_library_helper_over_nes_intrinsic_like_sdk_operation()
    {
        const string sdkSource = """
                                 void main() {
                                     video_wait_vblank();
                                 }
                                 """;

        const string intrinsicSource = """
                                       [target("nes")]
                                       [intrinsic("wait_frame")]
                                       extern void nes_wait_frame();

                                       inline void wait_frame() {
                                           nes_wait_frame();
                                       }

                                       void main() {
                                           wait_frame();
                                       }
                                       """;

        Assert.Equal(NesRomCompiler.CompileSource(sdkSource), NesRomCompiler.CompileSource(intrinsicSource));
    }

    [Fact]
    public void Compiles_input_poll_library_helper_over_nes_intrinsic_like_sdk_operation()
    {
        const string sdkSource = """
                                 void main() {
                                     input_poll();
                                 }
                                 """;

        const string intrinsicSource = """
                                       [target("nes")]
                                       [intrinsic("poll_input")]
                                       extern void nes_poll_input();

                                       inline void poll_input() {
                                           nes_poll_input();
                                       }

                                       void main() {
                                           poll_input();
                                       }
                                       """;

        Assert.Equal(NesRomCompiler.CompileSource(sdkSource), NesRomCompiler.CompileSource(intrinsicSource));
    }

    [Fact]
    public void Compiles_audio_update_library_helper_over_nes_intrinsic_like_sdk_operation()
    {
        const string sdkSource = """
                                 void main() {
                                     audio_update();
                                 }
                                 """;

        const string intrinsicSource = """
                                       [target("nes")]
                                       [intrinsic("audio_update")]
                                       extern void nes_audio_update();

                                       inline void audio_update() {
                                           nes_audio_update();
                                       }

                                       void main() {
                                           audio_update();
                                       }
                                       """;

        Assert.Equal(NesRomCompiler.CompileSource(sdkSource), NesRomCompiler.CompileSource(intrinsicSource));
    }

    [Fact]
    public void Injected_nes_audio_update_helper_keeps_surface_byte_identical()
    {
        const string source = """
                              void main() {
                                  audio.Init();
                                  audio.Update();
                              }
                              """;
        var explicitLibrarySource = SdkLibrarySource.ForTarget(NesTarget.Intrinsics) + source;
        var library = SdkLibrarySource.ForTarget(NesTarget.Intrinsics);

        Assert.Contains("class audio", library, StringComparison.Ordinal);
        Assert.Equal(NesRomCompiler.CompileSource(explicitLibrarySource), NesRomCompiler.CompileSource(source));
    }

    [Fact]
    public void Compiles_camera_library_helpers_over_nes_intrinsic_like_sdk_operations()
    {
        const string direct = """
                              void main() {
                                  video_init();
                                  world_column(0, 1, 2);
                                  world_map(1, 10, 2);
                                  camera_init(1, 10, 2);
                                  i16 x = 4;
                                  camera_set_position(x, 0);
                                  camera_apply();
                                  return;
                              }
                              """;
        const string library = """
                               void main() {
                                   video.Init();
                                   world.Column(0, 1, 2);
                                   world.Map(1, 10, 2);
                                   camera.Init(1, 10, 2);
                                   i16 x = 4;
                                   camera.SetPosition(x, 0);
                                   camera.Apply();
                                   return;
                               }
                               """;
        Assert.Contains("class camera", SdkLibrarySource.ForTarget(NesTarget.Intrinsics), StringComparison.Ordinal);
        Assert.Equal(NesRomCompiler.CompileSource(direct), NesRomCompiler.CompileSource(library));
    }

    [Fact]
    public void Nes_sdk_library_does_not_expose_capability_gated_world_tile_flags_helper()
    {
        // world.TileFlagsAt(...) is gated on the WorldTileFlags collision query,
        // which NES does not declare. The injected NES library must not expose it.
        var library = SdkLibrarySource.ForTarget(NesTarget.Intrinsics);

        Assert.DoesNotContain("class world", library, StringComparison.Ordinal);
        Assert.DoesNotContain("world_tile_flags_at", library, StringComparison.Ordinal);
    }

    [Fact]
    public void Injected_nes_sdk_library_helpers_keep_video_and_input_surface_byte_identical()
    {
        const string source = """
                              void main() {
                                  video.WaitVBlank();
                                  input.Poll();
                              }
                              """;
        var explicitLibrarySource = SdkLibrarySource.ForTarget(NesTarget.Intrinsics) + source;

        var library = SdkLibrarySource.ForTarget(NesTarget.Intrinsics);

        Assert.Contains("class video", library, StringComparison.Ordinal);
        Assert.Contains("class input", library, StringComparison.Ordinal);
        Assert.Equal(NesRomCompiler.CompileSource(explicitLibrarySource), NesRomCompiler.CompileSource(source));
    }

    [Fact]
    public void Nes_selects_matching_target_intrinsic_variant_for_portable_helper()
    {
        const string sdkSource = """
                                 void main() {
                                     video_wait_vblank();
                                 }
                                 """;

        const string source = """
                              [target("gb")]
                              [intrinsic("wait_frame")]
                              extern void target_wait_frame();

                              [target("nes")]
                              [intrinsic("wait_frame")]
                              extern void target_wait_frame();

                              inline void wait_frame() {
                                  target_wait_frame();
                              }

                              void main() {
                                  wait_frame();
                              }
                              """;

        Assert.Equal(NesRomCompiler.CompileSource(sdkSource), NesRomCompiler.CompileSource(source));
    }

    [Fact]
    public void Unknown_nes_intrinsic_reports_target_catalog_error()
    {
        const string source = """
                              [target("nes")]
                              [intrinsic("read_magic")]
                              extern void nes_read_magic();

                              void main() {
                                  nes_read_magic();
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => NesRomCompiler.CompileSource(source));

        Assert.Equal("Target 'nes' does not support intrinsic 'read_magic' on extern function 'nes_read_magic'.", exception.Message);
    }

    [Fact]
    public void Nes_sdk_dot_calls_accept_vertical_camera_on_four_screen_target()
    {
        const string source = """
                              void main() {
                                  video_init();
                                  world_column(0, 1, 2);
                                  world_column(1, 3, 4);
                                  world_map(2, 10, 2);
                                  camera.Init(2, 10, 2);
                                  camera.SetPosition(4, 1);
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);

        Assert.Equal(0x08, rom[6] & 0x08);
    }

    [Fact]
    public void Nes_lowers_explicit_stream_map_row_to_ppu_row_writes()
    {
        const string source = """
                              void main() {
                                  video_init();
                                  world_column(0, 1, 2, 3, 4);
                                  world_column(1, 5, 6, 7, 8);
                                  world_column(2, 9, 10, 11, 12);
                                  world_column(3, 13, 14, 15, 16);
                                  world_map(4, 0, 4);
                                  map_stream_row(29, 3, 0, 4);
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.True(CountOccurrences(prg, [0x8D, 0x06, 0x20]) >= 1, "Streaming a row should set the PPU address through $2006.");
        Assert.True(CountOccurrences(prg, [0x8D, 0x07, 0x20]) >= 4, "Streaming a row should write each requested tile through $2007.");
        Assert.True(
            ContainsSequence(prg, [0xA9, 0x23, 0x8D, 0x06, 0x20, 0xA9, 0xF8, 0x8D, 0x06, 0x20]),
            "Streaming a row should also refresh the matching bottom-row attribute byte at $23F8.");
    }

    [Fact]
    public void Nes_accepts_world_rows_beyond_four_screen_surface_for_runtime_row_streaming()
    {
        var tallColumn = string.Join(", ", Enumerable.Range(0, 61).Select(row => row % 4 + 1));
        var source = $$"""
                       void main() {
                           video_init();
                           world_column(0, {{tallColumn}});
                           world_map(1, 0, 61);
                           camera_init(1, 0, 60);
                           map_stream_row(0, 60, 0, 1);
                           camera_set_position(0, 1);
                           camera_apply();
                       }
                       """;

        var rom = NesRomCompiler.CompileSource(source);

        Assert.Equal(0x08, rom[6] & 0x08);
    }

    [Fact]
    public void Four_screen_horizontal_streaming_prepares_the_next_offscreen_column()
    {
        var column = string.Join(", ", Enumerable.Range(0, 14).Select(row => row % 4 + 1));
        var source = $$"""
                       void main() {
                           video_init();
                           world_column(0, {{column}});
                           world_column(67, {{column}});
                           world_map(68, 0, 14);
                           camera_init(68, 0, 14);
                           u8 x = 8;
                           u8 y = 1;
                           camera_set_position(x, y);
                           camera_apply();
                       }
                       """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.True(
            ContainsSequence(prg, [0xA5, 0xE1, 0x18, 0x69, 0x20, 0x29, 0x3F, 0x85, 0xE2]),
            "Four-screen horizontal streaming should prepare the next offscreen visible-edge column, not the current visible column.");
        Assert.False(
            ContainsSequence(prg, [0xA5, 0xE1, 0x18, 0x69, 0x40, 0x29, 0x3F, 0x85, 0xE2]),
            "Adding 64 wraps onto the visible left column after a tile crossing and causes four-screen artifacts.");
    }

    [Fact]
    public void Nes_streams_runtime_rows_when_vertical_camera_crosses_tall_world_boundary()
    {
        var tallColumn = string.Join(", ", Enumerable.Range(0, 61).Select(row => row % 4 + 1));
        var source = $$"""
                       void main() {
                           video_init();
                           world_column(0, {{tallColumn}});
                           world_map(1, 0, 61);
                           camera_init(1, 0, 60);
                           u8 y = 8;
                           camera_set_position(0, y);
                           camera_apply();
                       }
                       """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.True(
            ContainsSequence(prg, [0xA4, 0xE3, 0xB1, 0xE8, 0x8D, 0x07, 0x20]),
            "A tall four-screen world should stream the exposed row from a runtime-selected world row pointer.");
        Assert.True(
            ContainsSequence(prg, [0xA9, 0x09, 0x85, 0xEF]),
            "A tall four-screen row stream should refresh the worst-case 9 touched attribute bytes for the visible row.");
    }

    [Fact]
    public void Nes_automatic_camera_row_streaming_uses_the_callers_vblank()
    {
        var tallColumn = string.Join(", ", Enumerable.Range(0, 61).Select(row => row % 4 + 1));
        var source = $$"""
                       void main() {
                           video_init();
                           world_column(0, {{tallColumn}});
                           world_map(1, 0, 61);
                           camera_init(1, 0, 60);
                           u8 y = 8;
                           video_wait_vblank();
                           camera_set_position(0, y);
                           camera_apply();
                       }
                       """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        var waitFrameCount = CountOccurrences(prg, [0x2C, 0x02, 0x20, 0x10]);
        Assert.True(
            waitFrameCount == 3,
            $"Startup should wait for two VBlanks and source video_wait_vblank should wait once; automatic camera row streaming must not insert another frame wait before restoring scroll. Actual count: {waitFrameCount}.");
    }

    [Fact]
    public void Nes_video_wait_vblank_waits_for_the_next_vblank_edge()
    {
        const string source = """
                       void main() {
                           video_init();
                           loop {
                               video_wait_vblank();
                           }
                       }
                       """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.True(
            ContainsSequence(prg, [0x2C, 0x02, 0x20, 0x30, 0xFB, 0x2C, 0x02, 0x20, 0x10, 0xFB]),
            "video_wait_vblank should first wait for any active VBlank flag to clear, then wait for the next VBlank edge.");
    }

    [Fact]
    public void Nes_video_wait_vblank_applies_pending_camera_scroll_before_sprite_dma()
    {
        const string source = """
                       void main() {
                           video_init();
                           palette_sprite(0, 0, 1, 2, 3);
                           world_column(0, 1);
                           world_map(1, 0, 1);
                           camera_init(1, 0, 1);
                           sprite_asset(marker, "samples/cross-target-camera/marker.json");
                           camera_set_position(0, 0);
                           loop {
                               video_wait_vblank();
                               sprite_draw(marker, 72, 72, 0, false, 0);
                               camera_apply();
                           }
                       }
                       """;

        var rom = NesRomCompiler.CompileSource(source, Path.GetDirectoryName(RepositoryFile("RetroSharp.sln")));
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();
        var waitIndex = IndexOfSequence(prg, [0x2C, 0x02, 0x20, 0x30, 0xFB, 0x2C, 0x02, 0x20, 0x10, 0xFB]);
        Assert.True(waitIndex >= 0, "The runtime VBlank wait should use edge polling.");

        var afterWait = prg.Skip(waitIndex).ToArray();
        var scrollIndex = IndexOfSequence(afterWait, [0x8D, 0x05, 0x20]);
        var dmaIndex = IndexOfSequence(afterWait, [0x8D, 0x14, 0x40]);

        Assert.True(scrollIndex >= 0, "The camera scroll restore should write PPUSCROLL.");
        Assert.True(dmaIndex >= 0, "sprite_draw should DMA the OAM shadow page.");
        Assert.True(scrollIndex < dmaIndex, "Pending camera scroll should be restored at the start of VBlank before sprite DMA consumes the window.");
    }

    [Fact]
    public void Nes_runtime_row_streaming_writes_contiguous_ppudata_segments()
    {
        var tallColumn = string.Join(", ", Enumerable.Range(0, 61).Select(row => row % 4 + 1));
        var source = $$"""
                       void main() {
                           video_init();
                           world_column(0, {{tallColumn}});
                           world_map(1, 0, 61);
                           camera_init(1, 0, 60);
                           u8 y = 8;
                           video_wait_vblank();
                           camera_set_position(0, y);
                           camera_apply();
                       }
                       """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.True(
            ContainsSequence(prg, [0xA5, 0xE2, 0x29, 0x1F, 0xC9, 0x00]),
            "Runtime row streaming should keep PPUDATA auto-incrementing within a nametable row and only reset PPUADDR when the target column crosses a 32-column nametable boundary.");
    }

    [Fact]
    public void Nes_runtime_row_streaming_is_split_across_vblanks()
    {
        var tallColumn = string.Join(", ", Enumerable.Range(0, 61).Select(row => row % 4 + 1));
        var source = $$"""
                       void main() {
                           video_init();
                           world_column(0, {{tallColumn}});
                           world_map(1, 0, 61);
                           camera_init(1, 0, 60);
                           u8 y = 8;
                           video_wait_vblank();
                           camera_set_position(0, y);
                           camera_apply();
                       }
                       """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.True(
            ContainsSequence(prg, [0xA9, 0x08, 0x85, 0xEF]),
            "Runtime row streaming should write only an 8-tile row segment per VBlank before restoring scroll.");
        Assert.False(
            ContainsSequence(prg, [0xA9, 0x20, 0x85, 0xEF]),
            "Runtime row streaming must not attempt the full 32-tile row in one VBlank.");
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
                                        return;
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

                                    void main() {
                                        video_init();
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

                                   void main() {
                                       video_init();
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

                                    void main() {
                                        video_init();
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

                                   void main() {
                                       video_init();
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

                                  void main() {
                                      video_init();
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

                                   void main() {
                                       video_init();
                                       Actor actor = { x: Tuning.Step };
                                       actor.x = Tuning.Apply(actor.x);
                                       return;
                                   }
                                   """;

        Assert.Equal(NesRomCompiler.CompileSource(flatSource), NesRomCompiler.CompileSource(classSource));
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
                                        return;
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
                                          return;
                                      }
                                      """;

        Assert.Equal(NesRomCompiler.CompileSource(staticSource), NesRomCompiler.CompileSource(receiverSource));
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
                                        return;
                                    }
                                    """;

        const string pipelineSource = """
                                      u8 Clamp(u8 value, u8 min, u8 max) => value < min ? min : value > max ? max : value;
                                      u8 SnapToTile(u8 value) => value & 0xF8;

                                      void main() {
                                          video_init();
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

                                    void main() {
                                        video_init();
                                        Actor actor = { x: 130 };
                                        u8 clamped = Clamp(X(actor), max: 120);
                                        return;
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
                                          return;
                                      }
                                      """;

        Assert.Equal(NesRomCompiler.CompileSource(nestedSource), NesRomCompiler.CompileSource(pipelineSource));
    }

    [Fact]
    public void Rejects_nes_invalid_pure_helper_contracts_before_lowering()
    {
        var statementEffect = Assert.Throws<InvalidOperationException>(() => NesRomCompiler.CompileSource("""
                                                                                                         pure void draw() {
                                                                                                             video_init();
                                                                                                         }

                                                                                                         void main() {
                                                                                                             draw();
                                                                                                             return;
                                                                                                         }
                                                                                                         """));
        Assert.Equal("pure helper 'draw' contains side-effecting statements; pure helpers must be a single return expression.", statementEffect.Message);

        var callEffect = Assert.Throws<InvalidOperationException>(() => NesRomCompiler.CompileSource("""
                                                                                                    u8 next(u8 value) => value + 1;
                                                                                                    pure u8 step(u8 value) => next(value);

                                                                                                    void main() {
                                                                                                        video_init();
                                                                                                        u8 result = step(4);
                                                                                                        return;
                                                                                                    }
                                                                                                    """));
        Assert.Equal("pure helper 'step' return expression contains side-effecting operations.", callEffect.Message);
    }

    [Fact]
    public void Rejects_nes_explicit_inline_value_helper_when_not_substitutable()
    {
        const string source = """
                              inline u8 step(u8 value) {
                                  u8 next = value + 1;
                                  return next;
                              }

                              void main() {
                                  video_init();
                                  u8 result = step(4);
                                  return;
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => NesRomCompiler.CompileSource(source));
        Assert.Equal("NES target cannot inline helper 'step' as a value because inline value helpers must be exactly one return expression.", exception.Message);
    }

    [Fact]
    public void Rejects_nes_assignment_to_immutable_let_binding()
    {
        const string source = """
                              void main() {
                                  video_init();
                                  let speed = 2;
                                  speed = 3;
                                  return;
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => NesRomCompiler.CompileSource(source));
        Assert.Equal("Cannot assign to immutable local 'speed'.", exception.Message);
    }

    [Fact]
    public void Rejects_nes_compound_and_postfix_mutation_of_immutable_let_binding()
    {
        var compound = Assert.Throws<InvalidOperationException>(() => NesRomCompiler.CompileSource("""
                                                                                                  void main() {
                                                                                                      video_init();
                                                                                                      let speed = 2;
                                                                                                      speed += 1;
                                                                                                      return;
                                                                                                  }
                                                                                                  """));
        Assert.Equal("Cannot assign to immutable local 'speed'.", compound.Message);

        var postfix = Assert.Throws<InvalidOperationException>(() => NesRomCompiler.CompileSource("""
                                                                                                 void main() {
                                                                                                     video_init();
                                                                                                     let speed = 2;
                                                                                                     speed++;
                                                                                                     return;
                                                                                                 }
                                                                                                 """));
        Assert.Equal("Cannot assign to immutable local 'speed'.", postfix.Message);
    }

    [Fact]
    public void Nes_drawing_sample_compiles_with_helper_functions()
    {
        var source = File.ReadAllText(RepositoryFile("samples/nes-drawing/drawing.rs"));

        Assert.Contains("void draw_face()", source);

        var rom = NesRomCompiler.CompileSource(source);

        Assert.Equal(40976, rom.Length);
    }

    [Fact]
    public void Cross_target_camera_sample_compiles_for_game_boy_and_nes()
    {
        var sourcePath = RepositoryFile("samples/cross-target-camera/camera.rs");
        var source = File.ReadAllText(sourcePath);
        var baseDirectory = Path.GetDirectoryName(sourcePath);

        Assert.Equal(32768, GameBoyRomCompiler.CompileSource(source, baseDirectory).Length);
        Assert.Equal(40976, NesRomCompiler.CompileSource(source, baseDirectory).Length);
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

        Assert.Equal(40976, rom.Length);
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
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.Equal(40976, rom.Length);
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
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.Equal(40976, rom.Length);
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
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.Equal(40976, rom.Length);
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
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.Equal(40976, rom.Length);
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
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.Equal(40976, rom.Length);
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
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.Equal(40976, rom.Length);
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
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.Equal(40976, rom.Length);
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
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.Equal(40976, rom.Length);
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
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.Equal(40976, rom.Length);
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
    public void Compiles_struct_array_initializer_like_explicit_field_assignments()
    {
        const string explicitSource = """
                                      struct Actor {
                                          u8 x;
                                          u8 y;
                                          bool active;
                                      }

                                      void main() {
                                          video_init();
                                          u8 seed = 3;
                                          Actor actors[3];
                                          actors[0].x = 1;
                                          actors[0].active = 1;
                                          actors[1].y = seed + 1;
                                          u8 copy = actors[2].active;
                                          return;
                                      }
                                      """;

        const string initializerSource = """
                                         struct Actor {
                                             u8 x;
                                             u8 y;
                                             bool active;
                                         }

                                         void main() {
                                             video_init();
                                             u8 seed = 3;
                                             Actor actors[3] = [{ x: 1, active: 1 }, { y: seed + 1 }];
                                             u8 copy = actors[2].active;
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
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.Equal(40976, rom.Length);
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
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.Equal(40976, rom.Length);
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
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.Equal(40976, rom.Length);
        Assert.True(ContainsSequence(prg, [0xA5, 0x01, 0x18, 0x65, 0x00, 0x85, 0x00]), "x += y should lower to direct zero-page addition and store.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0x38, 0xE9, 0x01, 0x85, 0x00]), "x -= 1 should lower to direct zero-page subtract/store without a helper call.");
    }

    [Fact]
    public void Compiles_nested_variable_subtraction_without_reusing_expression_scratch()
    {
        const string source = """
                              void main() {
                                  u8 a = 10;
                                  u8 b = 7;
                                  u8 c = 2;
                                  u8 result = a - (b - c);
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.Equal(40976, rom.Length);
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0x48, 0xA5, 0x01, 0x38, 0xE5, 0x02, 0x85, 0xE9, 0x68, 0x38, 0xE5, 0xE9, 0x85, 0x03]), "Nested subtraction should preserve the outer left operand on the CPU stack while evaluating the right operand.");
    }

    [Fact]
    public void Compiles_nested_variable_relational_compare_without_reusing_expression_scratch()
    {
        const string source = """
                              void main() {
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

        Assert.Equal(40976, rom.Length);
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0x38, 0xE5, 0x01, 0x48, 0xA5, 0x02, 0x38, 0xE5, 0x03, 0x85, 0xE9, 0x68, 0xC5, 0xE9]), "Nested relational compares should preserve the left expression on the CPU stack while evaluating the right expression.");
    }

    [Fact]
    public void Compiles_nested_variable_equality_compare_without_reusing_expression_scratch()
    {
        const string source = """
                              void main() {
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

        Assert.Equal(40976, rom.Length);
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0x38, 0xE5, 0x01, 0x48, 0xA5, 0x02, 0x38, 0xE5, 0x03, 0x85, 0xE9, 0x68, 0xC5, 0xE9, 0xD0]), "Nested == should preserve the left expression on the CPU stack before comparing.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0x38, 0xE5, 0x01, 0x48, 0xA5, 0x02, 0x38, 0xE5, 0x03, 0x85, 0xE9, 0x68, 0xC5, 0xE9, 0xF0]), "Nested != should preserve the left expression on the CPU stack before comparing.");
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
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.Equal(40976, rom.Length);
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
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.Equal(40976, rom.Length);
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
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.Equal(40976, rom.Length);
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
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.Equal(40976, rom.Length);
        Assert.True(ContainsSequence(prg, [0xA5, 0x04, 0xAA]), "runtime array indexing should transfer the byte index to X without a helper call or implicit bounds check.");
        Assert.True(ContainsSequence(prg, [0xB5, 0x00, 0x18, 0x69, 0x01, 0x95, 0x00]), "values[i] += 1 should use zero-page indexed load/add/store without a helper call.");
    }

    [Fact]
    public void Compiles_struct_array_field_access_with_runtime_index_stride()
    {
        const string source = """
                              struct Actor {
                                  u8 x;
                                  u8 y;
                                  bool active;
                              }

                              void main() {
                                  video_init();
                                  Actor actors[3];
                                  u8 i = 2;
                                  actors[1].active = 7;
                                  u8 copy = actors[1].active;
                                  actors[i].y += 1;
                                  u8 runtimeCopy = actors[i].x;
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.Equal(40976, rom.Length);
        Assert.True(ContainsSequence(prg, [0xA9, 0x07, 0x85, 0x05]), "actors[1].active should store at base + sizeof(Actor) + offsetof(active).");
        Assert.True(ContainsSequence(prg, [0xA5, 0x05, 0x85, 0x0A]), "constant indexed field reads should use the flattened field address directly.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x09, 0x85, 0xE8, 0x18, 0x65, 0xE8, 0x18, 0x65, 0xE8, 0xAA, 0xB5, 0x01, 0x18, 0x69, 0x01, 0x95, 0x01]), "actors[i].y should compute X from i * sizeof(Actor) and use the y-field base address.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x09, 0x85, 0xE8, 0x18, 0x65, 0xE8, 0x18, 0x65, 0xE8, 0xAA, 0xB5, 0x00, 0x85, 0x0B]), "runtime indexed field reads should compute X from i * sizeof(Actor) and use the field base address.");
    }

    [Fact]
    public void Compiles_hand_written_actor_pool_update_loop_as_fixed_storage_and_branches()
    {
        const string source = """
                              struct Actor {
                                  u8 x;
                                  u8 y;
                                  u8 active;
                              }

                              void main() {
                                  video_init();
                                  Actor actors[3] = [{ active: 1, x: 4 }, { x: 9 }, { active: 1, x: 12 }];
                                  for (u8 i = 0; i < countof(actors); i += 1) {
                                      if (actors[i].active != 0) {
                                          actors[i].x += 1;
                                      }
                                  }
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.Equal(40976, rom.Length);
        Assert.True(ContainsSequence(prg, [0xA5, 0x09, 0xC9, 0x03]), "pool loop should compare the byte index with countof(actors), not use an iterator object.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x09, 0x85, 0xE8, 0x18, 0x65, 0xE8, 0x18, 0x65, 0xE8, 0xAA, 0xB5, 0x02, 0xC9, 0x00]), "active checks should read actors[i].active through the fixed field base plus i * stride.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x09, 0x85, 0xE8, 0x18, 0x65, 0xE8, 0x18, 0x65, 0xE8, 0xAA, 0xB5, 0x00, 0x18, 0x69, 0x01, 0x95, 0x00]), "updates should mutate actors[i].x through fixed storage with no actor object or dispatch table.");
    }

    [Fact]
    public void Rejects_struct_array_fields_that_are_not_byte_sized()
    {
        const string source = """
                              struct Actor {
                                  u16 worldX;
                                  u8 y;
                              }

                              void main() {
                                  Actor actors[2];
                                  return;
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => NesRomCompiler.CompileSource(source));

        Assert.Equal("NES target struct array field type 'u16' is not byte-sized; use u8, i8, bool, or enum fields until mixed-width pool layout is implemented.", exception.Message);
    }

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

                                    void main() {
                                        video_init();
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
                                   void main() {
                                       video_init();
                                       actor.Pool(enemies, 0b10);
                                       enemy.Def(Goomba, behavior: Walker, speed: 0x01u8, hp: 0b1, cooldown: 0x3Cu8);
                                       enemies[0].active = 1;
                                       enemies[0].kind = Goomba;
                                       enemies[0].health = enemy.Hp(enemies[0].kind);
                                       enemies[0].vx = (i8)enemy.Speed(enemies[0].kind);
                                       enemies[0].timer = enemy.Cooldown(enemies[0].kind);
                                       return;
                                   }
                                   """;

        Assert.Equal(NesRomCompiler.CompileSource(manualSource), NesRomCompiler.CompileSource(actorSource));
    }

    [Fact]
    public void Rejects_actor_pool_without_literal_capacity()
    {
        const string source = """
                              void main() {
                                  u8 capacity = 2;
                                  actor.Pool(enemies, capacity);
                                  return;
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => NesRomCompiler.CompileSource(source));

        Assert.Equal("actor.Pool for 'enemies' requires a literal capacity from 1 to 255.", exception.Message);
    }

    [Fact]
    public void Rejects_actor_pool_above_nes_struct_array_storage_capacity()
    {
        const string source = """
                              void main() {
                                  actor.Pool(enemies, 65);
                                  return;
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => NesRomCompiler.CompileSource(source));

        Assert.Equal("NES target struct array 'enemies' uses 845 byte slot(s), but runtime indexed struct arrays are limited to 255 byte slots.", exception.Message);
    }

    [Fact]
    public void Actor_framework_projects_wide_y_spawns_for_draw_and_collision_on_nes()
    {
        var baseDirectory = WriteActorSpawnMap(
            """
            [
              { "id": 1, "type": "Goomba", "x": 24, "y": 261 }
            ]
            """);
        const string source = """
                              void main() {
                                  world_column(0, 0, 0);
                                  world_map(40, 10, 40);
                                  camera_init(40, 10, 40);
                                  sprite.Asset(goomba, "goomba.nes.json");
                                  actor.Pool(enemies, 2);
                                  enemy.Def(Goomba, sprite: goomba, behavior: Walker, hitboxWidth: 8, hitboxHeight: 8);
                                  camera_set_position(0, 160);
                                  actor.SpawnLayer(enemies, "level.tmj", "actors");
                                  enemies.TouchTiles(0, 1);
                                  enemies.LandOnTiles(4, 12, 1);
                                  enemies.Draw();
                                  return;
                              }
                              """;

        var parse = new SomeParser().Parse(source);
        Assert.True(parse.IsSuccess, parse.IsFailure ? parse.Error : string.Empty);

        var loweredProgram = ActorFrameworkLowerer.Lower(parse.Value, NesTarget.Capabilities, supportsUpdate: true, supportsDraw: true, baseDirectory);
        var visitor = new PrintNodeVisitor();
        loweredProgram.Accept(visitor);
        var lowered = visitor.ToString();

        Assert.Contains("u8 yHi;", lowered);
        Assert.Contains("inline u8 __enemies_spawn_0_y(u8 index)=>5;", lowered);
        Assert.Contains("inline u8 __enemies_spawn_0_yHi(u8 index)=>1;", lowered);
        Assert.Contains("u8 __enemies_touch_screen_y=enemies[__enemies_touch_i].y-__enemies_touch_camera_y_lo;", lowered);
        Assert.Contains("camera.ScreenAabbTiles(__enemies_touch_screen_x, __enemies_touch_screen_y, 8, 8, 1)", lowered);
        Assert.Contains("camera.ScreenAabbHitTop(__enemies_land_screen_x, __enemies_land_screen_y-4", lowered);
        Assert.Contains("sprite.Draw(goomba, __enemies_draw_screen_x, __enemies_draw_screen_y, 0, false, 0);", lowered);
    }

    [Fact]
    public void Rejects_unknown_actor_behavior_with_named_diagnostic_on_nes()
    {
        const string source = """
                              void main() {
                                  actor.Pool(enemies, 1);
                                  enemy.Def(Goomba, behavior: Ghost);
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
                              void main() {
                                  video_init();
                                  sprite.Asset(goomba, "wide-goomba.nes.json");
                                  actor.Pool(enemies, 23);
                                  enemy.Def(Goomba, sprite: goomba, behavior: Walker, speed: 1);
                                  video.WaitVBlank();
                                  enemies.Draw();
                                  return;
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => NesRomCompiler.CompileSource(source, baseDirectory));

        Assert.Equal(
            "Target 'nes' supports 64 hardware sprites per frame, but actor.Pool for 'enemies' can draw up to 69 because capacity 23 times enemy.Def 'Goomba' sprite 'goomba' uses 3 hardware sprites.",
            exception.Message);
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
                              void main() {
                                  video_init();
                                  sprite.Asset(goomba, "goomba.png", 8, 16);
                                  actor.Pool(enemies, 8);
                                  enemy.Def(Goomba, sprite: goomba, behavior: Walker, speed: 1);
                                  video.WaitVBlank();
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
                              void main() {
                                  video_init();
                                  sprite.Asset(goomba, "goomba.nes.json");
                                  actor.Pool(enemies, 9);
                                  enemy.Def(Goomba, sprite: goomba, behavior: Walker, speed: 1);
                                  video.WaitVBlank();
                                  enemies.Draw();
                                  return;
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => NesRomCompiler.CompileSource(source, baseDirectory));

        Assert.Equal(
            "Target 'nes' supports 8 hardware sprites per scanline, but actor.Pool for 'enemies' can draw up to 9 on one scanline because capacity 9 times enemy.Def 'Goomba' sprite 'goomba' uses 1 hardware sprite on its busiest scanline.",
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

                                    void main() {
                                        video_init();
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
                                   void main() {
                                       video_init();
                                       actor.Pool(enemies, 1);
                                       enemy.Def(Goomba, behavior: Walker, speed: 1, hp: 1);
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

                                    void main() {
                                        video_init();
                                        sprite.Asset(goomba, "goomba.nes.json");
                                        Actor enemies[1];
                                        enemies[0].active = 1;
                                        enemies[0].kind = Goomba;
                                        enemies[0].x = 24;
                                        enemies[0].y = 48;
                                        video.WaitVBlank();

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
                                                sprite.Draw(goomba, __enemies_draw_x_Goomba, __enemies_draw_y_Goomba, 0, false, 0);
                                            }
                                        }

                                        return;
                                    }
                                    """;

        const string actorSource = """
                                   void main() {
                                       video_init();
                                       sprite.Asset(goomba, "goomba.nes.json");
                                       actor.Pool(enemies, 1);
                                       enemy.Def(Goomba, sprite: goomba, behavior: Walker, speed: 1, hp: 1);
                                       enemies[0].active = 1;
                                       enemies[0].kind = Goomba;
                                       enemies[0].x = 24;
                                       enemies[0].y = 48;
                                       video.WaitVBlank();
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
                              void main() {
                                  video_init();
                                  world_column(0, 0, 0);
                                  world_map(1, 10, 2);
                                  camera_init(1, 10, 2);
                                  sprite.Asset(goomba, "goomba.nes.json");
                                  actor.Pool(enemies, 1);
                                  enemy.Def(Goomba, sprite: goomba, behavior: Walker, speed: 1, hp: 1);
                                  enemies[0].active = 1;
                                  enemies[0].kind = Goomba;
                                  enemies[0].x = 20;
                                  enemies[0].xHi = 0;
                                  enemies[0].y = 48;
                                  camera_set_position(4, 0);
                                  video.WaitVBlank();
                                  enemies.Draw();
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source, baseDirectory);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.Equal(40976, rom.Length);
        Assert.True(ContainsSequence(prg, [0xA5, 0xE0]), "actor draw should read the camera X low byte.");
        Assert.True(ContainsSequence(prg, [0xA5, 0xE1, 0x4A, 0x4A, 0x4A, 0x4A, 0x4A]), "actor draw should derive the camera X high byte from the absolute camera tile.");
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

                                    void main() {
                                        world_column(0, 0, 0);
                                        world_flags(0, 0, 1);
                                        world_column(1, 0, 0);
                                        world_flags(1, 0, 2);
                                        world_map(2, 10, 2);
                                        camera_init(2, 10, 2);
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
                                        camera_set_position(0, 0);

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
                                                        if (camera.ScreenAabbTiles(__enemies_touch_screen_x, __enemies_touch_screen_y, 8, 8, 1) != 0) {
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
                                   void main() {
                                       world_column(0, 0, 0);
                                       world_flags(0, 0, 1);
                                       world_column(1, 0, 0);
                                       world_flags(1, 0, 2);
                                       world_map(2, 10, 2);
                                       camera_init(2, 10, 2);
                                       actor.Pool(enemies, 2);
                                       enemy.Def(Goomba, behavior: Walker, hitboxWidth: 8, hitboxHeight: 8);
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
                                       camera_set_position(0, 0);
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

                             void main() {
                                 world_column(0, 0, 0);
                                 world_map(40, 10, 2);
                                 camera_init(40, 10, 2);
                                 Actor enemies[1];
                                 u8 __enemies_spawn_0_used[2];

                                 camera_set_position(0, 0);
                             {{RuntimeSpawnActivationBlockForNes("__enemies_spawn_0_call0")}}

                                 camera_set_position(128, 0);
                             {{RuntimeSpawnActivationBlockForNes("__enemies_spawn_0_call1")}}
                                 return;
                             }
                             """;

        const string actorSource = """
                                   void main() {
                                       world_column(0, 0, 0);
                                       world_map(40, 10, 2);
                                       camera_init(40, 10, 2);
                                       actor.Pool(enemies, 1);
                                       enemy.Def(Goomba, behavior: Walker);
                                       enemy.Def(Bat, behavior: Flyer);
                                       camera_set_position(0, 0);
                                       actor.SpawnLayer(enemies, "level.tmj", "actors");
                                       camera_set_position(128, 0);
                                       actor.SpawnLayer(enemies, "level.tmj", "actors");
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
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        var loopStart = IndexOfSequence(prg, [0xA5, 0x00, 0x18, 0x69, 0x01, 0x85, 0x00]);
        var continueCompare = IndexOfSequence(prg, [0xA5, 0x00, 0xC9, 0x01, 0xD0]);
        var breakCompare = IndexOfSequence(prg, [0xA5, 0x00, 0xC9, 0x03, 0xD0]);

        Assert.Equal(40976, rom.Length);
        Assert.True(loopStart >= 0, "loop body should start with direct x++ zero-page arithmetic.");
        Assert.True(continueCompare >= 0, "continue guard should compare x with 1.");
        Assert.True(breakCompare >= 0, "break guard should compare x with 3.");
        Assert.True(ContainsAbsoluteJumpTo(prg, 0x8000 + loopStart, continueCompare, breakCompare), "continue should jump back to the loop start.");
        Assert.True(ContainsAbsoluteJumpAfter(prg, breakCompare, prg.Length, 0x8000 + breakCompare), "break should jump beyond the loop body.");
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
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        var continueCompare = IndexOfSequence(prg, [0xA5, 0x01, 0xC9, 0x01, 0xD0]);
        var breakCompare = IndexOfSequence(prg, [0xA5, 0x01, 0xC9, 0x03, 0xD0]);
        var increment = IndexOfSequence(prg, [0xA5, 0x01, 0x18, 0x69, 0x01, 0x85, 0x01]);

        Assert.Equal(40976, rom.Length);
        Assert.True(continueCompare >= 0, "continue guard should compare i with 1.");
        Assert.True(breakCompare >= 0, "break guard should compare i with 3.");
        Assert.True(increment >= 0, "for increment should still be emitted as direct i += 1 arithmetic.");

        Assert.True(ContainsAbsoluteJumpTo(prg, 0x8000 + increment, continueCompare, breakCompare), "continue should jump to the for increment.");
        Assert.True(ContainsAbsoluteJumpAfter(prg, breakCompare, prg.Length, 0x8000 + increment + 7), "break should jump beyond the increment and final loop jump.");
    }

    [Fact]
    public void Compiles_short_for_loop_condition_as_direct_relative_branch_without_trampoline()
    {
        const string source = """
                              void main() {
                                  u8 x = 0;
                                  for (u8 i = 0; i < 3; i += 1) {
                                      x += 1;
                                  }
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        var conditionCompare = IndexOfSequence(prg, [0xA5, 0x01, 0xC9, 0x03, 0xB0]);
        var bodyIncrement = IndexOfSequence(prg, [0xA5, 0x00, 0x18, 0x69, 0x01, 0x85, 0x00]);

        Assert.True(conditionCompare >= 0, "for condition should compare i with the literal upper bound.");
        Assert.True(bodyIncrement > conditionCompare, "for body should follow the condition.");
        Assert.NotEqual(0x4C, prg[conditionCompare + 6]);
        Assert.True(ReadRelativeTarget(prg, conditionCompare + 4) > 0x8000 + bodyIncrement, "direct false branch should skip the body and increment without landing on a trampoline.");
    }

    [Fact]
    public void Compiles_long_for_loop_condition_with_branch_trampoline()
    {
        var body = string.Join(Environment.NewLine, Enumerable.Repeat("x += 1;", 40));
        var source = $$"""
                       void main() {
                           u8 x = 0;
                           for (u8 i = 0; i < 1; i += 1) {
                       {{body}}
                           }
                           return;
                       }
                       """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        var conditionCompare = IndexOfSequence(prg, [0xA5, 0x01, 0xC9, 0x01, 0xB0]);
        var trampolineAddress = ReadRelativeTarget(prg, conditionCompare + 4);
        var trampolineOffset = trampolineAddress - 0x8000;

        Assert.True(conditionCompare >= 0, "for condition should compare i with the literal upper bound.");
        Assert.Equal(0x4C, prg[conditionCompare + 6]);
        Assert.Equal(0x4C, prg[trampolineOffset]);
        Assert.True(ReadLittleEndian16(prg, trampolineOffset + 1) > trampolineAddress, "long false path should jump forward to the for end label.");
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
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        var continueCompare = IndexOfSequence(prg, [0xA5, 0x00, 0xC9, 0x01, 0xD0]);
        var conditionCompare = IndexOfSequence(prg, [0xA5, 0x00, 0xC9, 0x03, 0xB0]);

        Assert.Equal(40976, rom.Length);
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0x18, 0x69, 0x01, 0x85, 0x00]), "do body should emit x++ as direct zero-page arithmetic before the first condition check.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0x18, 0x69, 0x02, 0x85, 0x00]), "do body should emit direct zero-page arithmetic after the continue guard.");
        Assert.True(continueCompare >= 0, "continue guard should compare x with 1.");
        Assert.True(conditionCompare >= 0, "do-while condition should compare x with 3 at the bottom of the loop.");

        Assert.True(ContainsAbsoluteJumpTo(prg, 0x8000 + conditionCompare, continueCompare, conditionCompare), "continue should jump to the do-while condition check.");
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
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        var andLeftCompare = IndexOfSequence(prg, [0xA5, 0x00, 0xC9, 0x00, 0xF0]);
        var andRightCompare = IndexOfSequence(prg, [0xA5, 0x01, 0xC9, 0x00, 0xF0]);
        var orLeftCompare = IndexOfSequence(prg, [0xA5, 0x00, 0xC9, 0x00, 0xD0]);
        var orBody = IndexOfSequence(prg, [0xA5, 0x02, 0x18, 0x69, 0x02, 0x85, 0x02]);

        Assert.Equal(40976, rom.Length);
        Assert.True(andLeftCompare >= 0, "&& should test the left condition and branch false before touching the right side.");
        Assert.True(andRightCompare > andLeftCompare, "&& should evaluate the right condition only after the left condition succeeds.");
        Assert.True(orLeftCompare >= 0, "|| should test the left condition with a direct true branch.");
        Assert.True(orBody > orLeftCompare, "|| body should be emitted after the left condition branch.");
        var orLeftTarget = ReadRelativeTarget(prg, orLeftCompare + 4);
        Assert.True(orLeftTarget > 0x8000 + orLeftCompare && orLeftTarget <= 0x8000 + orBody, "|| should short-circuit toward the body when the left condition succeeds.");
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
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.Equal(40976, rom.Length);
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
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.Equal(40976, rom.Length);
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
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.Equal(40976, rom.Length);
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
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.Equal(40976, rom.Length);
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
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.Equal(40976, rom.Length);
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
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.Equal(40976, rom.Length);
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
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.Equal(40976, rom.Length);
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
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.Equal(40976, rom.Length);
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
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.Equal(40976, rom.Length);
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
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.Equal(40976, rom.Length);
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
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.Equal(40976, rom.Length);
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
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();
        var chr = rom.Skip(16 + 32 * 1024).Take(8 * 1024).ToArray();
        var spriteTile = chr.Skip(6 * 16).Take(16).ToArray();

        Assert.Equal(40976, rom.Length);
        Assert.Equal(Enumerable.Repeat((byte)0xFF, 8).Concat(Enumerable.Repeat((byte)0x00, 8)), spriteTile);
        Assert.True(ContainsSequence(prg, [0xA9, 0x20, 0x38, 0xE9, 0x01, 0x8D, 0x00, 0x02]), "sprite_draw should write NES OAM Y as logical y - 1.");
        Assert.True(ContainsSequence(prg, [0xA9, 0x06, 0x8D, 0x01, 0x02]), "sprite_draw should write the first compiled sprite tile to OAM.");
        Assert.True(ContainsSequence(prg, [0xA9, 0x02, 0x8D, 0x02, 0x02]), "sprite_draw should lower portable palette slot 2 to NES OAM attributes.");
        Assert.True(ContainsSequence(prg, [0xA9, 0x18, 0x8D, 0x03, 0x02]), "sprite_draw should write logical x to NES OAM.");
        Assert.True(ContainsSequence(prg, [0xA9, 0x02, 0x8D, 0x14, 0x40]), "sprite_draw should DMA the OAM shadow page after writing logical sprites.");
    }

    [Fact]
    public void Compiles_png_sprite_sheet_using_nes_platform_variant()
    {
        var baseDirectory = WriteSpritePng(
            "hero.nes.png",
            8,
            8,
            Rows(8, 8, Enumerable.Repeat("33333333", 8).ToArray()));

        const string source = """
                              void main() {
                                  video_init();
                                  sprite_asset(hero, "hero.png", 8, 8);
                                  while (true) {
                                      video_wait_vblank();
                                      sprite_draw(hero, 24, 32, 0, 0, 0);
                                  }
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source, baseDirectory);
        var chr = rom.Skip(16 + 32 * 1024).Take(8 * 1024).ToArray();
        var spriteTile = chr.Skip(6 * 16).Take(16).ToArray();

        Assert.Equal(40976, rom.Length);
        Assert.Equal(Enumerable.Repeat((byte)0xFF, 16), spriteTile);
    }

    [Fact]
    public void Colored_png_sprite_sheet_applies_derived_nes_sprite_palette_to_draw_slot()
    {
        var baseDirectory = WriteSpritePng(
            "hero.nes.png",
            8,
            8,
            [
                (R: (byte)0x00, G: (byte)0x00, B: (byte)0x00, A: (byte)0x00),
                (R: (byte)0xFC, G: (byte)0xBC, B: (byte)0xB0, A: (byte)0xFF),
                (R: (byte)0xD8, G: (byte)0x28, B: (byte)0x00, A: (byte)0xFF),
                (R: (byte)0x00, G: (byte)0x00, B: (byte)0x00, A: (byte)0xFF),
            ],
            Rows(
                8,
                8,
                "11112222",
                "11112222",
                "11112222",
                "11112222",
                "33333333",
                "33333333",
                "33333333",
                "33333333"));

        const string source = """
                              void main() {
                                  video_init();
                                  palette.Background(0, 0, 1, 2, 3);
                                  palette.Sprite(0, 0, 0, 1, 3);
                                  sprite_asset(hero, "hero.png", 8, 8);
                                  while (true) {
                                      video_wait_vblank();
                                      sprite_draw(hero, 24, 32, 0, 0, 0);
                                  }
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source, baseDirectory);

        Assert.Equal(40976, rom.Length);
        Assert.True(
            ContainsSequence(rom, [0x30, 0x36, 0x16, 0x0F]),
            "colored NES PNG sprite assets should drive the sprite palette slot without overwriting the universal background color.");
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
    public void Rejects_png_sprite_palette_slots_outside_nes_capabilities_before_palette_derivation()
    {
        var baseDirectory = WriteSpritePng(
            "hero.nes.png",
            8,
            8,
            Rows(8, 8, Enumerable.Repeat("33333333", 8).ToArray()));

        const string source = """
                              void main() {
                                  sprite_asset(hero, "hero.nes.png", 8, 8);
                                  while (true) {
                                      sprite_draw(hero, 24, 32, 0, 0, 4);
                                  }
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => NesRomCompiler.CompileSource(source, baseDirectory));

        Assert.Contains("Target 'nes' supports sprite palette slots 0..3, but slot 4 was requested.", exception.Message);
    }

    [Fact]
    public void Rejects_constant_y_sprite_draws_that_exceed_nes_scanline_budget()
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
        var draws = string.Join(
            Environment.NewLine,
            Enumerable.Range(0, 9).Select(index => $"        sprite_draw(hero, {index * 8}, 24, 0);"));
        var source = """
                     void main() {
                         video_init();
                         sprite_asset(hero, "hero.nes.json");
                         while (true) {
                             video_wait_vblank();

                     """ + draws + """
                         }
                     }
                     """;

        var exception = Assert.Throws<InvalidOperationException>(() => NesRomCompiler.CompileSource(source, baseDirectory));

        Assert.Equal(
            "Target 'nes' supports 8 hardware sprites per scanline, but 9 are required on scanline 24 for drawing logical sprites in one frame.",
            exception.Message);
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
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();
        var expectedRows = Enumerable
            .Repeat((byte)0, 64)
            .ToArray();
        expectedRows[0] = 1;
        expectedRows[1] = 3;
        expectedRows[32] = 2;
        expectedRows[33] = 4;

        Assert.Equal(40976, rom.Length);
        Assert.True(ContainsSequence(prg, expectedRows), "world_map should seed the visible NES nametable from world_column data.");
        Assert.True(ContainsSequence(prg, [0xA9, 0x08, 0x85, 0xE7, 0xA5, 0xE7, 0x85, 0xE0]), "camera_set_position(8, 0) should store the requested horizontal camera byte after the movement check.");
        Assert.True(ContainsSequence(prg, [0xA5, 0xE7, 0x4A, 0x4A, 0x4A, 0x85, 0xE1]), "camera_set_position(8, 0) should derive the absolute camera tile on short maps too.");
        Assert.True(ContainsSequence(prg, [0xAD, 0x02, 0x20, 0xA5, 0xE1, 0x29, 0x20]), "camera_apply should derive the horizontal nametable bit from the absolute camera tile.");
        Assert.True(ContainsSequence(prg, [0x8D, 0x00, 0x20, 0xA5, 0xE0, 0x8D, 0x05, 0x20, 0xA9, 0x00, 0x8D, 0x05, 0x20]), "camera_apply should write PPUCTRL before horizontal and zero vertical scroll.");
    }

    [Fact]
    public void Compiles_four_screen_camera_path_from_world_map_to_nes_scroll()
    {
        var tallColumn = string.Join(", ", Enumerable.Range(0, 60).Select(row => row % 4 + 1));
        var source = $$"""
                       void main() {
                           video_init();
                           world_column(0, {{tallColumn}});
                           world_column(63, {{tallColumn}});
                           world_map(64, 0, 60);
                           camera_init(64, 0, 60);
                           u8 x = 0;
                           u8 y = 0;
                           while (true) {
                               video_wait_vblank();
                               x += 1;
                               y += 1;
                               camera_set_position(x, y);
                               camera_apply();
                           }
                       }
                       """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.Equal(40976, rom.Length);
        Assert.Equal(0x08, rom[6] & 0x08);
        Assert.True(CountOccurrences(prg, [0x8D, 0x07, 0x20]) >= 17, "four-screen startup should upload palette plus 16 nametable pages.");
        Assert.True(ContainsSequence(prg, [0xA5, 0xE8, 0xC9, 0x1E]), "camera_apply should derive the vertical nametable bit from the buffer tile row modulo 60.");
        Assert.True(ContainsSequence(prg, [0xA5, 0xEA, 0x29, 0x07]), "camera_apply should preserve fine Y when wrapping NES scroll Y at 240 pixels.");
    }

    [Fact]
    public void Nes_vertical_camera_large_delta_steps_one_pixel_instead_of_jumping()
    {
        var column = string.Join(", ", Enumerable.Range(0, 14).Select(row => row % 4 + 1));
        var source = $$"""
                       void main() {
                           world_column(0, {{column}});
                           world_map(1, 0, 14);
                           camera_init(1, 0, 14);
                           u8 y = 56;
                           camera_set_position(0, y);
                           camera_apply();
                       }
                       """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.True(
            ContainsSequence(prg, [0xA5, 0xEC, 0x38, 0xE5, 0xEA, 0xC9, 0x80]),
            "NES vertical camera fallback should compare the target-current delta before choosing a one-pixel step.");
        Assert.False(
            ContainsSequence(prg, [0xA5, 0xEC, 0x85, 0xEA, 0xA5, 0xEC, 0x4A, 0x4A, 0x4A, 0x85, 0xEB]),
            "NES vertical camera must not jump directly to a non-adjacent requested Y.");
    }

    [Fact]
    public void Camera_relative_collision_uses_absolute_camera_tile_after_scroll_wrap()
    {
        const string source = """
                              void main() {
                                  world_column(0, 1, 2);
                                  world_flags(0, 0, 1);
                                  world_map(68, 10, 2);
                                  camera_init(68, 10, 2);
                                  while (true) {
                                      video_wait_vblank();
                                      u8 x = button_hold_ticks(right);
                                      camera_set_position(x, 0);
                                      u8 hit = camera.AabbTiles(72, 8, 16, 8, 1);
                                  }
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.Equal(40976, rom.Length);
        Assert.True(
            ContainsSequence(prg, [0xA5, 0xE0, 0x29, 0x07, 0x18, 0x69, 0x48, 0x4A, 0x4A, 0x4A, 0x18, 0x65, 0xE1]),
            "camera.AabbTiles should combine camera fine X with the absolute source tile, not the wrapped scroll byte.");
    }

    [Fact]
    public void Accepts_vertical_camera_stream_area_taller_than_four_screen_buffer()
    {
        var tallColumn = string.Join(", ", Enumerable.Range(0, 96).Select(row => row % 4 + 1));
        var source = $$"""
                       void main() {
                           world_column(0, {{tallColumn}});
                           world_map(1, 0, 96);
                           camera_init(1, 0, 96);
                           while (true) {
                               camera_set_position(0, 1);
                               camera_apply();
                           }
                       }
                       """;

        var rom = NesRomCompiler.CompileSource(source);

        Assert.Equal(0x08, rom[6] & 0x08);
    }

    [Fact]
    public void Rejects_vertical_camera_stream_start_outside_four_screen_buffer()
    {
        const string source = """
                              void main() {
                                  world_column(0, 1);
                                  world_map(1, 0, 1);
                                  camera_init(1, 60, 1);
                                  while (true) {
                                      camera_set_position(0, 1);
                                      camera_apply();
                                  }
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => NesRomCompiler.CompileSource(source));

        Assert.Contains("NES four-screen free scroll stream area must fit within the 60-row four-screen height", exception.Message);
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

    private static string WriteSpritePng(string fileName, int frameWidth, int frameHeight, params string[][] frames)
    {
        var palette = new[]
        {
            (R: (byte)0x00, G: (byte)0x00, B: (byte)0x00, A: (byte)0x00),
            (R: (byte)0xFF, G: (byte)0xFF, B: (byte)0xFF, A: (byte)0xFF),
            (R: (byte)0xB8, G: (byte)0xB8, B: (byte)0xB8, A: (byte)0xFF),
            (R: (byte)0x00, G: (byte)0x00, B: (byte)0x00, A: (byte)0xFF),
        };

        return WriteSpritePng(fileName, frameWidth, frameHeight, palette, frames);
    }

    private static string WriteSpritePng(
        string fileName,
        int frameWidth,
        int frameHeight,
        IReadOnlyList<(byte R, byte G, byte B, byte A)> palette,
        params string[][] frames)
    {
        var directory = Path.Combine(Path.GetTempPath(), "RetroSharp.NES.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        var width = frameWidth * frames.Length;
        var height = frameHeight;
        var rgba = new byte[width * height * 4];

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
            crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
        }

        return crc;
    }

    private static bool ContainsSequence(IReadOnlyList<byte> bytes, IReadOnlyList<byte> sequence)
    {
        return IndexOfSequence(bytes, sequence) >= 0;
    }

    private static int CountOccurrences(IReadOnlyList<byte> bytes, IReadOnlyList<byte> sequence)
    {
        var count = 0;
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
                count++;
            }
        }

        return count;
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

    private static string BytesAround(IReadOnlyList<byte> bytes, int offset)
    {
        var start = Math.Max(0, offset - 8);
        var length = Math.Min(bytes.Count - start, 17);
        return $"0x{offset:X4} [" + string.Join(" ", bytes.Skip(start).Take(length).Select(value => value.ToString("X2"))) + "]";
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

    private static bool ContainsAbsoluteJumpTo(IReadOnlyList<byte> bytes, int target, int startInclusive, int endExclusive)
    {
        for (var i = Math.Max(0, startInclusive); i <= Math.Min(bytes.Count, endExclusive) - 3; i++)
        {
            if (bytes[i] == 0x4C && ReadLittleEndian16(bytes, i + 1) == target)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsAbsoluteJumpAfter(IReadOnlyList<byte> bytes, int startInclusive, int endExclusive, int minimumTarget)
    {
        for (var i = Math.Max(0, startInclusive); i <= Math.Min(bytes.Count, endExclusive) - 3; i++)
        {
            if (bytes[i] == 0x4C && ReadLittleEndian16(bytes, i + 1) > minimumTarget)
            {
                return true;
            }
        }

        return false;
    }

    private static int ReadRelativeTarget(IReadOnlyList<byte> bytes, int branchOffset)
    {
        return 0x8000 + branchOffset + 2 + unchecked((sbyte)bytes[branchOffset + 1]);
    }
}
