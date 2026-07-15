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

public class GameBoyRomCompilerTests
{
    [Fact]
    public void Word_compound_add_and_subtract_with_i8_operand_preserve_the_carry()
    {
        // Regression for the fall-through bug: adding/subtracting an i8 operand to a 16-bit local
        // must sign-extend the operand without clobbering the carry between the low and high byte
        // arithmetic. Covers a positive addend with no low-byte overflow (a += da), a positive
        // addend that overflows the low byte (b += db), a negative addend (c += dc), and a
        // subtraction that borrows into the high byte (e -= de).
        const string source = """
                              void Main() {
                                  Video.Init();
                                  i16 a = 10;   i8 da = 5;    a += da;
                                  i16 b = 200;  i8 db = 100;  b += db;
                                  i16 c = 300;  i8 dc = -50;  c += dc;
                                  i16 e = 40;   i8 de = 100;  e -= de;
                                  while (true) {
                                      Video.WaitVBlank();
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);
        var cpu = new GameBoyTestCpu(rom);
        cpu.RunFrames(2);

        Assert.Equal(15, cpu.Wram(0xC000) | cpu.Wram(0xC001) << 8);
        Assert.Equal(300, cpu.Wram(0xC003) | cpu.Wram(0xC004) << 8);
        Assert.Equal(250, cpu.Wram(0xC006) | cpu.Wram(0xC007) << 8);
        Assert.Equal(0xFFC4, cpu.Wram(0xC009) | cpu.Wram(0xC00A) << 8);
    }

    [Fact]
    public void Word_variable_vs_variable_relational_comparisons_do_not_clobber_the_left_operand()
    {
        // Regression: 16-bit relational compares loaded the left operand into A and then materialized
        // the right operand into A as well, degrading the comparison to right-vs-right (always equal).
        // The runner's Camera.FollowPlayer used `y < maxScrollY` (both i16), so the camera could scroll
        // up but never back down. Force runtime i16 values (so they are not folded to constants) and
        // check every relational operator resolves against the real operands.
        const string source = """
                              void Main() {
                                  Video.Init();
                                  i16 a = 10;   i8 da = 5;   a += da;   // a = 15
                                  i16 b = 40;   i8 db = 5;   b += db;   // b = 45
                                  u8 lt = 0;    if (a < b)  { lt = 1; }
                                  u8 le = 0;    if (a <= b) { le = 1; }
                                  u8 gt = 0;    if (a > b)  { gt = 1; }
                                  u8 ge = 0;    if (a >= b) { ge = 1; }
                                  u8 gtR = 0;   if (b > a)  { gtR = 1; }
                                  u8 ltR = 0;   if (b < a)  { ltR = 1; }
                                  while (true) {
                                      Video.WaitVBlank();
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);
        var cpu = new GameBoyTestCpu(rom);
        cpu.RunFrames(2);

        // a(C000/C001) da(C002) b(C003/C004) db(C005) lt(C006) le(C007) gt(C008) ge(C009) gtR(C00A) ltR(C00B)
        Assert.Equal(15, cpu.Wram(0xC000) | cpu.Wram(0xC001) << 8);
        Assert.Equal(45, cpu.Wram(0xC003) | cpu.Wram(0xC004) << 8);
        Assert.Equal(1, cpu.Wram(0xC006)); // 15 < 45
        Assert.Equal(1, cpu.Wram(0xC007)); // 15 <= 45
        Assert.Equal(0, cpu.Wram(0xC008)); // 15 > 45
        Assert.Equal(0, cpu.Wram(0xC009)); // 15 >= 45
        Assert.Equal(1, cpu.Wram(0xC00A)); // 45 > 15
        Assert.Equal(0, cpu.Wram(0xC00B)); // 45 < 15
    }

    [Fact]
    public void Compiles_video_api_calls_to_a_game_boy_rom()
    {
        const string source = """
                              void Main() {
                                  Video.Init();
                                  Palette.Set(0, 0);
                                  Palette.Set(1, 1);
                                  Palette.Set(2, 2);
                                  Palette.Set(3, 3);
                                  Tilemap.Set(8, 7, 1);
                                  Tilemap.Set(9, 7, 2);
                                  Tilemap.Set(10, 7, 1);
                                  Tilemap.Set(9, 8, 3);
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
    public void Comments_do_not_affect_game_boy_rom_bytes()
    {
        const string withoutComments = """
                                       void Main() {
                                           Video.Init();
                                           Palette.Background(0, 0, 1, 2, 3);
                                       }
                                       """;
        const string withComments = """
                                    // Source-only documentation.
                                    void Main() {
                                        Video.Init(); /* zero-cost comment */
                                        Palette.Background(0, 0, 1, 2, 3);
                                    }
                                    """;

        Assert.Equal(
            GameBoyRomCompiler.CompileSource(withoutComments),
            GameBoyRomCompiler.CompileSource(withComments));
    }

    [Fact]
    public void Portable2D_import_does_not_affect_game_boy_rom_bytes()
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
            GameBoyRomCompiler.CompileSource(implicitSdk),
            GameBoyRomCompiler.CompileSource(explicitSdk));
    }

    [Fact]
    public void Built_in_portable2d_sdk_is_registered_as_an_importable_library()
    {
        Assert.True(SdkLibraryRegistry.Default.TryResolve("RetroSharp.Portable2D", out var library));

        Assert.Contains("class RetroSharp_Portable2D_Video", library!.SourceForTarget(GameBoyTarget.Intrinsics), StringComparison.Ordinal);
    }

    [Fact]
    public void Built_in_portable2d_sdk_is_a_manifest_backed_source_package()
    {
        var packageDirectory = RepositoryDirectory("sdk/RetroSharp.Portable2D");
        var manifestPath = Path.Combine(packageDirectory, "retrosharp-library.json");
        var sourceRoot = Path.Combine(packageDirectory, "src");
        var registry = SdkLibraryRegistry.FromDirectories([packageDirectory], includeDefaultLibraries: false);

        Assert.True(File.Exists(manifestPath));
        Assert.True(Directory.Exists(sourceRoot));
        Assert.True(registry.TryResolve("RetroSharp.Portable2D", out var library));
        Assert.Contains("class RetroSharp_Portable2D_Video", library!.SourceForTarget(GameBoyTarget.Intrinsics), StringComparison.Ordinal);
    }

    [Fact]
    public void Built_in_portable2d_sdk_declares_resource_facades_as_package_contracts()
    {
        var source = SdkLibrarySource.ForTarget(GameBoyTarget.Intrinsics);

        Assert.Contains("[resource(\"sprite_asset\")]", source, StringComparison.Ordinal);
        Assert.Contains("[resource(\"world_load\")]", source, StringComparison.Ordinal);
        Assert.Contains("[resource(\"music_asset\")]", source, StringComparison.Ordinal);
        Assert.Contains("[resource(\"palette_background\")]", source, StringComparison.Ordinal);
        Assert.Contains("[resource(\"palette_sprite\")]", source, StringComparison.Ordinal);
        Assert.Contains("[resource(\"animation_clip\")]", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Portable2d_public_facade_source_is_not_embedded_in_csharp()
    {
        var source = File.ReadAllText(RepositoryFile("src/RetroSharp.Sdk.Frontend/SdkLibrarySource.cs"));

        Assert.DoesNotContain("class Video", source, StringComparison.Ordinal);
        Assert.DoesNotContain("class Input", source, StringComparison.Ordinal);
        Assert.DoesNotContain("class Audio", source, StringComparison.Ordinal);
        Assert.DoesNotContain("class Camera", source, StringComparison.Ordinal);
        Assert.DoesNotContain("class Sprite", source, StringComparison.Ordinal);
        Assert.DoesNotContain("class World", source, StringComparison.Ordinal);
        Assert.DoesNotContain("class Music", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Explicit_sdk_import_mode_compiles_without_the_sdk()
    {
        const string source = """
                              void Main() {
                              }
                              """;

        var rom = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSource(source, sdkImportMode: SdkLibraryImportMode.ExplicitOnly);

        Assert.Equal(32768, rom.Length);
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
            () => RetroSharp.GameBoy.GameBoyRomCompiler.CompileSource(source, sdkImportMode: SdkLibraryImportMode.ExplicitOnly));

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

        var rom = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSource(source, sdkImportMode: SdkLibraryImportMode.ExplicitOnly);

        Assert.Equal(32768, rom.Length);
    }

    [Fact]
    public void Explicit_sdk_import_mode_uses_manifest_declared_portable2d_sdk()
    {
        const string source = """
                              void Main() {
                                  Video.WaitVBlank();
                              }
                              """;

        var rom = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSource(
            source,
            sdkImportMode: SdkLibraryImportMode.ExplicitOnly,
            sdkLibraryImports: [SdkImportResolver.Portable2D]);

        Assert.Equal(32768, rom.Length);
    }

    [Fact]
    public void Imported_sdk_library_can_come_from_a_custom_registry()
    {
        var registry = new SdkLibraryRegistry(
        [
            new SdkLibrary(
                "Acme.Empty",
                _ => """
                     class Video
                     {
                         static inline void WaitVBlank()
                         {
                         }
                     }

                     """)
        ]);
        const string source = """
                              import Acme.Empty;

                              void Main() {
                                  Video.WaitVBlank();
                              }
                              """;

        var rom = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSource(
            source,
            sdkImportMode: SdkLibraryImportMode.ExplicitOnly,
            sdkLibraryRegistry: registry);

        Assert.Equal(32768, rom.Length);
    }

    [Fact]
    public void Imported_sdk_library_can_come_from_a_local_manifest_directory()
    {
        var libraryRoot = WriteLibraryPackage(
            "Acme.Wait",
            "wait.rs",
            """
            [target("gb")]
            [intrinsic("wait_frame")]
            extern void acme_wait_frame();

            class AcmeWait
            {
                static inline void Tick()
                {
                    acme_wait_frame();
                }
            }
            """,
            "gb");
        var registry = SdkLibraryRegistry.FromDirectories([libraryRoot]);
        const string imported = """
                                import Acme.Wait;

                                void Main() {
                                    AcmeWait.Tick();
                                }
                                """;
        const string direct = """
                              void Main() {
                                  Video.WaitVBlank();
                              }
                              """;

        Assert.True(registry.TryResolve("RetroSharp.Portable2D", out _));
        Assert.True(registry.TryResolve("Acme.Wait", out _));
        Assert.Equal(
            GameBoyRomCompiler.CompileSource(direct),
            GameBoyRomCompiler.CompileSource(
                imported,
                sdkImportMode: SdkLibraryImportMode.ExplicitOnly,
                sdkLibraryRegistry: registry));
    }

    [Fact]
    public void Imported_sdk_library_manifest_rejects_unsupported_targets()
    {
        var libraryRoot = WriteLibraryPackage(
            "Acme.NesOnly",
            "nes-only.rs",
            """
            class NesOnly
            {
                static inline void Touch()
                {
                }
            }
            """,
            "nes");
        var registry = SdkLibraryRegistry.FromDirectories([libraryRoot]);
        const string source = """
                              import Acme.NesOnly;

                              void Main() {
                                  NesOnly.Touch();
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(
            () =>
            {
                GameBoyRomCompiler.CompileSource(
                    source,
                    sdkImportMode: SdkLibraryImportMode.ExplicitOnly,
                    sdkLibraryRegistry: registry);
            });

        Assert.Equal("Library 'Acme.NesOnly' does not support target 'gb'.", exception.Message);
    }

    [Fact]
    public void Rejects_unknown_imports()
    {
        const string source = """
                              import RetroSharp.Experimental;

                              void Main() {
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource(source));

        Assert.Equal("Unknown import 'RetroSharp.Experimental'.", exception.Message);
    }

    [Fact]
    public void Logical_palette_declarations_lower_to_game_boy_palette_registers()
    {
        const string source = """
                              void Main() {
                                  Video.Init();
                                  Palette.Background(0, 0, 1, 2, 3);
                                  Palette.Sprite(0, 0, 0, 1, 3);
                                  Palette.Sprite(1, 0, 3, 2, 1);
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x3E, 0xE4, 0xE0, 0x47]), "Palette.Background should lower slot 0 to BGP.");
        Assert.True(ContainsSequence(rom, [0x3E, 0xD0, 0xE0, 0x48]), "Palette.Sprite slot 0 should lower to OBP0.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x6C, 0xE0, 0x49]), "Palette.Sprite slot 1 should lower to OBP1.");
    }

    [Fact]
    public void Rejects_logical_sprite_palette_slots_outside_game_boy_capabilities()
    {
        const string source = """
                              void Main() {
                                  Video.Init();
                                  Palette.Sprite(2, 0, 1, 2, 3);
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource(source));

        Assert.Equal("Target 'gb' supports sprite palette slots 0..1, but palette slot 2 was requested.", exception.Message);
    }

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
    public void Rejects_game_boy_switch_expression_that_would_re_evaluate_subject()
    {
        const string source = """
                              u8 next(u8 value) => value + 1;

                              void Main() {
                                  Video.Init();
                                  u8 speed = next(1) switch { 0 => 0, _ => 1 };
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource(source));
        Assert.Equal("switch expression subject must be a simple value expression so lowering cannot re-evaluate a call or side effect.", exception.Message);
    }

    [Fact]
    public void Inline_helper_wrapping_camera_set_position_is_byte_identical()
    {
        const string direct = """
                              void Main() {
                                  Video.Init();
                                  World.Column(0, 1, 2);
                                  World.Map(1, 10, 2);
                                  Camera.Init(1, 10, 2);
                                  i16 x = 4;
                                  Camera.SetPosition(x, 0);
                              }
                              """;
        const string wrapped = """
                               inline void csp(i16 px) {
                                   Camera.SetPosition(px, 0);
                               }
                               void Main() {
                                   Video.Init();
                                   World.Column(0, 1, 2);
                                   World.Map(1, 10, 2);
                                   Camera.Init(1, 10, 2);
                                   i16 x = 4;
                                   csp(x);
                               }
                               """;
        Assert.Equal(GameBoyRomCompiler.CompileSource(direct), GameBoyRomCompiler.CompileSource(wrapped));
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
                                 }
                                 """;

        Assert.Equal(GameBoyRomCompiler.CompileSource(functionSource), GameBoyRomCompiler.CompileSource(dotSource));
    }

    [Fact]
    public void Compiles_wait_frame_library_helper_over_game_boy_intrinsic_like_sdk_operation()
    {
        const string sdkSource = """
                                 void Main() {
                                     Video.WaitVBlank();
                                 }
                                 """;

        const string intrinsicSource = """
                                       [target("gb")]
                                       [intrinsic("wait_frame")]
                                       extern void gb_wait_frame();

                                       inline void wait_frame() {
                                           gb_wait_frame();
                                       }

                                       void Main() {
                                           wait_frame();
                                       }
                                       """;

        Assert.Equal(GameBoyRomCompiler.CompileSource(sdkSource), GameBoyRomCompiler.CompileSource(intrinsicSource));
    }

    [Fact]
    public void Compiles_input_poll_library_helper_over_game_boy_intrinsic_like_sdk_operation()
    {
        const string sdkSource = """
                                 void Main() {
                                     Input.Poll();
                                 }
                                 """;

        const string intrinsicSource = """
                                       [target("gb")]
                                       [intrinsic("poll_input")]
                                       extern void gb_poll_input();

                                       inline void poll_input() {
                                           gb_poll_input();
                                       }

                                       void Main() {
                                           poll_input();
                                       }
                                       """;

        Assert.Equal(GameBoyRomCompiler.CompileSource(sdkSource), GameBoyRomCompiler.CompileSource(intrinsicSource));
    }

    [Fact]
    public void Compiles_audio_update_library_helper_over_game_boy_intrinsic_like_sdk_operation()
    {
        const string sdkSource = """
                                 void Main() {
                                     Audio.Update();
                                 }
                                 """;

        const string intrinsicSource = """
                                       [target("gb")]
                                       [intrinsic("audio_update")]
                                       extern void gb_audio_update();

                                       class Audio {
                                           static inline void Update() {
                                               gb_audio_update();
                                           }
                                       }

                                       void Main() {
                                           Audio.Update();
                                       }
                                       """;

        Assert.Equal(GameBoyRomCompiler.CompileSource(sdkSource), GameBoyRomCompiler.CompileSource(intrinsicSource));
    }

    [Fact]
    public void Audio_init_via_library_helper_is_byte_identical_gb()
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

        var sdkLibrary = SdkLibrarySource.ForTarget(GameBoyTarget.Intrinsics);

        Assert.Contains("[intrinsic(\"audio_init\")]", sdkLibrary, StringComparison.Ordinal);
        Assert.Equal(GameBoyRomCompiler.CompileSource(direct), GameBoyRomCompiler.CompileSource(library));
    }

    [Fact]
    public void Injected_game_boy_audio_update_helper_keeps_surface_byte_identical()
    {
        const string source = """
                              void Main() {
                                  Audio.Init();
                                  Audio.Update();
                              }
                              """;
        var explicitLibrarySource = SdkLibrarySource.Merge(
            GameBoyTarget.Intrinsics,
            source,
            SdkLibraryImportMode.ExplicitOnly,
            libraryImportPaths: [SdkImportResolver.Portable2D]);
        var library = SdkLibrarySource.ForTarget(GameBoyTarget.Intrinsics);

        Assert.Contains("class RetroSharp_Portable2D_Audio", library, StringComparison.Ordinal);
        Assert.Equal(GameBoyRomCompiler.CompileSource(explicitLibrarySource), GameBoyRomCompiler.CompileSource(source));
    }

    [Fact]
    public void Compiles_camera_library_helpers_over_game_boy_intrinsic_like_sdk_operations()
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
                               }
                               """;
        Assert.Contains("class RetroSharp_Portable2D_Camera", SdkLibrarySource.ForTarget(GameBoyTarget.Intrinsics), StringComparison.Ordinal);
        Assert.Equal(GameBoyRomCompiler.CompileSource(direct), GameBoyRomCompiler.CompileSource(library));
    }

    [Fact]
    public void Sprite_draw_via_library_helper_is_byte_identical_gb()
    {
        var baseDirectory = WriteSpriteAsset(
            "player.sprite.json",
            SpriteJson(Rows(8, 16, "01230123", "32103210")));

        const string direct = """
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(player, "player.sprite.json");
                                  Sprite.Draw(player, 72, 80, 0, false, 1);
                              }
                              """;
        const string library = """
                               void Main() {
                                   Video.Init();
                                   Sprite.Asset(player, "player.sprite.json");
                                   Sprite.Draw(player, 72, 80, 0, false, 1);
                               }
                               """;

        var sdkLibrary = SdkLibrarySource.ForTarget(GameBoyTarget.Intrinsics);

        Assert.Contains("class RetroSharp_Portable2D_Sprite", sdkLibrary, StringComparison.Ordinal);
        Assert.Contains("[intrinsic(\"sprite_draw\")]", sdkLibrary, StringComparison.Ordinal);
        Assert.Equal(GameBoyRomCompiler.CompileSource(direct, baseDirectory), GameBoyRomCompiler.CompileSource(library, baseDirectory));
    }

