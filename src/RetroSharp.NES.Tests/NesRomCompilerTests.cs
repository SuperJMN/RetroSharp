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
    public void Signed_i8_relational_constants_compile_in_either_operand_order()
    {
        const string source = """
                              void Main() {
                                  i8 velocityY = -33;
                                  if (velocityY < -32) {
                                      velocityY += 1;
                                  }
                                  if (-32 > velocityY) {
                                      velocityY += 1;
                                  }
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);

        Assert.Equal(40976, rom.Length);
    }

    [Fact]
    public void Word_compound_add_and_subtract_with_i8_operand_do_not_clobber_the_carry()
    {
        // Regression for the NES fall-through bug: the sign-extension of an i8 operand clobbers the
        // carry flag, so it must be computed into scratch before the low-byte ADC/SBC. The low-byte
        // arithmetic and its STA must therefore be immediately followed by the high-byte ADC/SBC,
        // with no sign-extension code in between.
        const string source = """
                              void Main() {
                                  i16 a = 10;
                                  i8 v = 5;
                                  a += v;
                                  a -= v;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        // a occupies $00/$01, v occupies $02, and the word scratch high byte is $E9.
        Assert.True(
            ContainsSequence(prg, [0x18, 0x65, 0x02, 0x85, 0x00, 0xA5, 0x01, 0x65, 0xE9, 0x85, 0x01]),
            "word += i8 should CLC/ADC the low byte then immediately ADC the high byte with the sign-extended scratch.");
        Assert.True(
            ContainsSequence(prg, [0x38, 0xE5, 0x02, 0x85, 0x00, 0xA5, 0x01, 0xE5, 0xE9, 0x85, 0x01]),
            "word -= i8 should SEC/SBC the low byte then immediately SBC the high byte with the sign-extended scratch.");
    }

    [Fact]
    public void Compiles_video_api_calls_to_an_ines_rom()
    {
        const string source = """
                              void Main() {
                                  Video.Init();
                                  Palette.Set(0, 15);
                                  Palette.Set(1, 39);
                                  Palette.Set(2, 22);
                                  Palette.Set(3, 48);
                                  Tilemap.Set(14, 12, 1);
                                  Tilemap.Set(15, 12, 2);
                                  Tilemap.Set(16, 12, 1);
                                  Tilemap.Set(15, 13, 3);
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
    public void Compiles_while_with_large_body_using_far_condition_trampoline()
    {
        var repeatedBody = string.Join(Environment.NewLine, Enumerable.Repeat("x += 1;", 120));
        var source = $$"""
                       void Main() {
                           Video.Init();
                           u8 x = 0;
                           while (x != 1) {
                               {{repeatedBody}}
                           }
                       }
                       """;

        var rom = NesRomCompiler.CompileSource(source);

        Assert.Equal(40976, rom.Length);
    }

    [Fact]
    public void Portable2D_import_does_not_affect_nes_rom_bytes()
    {
        const string implicitSdk = """
                                   void Main() {
                                       Video.WaitVBlank();
                                   }
                                   """;
        const string explicitSdk = """
                                   import RetroSharp.Portable2D;

                                   void Main() {
                                       Video.WaitVBlank();
                                   }
                                   """;

        Assert.Equal(
            NesRomCompiler.CompileSource(implicitSdk),
            NesRomCompiler.CompileSource(explicitSdk));
    }

    [Fact]
    public void Explicit_sdk_import_mode_requires_the_portable2d_import_for_sdk_calls()
    {
        const string source = """
                              void Main() {
                                  Video.WaitVBlank();
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(
            () => RetroSharp.NES.NesRomCompiler.CompileSource(source, sdkImportMode: SdkLibraryImportMode.ExplicitOnly));

        Assert.Equal("Unknown static or receiver method 'Video.WaitVBlank'.", exception.Message);
    }

    [Fact]
    public void Explicit_sdk_import_mode_uses_imported_portable2d_sdk()
    {
        const string source = """
                              import RetroSharp.Portable2D;

                              void Main() {
                                  Video.WaitVBlank();
                              }
                              """;

        var rom = RetroSharp.NES.NesRomCompiler.CompileSource(source, sdkImportMode: SdkLibraryImportMode.ExplicitOnly);

        Assert.Equal(40976, rom.Length);
    }

    [Fact]
    public void Explicit_sdk_import_mode_uses_manifest_declared_portable2d_sdk()
    {
        const string source = """
                              void Main() {
                                  Video.WaitVBlank();
                              }
                              """;

        var rom = RetroSharp.NES.NesRomCompiler.CompileSource(
            source,
            sdkImportMode: SdkLibraryImportMode.ExplicitOnly,
            sdkLibraryImports: [SdkImportResolver.Portable2D]);

        Assert.Equal(40976, rom.Length);
    }

    [Fact]
    public void Rejects_unknown_imports()
    {
        const string source = """
                              import RetroSharp.Experimental;

                              void Main() {
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => NesRomCompiler.CompileSource(source));

        Assert.Equal("Unknown import 'RetroSharp.Experimental'.", exception.Message);
    }

    [Fact]
    public void Logical_palette_declarations_map_tones_to_nes_grayscale_palette_slots()
    {
        const string source = """
                              void Main() {
                                  Video.Init();
                                  Palette.Background(2, 0, 1, 2, 3);
                                  Palette.Sprite(3, 0, 0, 1, 3);
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);

        Assert.Equal(40976, rom.Length);
        Assert.True(ContainsSequence(rom, [0x30, 0x10, 0x00, 0x0F]), "Palette.Background should map logical light-to-dark tones to NES grayscale colors.");
        Assert.True(ContainsSequence(rom, [0x30, 0x30, 0x10, 0x0F]), "Palette.Sprite should map the runner's logical sprite tones to NES grayscale colors.");
    }

    [Fact]
    public void Rejects_logical_palette_sprite_slots_outside_nes_capabilities()
    {
        const string source = """
                              void Main() {
                                  Video.Init();
                                  Palette.Sprite(4, 15, 17, 34, 51);
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => NesRomCompiler.CompileSource(source));

        Assert.Equal("Target 'nes' supports sprite palette slots 0..3, but palette slot 4 was requested.", exception.Message);
    }

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

        var rom = NesRomCompiler.CompileSource(source);

        Assert.Equal(40976, rom.Length);
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
    public void Bool_flags_lower_like_int_flags_with_explicit_comparisons()
    {
        const string intSource = """
                                 type Pixel = i16;
                                 struct S { u8 grounded; u8 moving; Pixel x; }
                                 inline void step(this S s, u8 grounded) {
                                     if (grounded != 0) { s.x += 1; }
                                     if (s.grounded == 0) { s.x += 1; }
                                 }
                                 void Main() {
                                     Video.Init();
                                     S s; s.grounded = 1; s.moving = 0; s.x = 0;
                                     s.step(s.grounded);
                                     Pixel frame = s.grounded switch { 0 => 4, _ => s.moving switch { 0 => 0, _ => 7 } };
                                     i16 sink = frame + s.x;
                                     if (sink != 0) { Video.Present(); }
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
                                  void Main() {
                                      Video.Init();
                                      S s; s.grounded = true; s.moving = false; s.x = 0;
                                      s.step(s.grounded);
                                      Pixel frame = s.grounded switch { false => 4, _ => s.moving switch { false => 0, _ => 7 } };
                                      i16 sink = frame + s.x;
                                      if (sink != 0) { Video.Present(); }
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
    public void Rejects_nes_switch_expression_that_would_re_evaluate_subject()
    {
        const string source = """
                              u8 next(u8 value) => value + 1;

                              void Main() {
                                  Video.Init();
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
                                      void Main() {
                                          Video.Init();
                                          World.Column(0, 1, 2);
                                          World.Column(1, 3, 4);
                                          World.Map(2, 10, 2);
                                          Camera.Init(2, 10, 2);
                                          Camera.SetPosition(4, 0);
                                          Video.WaitVBlank();
                                          Input.Poll();
                                          return;
                                      }
                                      """;

        const string dotSource = """
                                 void Main() {
                                     Video.Init();
                                     World.Column(0, 1, 2);
                                     World.Column(1, 3, 4);
                                     World.Map(2, 10, 2);
                                     Camera.Init(2, 10, 2);
                                     Camera.SetPosition(4, 0);
                                     Video.WaitVBlank();
                                     Input.Poll();
                                     return;
                                 }
                                 """;

        Assert.Equal(NesRomCompiler.CompileSource(functionSource), NesRomCompiler.CompileSource(dotSource));
    }

    [Fact]
    public void Compiles_audio_update_library_helper_over_nes_intrinsic_like_sdk_operation()
    {
        const string sdkSource = """
                                 void Main() {
                                     Audio.Update();
                                 }
                                 """;

        const string intrinsicSource = """
                                       [target("nes")]
                                       [intrinsic("audio_update")]
                                       extern void nes_audio_update();

                                       class Audio {
                                           static inline void Update() {
                                               nes_audio_update();
                                           }
                                       }

                                       void Main() {
                                           Audio.Update();
                                       }
                                       """;

        Assert.Equal(NesRomCompiler.CompileSource(sdkSource), NesRomCompiler.CompileSource(intrinsicSource));
    }

    [Fact]
    public void Injected_nes_audio_update_helper_keeps_surface_byte_identical()
    {
        const string source = """
                              void Main() {
                                  Audio.Init();
                                  Audio.Update();
                              }
                              """;
        var explicitLibrarySource = SdkLibrarySource.Merge(
            NesTarget.Intrinsics,
            source,
            SdkLibraryImportMode.ExplicitOnly,
            libraryImportPaths: [SdkImportResolver.Portable2D]);
        var library = SdkLibrarySource.ForTarget(NesTarget.Intrinsics);

        Assert.Contains("class RetroSharp_Portable2D_Audio", library, StringComparison.Ordinal);
        Assert.Equal(NesRomCompiler.CompileSource(explicitLibrarySource), NesRomCompiler.CompileSource(source));
    }

    [Fact]
    public void Audio_init_via_library_helper_is_byte_identical_nes()
    {
        const string direct = """
                              void Main() {
                                  Audio.Init();
                                  Audio.Update();
                              }
                              """;
        const string library = """
                               void Main() {
                                   Audio.Init();
                                   Audio.Update();
                               }
                               """;

        var sdkLibrary = SdkLibrarySource.ForTarget(NesTarget.Intrinsics);

        Assert.Contains("[intrinsic(\"audio_init\")]", sdkLibrary, StringComparison.Ordinal);
        Assert.Equal(NesRomCompiler.CompileSource(direct), NesRomCompiler.CompileSource(library));
    }

    [Fact]
    public void Compiles_camera_library_helpers_over_nes_intrinsic_like_sdk_operations()
    {
        const string direct = """
                              void Main() {
                                  Video.Init();
                                  World.Column(0, 1, 2);
                                  World.Map(1, 10, 2);
                                  Camera.Init(1, 10, 2);
                                  i16 x = 4;
                                  Camera.SetPosition(x, 0);
                                  Camera.Apply();
                                  return;
                              }
                              """;
        const string library = """
                               void Main() {
                                   Video.Init();
                                   World.Column(0, 1, 2);
                                   World.Map(1, 10, 2);
                                   Camera.Init(1, 10, 2);
                                   i16 x = 4;
                                   Camera.SetPosition(x, 0);
                                   Camera.Apply();
                                   return;
                               }
                               """;
        Assert.Contains("class RetroSharp_Portable2D_Camera", SdkLibrarySource.ForTarget(NesTarget.Intrinsics), StringComparison.Ordinal);
        Assert.Equal(NesRomCompiler.CompileSource(direct), NesRomCompiler.CompileSource(library));
    }

    [Fact]
    public void Sprite_draw_via_library_helper_is_byte_identical_nes()
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

        const string direct = """
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(hero, "hero.nes.json");
                                  while (true) {
                                      Video.WaitVBlank();
                                      Sprite.Draw(hero, 24, 32, 0, false, 2);
                                  }
                              }
                              """;
        const string library = """
                               void Main() {
                                   Video.Init();
                                   Sprite.Asset(hero, "hero.nes.json");
                                   while (true) {
                                       Video.WaitVBlank();
                                       Sprite.Draw(hero, 24, 32, 0, false, 2);
                                   }
                               }
                               """;

        var sdkLibrary = SdkLibrarySource.ForTarget(NesTarget.Intrinsics);

        Assert.Contains("class RetroSharp_Portable2D_Sprite", sdkLibrary, StringComparison.Ordinal);
        Assert.Contains("[intrinsic(\"sprite_draw\")]", sdkLibrary, StringComparison.Ordinal);
        Assert.Equal(NesRomCompiler.CompileSource(direct, baseDirectory), NesRomCompiler.CompileSource(library, baseDirectory));
    }

    [Fact]
    public void Runner_shaped_sprite_draw_is_byte_identical_nes()
    {
        var source = RunnerSample.CompiledSource();

        var defaultImportRom = NesRomCompiler.CompileSource(source, RunnerSample.Directory);
        var mergedSourceRom = NesRomCompiler.CompileSource(
            SdkLibrarySource.Merge(NesTarget.Intrinsics, source),
            RunnerSample.Directory);

        Assert.Equal(mergedSourceRom, defaultImportRom);
    }

    [Fact]
    public void Sprite_draw_library_preserves_capability_and_budget_checks_nes()
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

        const string paletteSource = """
                                     void Main() {
                                         Sprite.Asset(hero, "hero.nes.json");
                                         while (true) {
                                             Sprite.Draw(hero, 24, 32, 0, false, 4);
                                         }
                                     }
                                     """;

        var paletteException = Assert.Throws<InvalidOperationException>(() => NesRomCompiler.CompileSource(paletteSource, baseDirectory));
        Assert.Equal("Target 'nes' supports sprite palette slots 0..3, but slot 4 was requested.", paletteException.Message);

        var draws = string.Join(
            Environment.NewLine,
            Enumerable.Range(0, 9).Select(index => $"        Sprite.Draw(hero, {index * 8}, 24, 0);"));
        var budgetSource = """
                           void Main() {
                               Video.Init();
                               Sprite.Asset(hero, "hero.nes.json");
                               while (true) {
                                   Video.WaitVBlank();

                           """ + draws + """
                               }
                           }
                           """;

        var budgetException = Assert.Throws<InvalidOperationException>(() => NesRomCompiler.CompileSource(budgetSource, baseDirectory));
        Assert.Equal(
            "Target 'nes' supports 8 hardware sprites per scanline, but 9 are required on scanline 24 for drawing logical sprites in one frame.",
            budgetException.Message);
    }

    [Fact]
    public void Sprite_draw_source_package_helper_compiles_nes()
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
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(hero, "hero.nes.json");
                                  while (true) {
                                      Video.WaitVBlank();
                                      Sprite.Draw(hero, 24, 32, 0);
                                  }
                              }
                              """;

        Assert.Equal(40976, NesRomCompiler.CompileSource(source, baseDirectory).Length);
    }

    [Fact]
    public void Nes_sdk_library_keeps_world_tile_flags_helper_target_gated()
    {
        // World.TileFlagsAt(...) is gated to Game Boy in source, then removed by
        // target selection before NES duplicate-name and intrinsic resolution.
        var library = SdkLibrarySource.ForTarget(NesTarget.Intrinsics);

        Assert.Contains("class RetroSharp_Portable2D_World", library, StringComparison.Ordinal);
        Assert.Contains("[target(\"gb\")]", library, StringComparison.Ordinal);
        Assert.Contains("[intrinsic(\"world_tile_flags_at\")]", library, StringComparison.Ordinal);
    }

    [Fact]
    public void Injected_nes_sdk_library_helpers_keep_video_and_input_surface_byte_identical()
    {
        const string source = """
                              void Main() {
                                  Video.WaitVBlank();
                                  Input.Poll();
                              }
                              """;
        var explicitLibrarySource = SdkLibrarySource.Merge(
            NesTarget.Intrinsics,
            source,
            SdkLibraryImportMode.ExplicitOnly,
            libraryImportPaths: [SdkImportResolver.Portable2D]);

        var library = SdkLibrarySource.ForTarget(NesTarget.Intrinsics);

        Assert.Contains("class RetroSharp_Portable2D_Video", library, StringComparison.Ordinal);
        Assert.Contains("class RetroSharp_Portable2D_Input", library, StringComparison.Ordinal);
        Assert.Equal(NesRomCompiler.CompileSource(explicitLibrarySource), NesRomCompiler.CompileSource(source));
    }

    [Fact]
    public void Nes_selects_matching_target_intrinsic_variant_for_portable_helper()
    {
        const string sdkSource = """
                                 void Main() {
                                     Video.WaitVBlank();
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

                              void Main() {
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

                              void Main() {
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
                              void Main() {
                                  Video.Init();
                                  World.Column(0, 1, 2);
                                  World.Column(1, 3, 4);
                                  World.Map(2, 10, 2);
                                  Camera.Init(2, 10, 2);
                                  Camera.SetPosition(4, 1);
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);

        Assert.Equal(0x08, rom[6] & 0x08);
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
    public void Compiles_receiver_method_calls_inside_nested_blocks_and_sdk_name_shadows()
    {
        const string staticSource = """
                                    struct Actor { u8 x; }

                                    inline void Move(this Actor actor, u8 dx) {
                                        actor.x += dx;
                                    }

                                    void Main() {
                                        Video.Init();
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

                                      void Main() {
                                          Video.Init();
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
    public void Rejects_nes_invalid_pure_helper_contracts_before_lowering()
    {
        var statementEffect = Assert.Throws<InvalidOperationException>(() => NesRomCompiler.CompileSource("""
                                                                                                         pure void draw() {
                                                                                                             Video.Init();
                                                                                                         }

                                                                                                         void Main() {
                                                                                                             draw();
                                                                                                             return;
                                                                                                         }
                                                                                                         """));
        Assert.Equal("pure helper 'draw' contains side-effecting statements; pure helpers must be a single return expression.", statementEffect.Message);

        var callEffect = Assert.Throws<InvalidOperationException>(() => NesRomCompiler.CompileSource("""
                                                                                                    u8 next(u8 value) => value + 1;
                                                                                                    pure u8 step(u8 value) => next(value);

                                                                                                    void Main() {
                                                                                                        Video.Init();
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

                              void Main() {
                                  Video.Init();
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
                              void Main() {
                                  Video.Init();
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
                                                                                                  void Main() {
                                                                                                      Video.Init();
                                                                                                      let speed = 2;
                                                                                                      speed += 1;
                                                                                                      return;
                                                                                                  }
                                                                                                  """));
        Assert.Equal("Cannot assign to immutable local 'speed'.", compound.Message);

        var postfix = Assert.Throws<InvalidOperationException>(() => NesRomCompiler.CompileSource("""
                                                                                                 void Main() {
                                                                                                     Video.Init();
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
        var source = File.ReadAllText(RepositoryFile("samples/static-drawing/drawing.rs"));

        Assert.Contains("void DrawFace()", source);

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
                              void Main() {
                                  Video.Init();
                                  Hud.SetTile(window, 0, 0, 1);
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
                              void Main() {
                                  Video.Init();
                                  Hud.SetTile(none, 0, 0, 1);
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);

        Assert.Equal(40976, rom.Length);
    }



    [Theory]
    [InlineData("video_wait_vblank();", "video_wait_vblank")]
    [InlineData("input_poll();", "input_poll")]
    [InlineData("i16 down = button_down(Button.A);", "button_down")]
    [InlineData("i16 pressed = button_just_pressed(Button.A);", "button_just_pressed")]
    [InlineData("i16 released = button_just_released(Button.A);", "button_just_released")]
    [InlineData("i16 ticks = button_hold_ticks(Button.A);", "button_hold_ticks")]
    public void Direct_legacy_sdk_builtins_are_rejected(string statement, string legacyName)
    {
        var source = $$"""
                       void Main() {
                           {{statement}}
                       }
                       """;

        var exception = Assert.Throws<InvalidOperationException>(() => NesRomCompiler.CompileSource(source));
        Assert.Contains(legacyName, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Direct_legacy_resource_declarations_are_rejected()
    {
        const string source = """
                              void Main() {
                                  world_column(0, 1, 2);
                                  world_map(1, 10, 2);
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => NesRomCompiler.CompileSource(source));
        Assert.Contains("world_column", exception.Message, StringComparison.Ordinal);
    }



    [Fact]
    public void Compiles_struct_member_access_as_adjacent_zero_page_fields()
    {
        const string source = """
                              struct Vec2 {
                                  i16 x;
                                  i16 y;
                              }

                              void Main() {
                                  Video.Init();
                                  Vec2 position;
                                  position.x = 40;
                                  position.y = position.x;
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.Equal(40976, rom.Length);
        Assert.True(ContainsSequence(prg, [0xA9, 0x28, 0x85, 0x00, 0xA9, 0x00, 0x85, 0x01]), "ROM should store position.x as a two-byte zero-page field.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0x85, 0x02, 0xA5, 0x01, 0x85, 0x03]), "ROM should copy position.x to the next two-byte position.y field with direct zero-page access.");
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
    public void Compiles_struct_initializer_like_explicit_member_assignments()
    {
        const string explicitSource = """
                                      struct Vec2 {
                                          u8 x;
                                          u8 y;
                                      }

                                      void Main() {
                                          Video.Init();
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

                                         void Main() {
                                             Video.Init();
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

                                      void Main() {
                                          Video.Init();
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

                                         void Main() {
                                             Video.Init();
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

                              void Main() {
                                  Video.Init();
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

                              void Main() {
                                  Video.Init();
                                  i16 x = StartX;
                                  i16 y = Copy;
                                  y = x;
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.Equal(40976, rom.Length);
        Assert.True(ContainsSequence(prg, [0xA9, 0x28, 0x85, 0x00, 0xA9, 0x00, 0x85, 0x01]), "Const StartX should compile as an immediate store to the first two-byte zero-page local.");
        Assert.True(ContainsSequence(prg, [0xA9, 0x01, 0x85, 0x02, 0xA9, 0x00, 0x85, 0x03]), "Const Copy should compile as an immediate store to the second two-byte zero-page local, with no const storage slot.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0x85, 0x02, 0xA5, 0x01, 0x85, 0x03]), "Const declarations should not shift runtime local addresses.");
    }

    [Fact]
    public void Compiles_local_const_identifiers_as_immediates_without_local_storage()
    {
        const string source = """
                              void Main() {
                                  Video.Init();
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
        Assert.True(ContainsSequence(prg, [0xA9, 0x29, 0x85, 0x00, 0xA9, 0x00, 0x85, 0x01]), "Local const Copy should compile its derived value as an immediate store to the first two-byte zero-page local.");
        Assert.True(ContainsSequence(prg, [0xA9, 0x01, 0x85, 0x02, 0xA9, 0x00, 0x85, 0x03]), "Local const declarations should not reserve zero-page storage before the second local.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x00, 0x85, 0x02, 0xA5, 0x01, 0x85, 0x03]), "Local const declarations should not shift runtime local addresses.");
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

        Assert.Equal(40976, rom.Length);
        Assert.True(ContainsSequence(prg, [0xA9, 0x02, 0x85, 0x00]), "Const conditional expression should fold to one immediate store with no runtime conditional.");
    }

    [Fact]
    public void Compiles_hex_binary_and_separated_literals_like_decimal_immediates()
    {
        const string decimalSource = """
                                     const Mask = 160;
                                     const Tile = 42;

                                     void Main() {
                                         Video.Init();
                                         u8 flags = Mask | 15;
                                         u8 tile = Tile;
                                         u16 distance = 128;
                                         return;
                                     }
                                     """;

        const string literalSource = """
                                     const Mask = 0b1010_0000;
                                     const Tile = 0x2A;

                                     void Main() {
                                         Video.Init();
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

                              void Main() {
                                  Video.Init();
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

                              void Main() {
                                  Video.Init();
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
    public void Compiles_u16_i16_locals_and_struct_fields_as_adjacent_zero_page_words()
    {
        const string source = """
                              struct Position {
                                  u8 tag;
                                  u16 x;
                                  i16 y;
                              }

                              void Main() {
                                  Position position = { tag: 1, x: 300u16, y: -2i16 };
                                  u16 total = position.x + 20u16;
                                  total -= 5u16;
                                  if (total == 315u16) {
                                      position.y += 3i16;
                                  }
                                  if (position.y > 0i16) {
                                      position.x = total;
                                  }
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.Equal(40976, rom.Length);
        Assert.True(
            ContainsSequence(prg, [0xA9, 0x01, 0x85, 0x00, 0xA9, 0x2C, 0x85, 0x01, 0xA9, 0x01, 0x85, 0x02, 0xA9, 0xFE, 0x85, 0x03, 0xA9, 0xFF, 0x85, 0x04]),
            "mixed-width struct fields should reserve adjacent zero-page bytes: tag, x low/high, then y low/high.");
    }

    [Fact]
    public void Compiles_fixed_size_array_constant_indices_as_adjacent_zero_page_bytes()
    {
        const string source = """
                              const u8 Count = 4;
                              const u8 First = 0;
                              const u8 Second = 1;

                              void Main() {
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
                                      void Main() {
                                          Video.Init();
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
                                         void Main() {
                                             Video.Init();
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

                                      void Main() {
                                          Video.Init();
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

                                         void Main() {
                                             Video.Init();
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
                                            void Main() {
                                                Video.Init();
                                                u8 seed = 3;
                                                u8 values[3] = [1, seed, seed + 1];
                                                u8 size = countof(values);
                                                return;
                                            }
                                            """;

        const string inferredLengthSource = """
                                            void Main() {
                                                Video.Init();
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
                              void Main() {
                                  Video.Init();
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

                              void Main() {
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
                              void Main() {
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

        Assert.Equal(40976, rom.Length);
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

        Assert.Equal(40976, rom.Length);
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

        Assert.Equal(40976, rom.Length);
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

        Assert.Equal(40976, rom.Length);
        Assert.True(ContainsSequence(prg, [0xA5, 0x01, 0xC9, 0x03, 0xB0]), "for condition should compare i with 3 and branch out when i >= 3.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x01, 0x18, 0x65, 0x00, 0x85, 0x00]), "for body should use direct x += i zero-page arithmetic.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x01, 0x18, 0x69, 0x01, 0x85, 0x01]), "for increment should use direct i += 1 zero-page arithmetic.");
    }

    [Fact]
    public void Compiles_increment_decrement_and_for_postfix_increment_as_direct_arithmetic()
    {
        const string source = """
                              void Main() {
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
                              void Main() {
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
                              void Main() {
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

                              void Main() {
                                  Video.Init();
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

                              void Main() {
                                  Video.Init();
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
    public void Compiles_struct_array_fields_with_mixed_width_stride()
    {
        const string source = """
                              struct Actor {
                                  u16 worldX;
                                  u8 y;
                              }

                              void Main() {
                                  Actor actors[2];
                                  actors[1].worldX = 300u16;
                                  actors[1].y = 7;
                                  u8 i = 1;
                                  actors[i].y += 1;
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.Equal(40976, rom.Length);
        Assert.True(ContainsSequence(prg, [0xA9, 0x2C, 0x85, 0x03, 0xA9, 0x01, 0x85, 0x04]), "actors[1].worldX should store low/high at base + sizeof(Actor).");
        Assert.True(ContainsSequence(prg, [0xA9, 0x07, 0x85, 0x05]), "actors[1].y should be placed after the two-byte worldX field.");
        Assert.True(ContainsSequence(prg, [0xA5, 0x06, 0x85, 0xE8, 0x18, 0x65, 0xE8, 0x18, 0x65, 0xE8, 0xAA, 0xB5, 0x02, 0x18, 0x69, 0x01, 0x95, 0x02]), "actors[i].y should use the mixed-width struct stride.");
    }

    [Fact]
    public void Compiles_bare_loop_break_and_continue_as_direct_jumps()
    {
        const string source = """
                              void Main() {
                                  u8 x = 0;
                                  while (true) {
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
                              void Main() {
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
    public void Compiles_runtime_while_condition_as_direct_branching()
    {
        const string source = """
                              void Main() {
                                  u8 x = 0;
                                  while (x < 3) {
                                      x += 1;
                                  }
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        var conditionCompare = IndexOfSequence(prg, [0xA5, 0x00, 0xC9, 0x03, 0xB0]);
        var bodyIncrement = IndexOfSequence(prg, [0xA5, 0x00, 0x18, 0x69, 0x01, 0x85, 0x00]);

        Assert.Equal(40976, rom.Length);
        Assert.True(conditionCompare >= 0, "while condition should compare runtime x with 3.");
        Assert.True(bodyIncrement > conditionCompare, "while body should emit after the runtime condition check.");
        Assert.True(ContainsAbsoluteJumpTo(prg, 0x8000 + conditionCompare, bodyIncrement, prg.Length), "while should jump back to the condition after the body.");
        Assert.True(ReadRelativeTarget(prg, conditionCompare + 4) > 0x8000 + bodyIncrement, "false condition should branch beyond the loop body.");
    }

    [Fact]
    public void Compiles_short_for_loop_condition_as_direct_relative_branch_without_trampoline()
    {
        const string source = """
                              void Main() {
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
                       void Main() {
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
                              void Main() {
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
                              void Main() {
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
                                      void Main() {
                                          u8 tile = 2;
                                          u8 hit = 0;
                                          if (tile >= 1 && tile < 4) {
                                              hit = 1;
                                          }
                                          return;
                                      }
                                      """;

        const string membershipSource = """
                                        void Main() {
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
                              void Main() {
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
                              void Main() {
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
                              void Main() {
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
                              void Main() {
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

                              void Main() {
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
                              void Main() {
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
                              void Main() {
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
    public void Compiles_png_sprite_sheet_using_nes_platform_variant()
    {
        var baseDirectory = WriteSpritePng(
            "hero.nes.png",
            8,
            8,
            Rows(8, 8, Enumerable.Repeat("33333333", 8).ToArray()));

        const string source = """
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(hero, "hero.png", 8, 8);
                                  while (true) {
                                      Video.WaitVBlank();
                                      Sprite.Draw(hero, 24, 32, 0, 0, 0);
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
    public void Colored_png_sprite_sheet_splits_extra_colors_into_optional_overlay_pieces()
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
                (R: (byte)0xFF, G: (byte)0xFF, B: (byte)0xFF, A: (byte)0xFF),
            ],
            Rows(
                8,
                8,
                "11111111",
                "11111111",
                "22222222",
                "22222222",
                "33333333",
                "33333333",
                "11114411",
                "11114411"));

        var asset = NesSpriteAssetCompiler.CompileFromFile(
            "hero",
            Path.Combine(baseDirectory, "hero.nes.png"),
            6,
            8,
            8);

        Assert.Equal(2, asset.Pieces.Count);
        Assert.False(asset.Pieces[0].Optional);
        Assert.True(asset.Pieces[1].Optional);
        Assert.Equal(0, asset.Pieces[0].PaletteSlotOffset);
        Assert.Equal(1, asset.Pieces[1].PaletteSlotOffset);
        Assert.Equal(2, asset.TilesPerFrame);

        var baseTile = asset.TileData.Take(16).ToArray();
        var overlayTile = asset.TileData.Skip(16).Take(16).ToArray();
        Assert.Equal(0, TileColor(baseTile, 4, 6));
        Assert.NotEqual(0, TileColor(baseTile, 0, 6));
        Assert.NotEqual(0, TileColor(overlayTile, 4, 6));
        Assert.Equal(0, TileColor(overlayTile, 0, 6));
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
                              void Main() {
                                  Video.Init();
                                  Palette.Background(0, 0, 1, 2, 3);
                                  Palette.Sprite(0, 0, 0, 1, 3);
                                  Sprite.Asset(hero, "hero.png", 8, 8);
                                  while (true) {
                                      Video.WaitVBlank();
                                      Sprite.Draw(hero, 24, 32, 0, 0, 0);
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
    public void Colored_png_sprite_sheets_with_distinct_palettes_can_share_a_logical_slot_when_nes_has_free_physical_slots()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), "RetroSharp.NES.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(baseDirectory);

        WriteSpritePngFile(
            baseDirectory,
            "hero.nes.png",
            8,
            8,
            [
                (R: (byte)0x00, G: (byte)0x00, B: (byte)0x00, A: (byte)0x00),
                (R: (byte)0xFC, G: (byte)0xBC, B: (byte)0xB0, A: (byte)0xFF),
                (R: (byte)0xD8, G: (byte)0x28, B: (byte)0x00, A: (byte)0xFF),
                (R: (byte)0x00, G: (byte)0x00, B: (byte)0x00, A: (byte)0xFF),
                (R: (byte)0xFF, G: (byte)0xFF, B: (byte)0xFF, A: (byte)0xFF),
            ],
            Rows(
                8,
                8,
                "11111111",
                "11111111",
                "22222222",
                "22222222",
                "33333333",
                "33333333",
                "11114411",
                "11114411"));
        WriteSpritePngFile(
            baseDirectory,
            "enemy.nes.png",
            8,
            8,
            [
                (R: (byte)0x00, G: (byte)0x00, B: (byte)0x00, A: (byte)0x00),
                (R: (byte)0x00, G: (byte)0xEB, B: (byte)0xDB, A: (byte)0xFF),
                (R: (byte)0x4F, G: (byte)0xDF, B: (byte)0x4B, A: (byte)0xFF),
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
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(hero, "hero.png", 8, 8);
                                  Sprite.Asset(enemy, "enemy.png", 8, 8);
                                  while (true) {
                                      Video.WaitVBlank();
                                      Sprite.Draw(hero, 24, 32, 0, false, 0);
                                      Sprite.Draw(enemy, 40, 32, 0, false, 0);
                                  }
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source, baseDirectory);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.True(ContainsSequence(prg, [0xA9, 0x00, 0x8D, 0x02, 0x02]), "hero base pieces should keep the requested logical slot 0.");
        Assert.True(ContainsSequence(prg, [0xA9, 0x01, 0x8D, 0x06, 0x02]), "hero overlay pieces should use the next physical sprite palette slot.");
        Assert.True(ContainsSequence(prg, [0xA9, 0x02, 0x8D, 0x0A, 0x02]), "enemy pieces should be remapped to a free NES physical sprite palette slot.");
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

    private static string Fingerprint(byte[] bytes)
    {
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
    }

    private static string CollisionHitContractSource(int height, int solidRow, string body)
    {
        var visual = string.Join(", ", Enumerable.Repeat("0", height));
        var flags = string.Join(", ", Enumerable.Range(0, height).Select(row => row == solidRow ? "1" : "0"));
        return $$"""
                 void Main() {
                     World.Column(0, {{visual}});
                     World.Flags(0, {{flags}});
                     World.Map(1, 0, {{height}});
                     Camera.Init(1, 0, {{height}});
                     Camera.SetPosition(0, 1);
                     Camera.Apply();
                     {{body}}
                 }
                 """;
    }

    private static string BytesAround(IReadOnlyList<byte> bytes, int offset)
    {
        var start = Math.Max(0, offset - 8);
        var length = Math.Min(bytes.Count - start, 17);
        return $"0x{offset:X4} [" + string.Join(" ", bytes.Skip(start).Take(length).Select(value => value.ToString("X2"))) + "]";
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
