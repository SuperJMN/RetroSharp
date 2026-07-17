namespace RetroSharp.GameBoy.Tests;

using Xunit;
using static RetroSharp.GameBoy.Tests.GameBoySdkOperationBoundaryTests;
using static RetroSharp.GameBoy.Tests.GameBoyTestSupport;

[Trait("RetroSharp.TestOwnership", "SdkLowering")]
public sealed class GameBoySdkCollisionRuntimeLoweringTests
{
    [Fact]
    [Trait("RetroSharp.TestOwnership", "SdkLowering")]
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
    [Trait("RetroSharp.TestOwnership", "SdkLowering")]
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
    [Trait("RetroSharp.TestOwnership", "SdkLowering")]
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
    [Trait("RetroSharp.TestOwnership", "SdkLowering")]
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
    [Trait("RetroSharp.TestOwnership", "SdkLowering")]
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
    [Trait("RetroSharp.TestOwnership", "SdkLowering")]
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
    [Trait("RetroSharp.TestOwnership", "SdkLowering")]
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
    [Trait("RetroSharp.TestOwnership", "SdkLowering")]
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
    [Trait("RetroSharp.TestOwnership", "SdkLowering")]
    public void Screen_camera_aabb_uses_static_small_map_rows_without_changing_results_on_game_boy()
    {
        const string source = """
                              void Main() {
                                  World.Column(0, 0, 4);
                                  World.Column(1, 0, 4);
                                  World.Flags(0, 0, 1);
                                  World.Flags(1, 0, 1);
                                  World.Map(2, 0, 2);
                                  Camera.Init(2, 0, 2);

                                  u8 empty = Camera.ScreenAabbTiles(0, 0, 8, 8, 1);
                                  u8 solid = Camera.ScreenAabbTiles(0, 8, 8, 8, 1);
                                  u8 top = Camera.ScreenAabbHitTop(0, 8, 8, 8, 1);

                                  while (true) {
                                      Video.WaitVBlank();
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);
        var cpu = new GameBoyTestCpu(rom);
        cpu.RunFrames(2);

        Assert.Equal(0, cpu.Wram(0xC000));
        Assert.Equal(1, cpu.Wram(0xC001));
        Assert.Equal(8, cpu.Wram(0xC002));
        Assert.True(ContainsSequence(rom, [0xFE, 0x01, 0xCA]), "Screen AABB lowering should branch directly on a fully covered source row.");
        Assert.True(ContainsSequence(rom, [0xFE, 0x00, 0xCA]), "Screen AABB lowering should branch directly on an empty source row.");
    }

    [Fact]
    [Trait("RetroSharp.TestOwnership", "SdkLowering")]
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
    [Trait("RetroSharp.TestOwnership", "SdkLowering")]
    public void World_camera_hit_top_keeps_legacy_byte_destination_for_32_row_game_boy_world()
    {
        var source = CollisionHitContractSource(
            height: 32,
            solidRow: 31,
            body: "u8 noHit = Camera.AabbHitTop(0, 0, 8, 8, 4);");

        Assert.Equal(32768, GameBoyRomCompiler.CompileSource(source).Length);
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

}