    [Fact]
    public void Runner_shaped_sprite_draw_is_byte_identical_gb()
    {
        var source = RunnerSample.CompiledSource();

        var defaultImportRom = GameBoyRomCompiler.CompileSource(source, RunnerSample.Directory);
        var mergedSourceRom = GameBoyRomCompiler.CompileSource(
            SdkLibrarySource.Merge(GameBoyTarget.Intrinsics, source),
            RunnerSample.Directory);

        Assert.Equal(mergedSourceRom, defaultImportRom);
    }

    [Fact]
    public void Sprite_draw_library_preserves_capability_and_budget_checks_gb()
    {
        var baseDirectory = WriteSpriteAsset(
            "player.sprite.json",
            SpriteJson(Rows(16, 32)));

        const string paletteSource = """
                                     void Main() {
                                         Video.Init();
                                         Sprite.Asset(player, "player.sprite.json");
                                         Sprite.Draw(player, 72, 64, 0, false, 2);
                                     }
                                     """;

        var paletteException = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource(paletteSource, baseDirectory));
        Assert.Equal("Target 'gb' supports sprite palette slots 0..1, but slot 2 was requested.", paletteException.Message);

        var draws = string.Join(
            Environment.NewLine,
            Enumerable.Range(0, 41).Select(index => $"        Sprite.Draw(player, {index % 20}, {(index % 4) * 20}, 0);"));
        var budgetSource = """
                           void Main() {
                               Video.Init();
                               Sprite.Asset(player, "player.sprite.json");
                               while (true) {
                                   Video.WaitVBlank();

                           """ + draws + """
                               }
                           }
                           """;

