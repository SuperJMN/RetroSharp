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

        _ = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSource(source, sdkImportMode: SdkLibraryImportMode.ExplicitOnly);
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

        _ = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSource(source, sdkImportMode: SdkLibraryImportMode.ExplicitOnly);
    }

    [Fact]
    public void Explicit_sdk_import_mode_uses_manifest_declared_portable2d_sdk()
    {
        const string source = """
                              void Main() {
                                  Video.WaitVBlank();
                              }
                              """;

        _ = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSource(
            source,
            sdkImportMode: SdkLibraryImportMode.ExplicitOnly,
            sdkLibraryImports: [SdkImportResolver.Portable2D]);
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

        _ = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSource(
            source,
            sdkImportMode: SdkLibraryImportMode.ExplicitOnly,
            sdkLibraryRegistry: registry);
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

}
