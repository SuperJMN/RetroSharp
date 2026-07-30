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

}