        var budgetException = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource(budgetSource, baseDirectory));
        Assert.Equal(
            "Target 'gb' supports 40 hardware sprites per frame, but 164 are required for drawing logical sprites in one frame.",
            budgetException.Message);
    }

    [Fact]
    public void Sprite_draw_source_package_helper_compiles_gb()
    {
        var baseDirectory = WriteSpriteAsset(
            "player.sprite.json",
            SpriteJson(Rows(8, 16, "01230123", "32103210")));

        const string source = """
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(player, "player.sprite.json");
                                  Sprite.Draw(player, 72, 80, 0);
                              }
                              """;

        Assert.Equal(32768, GameBoyRomCompiler.CompileSource(source, baseDirectory).Length);
    }

    [Fact]
    public void Collision_aabb_via_compile_time_operand_intrinsic_is_byte_identical_gb()
    {
        const string direct = """
                              void DefineWorld() {
                                  World.Column(0, 0, 4);
                                  World.Column(1, 0, 4);
                                  World.Column(2, 0, 4);
                                  World.Flags(0, 0, 1);
                                  World.Flags(1, 0, 1);
                                  World.Flags(2, 0, 1);
                                  World.Map(3, 11, 2);
                                  Camera.Init(3, 11, 2);
                              }

                              void Main() {
                                  DefineWorld();
                                  i16 footY = 16;
                                  i16 hit = Camera.AabbTiles(72, footY - 8, 16, 16, 1);
                                  i16 hitTop = Camera.AabbHitTop(72, footY - 8, 16, 16, 1);
                              }
                              """;
        const string library = """
                               void DefineWorld() {
                                   World.Column(0, 0, 4);
                                   World.Column(1, 0, 4);
                                   World.Column(2, 0, 4);
                                   World.Flags(0, 0, 1);
                                   World.Flags(1, 0, 1);
                                   World.Flags(2, 0, 1);
                                   World.Map(3, 11, 2);
                                   Camera.Init(3, 11, 2);
                               }

                               void Main() {
                                   DefineWorld();
                                   i16 footY = 16;
                                   i16 hit = Camera.AabbTiles(72, footY - 8, 16, 16, 1);
                                   i16 hitTop = Camera.AabbHitTop(72, footY - 8, 16, 16, 1);
                               }
                               """;

        var sdkLibrary = SdkLibrarySource.ForTarget(GameBoyTarget.Intrinsics);

        Assert.Contains("[intrinsic(\"camera_aabb_tiles\")]", sdkLibrary, StringComparison.Ordinal);
        Assert.Contains("[intrinsic(\"camera_aabb_hit_top\")]", sdkLibrary, StringComparison.Ordinal);
        Assert.Equal(GameBoyRomCompiler.CompileSource(direct), GameBoyRomCompiler.CompileSource(library));
    }

    [Fact]
    public void Screen_collision_aabb_via_compile_time_operand_intrinsic_is_byte_identical_gb()
    {
        const string direct = """
                              void DefineWorld() {
                                  World.Column(0, 0, 4);
                                  World.Column(1, 0, 4);
                                  World.Column(2, 0, 4);
                                  World.Flags(0, 0, 1);
                                  World.Flags(1, 0, 1);
                                  World.Flags(2, 0, 1);
                                  World.Map(3, 11, 2);
                                  Camera.Init(3, 11, 2);
                              }

                              void Main() {
                                  DefineWorld();
                                  i16 screenX = 40;
                                  i16 screenY = 16;
                                  i16 hit = Camera.ScreenAabbTiles(screenX, screenY, 16, 16, 1);
                                  i16 hitTop = Camera.ScreenAabbHitTop(screenX, screenY, 16, 16, 1);
                              }
                              """;
        const string library = """
                               void DefineWorld() {
                                   World.Column(0, 0, 4);
                                   World.Column(1, 0, 4);
                                   World.Column(2, 0, 4);
                                   World.Flags(0, 0, 1);
                                   World.Flags(1, 0, 1);
                                   World.Flags(2, 0, 1);
                                   World.Map(3, 11, 2);
                                   Camera.Init(3, 11, 2);
                               }

                               void Main() {
                                   DefineWorld();
                                   i16 screenX = 40;
                                   i16 screenY = 16;
                                   i16 hit = Camera.ScreenAabbTiles(screenX, screenY, 16, 16, 1);
                                   i16 hitTop = Camera.ScreenAabbHitTop(screenX, screenY, 16, 16, 1);
                               }
                               """;

        var sdkLibrary = SdkLibrarySource.ForTarget(GameBoyTarget.Intrinsics);

        Assert.Contains("[intrinsic(\"camera_screen_aabb_tiles\")]", sdkLibrary, StringComparison.Ordinal);
        Assert.Contains("[intrinsic(\"camera_screen_aabb_hit_top\")]", sdkLibrary, StringComparison.Ordinal);
        Assert.Equal(GameBoyRomCompiler.CompileSource(direct), GameBoyRomCompiler.CompileSource(library));
    }

    [Fact]
    public void Collision_capability_checks_preserved()
    {
        const string source = """
                              void Main() {
                                  World.Column(0, 0, 4);
                                  World.Flags(0, 0, 1);
                                  World.Map(1, 11, 2);
                                  Camera.Init(1, 11, 2);
                                  i16 footY = 16;
                                  i16 hit = Camera.AabbTiles(150, footY, 16, 8, 1);
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource(source));

        Assert.Equal("camera AABB screen span must fit within target 'gb' visible width 160.", exception.Message);
    }

    [Fact]
    public void Injected_game_boy_sdk_library_helpers_keep_video_and_input_surface_byte_identical()
    {
        const string source = """
                              void Main() {
                                  Video.WaitVBlank();
                                  Input.Poll();
                              }
                              """;
        var explicitLibrarySource = SdkLibrarySource.Merge(
            GameBoyTarget.Intrinsics,
            source,
            SdkLibraryImportMode.ExplicitOnly,
            libraryImportPaths: [SdkImportResolver.Portable2D]);

        var library = SdkLibrarySource.ForTarget(GameBoyTarget.Intrinsics);

        Assert.Contains("class RetroSharp_Portable2D_Video", library, StringComparison.Ordinal);
        Assert.Contains("class RetroSharp_Portable2D_Input", library, StringComparison.Ordinal);
        Assert.Equal(GameBoyRomCompiler.CompileSource(explicitLibrarySource), GameBoyRomCompiler.CompileSource(source));
    }

    [Fact]
    public void Game_boy_selects_matching_target_intrinsic_variant_for_portable_helper()
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

        Assert.Equal(GameBoyRomCompiler.CompileSource(sdkSource), GameBoyRomCompiler.CompileSource(source));
    }

    [Fact]
    public void Unknown_game_boy_intrinsic_reports_target_catalog_error()
    {
        const string source = """
                              [target("gb")]
                              [intrinsic("read_magic")]
                              extern void gb_read_magic();

                              void Main() {
                                  gb_read_magic();
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource(source));

        Assert.Equal("Target 'gb' does not support intrinsic 'read_magic' on extern function 'gb_read_magic'.", exception.Message);
    }

    [Fact]
    public void Intrinsic_extern_return_type_must_match_descriptor()
    {
        const string source = """
                              [target("gb")]
                              [intrinsic("wait_frame")]
                              extern i16 wrong_wait_frame();

                              void Main() {
                                  i16 value = wrong_wait_frame();
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource(source));

        Assert.Equal("Extern intrinsic 'wrong_wait_frame' declares return type 'i16', but intrinsic 'wait_frame' returns 'void'.", exception.Message);
    }

    [Fact]
    public void Compile_time_operand_slot_rejects_runtime_value()
    {
        const string source = """
                              [target("gb")]
                              [intrinsic("world_tile_flags_for_world")]
                              extern i16 flags_for_world(i16 world, i16 x, i16 y);

                              void Main() {
                                  World.Column(0, 1, 2);
                                  World.Flags(0, 0, 1);
                                  World.Map(1, 10, 2);
                                  i16 selectedWorld = 0;
                                  i16 flags = flags_for_world(selectedWorld, 0, 8);
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource(source));

        Assert.Equal(
            "Intrinsic 'world_tile_flags_for_world' argument 1 on extern 'flags_for_world' is compile-time WorldId and cannot use runtime local 'selectedWorld'.",
            exception.Message);
    }

    [Fact]
    public void Minimal_compile_time_operand_intrinsic_is_byte_identical()
    {
        const string direct = """
                              void Main() {
                                  World.Column(0, 1, 2);
                                  World.Flags(0, 0, 1);
                                  World.Map(1, 10, 2);
                                  i16 flags = World.TileFlagsAt(0, 8);
                              }
                              """;

        const string intrinsic = """
                                 [target("gb")]
                                 [intrinsic("world_tile_flags_for_world")]
                                 extern i16 flags_for_world(i16 world, i16 x, i16 y);

                                 void Main() {
                                     World.Column(0, 1, 2);
                                     World.Flags(0, 0, 1);
                                     World.Map(1, 10, 2);
                                     i16 flags = flags_for_world("default", 0, 8);
                                 }
                                 """;

        Assert.Equal(GameBoyRomCompiler.CompileSource(direct), GameBoyRomCompiler.CompileSource(intrinsic));
    }

    [Fact]
    public void Single_descriptor_covers_multiple_assets_without_duplication()
    {
        var descriptor = TargetIntrinsicDescriptor.DrawLogicalSprite(
            "sprite_draw",
            runtimeArity: 4,
            compileTimeOperands: [new TargetIntrinsicCompileTimeOperand(0, TargetIntrinsicOperandRole.AssetRef)]);
        var catalog = new TargetIntrinsicCatalog("gb", "Game Boy", [descriptor]);
        var function = ExternIntrinsic("gb", "sprite_draw", "__sprite_draw");

        var first = TargetIntrinsicResolver.ResolveCall(
            function,
            new FunctionCall("__sprite_draw", [new IdentifierSyntax("player"), new ConstantSyntax("24"), new ConstantSyntax("32"), new ConstantSyntax("0"), new ConstantSyntax("0")]),
            catalog);
        var second = TargetIntrinsicResolver.ResolveCall(
            function,
            new FunctionCall("__sprite_draw", [new IdentifierSyntax("enemy"), new ConstantSyntax("24"), new ConstantSyntax("32"), new ConstantSyntax("0"), new ConstantSyntax("0")]),
            catalog);

        Assert.Same(descriptor, first.Descriptor);
        Assert.Same(descriptor, second.Descriptor);
        Assert.Equal("player", Assert.Single(first.CompileTimeOperands).Identifier);
        Assert.Equal("enemy", Assert.Single(second.CompileTimeOperands).Identifier);
        Assert.Equal(1, catalog.Intrinsics.Count);
    }

    [Fact]
    public void Language_ir_gains_no_framework_concepts()
    {
        var intermediateFiles = Directory.GetFiles(
            RepositoryDirectory("src/RetroSharp.Generation.Intermediate"),
            "*.cs",
            SearchOption.AllDirectories);

        var intermediateSource = string.Join("\n", intermediateFiles.Select(File.ReadAllText));

        Assert.DoesNotContain("AssetRef", intermediateSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ConstPaletteSlot", intermediateSource, StringComparison.Ordinal);
        Assert.DoesNotContain("EnumFlags", intermediateSource, StringComparison.Ordinal);
        Assert.DoesNotContain("WorldId", intermediateSource, StringComparison.Ordinal);
        Assert.DoesNotContain("TargetIntrinsic", intermediateSource, StringComparison.Ordinal);
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
    public void Rejects_game_boy_invalid_pure_helper_contracts_before_lowering()
    {
        var statementEffect = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource("""
                                                                                                             pure void draw() {
                                                                                                                 Video.Init();
                                                                                                             }

                                                                                                             void Main() {
                                                                                                                 draw();
                                                                                                             }
                                                                                                             """));
        Assert.Equal("pure helper 'draw' contains side-effecting statements; pure helpers must be a single return expression.", statementEffect.Message);

        var callEffect = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource("""
                                                                                                        u8 next(u8 value) => value + 1;
                                                                                                        pure u8 step(u8 value) => next(value);

                                                                                                        void Main() {
                                                                                                            Video.Init();
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

                              void Main() {
                                  Video.Init();
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
                              void Main() {
                                  Video.Init();
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
                                                                                                      void Main() {
                                                                                                          Video.Init();
                                                                                                          let speed = 2;
                                                                                                          speed += 1;
                                                                                                      }
                                                                                                      """));
        Assert.Equal("Cannot assign to immutable local 'speed'.", compound.Message);

        var postfix = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource("""
                                                                                                     void Main() {
                                                                                                         Video.Init();
                                                                                                         let speed = 2;
                                                                                                         speed++;
                                                                                                     }
                                                                                                     """));
        Assert.Equal("Cannot assign to immutable local 'speed'.", postfix.Message);
    }

    [Fact]
    public void GameBoy_drawing_sample_compiles_with_helper_functions()
    {
        var sourcePath = RepositoryFile("samples/static-drawing/drawing.rs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("void DrawFace()", source);

        var rom = GameBoyRomCompiler.CompileSource(source, Path.GetDirectoryName(sourcePath));

        Assert.Equal(32768, rom.Length);
    }

    [Fact]
    public void Hud_set_tile_resource_compiles_to_game_boy_window_tilemap()
    {
        const string source = """
                              void Main() {
                                  Video.Init();
                                  Hud.SetTile(window, 1, 0, 5);
                                  return;
                              }
                              """;

        Assert.Empty(GameBoyRomCompiler.CollectSdkOperations(source));

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
                              void Main() {
                                  Video.Init();
                                  Hud.SetTile(split_scroll, 0, 0, 1);
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
        var sourcePath = RepositoryFile("samples/window-hud/hud.rs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("Hud.SetTile(window", source);

        var rom = GameBoyRomCompiler.CompileSource(source, Path.GetDirectoryName(sourcePath));

        Assert.Equal(32768, rom.Length);
    }

    [Fact]
    public void Compiles_runtime_sprite_loop_to_a_game_boy_rom()
    {
        const string source = """
                              void Main() {
                                  Video.Init();
                                  Palette.Set(0, 0);
                                  Palette.Set(1, 1);
                                  Palette.Set(2, 2);
                                  Palette.Set(3, 3);
                                  i16 x = 8;
                                  while (true) {
                                      Video.WaitVBlank();
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
    public void Inline_helper_wrapping_sprite_draw_and_camera_apply_is_byte_identical()
    {
        var baseDirectory = WriteSpriteAsset(
            "player.sprite.json",
            SpriteJson(
                Rows(
                    8,
                    16,
                    "01230123",
                    "32103210")));

        const string direct = """
                              void Main() {
                                  Video.Init();
                                  World.Column(0, 1, 2);
                                  World.Map(1, 10, 2);
                                  Camera.Init(1, 10, 2);
                                  Sprite.Asset(player_run, "player.sprite.json");
                                  i16 sy = 80;
                                  Sprite.Draw(player_run, 72, sy, 0);
                                  Camera.Apply();
                              }
                              """;
        const string wrapped = """
                               inline void sd(i16 y) {
                                   Sprite.Draw(player_run, 72, y, 0);
                               }
                               inline void ca() {
                                   Camera.Apply();
                               }
                               void Main() {
                                   Video.Init();
                                   World.Column(0, 1, 2);
                                   World.Map(1, 10, 2);
                                   Camera.Init(1, 10, 2);
                                   Sprite.Asset(player_run, "player.sprite.json");
                                   i16 sy = 80;
                                   sd(sy);
                                   ca();
                               }
                               """;
        Assert.Equal(GameBoyRomCompiler.CompileSource(direct, baseDirectory), GameBoyRomCompiler.CompileSource(wrapped, baseDirectory));
    }

    [Fact]
    public void Golden_sprite_draw_emission_is_pinned_gb()
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
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(player_run, "player.sprite.json");
                                  Sprite.Draw(player_run, 72, 80, 0, false, 1);
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source, baseDirectory);

        Assert.Equal("5D42DDFDB36FD0FCE746A9960C0379D6F9DE6D747735D7498BB11261380D407C", Fingerprint(rom));
    }

    [Fact]
    public void Golden_collision_aabb_emission_is_pinned_gb()
    {
        const string source = """
                              void DefineWorld() {
                                  World.Column(0, 0, 4);
                                  World.Column(1, 0, 4);
                                  World.Column(2, 0, 4);
                                  World.Flags(0, 0, 1);
                                  World.Flags(1, 0, 1);
                                  World.Flags(2, 0, 1);
                                  World.Map(3, 11, 2);
                                  Camera.Init(3, 11, 2);
                              }

                              void Main() {
                                  DefineWorld();
                                  i16 footY = 16;
                                  i16 hit = Camera.AabbTiles(72, footY - 8, 16, 16, 1);
                                  i16 hitTop = Camera.AabbHitTop(72, footY - 8, 16, 16, 1);
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal("927F804320BC973C4139D33F5010C236EE595EE39AEC17DA0A2D02D05B42099F", Fingerprint(rom));
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
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(player_run, "player.sprite.json");
                                  Sprite.Draw(player_run, 72, 80, 0);
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
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(big_player, "player.sprite.json");
                                  Sprite.Draw(big_player, 72, 64, 0);
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
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(big_player, "player.sprite.json");
                                  bool flipX = true;
                                  Sprite.Draw(big_player, 72, 64, 0, flipX);
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
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(player, "player.sprite.json");
                                  Sprite.Draw(player, 72, 64, 0, false, 1);
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
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(player, "player.sprite.json");
                                  Sprite.Draw(player, 72, 64, 0, true, 1);
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
                              void Main() {
                                  Video.Init();
                                  Animation.Clip(run, 1, 6, 6, 6);
                                  sprite_set(0, 72, 80, Animation.Frame(run, 0), 0);
                                  sprite_set(1, 80, 80, Animation.Frame(run, 5), 0);
                                  sprite_set(2, 88, 80, Animation.Frame(run, 6), 0);
                                  sprite_set(3, 96, 80, Animation.Frame(run, 17), 0);
                                  sprite_set(4, 104, 80, Animation.Frame(run, 18), 0);
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x3E, 0x01, 0x6F, 0x26, 0x00, 0x7D, 0xEA, 0x02, 0xFE]), "tick 0 should select frame 1 through the complete zero-extended I16 intrinsic return.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x01, 0x6F, 0x26, 0x00, 0x7D, 0xEA, 0x06, 0xFE]), "tick 5 should still select frame 1 through the complete zero-extended I16 intrinsic return.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x02, 0x6F, 0x26, 0x00, 0x7D, 0xEA, 0x0A, 0xFE]), "tick 6 should select frame 2 through the complete zero-extended I16 intrinsic return.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x03, 0x6F, 0x26, 0x00, 0x7D, 0xEA, 0x0E, 0xFE]), "tick 17 should select frame 3 through the complete zero-extended I16 intrinsic return.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x01, 0x6F, 0x26, 0x00, 0x7D, 0xEA, 0x12, 0xFE]), "tick 18 should loop to frame 1 through the complete zero-extended I16 intrinsic return.");
    }

    [Fact]
    public void Animation_frame_lowers_dynamic_ticks_with_predictable_modulo_and_boundary_checks()
    {
        const string source = """
                              void Main() {
                                  Video.Init();
                                  Animation.Clip(run, 1, 6, 6, 6);
                                  i16 tick = 18;
                                  sprite_set(0, 72, 80, Animation.Frame(run, tick), 0);
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
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(player, "player.sprite.json");
                                  Sprite.Draw(player, 72, 64, 0, false, 2);
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
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(player, "player.sprite.json");
                                  bool flipX = true;
                                  Sprite.Draw(player, 72, 64, 0, flipX);
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
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(player, "player.sprite.json");
                                  Sprite.Draw(player, 72, 64, 0, 32);
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
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(player_run, "player.sprite.json");
                                  i16 frame = 1;
                                  Sprite.Draw(player_run, 72, 80, frame);
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
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(player_run, "player-run.gb.png", 16, 16);
                                  i16 frame = 1;
                                  Sprite.Draw(player_run, 72, 80, frame);
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
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(player_run, "player-run.png", 8, 16);
                                  Sprite.Draw(player_run, 72, 80, 0);
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
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(mario_run, "mario-run.gb.png", 16, 27);
                                  Sprite.Draw(mario_run, 72, 77, 0);
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
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(mario_run, "mario-run.gb.png", 16, 27);
                                  Sprite.Draw(mario_run, 72, 77, 0);
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
                              void Main() {
                                  Video.Init();
                                  i16 camera = 0;
                                  while (true) {
                                      Video.WaitVBlank();
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
                              void Main() {
                                  Video.Init();
                                  World.Column(0, 0, 0, 4, 5);
                                  World.Column(1, 0, 0, 4, 5);
                                  World.Column(2, 0, 0, 4, 5);
                                  World.Column(3, 0, 0, 4, 5);
                                  World.Column(4, 0, 0, 4, 5);
                                  World.Column(5, 0, 0, 4, 5);
                                  World.Column(6, 0, 0, 4, 5);
                                  World.Column(7, 0, 0, 4, 5);
                                  World.Column(8, 0, 0, 4, 5);
                                  World.Column(9, 0, 0, 4, 5);
                                  World.Column(10, 0, 0, 4, 5);
                                  World.Column(11, 0, 0, 4, 5);
                                  World.Column(12, 0, 0, 4, 5);
                                  World.Column(13, 0, 0, 4, 5);
                                  World.Column(14, 0, 0, 4, 5);
                                  World.Column(15, 0, 0, 4, 5);
                                  World.Map(16, 11, 4);
                                  Camera.Init(16, 11, 4);
                                  while (true) {
                                      Video.WaitVBlank();
                                      Camera.Apply();
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
        Assert.True(ContainsSequence(rom, [0xFA, 0xE6, 0xC0, 0xEA, 0x1B, 0xC1]), "camera_move_right should queue the right source column for deferred streaming.");
        Assert.True(ContainsSequence(rom, [0xFA, 0xE5, 0xC0, 0xEA, 0x1A, 0xC1]), "camera_move_left should queue the left background edge column for deferred streaming.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x19, 0xC1, 0xFE, 0x00, 0xCA]), "camera_apply should dispatch on the queued stream kind.");
        Assert.True(ContainsSequence(rom, [0xFA, 0xED, 0xC0, 0xEA, 0x28, 0xC1]), "camera_apply should seed the column stream from the current top source row.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x28, 0xC1, 0x5F, 0x16, 0x00, 0x21]), "camera_apply should resolve the top source row through the row-pointer table.");
        Assert.True(ContainsSequence(rom, [0x1A, 0x77, 0x7B, 0xC6, 0x10, 0x5F]), "camera_apply should stream source-column rows through a compact DE-to-HL loop.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x1A, 0xC1, 0x4F, 0xFA, 0xEB, 0xC0]), "camera_apply should stream the queued column into the circular background edge.");
    }

    [Fact]
    public void Camera_set_position_compares_requested_x_before_reusing_camera_steps()
    {
        const string source = """
                              void Main() {
                                  Video.Init();
                                  World.Column(0, 0, 0, 4, 5);
                                  World.Column(1, 0, 0, 4, 5);
                                  World.Map(2, 11, 4);
                                  Camera.Init(2, 11, 4);
                                  i16 requestedX = 1;
                                  Camera.SetPosition(requestedX, 0);
                                  Camera.Apply();
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xEA, 0x2D, 0xC1, 0xFA, 0x01, 0xC0, 0xEA, 0x4A, 0xC1, 0x3E, 0x10, 0xEA, 0x2E, 0xC1]), "camera_set_position should cache both requested word bytes and seed the two-tile per-frame step budget.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x4A, 0xC1, 0x47, 0xFA, 0xE1, 0xC0, 0xB8, 0xDA]), "camera_set_position should compare requested and current X high bytes before selecting a direction.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x2D, 0xC1, 0x47, 0xFA, 0xE0, 0xC0, 0xB8, 0xCA]), "camera_set_position should compare X low bytes when the high bytes match and keep a no-movement path.");
        Assert.True(ContainsSequence(rom, [0xFA, 0xE0, 0xC0, 0xC6, 0x01, 0xEA, 0xE0, 0xC0]), "camera_set_position should reuse the right-step camera movement path.");
        Assert.True(ContainsSequence(rom, [0xFA, 0xE0, 0xC0, 0xFE, 0x00, 0xC2]), "camera_set_position should reuse the left-step camera movement path.");
    }

    [Fact]
    public void Camera_set_position_tracks_y_state_and_applies_vertical_scroll()
    {
        const string source = """
                              void Main() {
                                  Video.Init();
                                  World.Column(0, 0, 0, 4, 5);
                                  World.Column(1, 0, 0, 4, 5);
                                  World.Map(2, 11, 4);
                                  Camera.Init(2, 11, 4);
                                  i16 cameraY = 1;
                                  Camera.SetPosition(0, cameraY);
                                  Camera.Apply();
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x3E, 0x00, 0xEA, 0xE8, 0xC0, 0xEA, 0xE9, 0xC0, 0xEA, 0xEA, 0xC0]), "camera_init should initialize the 16-bit world Y and fine scroll state.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x4A, 0xC1, 0x47, 0xFA, 0xE9, 0xC0, 0xB8, 0xDA]), "camera_set_position should compare requested and current Y high bytes before selecting a direction.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x2D, 0xC1, 0x47, 0xFA, 0xE8, 0xC0, 0xB8, 0xCA]), "camera_set_position should compare Y low bytes when the high bytes match and keep a no-movement path.");
        Assert.True(ContainsSequence(rom, [0xFA, 0xE8, 0xC0, 0xC6, 0x01, 0xEA, 0xE8, 0xC0]), "camera_set_position should reuse a down-step camera movement path.");
        Assert.True(ContainsSequence(rom, [0xFA, 0xEA, 0xC0, 0xC6, 0x01, 0xEA, 0xEA, 0xC0, 0xFE, 0x08]), "camera_set_position should track fine Y tile-boundary crossings.");
        Assert.True(ContainsSequence(rom, [0xFA, 0xE0, 0xC0, 0xE0, 0x43, 0xFA, 0xE8, 0xC0, 0xE0, 0x42]), "camera_apply should write camera X to SCX and camera Y to SCY.");
    }

    [Fact]
    public void Camera_set_position_streams_bottom_row_when_y_crosses_tile_down()
    {
        const string source = """
                              void Main() {
                                  Video.Init();
                                  World.Column(0, 0, 1, 2, 3, 4, 5);
                                  World.Column(1, 6, 7, 8, 9, 10, 11);
                                  World.Map(2, 11, 6);
                                  Camera.Init(2, 11, 4);
                                  i16 cameraY = 8;
                                  Camera.SetPosition(0, cameraY);
                                  Camera.Apply();
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x3E, 0x0B, 0xEA, 0xEB, 0xC0, 0x3E, 0x0F, 0xEA, 0xEC, 0xC0]), "camera_init should seed top and bottom background row cursors.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x00, 0xEA, 0xED, 0xC0, 0x3E, 0x04, 0xEA, 0xEE, 0xC0]), "camera_init should seed top and bottom source row cursors.");
        Assert.True(ContainsSequence(rom, [0xFA, 0xEE, 0xC0, 0xEA, 0x1B, 0xC1]), "downward crossing should queue the current bottom source row for deferred streaming.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x1B, 0xC1, 0x5F, 0x16, 0x00, 0x21]), "downward row streaming should resolve the queued bottom source row through the row-pointer table.");
        Assert.True(ContainsSequence(rom, [0xFA, 0xE5, 0xC0, 0xC6, 0x01, 0xFE, 0x20]), "downward row streaming should fill the visible row from the current background-left column.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x1A, 0xC1, 0xFE, 0x08, 0xDA]), "downward row streaming should compute the target background row address from the queued bottom row cursor.");
        Assert.True(ContainsSequence(rom, [0xFA, 0xEC, 0xC0, 0xC6, 0x01, 0xEA, 0xEC, 0xC0]), "downward row streaming should advance the bottom background row cursor.");
        Assert.True(ContainsSequence(rom, [0xFA, 0xEE, 0xC0, 0xC6, 0x01, 0xEA, 0xEE, 0xC0]), "downward row streaming should advance the bottom source row cursor.");
    }

    [Fact]
    public void Camera_set_position_streams_top_row_when_y_crosses_tile_up()
    {
        const string source = """
                              void Main() {
                                  Video.Init();
                                  World.Column(0, 0, 1, 2, 3, 4, 5);
                                  World.Column(1, 6, 7, 8, 9, 10, 11);
                                  World.Map(2, 11, 6);
                                  Camera.Init(2, 11, 4);
                                  i16 cameraY = 255;
                                  Camera.SetPosition(0, cameraY);
                                  Camera.Apply();
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0xFA, 0xEB, 0xC0, 0xD6, 0x01, 0xEA, 0xEB, 0xC0]), "upward row streaming should move the top background row cursor before streaming.");
        Assert.True(ContainsSequence(rom, [0xFA, 0xED, 0xC0, 0xD6, 0x01, 0xEA, 0xED, 0xC0]), "upward row streaming should move the top source row cursor before streaming.");
        Assert.True(ContainsSequence(rom, [0xFA, 0xED, 0xC0, 0xEA, 0x1B, 0xC1]), "upward crossing should queue the wrapped top source row for deferred streaming.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x1B, 0xC1, 0x5F, 0x16, 0x00, 0x21]), "upward row streaming should resolve the queued top source row through the row-pointer table.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x1A, 0xC1, 0xFE, 0x08, 0xDA]), "upward row streaming should compute the target background row address from the queued top row cursor.");
        Assert.True(ContainsSequence(rom, [0xFA, 0xE5, 0xC0, 0xC6, 0x01, 0xFE, 0x20]), "upward row streaming should fill the visible row from the current background-left column.");
    }

    [Fact]
    public void Compiles_camera_tile_column_at_to_map_width_wrapped_source_column()
    {
        const string source = """
                              void Main() {
                                  Video.Init();
                                  World.Column(0, 0, 0, 4, 5);
                                  World.Column(1, 0, 0, 4, 5);
                                  World.Column(2, 0, 0, 4, 5);
                                  World.Column(3, 0, 0, 4, 5);
                                  World.Column(4, 0, 0, 4, 5);
                                  World.Column(5, 0, 0, 4, 5);
                                  World.Column(6, 0, 0, 4, 5);
                                  World.Column(7, 0, 0, 4, 5);
                                  World.Column(8, 0, 0, 4, 5);
                                  World.Column(9, 0, 0, 4, 5);
                                  World.Column(10, 0, 0, 4, 5);
                                  World.Column(11, 0, 0, 4, 5);
                                  World.Column(12, 0, 0, 4, 5);
                                  World.Column(13, 0, 0, 4, 5);
                                  World.Column(14, 0, 0, 4, 5);
                                  World.Column(15, 0, 0, 4, 5);
                                  World.Map(16, 11, 4);
                                  Camera.Init(16, 11, 4);
                                  i16 tile = 0;
                                  while (true) {
                                      Video.WaitVBlank();
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
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(mario_player, "player-wide.gb.png", 33, 16);
                                  World.Column(0, 0, 0, 4, 5);
                                  World.Column(1, 0, 0, 4, 5);
                                  World.Column(2, 0, 0, 4, 5);
                                  World.Column(3, 0, 0, 4, 5);
                                  World.Column(4, 0, 0, 4, 5);
                                  World.Column(5, 0, 0, 4, 5);
                                  World.Column(6, 0, 0, 4, 5);
                                  World.Column(7, 0, 0, 4, 5);
                                  World.Column(8, 0, 0, 4, 5);
                                  World.Column(9, 0, 0, 4, 5);
                                  World.Column(10, 0, 0, 4, 5);
                                  World.Column(11, 0, 0, 4, 5);
                                  World.Column(12, 0, 0, 4, 5);
                                  World.Column(13, 0, 0, 3, 5);
                                  World.Column(14, 0, 0, 4, 5);
                                  World.Column(15, 0, 0, 4, 5);
                                  World.Map(16, 11, 4);
                                  Camera.Init(16, 11, 4);
                                  i16 logicalWidth = 0;
                                  i16 footTile = 0;
                                  i16 fail = 0;
                                  while (true) {
                                      Video.WaitVBlank();
                                      logicalWidth = Sprite.Width(mario_player);
                                      footTile = camera_span_tile_at(72, Sprite.Width(mario_player), 2);
                                      fail = camera_span_has_tile(72, Sprite.Width(mario_player), 2, 3);
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
                              void Main() {
                                  Video.Init();
                                  while (true) {
                                      Video.WaitVBlank();
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
                              void Main() {
                                  Video.Init();
                                  i16 column = 20;
                                  while (true) {
                                      Video.WaitVBlank();
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
                              void Main() {
                                  Video.Init();
                                  i16 camera = 0;
                                  i16 fine = 0;
                                  i16 streamColumn = 20;
                                  i16 marker = 0;
                                  i16 frame = 0;
                                  while (true) {
                                      Video.WaitVBlank();
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
                              void Main() {
                                  Video.Init();
                                  World.Column(0, 1, 2, 3, 4);
                                  World.Column(1, 5, 6, 7, 8);
                                  World.Map(2, 11, 4);
                                  i16 targetColumn = 20;
                                  i16 mapColumn = 0;
                                  while (true) {
                                      Video.WaitVBlank();
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
        Assert.True(ContainsSequence(rom, [0xFA, 0x02, 0xC0, 0x5F, 0x16, 0x00, 0x21]), "ROM should load the source map column into DE and a row-table address into HL.");
        Assert.True(ContainsSequence(rom, [0x19, 0x7E, 0x47]), "ROM should read a tile from the map row table and preserve it in B.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xC6, 0x60, 0x6F, 0x26, 0x99, 0x78, 0x77]), "ROM should stream row 11 into the target background column.");
    }

    [Fact]
    public void Compiles_map_tile_lookup_as_a_runtime_expression()
    {
        const string source = """
                              void Main() {
                                  Video.Init();
                                  World.Column(0, 1, 0);
                                  World.Column(1, 0, 2);
                                  World.Map(2, 0, 2);
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
        Assert.True(ContainsSequence(rom, [0x3E, 0x01, 0xEA, 0x02, 0xC0, 0x3E, 0x00, 0xEA, 0x03, 0xC0]), "ROM should execute the branch body when the tile is non-zero.");
    }

    [Fact]
    public void Compiles_relational_condition_against_a_constant()
    {
        const string source = """
                              void Main() {
                                  Video.Init();
                                  u8 y = 80;
                                  u8 grounded = 0;
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
                              void Main() {
                                  Video.Init();
                                  i16 y = 40;
                                  i16 velocity = 3;
                                  y = y + velocity;
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0xFA, 0x02, 0xC0, 0x47, 0xFA, 0x00, 0xC0, 0x80, 0xEA, 0x00, 0xC0]), "ROM should add the low bytes of two word-backed locals and store the result.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x03, 0xC0, 0x47, 0xFA, 0x01, 0xC0, 0x88, 0xEA, 0x01, 0xC0]), "ROM should propagate carry through the high bytes of two word-backed locals.");
    }

    [Fact]
    public void Compiles_struct_member_access_as_adjacent_wram_fields()
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
                                  position.y = 3;
                                  position.x = position.x + position.y;
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x3E, 0x28, 0xEA, 0x00, 0xC0, 0x3E, 0x00, 0xEA, 0x01, 0xC0]), "ROM should store position.x as a two-byte field.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x03, 0xEA, 0x02, 0xC0, 0x3E, 0x00, 0xEA, 0x03, 0xC0]), "ROM should store position.y at the next two-byte field address.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x02, 0xC0, 0x47, 0xFA, 0x00, 0xC0, 0x80, 0xEA, 0x00, 0xC0]), "ROM should use direct low-byte member arithmetic with no helper call.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x03, 0xC0, 0x47, 0xFA, 0x01, 0xC0, 0x88, 0xEA, 0x01, 0xC0]), "ROM should use direct high-byte member arithmetic with no helper call.");
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

                                      void Main() {
                                          Video.Init();
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

                                         void Main() {
                                             Video.Init();
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

                              void Main() {
                                  Video.Init();
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

                              void Main() {
                                  Video.Init();
                                  i16 x = StartX;
                                  i16 velocity = Velocity;
                                  x = x + velocity;
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x3E, 0x28, 0xEA, 0x00, 0xC0, 0x3E, 0x00, 0xEA, 0x01, 0xC0]), "Const StartX should compile as an immediate store to the first two-byte local.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x03, 0xEA, 0x02, 0xC0, 0x3E, 0x00, 0xEA, 0x03, 0xC0]), "Const Velocity should compile as an immediate store to the second two-byte local, with no const storage slot.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x02, 0xC0, 0x47, 0xFA, 0x00, 0xC0, 0x80, 0xEA, 0x00, 0xC0]), "Const declarations should not shift runtime local addresses.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x03, 0xC0, 0x47, 0xFA, 0x01, 0xC0, 0x88, 0xEA, 0x01, 0xC0]), "Const declarations should preserve high-byte arithmetic.");
    }

    [Fact]
    public void Compiles_local_const_identifiers_as_immediates_without_local_storage()
    {
        const string source = """
                              void Main() {
                                  Video.Init();
                                  const u8 StartX = 40;
                                  const u8 Velocity = StartX + 1;
                                  i16 x = Velocity;
                                  i16 y = 1;
                                  y = x;
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x3E, 0x29, 0xEA, 0x00, 0xC0, 0x3E, 0x00, 0xEA, 0x01, 0xC0]), "Local const Velocity should compile its derived value as an immediate store to the first two-byte local.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x01, 0xEA, 0x02, 0xC0, 0x3E, 0x00, 0xEA, 0x03, 0xC0]), "Local const declarations should not reserve WRAM before the second local.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xEA, 0x02, 0xC0, 0xFA, 0x01, 0xC0, 0xEA, 0x03, 0xC0]), "Local const declarations should not shift runtime local addresses.");
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

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x3E, 0x02, 0xEA, 0x00, 0xC0]), "Const conditional expression should fold to one immediate store with no runtime conditional.");
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

                              void Main() {
                                  Video.Init();
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

                              void Main() {
                                  Video.Init();
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

                              void Main() {
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
                                      void Main() {
                                          Video.Init();
                                          u8 seed = 3;
                                          u8 values[4];
                                          values[0] = 1;
                                          values[1] = seed;
                                          values[2] = seed + 1;
                                          u8 copy = values[3];
                                      }
                                      """;

        const string initializerSource = """
                                         void Main() {
                                             Video.Init();
                                             u8 seed = 3;
                                             u8 values[4] = [1, seed, seed + 1];
                                             u8 copy = values[3];
                                         }
                                         """;

        Assert.Equal(GameBoyRomCompiler.CompileSource(explicitSource), GameBoyRomCompiler.CompileSource(initializerSource));
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
                                         }
                                         """;

        Assert.Equal(GameBoyRomCompiler.CompileSource(explicitSource), GameBoyRomCompiler.CompileSource(initializerSource));
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
                                            }
                                            """;

        const string inferredLengthSource = """
                                            void Main() {
                                                Video.Init();
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
                              void Main() {
                                  Video.Init();
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

                              void Main() {
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
                              void Main() {
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
    public void Compiles_u16_i16_locals_and_struct_fields_as_adjacent_wram_words()
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
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(
            ContainsSequence(rom, [0x3E, 0x01, 0xEA, 0x00, 0xC0, 0x3E, 0x2C, 0xEA, 0x01, 0xC0, 0x3E, 0x01, 0xEA, 0x02, 0xC0, 0x3E, 0xFE, 0xEA, 0x03, 0xC0, 0x3E, 0xFF, 0xEA, 0x04, 0xC0]),
            "mixed-width struct fields should reserve adjacent WRAM bytes: tag, x low/high, then y low/high.");
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

        Assert.Equal(32768, rom.Length);
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

        Assert.Equal(32768, rom.Length);
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

        Assert.Equal(32768, rom.Length);
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

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0xFA, 0x01, 0xC0, 0xFE, 0x03, 0xD2]), "for condition should compare the loop local and jump out when i >= 3.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x01, 0xC0, 0x47, 0xFA, 0x00, 0xC0, 0x80, 0xEA, 0x00, 0xC0]), "for body should use direct x += i arithmetic.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x01, 0xC0, 0xC6, 0x01, 0xEA, 0x01, 0xC0]), "for increment should use direct i += 1 arithmetic.");
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
                              void Main() {
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
                              void Main() {
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
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x3E, 0x07, 0xEA, 0x05, 0xC0]), "actors[1].active should store at base + sizeof(Actor) + offsetof(active).");
        Assert.True(ContainsSequence(rom, [0xFA, 0x05, 0xC0, 0xEA, 0x0A, 0xC0]), "constant indexed field reads should use the flattened field address directly.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x09, 0xC0, 0x47, 0x80, 0x80, 0x21, 0x01, 0xC0, 0x5F, 0x16, 0x00, 0x19, 0x7E, 0xC6, 0x01, 0x77]), "actors[i].y should compute HL from the y-field base plus i * sizeof(Actor).");
        Assert.True(ContainsSequence(rom, [0xFA, 0x09, 0xC0, 0x47, 0x80, 0x80, 0x21, 0x00, 0xC0, 0x5F, 0x16, 0x00, 0x19, 0x7E, 0xEA, 0x0B, 0xC0]), "runtime indexed field reads should compute HL from the field base plus i * sizeof(Actor).");
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
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0xFA, 0x09, 0xC0, 0xFE, 0x03]), "pool loop should compare the byte index with countof(actors), not use an iterator object.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x09, 0xC0, 0x47, 0x80, 0x80, 0x21, 0x02, 0xC0, 0x5F, 0x16, 0x00, 0x19, 0x7E, 0xFE, 0x00]), "active checks should read actors[i].active through the fixed field base plus i * stride.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x09, 0xC0, 0x47, 0x80, 0x80, 0x21, 0x00, 0xC0, 0x5F, 0x16, 0x00, 0x19, 0x7E, 0xC6, 0x01, 0x77]), "updates should mutate actors[i].x through fixed storage with no actor object or dispatch table.");
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
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x3E, 0x2C, 0xEA, 0x03, 0xC0, 0x3E, 0x01, 0xEA, 0x04, 0xC0]), "actors[1].worldX should store low/high at base + sizeof(Actor).");
        Assert.True(ContainsSequence(rom, [0x3E, 0x07, 0xEA, 0x05, 0xC0]), "actors[1].y should be placed after the two-byte worldX field.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x06, 0xC0, 0x47, 0x80, 0x80, 0x21, 0x02, 0xC0, 0x5F, 0x16, 0x00, 0x19, 0x7E, 0xC6, 0x01, 0x77]), "actors[i].y should use the mixed-width struct stride.");
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
        Assert.Contains("RetroSharp_Portable2D_portable2d_sprite_draw(goomba, __enemies_draw_screen_x, __enemies_draw_screen_y, 0, false, 0);", lowered);
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
        var baseDirectory = WriteSpriteAsset(
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
        var baseDirectory = WriteSpriteAsset(
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
        var baseDirectory = WriteSpriteAsset(
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
        var baseDirectory = WriteSpriteAsset(
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
    public void Effect_framework_lowers_requests_update_and_draw_as_separate_fixed_pool()
    {
        const string source = """
                              void Main() {
                                  Sprite.Asset(spark_sprite, "spark.sprite.json");
                                  Effects.Pool(fx, capacity: 2, requests: 2);
                                  Effects.Def(Spark, sprite: spark_sprite, lifetime: 8);
                                  fx.Request(Spark, 40, 60);
                                  fx.ProcessRequests();
                                  fx.Update();
                                  fx.Draw();
                              }
                              """;

        var program = ParseGameBoySourceWithPortable2D(source);
        var loweredProgram = ActorFrameworkLowerer.Lower(program, GameBoyTarget.Capabilities, supportsUpdate: true, supportsDraw: true);
        var visitor = new PrintNodeVisitor();
        loweredProgram.Accept(visitor);
        var lowered = visitor.ToString();

        Assert.Contains("struct Effect", lowered);
        Assert.Contains("Effect fx[2];", lowered);
        Assert.Contains("EffectSpawnRequest fxRequests[2];", lowered);
        Assert.Contains("fxRequests[__fx_request_call0_i].kind=Spark;", lowered);
        Assert.Contains("fx[__fx_process_Spark_i].active=1;", lowered);
        Assert.Contains("fx[__fx_update_i].age+=1;", lowered);
        Assert.Contains("RetroSharp_Portable2D_portable2d_sprite_draw(spark_sprite, __fx_draw_0_x_Spark, __fx_draw_0_y_Spark, 0, false, 0);", lowered);
        Assert.Contains("RetroSharp_Portable2D_portable2d_sprite_draw(spark_sprite, __fx_draw_1_x_Spark, __fx_draw_1_y_Spark, 0, false, 0);", lowered);
    }

    [Fact]
    public void Effect_framework_draw_hides_inactive_or_offscreen_slots_without_skipping_the_sprite_call()
    {
        const string source = """
                              void Main() {
                                  Sprite.Asset(spark_sprite, "spark.sprite.json");
                                  Effects.Pool(fx, capacity: 1, requests: 1);
                                  Effects.Def(Spark, sprite: spark_sprite, lifetime: 4);
                                  fx[0].kind = Spark;
                                  fx.Draw();
                              }
                              """;

        var program = ParseGameBoySourceWithPortable2D(source);
        var loweredProgram = ActorFrameworkLowerer.Lower(program, GameBoyTarget.Capabilities, supportsUpdate: true, supportsDraw: true);
        var visitor = new PrintNodeVisitor();
        loweredProgram.Accept(visitor);
        var lowered = visitor.ToString();

        Assert.Contains("u8 __fx_draw_0_x_Spark=0;", lowered);
        Assert.Contains("u8 __fx_draw_0_y_Spark=144;", lowered);
        Assert.Contains("__fx_draw_0_x_Spark=__fx_draw_0_screen_x;", lowered);
        Assert.Contains("__fx_draw_0_y_Spark=__fx_draw_0_screen_y;", lowered);
        Assert.Contains("RetroSharp_Portable2D_portable2d_sprite_draw(spark_sprite, __fx_draw_0_x_Spark, __fx_draw_0_y_Spark, 0, false, 0);", lowered);
        Assert.Equal(1, CountOccurrences(lowered, "RetroSharp_Portable2D_portable2d_sprite_draw(spark_sprite"));
    }

    [Fact]
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
        var baseDirectory = WriteSpriteAsset(
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
    public void Effect_draw_runtime_hides_expired_slot_in_oam()
    {
        var baseDirectory = WriteSpriteAsset(
            "spark.sprite.json",
            SpriteJson(Rows(8, 16, "11111111")));
        const string source = """
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(spark_sprite, "spark.sprite.json");
                                  Effects.Pool(fx, capacity: 1, requests: 1);
                                  Effects.Def(Spark, sprite: spark_sprite, lifetime: 1);
                                  fx.Request(Spark, 40, 60);
                                  fx.ProcessRequests();
                                  while (true) {
                                      Video.WaitVBlank();
                                      fx.Draw();
                                      fx.Update();
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source, baseDirectory);
        var cpu = new GameBoyTestCpu(rom) { CycleAccurateLy = true };
        cpu.RunFrames(4);

        Assert.Contains(cpu.OamWrites, write => write.Address == 0xFE00 && write.Value == 76);
        Assert.Equal(160, cpu.Oam(0xFE00));
    }

    [Fact]
    public void Projectile_draw_runtime_uses_distinct_oam_slots_for_pool_slots()
    {
        var baseDirectory = WriteSpriteAsset(
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
    public void Effect_draw_runtime_uses_distinct_oam_slots_for_pool_slots()
    {
        var baseDirectory = WriteSpriteAsset(
            "spark.sprite.json",
            SpriteJson(Rows(8, 16, "11111111")));
        const string source = """
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(spark_sprite, "spark.sprite.json");
                                  Effects.Pool(fx, capacity: 2, requests: 2);
                                  Effects.Def(Spark, sprite: spark_sprite, lifetime: 8);
                                  fx.Request(Spark, 24, 40);
                                  fx.Request(Spark, 64, 60);
                                  fx.ProcessRequests();
                                  while (true) {
                                      Video.WaitVBlank();
                                      fx.Draw();
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
                              void Main() {
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
                                      void Main() {
                                          u8 tile = 2;
                                          u8 hit = 0;
                                          if (tile >= 1 && tile < 4) {
                                              hit = 1;
                                          }
                                      }
                                      """;

        const string membershipSource = """
                                        void Main() {
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
                              void Main() {
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
                              void Main() {
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
                              void Main() {
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
                              void Main() {
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

                              void Main() {
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
                              void Main() {
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
                              void Main() {
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
                              void Main() {
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

    [Theory]
    [InlineData("video_wait_vblank();", "video_wait_vblank")]
    [InlineData("input_poll();", "input_poll")]
    [InlineData("audio_update();", "audio_update")]
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

        var exception = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource(source));
        Assert.Contains(legacyName, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Direct_legacy_camera_builtins_are_rejected()
    {
        const string source = """
                              void Main() {
                                  World.Column(0, 1, 2);
                                  World.Map(1, 10, 2);
                                  Camera.Init(1, 10, 2);
                                  camera_set_position(4, 0);
                                  camera_apply();
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource(source));
        Assert.Contains("camera_set_position", exception.Message, StringComparison.Ordinal);
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

        var exception = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource(source));
        Assert.Contains("world_column", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Direct_legacy_sprite_draw_builtin_is_rejected()
    {
        var baseDirectory = WriteSpriteAsset(
            "player.sprite.json",
            SpriteJson(Rows(8, 16, "01230123", "32103210")));

        const string source = """
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(player, "player.sprite.json");
                                  sprite_draw(player, 72, 80, 0);
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource(source, baseDirectory));
        Assert.Contains("sprite_draw", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Direct_button_read_and_bare_button_identifiers_are_rejected()
    {
        const string source = """
                              void Main() {
                                  Video.Init();
                                  Input.Poll();
                                  if (button_pressed(a) != 0) {
                                  }
                                  if (Input.IsDown(Button.A)) {
                                  }
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource(source));
        Assert.Contains("button_pressed", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Bare_button_identifiers_are_rejected_by_input_facade()
    {
        const string source = """
                              void Main() {
                                  Video.Init();
                                  Input.Poll();
                                  if (Input.IsDown(a)) {
                                  }
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource(source));
        Assert.Contains("Button enum member", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Button.A")]
    [InlineData("Button.B")]
    [InlineData("Button.Select")]
    [InlineData("Button.Start")]
    [InlineData("Button.Right")]
    [InlineData("Button.Left")]
    [InlineData("Button.Up")]
    [InlineData("Button.Down")]
    public void Button_enum_members_are_accepted_by_input_facade(string enumMember)
    {
        var source = $$"""
                              void Main() {
                                  Video.Init();
                                  i16 held = 0;
                                  i16 ticks = 0;
                                  while (true) {
                                      Video.WaitVBlank();
                                      Input.Poll();
                                      if (Input.WasPressed({{enumMember}}) != 0) {
                                          held = 1;
                                      }
                                      if (Input.IsDown({{enumMember}}) != 0) {
                                          held = 2;
                                      }
                                      if (Input.WasReleased({{enumMember}}) != 0) {
                                          held = 0;
                                      }
                                      ticks = Input.HoldTicks({{enumMember}});
                                  }
                              }
                              """;

        Assert.Equal(32768, GameBoyRomCompiler.CompileSource(source).Length);
    }

    [Fact]
    public void Input_facade_predicates_lower_like_explicit_numeric_checks()
    {
        const string explicitComparisonSource = """
                                                void Main() {
                                                    Video.Init();
                                                    i16 w = 0;
                                                    i16 h = 0;
                                                    while (true) {
                                                        Video.WaitVBlank();
                                                        Input.Poll();
                                                        if (Input.WasPressed(Button.A) != 0) { w += 1; }
                                                        if (Input.IsDown(Button.A) != 0) { w += 1; }
                                                        if (Input.WasReleased(Button.A) != 0) { w += 1; }
                                                        h = Input.HoldTicks(Button.A);
                                                    }
                                                }
                                                """;

        const string predicateConditionSource = """
                                               void Main() {
                                                   Video.Init();
                                                   i16 w = 0;
                                                   i16 h = 0;
                                                   while (true) {
                                                       Video.WaitVBlank();
                                                       Input.Poll();
                                                       if (Input.WasPressed(Button.A)) { w += 1; }
                                                       if (Input.IsDown(Button.A)) { w += 1; }
                                                       if (Input.WasReleased(Button.A)) { w += 1; }
                                                       h = Input.HoldTicks(Button.A);
                                                   }
                                               }
                                               """;

        Assert.Equal(GameBoyRomCompiler.CompileSource(explicitComparisonSource), GameBoyRomCompiler.CompileSource(predicateConditionSource));
    }

    [Fact]
    public void Compiles_tick_input_api_for_variable_jump()
    {
        const string source = """
                              void Main() {
                                  Video.Init();
                                  i16 grounded = 1;
                                  i16 velocityY = 0;
                                  i16 jumping = 0;
                                  i16 jumpTicks = 0;
                                  while (true) {
                                      Video.WaitVBlank();
                                      Input.Poll();
                                      if (Input.WasPressed(Button.A) != 0) {
                                          if (grounded != 0) {
                                              velocityY = 252;
                                              jumping = 1;
                                          }
                                      }
                                      if (jumping != 0) {
                                          jumpTicks = Input.HoldTicks(Button.A);
                                          if (Input.IsDown(Button.A) != 0) {
                                              if (jumpTicks < 12) {
                                                  velocityY = velocityY - 1;
                                              }
                                          }
                                          if (Input.WasReleased(Button.A) != 0) {
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
        Assert.True(ContainsSequence(rom, [0xFA, 0xF2, 0xC0, 0x6F, 0x26, 0x00, 0x7D, 0xEA, 0x06, 0xC0, 0x7C, 0xEA, 0x07, 0xC0]), "Input.HoldTicks(Button.A) should return a complete zero-extended I16 value into a game variable.");
        Assert.True(ContainsSequence(rom, [0xFA, 0xF0, 0xC0, 0xE6, 0x01, 0xFE, 0x00, 0xCA]), "Input.IsDown(Button.A) and Input.WasPressed(Button.A) should test the current tick mask.");
        Assert.True(ContainsSequence(rom, [0xFA, 0xF1, 0xC0, 0xE6, 0x01, 0xFE, 0x00, 0xC2]), "Input.WasPressed(Button.A) should reject buttons that were already down in the previous tick.");
        Assert.True(ContainsSequence(rom, [0xFA, 0xF0, 0xC0, 0xE6, 0x01, 0xFE, 0x00, 0xC2]), "Input.WasReleased(Button.A) should require the button to be up in the current tick.");
        Assert.True(ContainsSequence(rom, [0xFA, 0xF1, 0xC0, 0xE6, 0x01, 0xFE, 0x00, 0xCA]), "Input.WasReleased(Button.A) should require the button to have been down in the previous tick.");
    }

    [Fact]
    public void Input_poll_settles_joypad_rows_before_latching_buttons()
    {
        const string source = """
                              void Main() {
                                  Video.Init();
                                  while (true) {
                                      Video.WaitVBlank();
                                      Input.Poll();
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
        var source = RunnerSample.FlattenedSource();

        var movementStart = source.IndexOf("inline void HandleHorizontalInput(PlayerState player, Pixel footWorldY)", StringComparison.Ordinal);
        var movementEnd = source.IndexOf("class FrameState", movementStart, StringComparison.Ordinal);
        Assert.True(movementStart >= 0);
        Assert.True(movementEnd > movementStart);

        var movementBlock = source[movementStart..movementEnd];
        var rightStart = movementBlock.IndexOf("if (Input.IsDown(Button.Right))", StringComparison.Ordinal);
        Assert.True(rightStart >= 0, "Runner should gate forward movement with the D-pad right button.");

        var leftStart = movementBlock.IndexOf("if (Input.IsDown(Button.Left))", StringComparison.Ordinal);
        Assert.True(leftStart >= 0, "Runner should gate backward movement with the D-pad left button.");

        var movementCall = source.IndexOf("view.HandleHorizontalInput(player, movementFootWorldY);", StringComparison.Ordinal);
        var animationCall = source.IndexOf("player.UpdateRunAnimation(view);", StringComparison.Ordinal);
        Assert.True(movementCall >= 0);
        Assert.True(animationCall > movementCall, "Runner should update movement before animation state.");

        Assert.Contains("type Pixel = i16;", source);
        Assert.Contains("CameraState view;", source);
        Assert.DoesNotContain("Pixel cameraX = 0;", source);

        Assert.Contains("UpdateIntent(desiredDirection, player.grounded);", movementBlock);
        Assert.Contains("ApplyMotion(player, wallProbeY);", movementBlock);
        var cameraBlock = source[source.IndexOf("class CameraState", StringComparison.Ordinal)..movementEnd];
        Assert.Contains("Pixel y;", cameraBlock);
        Assert.Contains("x += 1;", cameraBlock);
        Assert.Contains("player.x += 1;", cameraBlock);
        Assert.Contains("moving = true;", cameraBlock);
        Assert.Contains("x -= 1;", cameraBlock);
        Assert.Contains("player.x -= 1;", cameraBlock);
        Assert.Contains("Camera.SetPosition(x, y);", cameraBlock);
        Assert.DoesNotContain("view.ApplyFramePosition();", source);
        Assert.Equal(1, CountOccurrences(source, "view.ApplyPosition();"));
        Assert.Contains("Camera.SetPosition(x, y);", cameraBlock);
        Assert.DoesNotContain("if (view.x > 0)", movementBlock);
        Assert.DoesNotContain("camera_move_right();", source);
        Assert.DoesNotContain("camera_move_left();", source);
        Assert.Contains("if (view.moving)", source);
        Assert.Contains("animTick += view.speed;", source);
        Assert.Contains("Animation.Frame(run, animTick)", source);
        Assert.DoesNotContain("i16 frame = 0;", source);
        Assert.DoesNotContain("frame = frame + 1;", source);
        Assert.DoesNotContain("if (frame == 3)", source);
        Assert.DoesNotContain("displayFrame = frame + 1;", source);
        Assert.Equal(1, CountOccurrences(source, "Camera.SetPosition(x, y);"));
        Assert.Equal(1, CountOccurrences(source, "animTick += view.speed;"));

        var rom = GameBoyRomCompiler.CompileSource(RunnerSample.CompiledSource(), RunnerSample.Directory);
        AssertRunnerMbc1Rom(rom);
    }

    [Fact]
    public void GameBoy_runner_uses_dead_zone_screen_position_for_camera_collision_and_draw()
    {
        var source = RunnerSample.FlattenedSource();

        Assert.Contains("static class DeadZone", source);
        Assert.Contains("Left = 64", source);
        Assert.Contains("Right = 96", source);
        Assert.Contains("Top = 56", source);
        Assert.Contains("Bottom = 88", source);
        Assert.Contains("Camera.VerticalScrollMax()", source);
        Assert.DoesNotContain("static class CameraBounds", source);
        Assert.Contains("StartX = 72", source);
        Assert.DoesNotContain("ScreenX = 72", source);

        var playerStart = source.IndexOf("class PlayerState", StringComparison.Ordinal);
        var cameraStart = source.IndexOf("class CameraState", StringComparison.Ordinal);
        var frameStart = source.IndexOf("class FrameState", StringComparison.Ordinal);
        Assert.True(playerStart >= 0);
        Assert.True(cameraStart > playerStart);
        Assert.True(frameStart > cameraStart);
        var playerBlock = source[playerStart..cameraStart];
        var cameraBlock = source[cameraStart..frameStart];

        Assert.Contains("Pixel x;", playerBlock);
        Assert.Contains("Pixel y;", playerBlock);
        Assert.Contains("inline void Reset(CameraState view)", playerBlock);
        Assert.Contains("x = view.x + Player.StartX;", playerBlock);
        Assert.Contains("y = Player.StartY;", playerBlock);
        Assert.Contains("inline pure Pixel ScreenX(PlayerState player) => player.x - x;", cameraBlock);
        Assert.Contains("inline pure Pixel ScreenY(PlayerState player) => player.y - y;", cameraBlock);
        Assert.DoesNotContain("Pixel screenX;", cameraBlock);
        Assert.DoesNotContain("Pixel screenY;", cameraBlock);
        Assert.Contains("inline void FollowPlayer(PlayerState player)", cameraBlock);
        Assert.Contains("if (screenX >= DeadZone.Right)", cameraBlock);
        Assert.Contains("if (screenX <= DeadZone.Left)", cameraBlock);
        Assert.Contains("if (screenY > DeadZone.Bottom)", cameraBlock);
        Assert.Contains("Camera.SetPosition(x, y);", cameraBlock);
        Assert.DoesNotContain("view.ApplyFramePosition();", source);
        Assert.Equal(1, CountOccurrences(source, "view.ApplyPosition();"));

        Assert.Contains("inline void PresentFrame(PlayerState player, CameraState view)", source);
        Assert.DoesNotContain("view.CaptureScreen(player);", source);
        Assert.Contains("Sprite.Draw(mario_player, screenX, screenY, player.displayFrame, player.displayFlipX, 0);", source);

        Assert.Contains("frame.ResolveLanding(player, screenX, previousFootWorldY, footWorldY);", source);
        Assert.Contains("frame.ResolveCeilingHit(player, screenX, footWorldY);", source);
        Assert.Contains("i16 footTile = Camera.AabbHitTop(screenX, footWorldY - CollisionProbe.LandingSearchTopOffset, Sprite.Width(mario_player), CollisionProbe.LandingSearchHeight, CollisionFlag.Landable);", source);
        Assert.Contains("Camera.AabbTiles(screenX, headProbeY, Sprite.Width(mario_player), CollisionProbe.CeilingProbeHeight, CollisionFlag.Solid)", source);
        Assert.Contains("let rightProbeX = screenX + CollisionProbe.RightWallProbeOffset;", source);
        Assert.Contains("let leftProbeX = screenX - CollisionProbe.LeftWallProbeOffset;", source);
        Assert.Contains("Camera.AabbTiles(rightProbeX, wallProbeY, Sprite.Width(mario_player), CollisionProbe.WallProbeHeight, CollisionFlag.Solid) == 0", source);
        Assert.Contains("Camera.AabbTiles(leftProbeX, wallProbeY, Sprite.Width(mario_player), CollisionProbe.WallProbeHeight, CollisionFlag.Solid) == 0", source);

        var operations = GameBoyRomCompiler.CollectSdkOperations(RunnerSample.CompiledSource(), RunnerSample.Directory);
        Assert.Contains(
            operations.OfType<Sdk2DOperation.SetCameraPosition>(),
            operation => operation.Axes.HasFlag(ScrollAxes.Horizontal) && operation.Axes.HasFlag(ScrollAxes.Vertical));

        var rom = GameBoyRomCompiler.CompileSource(RunnerSample.CompiledSource(), RunnerSample.Directory);
        AssertRunnerMbc1Rom(rom);
    }

    [Fact]
    public void Compiles_constant_groups_like_flat_constants()
    {
        const string flatSource = """
                                  const WorldWidth = 16;
                                  const WorldStreamY = 9;
                                  const WorldHeight = 5;
                                  const PlayerScreenX = 72;

                                  void Main() {
                                      Video.Init();
                                      World.Column(0, 1, 2, 3, 4, 5);
                                      World.Flags(0, 0, 0, 1, 1, 1);
                                      World.Map(WorldWidth, WorldStreamY, WorldHeight);
                                      Camera.Init(WorldWidth, WorldStreamY, WorldHeight);
                                      i16 cameraX = 0;
                                      i16 playerWorldX = cameraX + PlayerScreenX;
                                      Camera.SetPosition(cameraX, 0);
                                  }
                                  """;

        const string groupedSource = """
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

        Assert.Equal(GameBoyRomCompiler.CompileSource(flatSource), GameBoyRomCompiler.CompileSource(groupedSource));
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
    public void Static_class_rejects_instance_members()
    {
        const string source = """
                              static class Level { const i16 Width = 16; i16 broken; }

                              void Main() {
                                  Video.Init();
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource(source));
        Assert.Contains("Static class 'Level'", exception.Message);
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

        Assert.Equal(GameBoyRomCompiler.CompileSource(intSource), GameBoyRomCompiler.CompileSource(boolSource));
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

    [Fact]
    public void Compiles_camera_state_receiver_helper_like_flat_horizontal_input()
    {
        const string flatSource = """
                                  type Pixel = i16;

                                  void Main() {
                                      Video.Init();
                                      World.Column(0, 1, 2);
                                      World.Flags(0, 0, 1);
                                      World.Map(1, 10, 2);
                                      Camera.Init(1, 10, 2);
                                      Input.Poll();
                                      Pixel cameraX;
                                      Pixel moving;
                                      bool displayFlipX;
                                      moving = 0;
                                      if (Input.IsDown(Button.Right) != 0) {
                                          moving = 1;
                                          displayFlipX = false;
                                          cameraX += 1;
                                      }
                                      if (Input.IsDown(Button.Left) != 0) {
                                          moving = 1;
                                          displayFlipX = true;
                                          cameraX -= 1;
                                      }
                                      if (moving != 0) {
                                          Camera.SetPosition(cameraX, 0);
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
                                          if (Input.IsDown(Button.Right) != 0) {
                                              view.moving = 1;
                                              player.displayFlipX = false;
                                              view.x += 1;
                                          }
                                          if (Input.IsDown(Button.Left) != 0) {
                                              view.moving = 1;
                                              player.displayFlipX = true;
                                              view.x -= 1;
                                          }
                                          if (view.moving != 0) {
                                              Camera.SetPosition(view.x, 0);
                                          }
                                      }

                                      void Main() {
                                          Video.Init();
                                          World.Column(0, 1, 2);
                                          World.Flags(0, 0, 1);
                                          World.Map(1, 10, 2);
                                          Camera.Init(1, 10, 2);
                                          Input.Poll();
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
        var source = RunnerSample.FlattenedSource();

        Assert.Contains("static class Level", source);
        Assert.Contains("Width = 312", source);
        Assert.Contains("Height = 40", source);
        Assert.Contains("StreamHeight = 40", source);
        Assert.DoesNotContain("SignedVelocityWrap", source);
        Assert.Contains("PixelWidth = 2496", source);
        Assert.Contains("static class Player", source);
        Assert.Contains("StartX = 72", source);
        Assert.Contains("class PlayerState", source);
        Assert.Contains("inline void Reset(CameraState view)", source);
        Assert.Contains("inline void ApplyGravity()", source);
        Assert.Contains("PlayerState player;", source);
        Assert.Contains("player.Reset(view);", source);
        Assert.Contains("player.ApplyGravity();", source);
        Assert.Contains("""World.Load("assets/maps/stage1.tmj");""", source);
        Assert.Contains("LoadWorld();", source);
        Assert.Contains("Sprite.Draw(mario_player, screenX, screenY", source);
        Assert.DoesNotContain("const WorldWidth", source);
        Assert.DoesNotContain("const PlayerScreenX", source);
        Assert.DoesNotContain("Pixel playerY =", source);
        Assert.DoesNotContain("Pixel velocityY =", source);
    }

    [Fact]
    public void GameBoy_runner_extracts_frame_loop_into_named_inline_helpers()
    {
        var source = RunnerSample.FlattenedSource();

        Assert.Contains("class CameraState", source);
        Assert.Contains("class FrameState", source);
        Assert.Contains("inline void PresentFrame(PlayerState player, CameraState view)", source);
        Assert.Contains("inline void HandleJumpInput(Pixel horizontalSpeed)", source);
        Assert.Contains("inline void HandleHorizontalInput(PlayerState player, Pixel footWorldY)", source);
        Assert.Contains("inline void ResolveLanding(PlayerState player, Pixel screenX, Pixel previousFootWorldY, Pixel footWorldY)", source);
        Assert.Contains("inline void ResolveFall(PlayerState player)", source);
        Assert.Contains("inline void ResolveReset(PlayerState player, CameraState view)", source);
        Assert.Contains("inline void UpdateRunAnimation(CameraState view)", source);
        Assert.Contains("PresentFrame(player, view);", source);
        Assert.Contains("frame.Begin();", source);
        Assert.DoesNotContain("view.CaptureScreen(player);", source);
        Assert.Contains("frame.ResolveLanding(player, screenX, previousFootWorldY, footWorldY);", source);
        Assert.Contains("frame.ResolveFall(player);", source);
        Assert.Contains("frame.ResolveReset(player, view);", source);
        Assert.Contains("player.HandleJumpInput(view.speed);", source);
        Assert.Contains("i16 movementFootWorldY = player.y + Player.FootOffset;", source);
        Assert.Contains("view.HandleHorizontalInput(player, movementFootWorldY);", source);
        Assert.Contains("player.UpdateRunAnimation(view);", source);
        Assert.DoesNotContain("Pixel cameraX = 0;", source);
        Assert.DoesNotContain("Pixel moving = 0;", source);
        Assert.DoesNotContain("Pixel resetRequested = 0;", source);
    }

    [Fact]
    public void GameBoy_runner_uses_player_spritesheet_for_playable_scene()
    {
        var source = RunnerSample.FlattenedSource();

        Assert.Contains("""Sprite.Asset(mario_player, "assets/mario-player.png", 18, 32);""", source);
        Assert.Contains("Animation.Clip(run, 1, 48, 48, 48);", source);
        Assert.DoesNotContain("Animation.Clip(enemy_walk", source);
        Assert.DoesNotContain("sprites_clear();", source);
        Assert.Contains("displayFrame = grounded switch", source);
        Assert.Contains("false => 4", source);
        Assert.Contains("_ => Animation.Frame(run, animTick)", source);
        Assert.Contains("false => 0", source);
        Assert.Contains("bool displayFlipX;", source);
        Assert.Contains("player.displayFlipX = true;", source);
        Assert.Contains("player.displayFlipX = false;", source);
        Assert.DoesNotContain("displayFlags = 32;", source);
        Assert.Contains("Sprite.Draw(mario_player, screenX, screenY, player.displayFrame, player.displayFlipX, 0);", source);
        Assert.DoesNotContain("enemy_slug", source);
        Assert.Equal(1, CountOccurrences(source, "Sprite.Draw("));

        var rom = GameBoyRomCompiler.CompileSource(RunnerSample.CompiledSource(), RunnerSample.Directory);
        AssertRunnerMbc1Rom(rom);
    }

    [Fact]
    public void GameBoy_runner_sprite_asset_preserves_portable_metadata()
    {
        var source = RunnerSample.FlattenedSource();

        var program = CompileVideoProgram(RunnerSample.CompiledSource(), RunnerSample.Directory);
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
        var source = RunnerSample.FlattenedSource();

        Assert.Contains("Palette.Background(0, 0, 1, 2, 3);", source);
        Assert.Contains("Palette.Sprite(0, 0, 0, 1, 3);", source);
        Assert.DoesNotContain("Palette.Set(", source);
        Assert.DoesNotContain("ObjectPalette.Set(", source);
        Assert.DoesNotContain("ObjectPalette.Set(1, 1);", source);
        Assert.DoesNotContain("ObjectPalette.Set(2, 2);", source);
        Assert.DoesNotContain("ObjectPalette.Set(1, 2);", source);
        Assert.DoesNotContain("ObjectPalette.Set(2, 3);", source);

        var rom = GameBoyRomCompiler.CompileSource(RunnerSample.CompiledSource(), RunnerSample.Directory);

        AssertRunnerMbc1Rom(rom);
        Assert.True(ContainsSequence(rom, [0x3E, 0xD0, 0xE0, 0x48]), "Runner should map sprite tones to OBP0 as 0, 0, 1, 3.");
    }

    [Fact]
    public void GameBoy_runner_keeps_layout_readable_and_gives_hit_feedback()
    {
        var source = RunnerSample.FlattenedSource();

        Assert.DoesNotContain("view.screenX", source);
        Assert.Contains("Player.FootOffset", source);
        Assert.Contains("CollisionProbe.LandingSearchHeight", source);
        Assert.Contains("CollisionFlag.Landable", source);
        Assert.DoesNotContain("EnemyState", source);
        Assert.DoesNotContain("hitFlashTicks", source);

        var program = CompileVideoProgram(RunnerSample.CompiledSource(), RunnerSample.Directory);
        var worldMap = Assert.IsType<WorldMap2D>(program.WorldMap);
        var worldTiles = Assert.IsType<WorldTileGrid>(program.WorldTileGrid);
        Assert.NotEqual(0, worldTiles.TileIdAt(0, 36));
        Assert.NotEqual(0, worldTiles.TileIdAt(16, 34));
        Assert.NotEqual(0, worldTiles.TileIdAt(4, 38));
        Assert.Equal(WorldTileFlags.Solid, worldMap.FlagsAt(30, 30));
        Assert.Equal(WorldTileFlags.Solid, worldMap.FlagsAt(4, 38));
        Assert.Equal(WorldTileFlags.Empty, worldMap.FlagsAt(16, 14));
    }

    [Fact]
    public void GameBoy_runner_presents_sprites_immediately_after_vblank()
    {
        var source = RunnerSample.FlattenedSource();

        Assert.Contains("PlayerState player;", source);
        Assert.Contains("player.Reset(view);", source);

        var vblankStart = source.IndexOf("Video.WaitVBlank();", StringComparison.Ordinal);
        var inputPoll = source.IndexOf("Input.Poll();", StringComparison.Ordinal);
        var gravity = source.IndexOf("player.ApplyGravity();", StringComparison.Ordinal);
        var audioUpdate = source.IndexOf("Audio.Update();", StringComparison.Ordinal);
        var cameraApply = source.IndexOf("Camera.Apply();", StringComparison.Ordinal);
        var present = source.IndexOf("PresentFrame(player, view);", StringComparison.Ordinal);

        Assert.True(vblankStart >= 0);
        Assert.True(cameraApply > vblankStart, "Runner should commit a resident packed edge at the start of VBlank.");
        Assert.True(present > cameraApply, "Runner should preserve the previous large metasprite only on packed-edge commit frames, then refresh it after other applies.");
        Assert.True(audioUpdate > present, "Runner should tick music after timing-sensitive camera and OAM presentation work.");
        Assert.True(inputPoll > present, "Runner should finish sprite presentation before input and gameplay updates consume VBlank time.");
        Assert.True(gravity > inputPoll, "Runner should update gameplay after the VBlank presentation block.");
    }

    [Fact]
    public void GameBoy_runner_declares_and_ticks_background_music()
    {
        var baseDirectory = RunnerSample.Directory;
        var source = RunnerSample.FlattenedSource();

        Assert.Contains("""Music.Asset(runner_theme, "assets/music/runner.vgz");""", source);
        Assert.Contains("Audio.Init();", source);
        Assert.Contains("Music.Play(runner_theme);", source);

        var vblankStart = source.IndexOf("Video.WaitVBlank();", StringComparison.Ordinal);
        var audioUpdate = source.IndexOf("Audio.Update();", StringComparison.Ordinal);
        var cameraApply = source.IndexOf("Camera.Apply();", StringComparison.Ordinal);
        var draw = source.IndexOf("Sprite.Draw(mario_player, screenX, screenY, player.displayFrame, player.displayFlipX, 0);", StringComparison.Ordinal);
        Assert.True(vblankStart >= 0);
        Assert.True(audioUpdate > vblankStart, "Runner should tick the music runtime once after VBlank starts.");
        Assert.True(draw < cameraApply, "Runner should write OAM before other VBlank work can run long on real hardware.");
        Assert.True(audioUpdate > cameraApply, "Runner should tick music once per frame after timing-sensitive presentation work.");

        var operations = GameBoyRomCompiler.CollectSdkAudioOperations(RunnerSample.CompiledSource(), baseDirectory);
        Assert.Contains(operations, operation => operation is SdkAudioOperation.InitializeAudio);
        Assert.Contains(operations, operation => operation is SdkAudioOperation.PlayMusic { ThemeId: "runner_theme" });
        Assert.Contains(operations, operation => operation is SdkAudioOperation.UpdateAudio);

        var rom = GameBoyRomCompiler.CompileSource(RunnerSample.CompiledSource(), baseDirectory);
        AssertRunnerMbc1Rom(rom);
        Assert.True(ContainsSequence(rom, [0x3E, 0x80, 0xE0, 0x26]), "Runner BGM should enable NR52.");
        Assert.True(ContainsSequence(rom, [0xE2]), "Runner gbapu playback should write dynamic APU register offsets through LDH (C),A during Audio.Update.");
    }

    [Fact]
    public void World_map_generates_initial_visible_tilemap_from_map_columns()
    {
        const string source = """
                              void define_level_columns() {
                                  World.Column(0, 0, 0, 4, 5);
                                  World.Column(1, 0, 0, 4, 5);
                                  World.Column(2, 0, 5, 4, 5);
                                  World.Column(3, 0, 0, 4, 5);
                                  World.Column(4, 0, 0, 4, 5);
                                  World.Column(5, 0, 0, 4, 5);
                                  World.Column(6, 0, 0, 4, 5);
                                  World.Column(7, 0, 0, 3, 5);
                                  World.Column(8, 0, 0, 4, 5);
                                  World.Column(9, 0, 0, 4, 5);
                                  World.Column(10, 5, 0, 4, 5);
                                  World.Column(11, 0, 0, 4, 5);
                                  World.Column(12, 0, 0, 4, 5);
                                  World.Column(13, 0, 0, 0, 0);
                                  World.Column(14, 0, 0, 0, 0);
                                  World.Column(15, 0, 0, 0, 0);
                                  return;
                              }

                              void Main() {
                                  define_level_columns();
                                  World.Map(16, 11, 4);
                                  return;
                              }
                              """;

        var program = CompileVideoProgram(source);
        var worldMap = Assert.IsType<WorldMap2D>(program.WorldMap);
        var worldTileGrid = Assert.IsType<WorldTileGrid>(program.WorldTileGrid);

        Assert.Equal(16, worldMap.Width);
        Assert.Equal(4, worldMap.Height);
        Assert.Equal(3, worldTileGrid.TileIdAt(7, 2));
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
                              void DefineWorld() {
                                  World.Column(0, 0, 0, 4, 5);
                                  World.Column(1, 0, 0, 4, 5);
                                  World.Column(2, 0, 5, 4, 5);
                                  World.Column(3, 0, 0, 4, 5);
                                  World.Column(4, 0, 0, 4, 5);
                                  World.Column(5, 0, 0, 4, 5);
                                  World.Column(6, 0, 0, 4, 5);
                                  World.Column(7, 0, 0, 3, 5);
                                  World.Column(8, 0, 0, 4, 5);
                                  World.Column(9, 0, 0, 4, 5);
                                  World.Column(10, 5, 0, 4, 5);
                                  World.Column(11, 0, 0, 4, 5);
                                  World.Column(12, 0, 0, 4, 5);
                                  World.Column(13, 0, 0, 0, 0);
                                  World.Column(14, 0, 0, 0, 0);
                                  World.Column(15, 0, 0, 0, 0);
                                  return;
                              }

                              void Main() {
                                  DefineWorld();
                                  World.Map(16, 11, 4);
                                  Camera.Init(16, 11, 4);
                                  return;
                              }
                              """;

        var program = CompileVideoProgram(source);
        var worldMap = Assert.IsType<WorldMap2D>(program.WorldMap);
        var worldTileGrid = Assert.IsType<WorldTileGrid>(program.WorldTileGrid);

        Assert.Equal(4, program.MapColumnHeight);
        Assert.Equal(3, worldTileGrid.TileIdAt(7, 2));
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
                              void Main() {
                                  World.Load("level.tmj");
                                  Camera.Init(3, 5, 2);
                                  return;
                              }
                              """;

        var program = CompileVideoProgram(source, directory);
        var worldMap = Assert.IsType<WorldMap2D>(program.WorldMap);
        var worldTileGrid = Assert.IsType<WorldTileGrid>(program.WorldTileGrid);

        Assert.Equal(3, worldMap.Width);
        Assert.Equal(2, worldMap.Height);
        Assert.Equal(6, worldTileGrid.TileIdAt(0, 0));
        Assert.Equal(7, worldTileGrid.TileIdAt(2, 0));
        Assert.Equal(8, worldTileGrid.TileIdAt(0, 1));
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
                              void Main() {
                                  World.Load("level.tmj");
                                  Camera.Init(3, 2, 2);
                                  return;
                              }
                              """;

        var program = CompileVideoProgram(source, directory);
        var worldMap = Assert.IsType<WorldMap2D>(program.WorldMap);
        var worldTileGrid = Assert.IsType<WorldTileGrid>(program.WorldTileGrid);

        Assert.Equal(8, worldTileGrid.TileIdAt(0, 0));
        Assert.Equal(8, worldTileGrid.TileIdAt(1, 0));
        Assert.Equal(8, worldTileGrid.TileIdAt(2, 0));
        Assert.Equal(WorldTileFlags.Empty, worldMap.FlagsAt(0, 0));
        Assert.Equal(7, worldTileGrid.TileIdAt(0, 1));
        Assert.Equal(WorldTileFlags.Solid, worldMap.FlagsAt(0, 1));
        Assert.Equal(0, worldTileGrid.TileIdAt(1, 1));
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
                              void Main() {
                                  World.Load("level.tmj");
                                  Camera.Init(3, 2, 2);
                                  while (true) {
                                      Video.WaitVBlank();
                                      Camera.Apply();
                                      Camera.SetPosition(1, 0);
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source, directory);

        // The world band starts at GB row 2 (0x9840). The background region above the band
        // (GB rows 0..1) must also stream when scrolling, writing GB row 0 at 0x9800. A crossing
        // queues the edge column for deferred streaming; both directions feed the same pending
        // slot, so Camera.Apply commits one column stream into the background tilemap.
        Assert.True(
            ContainsSequence(rom, [0xFA, 0xE4, 0xC0, 0xEA, 0x1A, 0xC1]),
            "a rightward crossing should queue the right background edge column for deferred streaming.");
        Assert.True(
            ContainsSequence(rom, [0xFA, 0xE5, 0xC0, 0xEA, 0x1A, 0xC1]),
            "a leftward crossing should queue the left background edge column for deferred streaming.");
        Assert.True(
            ContainsSequence(rom, [0xFA, 0x1A, 0xC1, 0x6F, 0x26, 0x98, 0x0E]),
            "Camera.Apply should stream the queued background row above the band into GB row 0 (0x9800).");

        // The deferred commit replaces the old per-crossing extra WaitVBlank: streaming now happens
        // only from Camera.Apply, at the top of the frame inside VBlank, so a scrolling frame costs a
        // single VBlank (and a single Audio.Update tick). The move steps must no longer busy-wait on LY
        // themselves; the streaming is gated by the queued-kind dispatch in Camera.Apply instead.
        Assert.True(
            ContainsSequence(rom, [0xFA, 0x19, 0xC1, 0xFE, 0x00, 0xCA]),
            "Camera.Apply should dispatch the deferred stream on the queued kind.");
    }

    [Fact]
    public void Camera_horizontal_streaming_fills_configured_visible_world_rows()
    {
        const string source = """
                              void DefineWorld() {
                                  World.Column(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14);
                                  World.Column(1, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28);
                                  return;
                              }

                              void Main() {
                                  DefineWorld();
                                  World.Map(2, 9, 14);
                                  Camera.Init(2, 9, 14);
                                  while (true) {
                                      Video.WaitVBlank();
                                      Camera.Apply();
                                      Camera.SetPosition(8, 0);
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.True(
            ContainsSequence(rom, [0x0E, 0x0E, 0x1A, 0x77]),
            "camera horizontal streaming should cover the configured visible world rows when fewer than the screen-plus-partial edge are configured.");
        Assert.True(
            ContainsSequence(rom, [0x1A, 0x77, 0x7B, 0xC6, 0x02, 0x5F]),
            "camera horizontal streaming should advance the source pointer by the map width while filling the column.");
        Assert.True(
            ContainsSequence(rom, [0x7D, 0xC6, 0x20, 0x6F]),
            "camera horizontal streaming should advance the target pointer by one GB background row.");
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
                              void Main() {
                                  World.Load("level.tmj");
                                  Camera.Init(6, 4, 4);
                                  return;
                              }
                              """;

        var program = CompileVideoProgram(source, directory);
        var worldMap = Assert.IsType<WorldMap2D>(program.WorldMap);
        var worldTileGrid = Assert.IsType<WorldTileGrid>(program.WorldTileGrid);

        Assert.Equal(6, worldMap.Width);
        Assert.Equal(4, worldMap.Height);
        Assert.Equal(6, worldTileGrid.TileIdAt(0, 0));
        Assert.Equal(6, worldTileGrid.TileIdAt(1, 0));
        Assert.Equal(7, worldTileGrid.TileIdAt(2, 0));
        Assert.Equal(7, worldTileGrid.TileIdAt(3, 0));
        Assert.Equal(6, worldTileGrid.TileIdAt(0, 1));
        Assert.Equal(6, worldTileGrid.TileIdAt(1, 1));
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
    public void World_pack_matches_raw_game_boy_import_for_a_shifted_composed_slice()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RetroSharp.GameBoy.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        WriteTiledTilesheetPng(directory, "tiles.png", 8, 8, 1, 3);
        File.WriteAllText(
            Path.Combine(directory, "level.tsx"),
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <tileset version="1.10" tiledversion="1.12.2" name="Level" tilewidth="8" tileheight="8" tilecount="2" columns="2">
             <image source="tiles.png" width="16" height="8"/>
            </tileset>
            """);
        File.WriteAllText(
            Path.Combine(directory, "level.tmj"),
            """
            {
              "type": "map",
              "orientation": "orthogonal",
              "infinite": false,
              "width": 3,
              "height": 3,
              "tilewidth": 8,
              "tileheight": 8,
              "properties": [
                { "name": "retrosharpStreamY", "type": "int", "value": 0 },
                { "name": "retrosharpWorldY", "type": "int", "value": 1 },
                { "name": "retrosharpWorldHeight", "type": "int", "value": 2 }
              ],
              "tilesets": [
                { "firstgid": 1, "source": "level.tsx" }
              ],
              "layers": [
                { "type": "tilelayer", "name": "background", "width": 3, "height": 3, "data": [2, 2, 2, 1, 1, 1, 1, 1, 1] },
                { "type": "tilelayer", "name": "world", "width": 3, "height": 3, "data": [0, 0, 0, 2, 0, 2, 0, 2, 0] },
                { "type": "tilelayer", "name": "collision", "width": 3, "height": 3, "data": [0, 0, 0, 1, 0, 2, 4, 0, 1] }
              ]
            }
            """);
        var path = Path.Combine(directory, "level.tmj");
        var firstGeneratedTile = GameBoyVideoProgram.FirstGeneratedBackgroundTile;
        var raw = GameBoyTiledMapImporter.Load(path, firstGeneratedTile);

        var compiled = GameBoyTiledMapImporter.CompileWorldPack(path, firstGeneratedTile);
        var decoded = WorldPackSerializer.Deserialize(compiled.SerializedBytes);
        var decodedTiles = decoded.ToWorldTileGrid(cell => cell.Span[0]);

        Assert.Equal(3, decoded.Descriptor.HardwareWidth);
        Assert.Equal(2, decoded.Descriptor.HardwareHeight);
        Assert.NotEqual(raw.WorldTileIds[0], raw.WorldTileIds[1]);
        Assert.Equal(raw.GeneratedTileData, compiled.GeneratedTileData);
        for (var index = 0; index < raw.WorldTileIds.Length; index++)
        {
            var x = index % raw.Width;
            var y = index / raw.Width;
            Assert.Equal(raw.WorldTileIds[index], decodedTiles.TileIdAt(x, y));
            Assert.Equal(raw.WorldFlags[index], decoded.CollisionAt(x, y));
        }
    }

    [Fact]
    public void World_load_uses_game_boy_tileset_png_variant_when_present()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RetroSharp.GameBoy.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        WriteTiledTilesheetPng(directory, "tiles.png", 8, 8, 3);
        WriteTiledTilesheetPng(directory, "tiles.gb.png", 8, 8, 0);
        File.WriteAllText(
            Path.Combine(directory, "level.tsx"),
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <tileset version="1.10" tiledversion="1.12.2" name="Level" tilewidth="8" tileheight="8" tilecount="1" columns="1">
             <image source="tiles.png" width="8" height="8"/>
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
              "tilewidth": 8,
              "tileheight": 8,
              "infinite": false,
              "properties": [
                { "name": "retrosharpStreamY", "type": "int", "value": 0 },
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
                              void Main() {
                                  World.Load("level.tmj");
                                  Camera.Init(1, 0, 1);
                                  return;
                              }
                              """;

        var program = CompileVideoProgram(source, directory);
        var worldMap = Assert.IsType<WorldMap2D>(program.WorldMap);
        var worldTileGrid = Assert.IsType<WorldTileGrid>(program.WorldTileGrid);
        var packed = GameBoyTiledMapImporter.CompileWorldPack(
            Path.Combine(directory, "level.tmj"),
            GameBoyVideoProgram.FirstGeneratedBackgroundTile);
        var packedTileGrid = WorldPackSerializer.Deserialize(packed.SerializedBytes).ToWorldTileGrid(cell => cell.Span[0]);

        Assert.Equal(0, worldTileGrid.TileIdAt(0, 0));
        Assert.Equal(0, packedTileGrid.TileIdAt(0, 0));
        Assert.Empty(packed.GeneratedTileData);
        Assert.Equal(0, program.TileMap[0]);
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
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(player, "player.sprite.json");
                                  World.Load("level.tmj");
                                  Sprite.Draw(player, 8, 8, 0);
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
                              void DefineWorld() {
                                  World.Column(0, 0, 4);
                                  World.Column(1, 3, 5);
                                  World.Flags(0, 0, 1);
                                  World.Flags(1, 2, 1);
                                  return;
                              }

                              void Main() {
                                  DefineWorld();
                                  World.Map(2, 11, 2);
                                  Camera.Init(2, 11, 2);
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
                              void DefineWorld() {
                                  World.Column(0, 0, 4);
                                  World.Column(1, 3, 5);
                                  World.Flags(0, 0, 1);
                                  World.Flags(1, 2, 1);
                                  return;
                              }

                              void Main() {
                                  DefineWorld();
                                  World.Map(2, 11, 2);
                                  i16 worldX = 8;
                                  i16 worldY = 0;
                                  i16 hazard = 0;
                                  i16 solid = 0;
                                  i16 empty = 0;
                                  if (World.TileFlagsAt(worldX, worldY) != 0) {
                                      hazard = 1;
                                  }
                                  if (World.TileFlagsAt(0, 8) != 0) {
                                      solid = 1;
                                  }
                                  if (World.TileFlagsAt(0, 0) != 0) {
                                      empty = 3;
                                  }
                                  if (World.TileFlagsAt(16, 0) != 0) {
                                      empty = 1;
                                  }
                                  if (World.TileFlagsAt(0, 16) != 0) {
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
                              void DefineWorld() {
                                  World.Column(0, 0, 4, 0);
                                  World.Column(1, 3, 5, 0);
                                  World.Column(2, 0, 0, 6);
                                  World.Flags(0, 0, 1, 0);
                                  World.Flags(1, 2, 1, 0);
                                  World.Flags(2, 0, 0, 4);
                                  return;
                              }

                              void Main() {
                                  DefineWorld();
                                  World.Map(3, 11, 3);
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
                              void DefineWorld() {
                                  World.Column(0, 0, 4);
                                  World.Column(1, 0, 5);
                                  World.Column(2, 0, 6);
                                  World.Column(3, 0, 7);
                                  World.Flags(0, 0, 1);
                                  World.Flags(1, 0, 0);
                                  World.Flags(2, 0, 1);
                                  World.Flags(3, 0, 0);
                                  return;
                              }

                              void Main() {
                                  DefineWorld();
                                  World.Map(4, 11, 2);
                                  Camera.Init(4, 11, 2);
                                  i16 y = 8;
                                  i16 hit = Camera.AabbTiles(72, y, 18, 8, 1);
                                  return;
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(
            ContainsSequence(rom, [0xFA, 0xE2, 0xC0, 0xC6, 0x48, 0xCB, 0x3F, 0xCB, 0x3F, 0xCB, 0x3F, 0x47, 0xFA, 0xE3, 0xC0, 0x80]),
            "Camera.AabbTiles should derive the source column from camera fine X plus the visible screen X.");
        Assert.True(
            ContainsSequence(rom, [0xFA, 0xE2, 0xC0, 0xC6, 0x59, 0xCB, 0x3F, 0xCB, 0x3F, 0xCB, 0x3F]),
            "Camera.AabbTiles should include the far edge of the sprite-width span.");
        Assert.True(ContainsSequence(rom, [0xE6, 0x01, 0xFE, 0x00, 0xC2]), "Camera.AabbTiles should mask requested collision flags.");
    }

    [Fact]
    public void Camera_aabb_hit_top_returns_top_edge_of_first_overlapped_tile()
    {
        const string source = """
                              void DefineWorld() {
                                  World.Column(0, 0, 4);
                                  World.Column(1, 0, 4);
                                  World.Column(2, 0, 4);
                                  World.Flags(0, 0, 1);
                                  World.Flags(1, 0, 1);
                                  World.Flags(2, 0, 1);
                                  World.Map(3, 11, 2);
                                  Camera.Init(3, 11, 2);
                              }

                              void Main() {
                                  DefineWorld();
                                  i16 footY = 16;
                                  i16 hitTop = Camera.AabbHitTop(72, footY - 8, 16, 16, 1);
                                  return;
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x21, 0xFF, 0xFF]), "Camera.AabbHitTop should return -1 as FF FF when no overlapped tile has the requested flags.");
        Assert.True(ContainsSequence(rom, [0xCE, 0xFF, 0x67, 0xFA]), "Camera.AabbHitTop should propagate the signed search offset into the high result byte.");
    }

    [Fact]
    public void World_camera_hit_top_returns_y_304_and_minus_one_as_complete_words_on_game_boy()
    {
        var source = CollisionHitContractSource(
            height: 40,
            solidRow: 38,
            body: """
                      i16 footWorldY = 304;
                      i16 hitTop = Camera.AabbHitTop(0, footWorldY - 4, 8, 12, 1);
                      i16 noHit = Camera.AabbHitTop(0, footWorldY - 4, 8, 12, 4);
                      while (true) {
                          Video.WaitVBlank();
                      }
                  """);

        var cpu = new GameBoyTestCpu(GameBoyRomCompiler.CompileSource(source));
        cpu.RunFrames(2);

        Assert.Equal(0x30, cpu.Wram(0xC002));
        Assert.Equal(0x01, cpu.Wram(0xC003));
        Assert.Equal(0xFF, cpu.Wram(0xC004));
        Assert.Equal(0xFF, cpu.Wram(0xC005));
    }

    [Fact]
    public void Screen_camera_hit_top_keeps_byte_semantics_and_zero_extends_word_results_on_game_boy()
    {
        var source = CollisionHitContractSource(
            height: 40,
            solidRow: 38,
            body: """
                      u8 legacyNoHit = Camera.ScreenAabbHitTop(0, 0, 8, 8, 4);
                      i16 completeNoHit = Camera.ScreenAabbHitTop(0, 0, 8, 8, 4);
                      while (true) {
                          Video.WaitVBlank();
                      }
                  """);

        var cpu = new GameBoyTestCpu(GameBoyRomCompiler.CompileSource(source));
        cpu.RunFrames(2);

        Assert.Equal(0xFF, cpu.Wram(0xC000));
        Assert.Equal(0xFF, cpu.Wram(0xC001));
        Assert.Equal(0x00, cpu.Wram(0xC002));
    }

    [Fact]
    public void World_camera_hit_top_rejects_unsafe_byte_narrowing_on_tall_game_boy_world()
    {
        var source = CollisionHitContractSource(
            height: 40,
            solidRow: 38,
            body: "u8 hitTop = Camera.AabbHitTop(0, 300, 8, 12, 1);");

        var exception = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource(source));

        Assert.Equal(
            "World hit-top cannot be stored in byte destination type 'u8' because the active world is 40 hardware rows tall; use an i16 local and compare it with -1.",
            exception.Message);
    }

    [Fact]
    public void World_camera_hit_top_keeps_legacy_byte_destination_for_32_row_game_boy_world()
    {
        var source = CollisionHitContractSource(
            height: 32,
            solidRow: 31,
            body: "u8 noHit = Camera.AabbHitTop(0, 0, 8, 8, 4);");

        Assert.Equal(32768, GameBoyRomCompiler.CompileSource(source).Length);
    }

    [Fact]
    public void GameBoy_runner_bounces_player_down_when_head_hits_solid_ceiling()
    {
        var source = RunnerSample.FlattenedSource();

        Assert.Contains("CeilingProbeTopOffset = 28", source);
        Assert.Contains("CeilingProbeHeight = 4", source);
        Assert.Contains("BounceVelocity = 32", source);
        Assert.Contains("inline void BounceDown()", source);
        Assert.Contains("velocityY = Jump.BounceVelocity;", source);
        Assert.Contains("inline void ResolveCeilingHit(PlayerState player, Pixel screenX, Pixel footWorldY)", source);
        Assert.Contains("i16 headProbeY = footWorldY - CollisionProbe.CeilingProbeTopOffset;", source);
        Assert.Contains("Camera.AabbTiles(screenX, headProbeY, Sprite.Width(mario_player), CollisionProbe.CeilingProbeHeight, CollisionFlag.Solid)", source);
        Assert.Contains("player.BounceDown();", source);
        Assert.Contains("frame.ResolveCeilingHit(player, screenX, footWorldY);", source);

        var ceilingStart = source.IndexOf("inline void ResolveCeilingHit", StringComparison.Ordinal);
        Assert.True(ceilingStart >= 0);
        var ceilingEnd = source.IndexOf("inline void ResolveReset", ceilingStart, StringComparison.Ordinal);
        Assert.True(ceilingEnd > ceilingStart);
        var ceilingBlock = source[ceilingStart..ceilingEnd];
        Assert.Contains("player.velocityY < 0", ceilingBlock);

        var landingCall = source.IndexOf("frame.ResolveLanding(player, screenX, previousFootWorldY, footWorldY);", StringComparison.Ordinal);
        var ceilingCall = source.IndexOf("frame.ResolveCeilingHit(player, screenX, footWorldY);", StringComparison.Ordinal);
        var jumpInputCall = source.IndexOf("player.HandleJumpInput(view.speed);", StringComparison.Ordinal);
        Assert.True(ceilingCall > landingCall, "Ceiling resolution should run after solid landing resolution.");
        Assert.True(jumpInputCall > ceilingCall, "Ceiling resolution should clear the jump before jump input is consumed.");

        var rom = GameBoyRomCompiler.CompileSource(RunnerSample.CompiledSource(), RunnerSample.Directory);
        AssertRunnerMbc1Rom(rom);
    }

    [Fact]
    public void GameBoy_runner_uses_dynamic_world_y_for_tiled_solid_landing()
    {
        var source = RunnerSample.FlattenedSource();

        Assert.Contains("LandingSearchTopOffset = 3", source);
        Assert.Contains("LandingSearchHeight = 9", source);
        Assert.Contains("inline void ResolveLanding(PlayerState player, Pixel screenX, Pixel previousFootWorldY, Pixel footWorldY)", source);
        Assert.Contains("i16 previousFootWorldY = player.y + Player.FootOffset;", source);
        Assert.Contains("i16 footWorldY = player.y + Player.FootOffset;", source);
        var previousFootCapture = source.IndexOf("i16 previousFootWorldY = player.y + Player.FootOffset;", StringComparison.Ordinal);
        var gravityCall = source.IndexOf("player.ApplyGravity();", previousFootCapture, StringComparison.Ordinal);
        var currentFootCapture = source.IndexOf("i16 footWorldY = player.y + Player.FootOffset;", gravityCall, StringComparison.Ordinal);
        Assert.True(gravityCall > previousFootCapture);
        Assert.True(currentFootCapture > gravityCall);
        Assert.Contains("i16 footTile = Camera.AabbHitTop(screenX, footWorldY - CollisionProbe.LandingSearchTopOffset, Sprite.Width(mario_player), CollisionProbe.LandingSearchHeight, CollisionFlag.Landable);", source);
        Assert.Contains("if (footTile >= 0 && previousFootWorldY <= footTile && footWorldY >= footTile)", source);
        Assert.Contains("player.Land(footTile - Player.FootOffset);", source);
        Assert.DoesNotContain("CollisionProbe.TileSize2", source);
        Assert.DoesNotContain("CollisionProbe.TileSize3", source);
        Assert.DoesNotContain("CollisionProbe.TileSize4", source);
        Assert.DoesNotContain("landedWorldY", source);
        Assert.Contains("frame.ResolveLanding(player, screenX, previousFootWorldY, footWorldY);", source);
        Assert.DoesNotContain("collision_aabb_tiles(footLeftX, 0", source);
        Assert.DoesNotContain("playerWorldX", source);
        Assert.DoesNotContain("WrapWorldX", source);
        Assert.DoesNotContain("CollisionProbe.GroundY", source);
    }

    [Fact]
    public void GameBoy_runner_blocks_horizontal_camera_motion_against_tall_solids()
    {
        var source = RunnerSample.FlattenedSource();

        Assert.Contains("LeftWallProbeOffset = 1", source);
        Assert.Contains("RightWallProbeOffset = 1", source);
        Assert.Contains("WallProbeHeight = 8", source);
        Assert.Contains("inline void HandleHorizontalInput(PlayerState player, Pixel footWorldY)", source);
        Assert.Contains("i16 wallProbeY = footWorldY - CollisionProbe.WallProbeHeight;", source);
        Assert.Contains("let rightProbeX = screenX + CollisionProbe.RightWallProbeOffset;", source);
        Assert.Contains("let leftProbeX = screenX - CollisionProbe.LeftWallProbeOffset;", source);
        Assert.Contains("Camera.AabbTiles(rightProbeX, wallProbeY, Sprite.Width(mario_player), CollisionProbe.WallProbeHeight, CollisionFlag.Solid) == 0", source);
        Assert.Contains("Camera.AabbTiles(leftProbeX, wallProbeY, Sprite.Width(mario_player), CollisionProbe.WallProbeHeight, CollisionFlag.Solid) == 0", source);
        Assert.Contains("i16 movementFootWorldY = player.y + Player.FootOffset;", source);
        Assert.Contains("view.HandleHorizontalInput(player, movementFootWorldY);", source);
        Assert.DoesNotContain("view.HandleHorizontalInput(player);", source);

        var rom = GameBoyRomCompiler.CompileSource(RunnerSample.CompiledSource(), RunnerSample.Directory);
        AssertRunnerMbc1Rom(rom);
    }

    [Fact]
    public void GameBoy_runner_uses_horizontal_speed_model_with_instant_turn()
    {
        var source = RunnerSample.FlattenedSource();

        Assert.Contains("enum Direction", source);
        Assert.Contains("Walk = 10", source);
        Assert.Contains("RunMax = 16", source);
        Assert.Contains("Subpixel = 8", source);
        Assert.Contains("RunAcceleration = 2", source);
        Assert.Contains("Friction = 3", source);
        Assert.Contains("MaxSteps = 2", source);

        var cameraStart = source.IndexOf("class CameraState", StringComparison.Ordinal);
        var frameStart = source.IndexOf("class FrameState", StringComparison.Ordinal);
        Assert.True(cameraStart >= 0);
        Assert.True(frameStart > cameraStart);
        var cameraBlock = source[cameraStart..frameStart];

        Assert.Contains("Pixel speed;", cameraBlock);
        Assert.Contains("Pixel direction;", cameraBlock);
        Assert.Contains("Pixel movementRemainder;", cameraBlock);
        Assert.Contains("inline void UpdateIntent(Pixel desiredDirection, bool grounded)", cameraBlock);
        Assert.Contains("if (direction == Direction.Right)", cameraBlock);
        Assert.Contains("if (direction == Direction.Left)", cameraBlock);
        Assert.Contains("StartDirection(Direction.Right);", cameraBlock);
        Assert.Contains("StartDirection(Direction.Left);", cameraBlock);
        Assert.Contains("speed = MotionSpeed.Walk;", cameraBlock);
        Assert.Contains("movementRemainder += speed;", cameraBlock);
        Assert.Contains("void ApplyMotionStep(PlayerState player, Pixel wallProbeY, Pixel collisionCameraX)", cameraBlock);
        Assert.Contains("movementRemainder -= MotionSpeed.Subpixel;", cameraBlock);
        Assert.Contains("MoveRightOnePixel(player, wallProbeY, collisionCameraX);", cameraBlock);
        Assert.Contains("MoveLeftOnePixel(player, wallProbeY, collisionCameraX);", cameraBlock);

        // The corrected model turns instantly: no skid/turn-friction that would let the actor walk backward.
        Assert.DoesNotContain("ApplyHorizontalIntent", cameraBlock);
        Assert.DoesNotContain("ApplyTurnFriction", cameraBlock);
        Assert.DoesNotContain("ApplyRightIntent", cameraBlock);
        Assert.DoesNotContain("ApplyLeftIntent", cameraBlock);

        var rom = GameBoyRomCompiler.CompileSource(RunnerSample.CompiledSource(), RunnerSample.Directory);
        AssertRunnerMbc1Rom(rom);
    }

    [Fact]
    public void GameBoy_runner_builds_run_speed_from_b_only_while_grounded()
    {
        var source = RunnerSample.FlattenedSource();

        Assert.Contains("RunMax = 16", source);
        Assert.Contains("RunAcceleration = 2", source);

        var cameraStart = source.IndexOf("class CameraState", StringComparison.Ordinal);
        var frameStart = source.IndexOf("class FrameState", StringComparison.Ordinal);
        Assert.True(cameraStart >= 0);
        Assert.True(frameStart > cameraStart);
        var cameraBlock = source[cameraStart..frameStart];

        Assert.Contains("inline void HoldDirection(bool grounded)", cameraBlock);
        Assert.Contains("inline void AccelerateRun()", cameraBlock);
        Assert.Contains("inline void DecelerateToWalk()", cameraBlock);
        Assert.Contains("inline void ApplyFriction()", cameraBlock);
        Assert.Contains("if (Input.IsDown(Button.B))", cameraBlock);
        Assert.Contains("AccelerateRun();", cameraBlock);
        Assert.Contains("DecelerateToWalk();", cameraBlock);
        Assert.Contains("speed += MotionSpeed.RunAcceleration;", cameraBlock);
        Assert.Contains("speed -= MotionSpeed.Friction;", cameraBlock);
        Assert.Contains("direction = Direction.None;", cameraBlock);

        // Run speed only builds while Mario has traction: acceleration and ground friction are gated by
        // grounded, so holding B in the air preserves momentum instead of building extra speed.
        Assert.Contains("HoldDirection(grounded);", cameraBlock);
        Assert.Contains("UpdateIntent(desiredDirection, player.grounded);", cameraBlock);
        Assert.Contains("if (grounded)\n        {\n            if (Input.IsDown(Button.B))\n            {", cameraBlock);
        Assert.DoesNotContain("ApplyGroundAcceleration", cameraBlock);

        var motionStart = cameraBlock.IndexOf("inline void ApplyMotion(PlayerState player, Pixel wallProbeY)", StringComparison.Ordinal);
        Assert.True(motionStart >= 0);
        var horizontalStart = cameraBlock.IndexOf("inline void HandleHorizontalInput", motionStart, StringComparison.Ordinal);
        Assert.True(horizontalStart > motionStart);
        var motionBlock = cameraBlock[motionStart..horizontalStart];
        Assert.Contains("while (steps < MotionSpeed.MaxSteps)", motionBlock);
        Assert.Contains("let collisionCameraX = x;", motionBlock);
        Assert.Equal(2, CountOccurrences(cameraBlock, "let screenX = player.x - collisionCameraX;"));
        Assert.Equal(1, CountOccurrences(motionBlock, "ApplyMotionStep(player, wallProbeY, collisionCameraX);"));
        Assert.DoesNotContain("while (movementRemainder >= MotionSpeed.Subpixel)", motionBlock);

        // Regression guard: every collision substep projects against the camera state from the start
        // of motion while source camera state advances per pixel and syncs once after both probes.
        Assert.Contains("player.x += 1;", cameraBlock);
        Assert.Contains("x += 1;", cameraBlock);
        Assert.Contains("player.x -= 1;", cameraBlock);
        Assert.Contains("x -= 1;", cameraBlock);
        Assert.DoesNotContain("Camera.SetPosition", motionBlock);
        Assert.DoesNotContain("view.ApplyFramePosition();", source);
        Assert.Equal(1, CountOccurrences(source, "view.ApplyPosition();"));

        var rom = GameBoyRomCompiler.CompileSource(RunnerSample.CompiledSource(), RunnerSample.Directory);
        AssertRunnerMbc1Rom(rom);
    }

    [Fact]
    public void GameBoy_runner_uses_smb3_4_4_speed_scaled_variable_jump_height()
    {
        var source = RunnerSample.FlattenedSource();

        Assert.Contains("StandingVelocity = -56", source);
        Assert.Contains("WalkingVelocity = -58", source);
        Assert.Contains("RunningVelocity = -60", source);
        Assert.Contains("PSpeedVelocity = -64", source);
        Assert.Contains("HeldGravityThreshold = -32", source);
        Assert.Contains("HeldGravity = 1", source);
        Assert.Contains("ReleasedGravity = 5", source);
        Assert.Contains("TerminalVelocity = 69", source);
        Assert.Contains("Subpixel = 16", source);
        Assert.Contains("Pixel verticalSubpixel;", source);
        Assert.DoesNotContain("heldGravityTicks", source);

        var gravityStart = source.IndexOf("inline void ApplyGravity()", StringComparison.Ordinal);
        var landStart = source.IndexOf("inline void Land(Pixel targetY)", StringComparison.Ordinal);
        Assert.True(gravityStart >= 0);
        Assert.True(landStart > gravityStart);
        var gravityBlock = source[gravityStart..landStart];
        Assert.Contains("if (jumping && Input.IsDown(Button.A) && velocityY < Jump.HeldGravityThreshold)", gravityBlock);
        Assert.Contains("velocityY += Jump.HeldGravity;", gravityBlock);
        Assert.Contains("velocityY += Jump.ReleasedGravity;", gravityBlock);
        Assert.Contains("if (velocityY > Jump.TerminalVelocity)", gravityBlock);
        Assert.Contains("Pixel verticalMotion = verticalSubpixel + velocityY;", gravityBlock);
        Assert.Contains("while (verticalMotion < 0)", gravityBlock);
        Assert.Contains("while (verticalMotion >= Jump.Subpixel)", gravityBlock);
        Assert.Contains("if (!grounded)", gravityBlock);
        Assert.Contains("verticalSubpixel = verticalMotion;", gravityBlock);

        var jumpStart = source.IndexOf("inline void StartJump(Pixel horizontalSpeed)", StringComparison.Ordinal);
        var animationStart = source.IndexOf("inline void SelectDisplayFrame(bool moving)", StringComparison.Ordinal);
        Assert.True(jumpStart >= 0);
        Assert.True(animationStart > jumpStart);
        var jumpBlock = source[jumpStart..animationStart];
        Assert.Contains("velocityY = Jump.StandingVelocity;", jumpBlock);
        Assert.Contains("if (horizontalSpeed > 0)", jumpBlock);
        Assert.Contains("velocityY = Jump.WalkingVelocity;", jumpBlock);
        Assert.Contains("if (horizontalSpeed > MotionSpeed.Walk)", jumpBlock);
        Assert.Contains("velocityY = Jump.RunningVelocity;", jumpBlock);
        Assert.Contains("if (horizontalSpeed >= MotionSpeed.RunMax)", jumpBlock);
        Assert.Contains("velocityY = Jump.PSpeedVelocity;", jumpBlock);

        Assert.Contains("inline void HandleJumpInput(Pixel horizontalSpeed)", source);
        Assert.Contains("StartJump(horizontalSpeed);", source);
        Assert.Contains("player.HandleJumpInput(view.speed);", source);
        Assert.DoesNotContain("Input.HoldTicks(Button.A)", source);

        var rom = GameBoyRomCompiler.CompileSource(RunnerSample.CompiledSource(), RunnerSample.Directory);
        AssertRunnerMbc1Rom(rom);
    }

    [Fact]
    public void GameBoy_runner_uses_actor_feet_holes_failure_tiles_and_reset_state()
    {
        var source = RunnerSample.FlattenedSource();

        Assert.DoesNotContain("Pixel footTile;", source);
        Assert.Contains("i16 footWorldY = player.y + Player.FootOffset;", source);
        Assert.Contains("bool resetRequested;", source);

        Assert.Contains("""World.Load("assets/maps/stage1.tmj");""", source);
        Assert.DoesNotContain("World.Column(", source);
        Assert.DoesNotContain("World.Flags(", source);
        Assert.DoesNotContain("World.Map(", source);
        Assert.DoesNotContain("World.Column(", source);
        Assert.DoesNotContain("inline pure Pixel WrapWorldX(Pixel x) => x;", source);
        Assert.DoesNotContain("playerWorldX", source);
        Assert.Contains("i16 footWorldY = player.y + Player.FootOffset;", source);
        Assert.DoesNotContain("if (velocityY < 0)", source);
        Assert.DoesNotContain("y = 0;", source);
        Assert.Contains("player.velocityY >= 0", source);
        Assert.Contains("i16 footTile = Camera.AabbHitTop(screenX, footWorldY - CollisionProbe.LandingSearchTopOffset, Sprite.Width(mario_player), CollisionProbe.LandingSearchHeight, CollisionFlag.Landable);", source);
        Assert.Contains("player.Land(footTile - Player.FootOffset);", source);
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
        Assert.Contains("if (!player.grounded)", source);
        Assert.Contains("if (player.y >= Player.FallResetY)", source);
        Assert.Contains("if (resetRequested)", source);
        Assert.Contains("player.Reset(view);", source);
        Assert.Contains("velocityY = 0;", source);
        Assert.Contains("jumping = false;", source);

        var rom = GameBoyRomCompiler.CompileSource(RunnerSample.CompiledSource(), RunnerSample.Directory);
        AssertRunnerMbc1Rom(rom);
    }

    [Fact]
    public void GameBoy_runner_keeps_visible_map_collision_and_streaming_cursors_in_sync()
    {
        var source = RunnerSample.FlattenedSource();

        Assert.DoesNotContain("void draw_starting_scene()", source);
        Assert.DoesNotContain("tilemap_fill(", source);
        Assert.DoesNotContain("void DrawBackground()", source);
        Assert.DoesNotContain("Tilemap.Set(", source);
        Assert.Contains("void LoadWorld()", source);
        Assert.Contains("""World.Load("assets/maps/stage1.tmj");""", source);
        Assert.True(File.Exists(RepositoryFile("samples/runner/assets/maps/stage1.tmj")));
        Assert.True(File.Exists(RepositoryFile("samples/runner/assets/maps/stage1.tsx")));
        Assert.True(File.Exists(RepositoryFile("samples/runner/assets/maps/stage1.png")));
        Assert.DoesNotContain("World.Column(", source);
        Assert.DoesNotContain("World.Flags(", source);
        Assert.DoesNotContain("World.Map(", source);
        Assert.DoesNotContain("World.Column(", source);
        Assert.Contains("Height = 40", source);
        Assert.Contains("StreamHeight = 40", source);

        Assert.Contains("Camera.Init(Level.Width, Level.StreamY, Level.StreamHeight);", source);
        Assert.True(
            source.IndexOf("Camera.Init(Level.Width, Level.StreamY, Level.StreamHeight);", StringComparison.Ordinal) >
            source.IndexOf("LoadWorld();", StringComparison.Ordinal));
        Assert.Contains("Camera.Apply();", source);
        Assert.Contains("i16 footWorldY = player.y + Player.FootOffset;", source);
        Assert.DoesNotContain("Player.TopWrapY", source);
        Assert.DoesNotContain("if (velocityY < 0)", source);
        var solidLandingStart = source.IndexOf("inline void ResolveLanding", StringComparison.Ordinal);
        var fallStart = source.IndexOf("inline void ResolveFall", StringComparison.Ordinal);
        Assert.True(solidLandingStart >= 0);
        Assert.True(fallStart > solidLandingStart);
        var solidLandingBlock = source[solidLandingStart..fallStart];
        Assert.Contains("player.velocityY >= 0", solidLandingBlock);
        Assert.Contains("Camera.AabbHitTop(screenX, footWorldY - CollisionProbe.LandingSearchTopOffset, Sprite.Width(mario_player), CollisionProbe.LandingSearchHeight, CollisionFlag.Landable)", solidLandingBlock);
        Assert.Contains("player.Land(footTile - Player.FootOffset);", solidLandingBlock);
        Assert.Contains("player.grounded = false;", solidLandingBlock);
        Assert.DoesNotContain("camera_span_has_flags(", source);
        Assert.DoesNotContain("camera_span_has_tile(", source);
        Assert.DoesNotContain("camera_span_tile_at(", source);
        Assert.Contains("Camera.SetPosition(x, y);", source);
        Assert.DoesNotContain("view.ApplyFramePosition();", source);
        Assert.Equal(1, CountOccurrences(source, "view.ApplyPosition();"));
        Assert.DoesNotContain("camera_move_right();", source);
        Assert.DoesNotContain("camera_move_left();", source);
        Assert.DoesNotContain("i16 screenLeftColumn = 0;", source);
        Assert.DoesNotContain("i16 rightSourceColumn = 4;", source);
        Assert.DoesNotContain("i16 leftSourceColumn = 15;", source);
        Assert.DoesNotContain("i16 leftStreamColumn = 31;", source);
        Assert.DoesNotContain("map_stream_column(streamColumn, rightSourceColumn, 11, 4);", source);
        Assert.DoesNotContain("map_stream_column(leftStreamColumn, leftSourceColumn, 11, 4);", source);

        var resetStart = source.IndexOf("inline void ResolveReset(PlayerState player, CameraState view)", StringComparison.Ordinal);
        Assert.True(resetStart >= 0);
        var resetEnd = source.IndexOf("void SetupVideo()", resetStart, StringComparison.Ordinal);
        Assert.True(resetEnd > resetStart);
        var resetBlock = source[resetStart..resetEnd];
        Assert.Contains("if (resetRequested)", resetBlock);
        Assert.DoesNotContain("camera = 0;", resetBlock);
        Assert.DoesNotContain("Camera.Init(", resetBlock);
        Assert.DoesNotContain("streamColumn = 20;", resetBlock);
        Assert.DoesNotContain("screenLeftColumn = 0;", resetBlock);
        Assert.DoesNotContain("rightSourceColumn = 4;", resetBlock);
        Assert.DoesNotContain("leftSourceColumn = 15;", resetBlock);

        var program = CompileVideoProgram(RunnerSample.CompiledSource(), RunnerSample.Directory);
        var worldMap = Assert.IsType<WorldMap2D>(program.WorldMap);

        Assert.Equal(312, worldMap.Width);
        Assert.Equal(40, worldMap.Height);
        Assert.Equal(WorldTileFlags.Solid, worldMap.FlagsAt(0, 38));
        Assert.Equal(WorldTileFlags.Solid, worldMap.FlagsAt(32, 38));
        Assert.Equal(WorldTileFlags.Solid, worldMap.FlagsAt(8, 38));
        Assert.Equal(WorldTileFlags.Empty, worldMap.FlagsAt(0, 14));
        Assert.Equal(WorldTileFlags.Solid, worldMap.FlagsAt(40, 30));
        Assert.Equal(WorldTileFlags.Empty, worldMap.FlagsAt(16, 14));

        var rom = GameBoyRomCompiler.CompileSource(RunnerSample.CompiledSource(), RunnerSample.Directory);
        AssertRunnerMbc1Rom(rom);
    }

    [Fact]
    public void GameBoy_runner_uses_stage1_wide_tiled_map_for_horizontal_scroll()
    {
        var mapPath = RepositoryFile("samples/runner/assets/maps/stage1.tmj");
        var source = RunnerSample.FlattenedSource();
        var map = LogicalTiledMapImporter.Load(mapPath);

        Assert.Equal(156, map.Geometry.SourceWidth);
        Assert.Equal(20, map.Geometry.SourceHeight);
        Assert.Equal(0, map.Geometry.WorldY);
        Assert.Equal(20, map.Geometry.WorldHeight);
        Assert.Equal(0, map.Geometry.StreamY);
        Assert.Equal(312, map.Geometry.Width);
        Assert.Equal(40, map.Geometry.Height);
        Assert.Equal(0, map.Geometry.BackgroundOffsetY);

        Assert.Contains("Width = 312", source);
        Assert.Contains("Height = 40", source);
        Assert.Contains("StreamHeight = 40", source);
        Assert.Contains("PixelWidth = 2496", source);
        Assert.Contains("Camera.Init(Level.Width, Level.StreamY, Level.StreamHeight);", source);

        var program = CompileVideoProgram(RunnerSample.CompiledSource(), RunnerSample.Directory);
        var worldMap = Assert.IsType<WorldMap2D>(program.WorldMap);
        Assert.Equal(312, worldMap.Width);
        Assert.Equal(40, worldMap.Height);
        Assert.Equal(WorldTileFlags.Solid, worldMap.FlagsAt(0, 38));
        Assert.Equal(WorldTileFlags.Solid, worldMap.FlagsAt(32, 38));
        Assert.Equal(WorldTileFlags.Solid, worldMap.FlagsAt(8, 38));
        Assert.Equal(WorldTileFlags.Solid, worldMap.FlagsAt(40, 30));
    }

    [Fact]
    public void GameBoy_runner_applies_reset_before_consuming_jump_input()
    {
        var source = RunnerSample.FlattenedSource();

        var resetStart = source.IndexOf("frame.ResolveReset(player, view);", StringComparison.Ordinal);
        var jumpStart = source.IndexOf("player.HandleJumpInput(view.speed);", StringComparison.Ordinal);
        var movementStart = source.IndexOf("view.HandleHorizontalInput(player, movementFootWorldY);", StringComparison.Ordinal);

        Assert.True(resetStart >= 0);
        Assert.True(jumpStart > resetStart, "Reset should restore safe actor state before jump input is consumed.");
        Assert.True(movementStart > resetStart, "Reset should not discard horizontal movement input for the same frame.");

        var rom = GameBoyRomCompiler.CompileSource(RunnerSample.CompiledSource(), RunnerSample.Directory);
        AssertRunnerMbc1Rom(rom);
    }

    [Fact]
    public void GameBoy_runner_keeps_ground_alignment_and_reset_animation_state()
    {
        var source = RunnerSample.FlattenedSource();

        Assert.Contains("StartY = 273", source);
        Assert.Contains("FootOffset = 31", source);
        Assert.DoesNotContain("TopWrapY", source);
        Assert.Contains("y = Player.StartY;", source);
        Assert.Equal(1, CountOccurrences(source, "y = Player.StartY;"));
        Assert.Equal(1, CountOccurrences(source, "player.Land(footTile - Player.FootOffset);"));
        Assert.DoesNotContain("player.y = 77;", source);
        Assert.Contains("""World.Load("assets/maps/stage1.tmj");""", source);
        Assert.DoesNotContain("World.Column(", source);
        Assert.DoesNotContain("World.Flags(", source);
        Assert.DoesNotContain("World.Map(", source);
        Assert.DoesNotContain("World.Column(", source);
        Assert.DoesNotContain("Tilemap.Set(", source);
        Assert.Contains("i16 footTile = Camera.AabbHitTop(screenX, footWorldY - CollisionProbe.LandingSearchTopOffset, Sprite.Width(mario_player), CollisionProbe.LandingSearchHeight, CollisionFlag.Landable);", source);
        Assert.Contains("player.velocityY >= 0", source);
        Assert.DoesNotContain("camera_span_has_flags(", source);
        Assert.DoesNotContain("failTile", source);
        Assert.DoesNotContain("hazardHit", source);
        Assert.DoesNotContain("if (footTile != 3)", source);

        var resetStart = source.IndexOf("inline void ResolveReset(PlayerState player, CameraState view)", StringComparison.Ordinal);
        Assert.True(resetStart >= 0);
        var resetEnd = source.IndexOf("void SetupVideo()", resetStart, StringComparison.Ordinal);
        Assert.True(resetEnd > resetStart);
        var resetBlock = source[resetStart..resetEnd];
        Assert.DoesNotContain("frame = 0;", resetBlock);
        Assert.DoesNotContain("displayFlipX = false;", resetBlock);
        Assert.DoesNotContain("animTick = 0;", resetBlock);
        Assert.DoesNotContain("view.moving = 0;", resetBlock);

        var resetCall = source.IndexOf("frame.ResolveReset(player, view);", StringComparison.Ordinal);
        var jumpCall = source.IndexOf("player.HandleJumpInput(view.speed);", StringComparison.Ordinal);
        Assert.True(jumpCall > resetCall);

        var rom = GameBoyRomCompiler.CompileSource(RunnerSample.CompiledSource(), RunnerSample.Directory);
        AssertRunnerMbc1Rom(rom);
    }

    [Fact]
    public void Compiles_long_if_body_with_map_streaming()
    {
        const string source = """
                              void Main() {
                                  Video.Init();
                                  World.Column(0, 0, 0, 3, 5);
                                  World.Column(1, 0, 5, 3, 5);
                                  World.Map(2, 11, 4);
                                  i16 fine = 0;
                                  i16 streamColumn = 20;
                                  i16 mapColumn = 0;
                                  while (true) {
                                      Video.WaitVBlank();
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

    private static void AssertRunnerMbc1Rom(byte[] rom)
    {
        Assert.Equal(131072, rom.Length);
        Assert.Equal(0x01, rom[0x0147]);
        Assert.Equal(0x02, rom[0x0148]);
    }

    private static string WriteSpriteAsset(string fileName, string json)
    {
        var directory = Path.Combine(Path.GetTempPath(), "RetroSharp.GameBoy.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, fileName), json);
        return directory;
    }

    private static string WriteLibraryPackage(string importPath, string sourceName, string source, params string[] targets)
    {
        var directory = Path.Combine(Path.GetTempPath(), "RetroSharp.GameBoy.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var targetList = string.Join(", ", targets.Select(target => "\"" + target + "\""));
        File.WriteAllText(
            Path.Combine(directory, "retrosharp-library.json"),
            $$"""
              {
                "import": "{{importPath}}",
                "sources": [ "{{sourceName}}" ],
                "targets": [ {{targetList}} ]
              }
              """);
        File.WriteAllText(Path.Combine(directory, sourceName), source);
        return directory;
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

    private static string Fingerprint(byte[] bytes)
    {
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
    }

    private static string BytesAround(byte[] bytes, int offset)
    {
        var start = Math.Max(0, offset - 8);
        var length = Math.Min(bytes.Length - start, 17);
        return $"0x{offset:X4} [" + string.Join(" ", bytes.Skip(start).Take(length).Select(value => value.ToString("X2"))) + "]";
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

    private static string RepositoryDirectory(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"Could not find repository directory '{relativePath}'.");
    }

    private static FunctionSyntax ExternIntrinsic(string target, string intrinsic, string functionName)
    {
        return new FunctionSyntax(
            "void",
            functionName,
            [],
            new BlockSyntax([]),
            isExtern: true,
            attributes:
            [
                new FunctionAttributeSyntax("target", [new ConstantSyntax($"\"{target}\"")]),
                new FunctionAttributeSyntax("intrinsic", [new ConstantSyntax($"\"{intrinsic}\"")]),
            ]);
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

    private static ProgramSyntax ParseGameBoySourceWithPortable2D(string source)
    {
        var parse = new SomeParser().Parse(
            SdkLibrarySource.Merge(
                GameBoyTarget.Intrinsics,
                source,
                libraryImportPaths: [SdkImportResolver.Portable2D]));
        if (parse.IsFailure)
        {
            throw new InvalidOperationException(parse.Error);
        }

        return TargetProgramSelector.Select(parse.Value, GameBoyTarget.Intrinsics);
    }

    private static GameBoyVideoProgram CompileVideoProgram(string source, string? baseDirectory)
    {
        var parse = new SomeParser().Parse(
            SdkLibrarySource.Merge(
                GameBoyTarget.Intrinsics,
                source,
                libraryImportPaths: [SdkImportResolver.Portable2D]));
        if (parse.IsFailure)
        {
            throw new InvalidOperationException(parse.Error);
        }

        var targetProgram = TargetProgramSelector.Select(parse.Value, GameBoyTarget.Intrinsics);
        var actorProgram = ActorFrameworkLowerer.Lower(targetProgram, GameBoyTarget.Capabilities, supportsUpdate: true, supportsDraw: true, baseDirectory);
        var lowered = SdkSourcePackageFacadeLowerer.Lower(actorProgram);
        return GameBoyVideoProgram.FromProgram(lowered, baseDirectory);
    }
}
