namespace RetroSharp.NES.Tests;

using RetroSharp.NES;
using RetroSharp.Sdk;
using Xunit;
using static NesSdkOperationBoundaryTests;

[Trait("RetroSharp.TestOwnership", "SdkLowering")]
public sealed class NesSdkCollisionLoweringTests
{
    [Fact]
    public void Golden_collision_aabb_emission_is_pinned_nes()
    {
        const string source = """
                              void Main() {
                                  World.Column(0, 1, 2);
                                  World.Flags(0, 0, 1);
                                  World.Map(1, 10, 2);
                                  Camera.Init(1, 10, 2);
                                  while (true) {
                                      Video.WaitVBlank();
                                      u8 footY = 16;
                                      u8 hit = Camera.AabbTiles(72, footY - 8, 16, 16, 1);
                                      u8 hitTop = Camera.AabbHitTop(72, footY - 8, 16, 16, 1);
                                  }
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);

        Assert.Equal("9DADC4F11B3870D538625243392EFDAD30BFDFE08220DAC0A6FBC7F9B2A7A9EC", Fingerprint(rom));
    }

    [Fact]
    public void World_camera_hit_top_materializes_y_304_and_minus_one_through_a_x_on_nes()
    {
        var source = CollisionHitContractSource(
            height: 40,
            solidRow: 38,
            body: """
                      i16 footWorldY = 304;
                      i16 hitTop = Camera.AabbHitTop(0, footWorldY - 4, 8, 12, 1);
                      i16 noHit = Camera.AabbHitTop(0, footWorldY - 4, 8, 12, 4);
                  """);

        var prg = NesRomCompiler.CompileSource(source).Skip(16).Take(32 * 1024).ToArray();

        Assert.True(
            ContainsSequence(prg, [0xA5, 0x01, 0x85, 0xE9, 0xA5, 0xE8, 0x18, 0x69, 0x04, 0x85, 0xE8, 0xA5, 0xE9, 0x69, 0x00, 0xAA, 0xA5, 0xE8, 0x29, 0xF8]),
            "hit-top should add the matching probe offset as a word, align the low byte, and propagate the world-Y high byte into X.");
        Assert.True(ContainsSequence(prg, [0x85, 0x02, 0x86, 0x03]), "word hit-top should store A:X into the little-endian destination.");
        Assert.True(ContainsSequence(prg, [0xA9, 0xFF, 0xAA, 0x85, 0x04, 0x86, 0x05]), "no hit should store FF FF through A:X.");
    }
    [Fact]
    public void Screen_camera_hit_top_keeps_byte_semantics_and_zero_extends_word_results_on_nes()
    {
        var source = CollisionHitContractSource(
            height: 40,
            solidRow: 38,
            body: """
                      u8 legacyNoHit = Camera.ScreenAabbHitTop(0, 0, 8, 8, 4);
                      i16 completeNoHit = Camera.ScreenAabbHitTop(0, 0, 8, 8, 4);
                  """);

        var prg = NesRomCompiler.CompileSource(source).Skip(16).Take(32 * 1024).ToArray();

        Assert.True(ContainsSequence(prg, [0xA9, 0xFF, 0xA2, 0x00, 0x85, 0x01, 0x86, 0x02]), "word screen no-hit should store FF 00 through A:X.");
        Assert.True(ContainsSequence(prg, [0x85, 0x00]), "the legacy byte destination should consume the low result byte.");
    }
    [Fact]
    public void World_camera_hit_top_rejects_unsafe_byte_narrowing_on_tall_nes_world()
    {
        var source = CollisionHitContractSource(
            height: 40,
            solidRow: 38,
            body: "u8 hitTop = Camera.AabbHitTop(0, 300, 8, 12, 1);");

        var exception = Assert.Throws<InvalidOperationException>(() => NesRomCompiler.CompileSource(source));

        Assert.Equal(
            "World hit-top cannot be stored in byte destination type 'u8' because the active world is 40 hardware rows tall; use an i16 local and compare it with -1.",
            exception.Message);
    }
    [Fact]
    public void World_camera_hit_top_keeps_legacy_byte_destination_for_32_row_nes_world()
    {
        var source = CollisionHitContractSource(
            height: 32,
            solidRow: 31,
            body: "u8 noHit = Camera.AabbHitTop(0, 0, 8, 8, 4);");

        Assert.NotEmpty(NesRomCompiler.CompileSource(source));
    }
    [Fact]
    public void Collision_aabb_via_compile_time_operand_intrinsic_is_byte_identical_nes()
    {
        const string direct = """
                              void Main() {
                                  World.Column(0, 1, 2);
                                  World.Flags(0, 0, 1);
                                  World.Map(1, 10, 2);
                                  Camera.Init(1, 10, 2);
                                  u8 footY = 16;
                                  u8 hit = Camera.AabbTiles(72, footY - 8, 16, 16, 1);
                                  u8 hitTop = Camera.AabbHitTop(72, footY - 8, 16, 16, 1);
                              }
                              """;
        const string library = """
                               void Main() {
                                   World.Column(0, 1, 2);
                                   World.Flags(0, 0, 1);
                                   World.Map(1, 10, 2);
                                   Camera.Init(1, 10, 2);
                                   u8 footY = 16;
                                   u8 hit = Camera.AabbTiles(72, footY - 8, 16, 16, 1);
                                   u8 hitTop = Camera.AabbHitTop(72, footY - 8, 16, 16, 1);
                               }
                               """;

        var sdkLibrary = SdkLibrarySource.ForTarget(NesTarget.Intrinsics);

        Assert.Contains("[intrinsic(\"camera_aabb_tiles\")]", sdkLibrary, StringComparison.Ordinal);
        Assert.Contains("[intrinsic(\"camera_aabb_hit_top\")]", sdkLibrary, StringComparison.Ordinal);
        Assert.Equal(NesRomCompiler.CompileSource(direct), NesRomCompiler.CompileSource(library));
    }
    [Fact]
    public void Screen_collision_aabb_via_compile_time_operand_intrinsic_is_byte_identical_nes()
    {
        const string direct = """
                              void Main() {
                                  World.Column(0, 1, 2);
                                  World.Flags(0, 0, 1);
                                  World.Map(1, 10, 2);
                                  Camera.Init(1, 10, 2);
                                  u8 screenX = 40;
                                  u8 screenY = 16;
                                  u8 hit = Camera.ScreenAabbTiles(screenX, screenY, 16, 16, 1);
                                  u8 hitTop = Camera.ScreenAabbHitTop(screenX, screenY, 16, 16, 1);
                              }
                              """;
        const string library = """
                               void Main() {
                                   World.Column(0, 1, 2);
                                   World.Flags(0, 0, 1);
                                   World.Map(1, 10, 2);
                                   Camera.Init(1, 10, 2);
                                   u8 screenX = 40;
                                   u8 screenY = 16;
                                   u8 hit = Camera.ScreenAabbTiles(screenX, screenY, 16, 16, 1);
                                   u8 hitTop = Camera.ScreenAabbHitTop(screenX, screenY, 16, 16, 1);
                               }
                               """;

        var sdkLibrary = SdkLibrarySource.ForTarget(NesTarget.Intrinsics);

        Assert.Contains("[intrinsic(\"camera_screen_aabb_tiles\")]", sdkLibrary, StringComparison.Ordinal);
        Assert.Contains("[intrinsic(\"camera_screen_aabb_hit_top\")]", sdkLibrary, StringComparison.Ordinal);
        Assert.Equal(NesRomCompiler.CompileSource(direct), NesRomCompiler.CompileSource(library));
    }
    [Fact]
    public void Camera_relative_collision_uses_absolute_camera_tile_after_scroll_wrap()
    {
        const string source = """
                              void Main() {
                                  World.Column(0, 1, 2);
                                  World.Flags(0, 0, 1);
                                  World.Map(68, 10, 2);
                                  Camera.Init(68, 10, 2);
                                  while (true) {
                                      Video.WaitVBlank();
                                      u8 x = Input.HoldTicks(Button.Right);
                                      Camera.SetPosition(x, 0);
                                      u8 hit = Camera.AabbTiles(72, 8, 16, 8, 1);
                                  }
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.Equal(40976, rom.Length);
        Assert.True(
            ContainsSequence(prg, [0xA5, 0xE0, 0x29, 0x07, 0x18, 0x69, 0x48, 0x4A, 0x4A, 0x4A, 0x18, 0x65, 0xE1]),
            "Camera.AabbTiles should combine camera fine X with the absolute source tile, not the wrapped scroll byte.");
    }

}
