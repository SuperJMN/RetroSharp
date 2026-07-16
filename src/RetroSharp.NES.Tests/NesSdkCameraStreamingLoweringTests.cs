namespace RetroSharp.NES.Tests;

using RetroSharp.NES;
using Xunit;
using static NesSdkOperationBoundaryTests;

[Trait("RetroSharp.TestOwnership", "SdkLowering")]
public sealed class NesSdkCameraStreamingLoweringTests
{
    [Fact]
    [Trait("RetroSharp.TestOwnership", "SdkLowering")]
    public void Compiles_horizontal_camera_path_from_world_map_to_nes_scroll()
    {
        const string source = """
                              void Main() {
                                  Video.Init();
                                  World.Column(0, 1, 2);
                                  World.Column(1, 3, 4);
                                  World.Map(2, 10, 2);
                                  Camera.Init(2, 10, 2);
                                  while (true) {
                                      Video.WaitVBlank();
                                      Camera.SetPosition(8, 0);
                                      Camera.Apply();
                                  }
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.Equal(40976, rom.Length);
        Assert.True(ContainsSequence(prg, [0xA9, 0x08, 0x85, 0xE7, 0xA5, 0xE7, 0x85, 0xE0]), "camera set should store the requested horizontal position.");
        Assert.True(ContainsSequence(prg, [0x8D, 0x00, 0x20, 0xA5, 0xE0, 0x8D, 0x05, 0x20]), "camera apply should publish PPU control and horizontal scroll.");
    }

    [Fact]
    [Trait("RetroSharp.TestOwnership", "SdkLowering")]
    public void Nes_lowers_explicit_stream_map_row_to_ppu_row_writes()
    {
        const string source = """
                              void Main() {
                                  Video.Init();
                                  World.Column(0, 1, 2, 3, 4);
                                  World.Column(1, 5, 6, 7, 8);
                                  World.Column(2, 9, 10, 11, 12);
                                  World.Column(3, 13, 14, 15, 16);
                                  World.Map(4, 0, 4);
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
    [Trait("RetroSharp.TestOwnership", "SdkLowering")]
    public void Nes_accepts_world_rows_beyond_four_screen_surface_for_runtime_row_streaming()
    {
        var tallColumn = string.Join(", ", Enumerable.Range(0, 61).Select(row => row % 4 + 1));
        var source = $$"""
                       void Main() {
                           Video.Init();
                           World.Column(0, {{tallColumn}});
                           World.Map(1, 0, 61);
                           Camera.Init(1, 0, 60);
                           map_stream_row(0, 60, 0, 1);
                           Camera.SetPosition(0, 1);
                           Camera.Apply();
                       }
                       """;

        var rom = NesRomCompiler.CompileSource(source);

        Assert.Equal(0x08, rom[6] & 0x08);
    }
    [Fact]
    [Trait("RetroSharp.TestOwnership", "SdkLowering")]
    public void Four_screen_horizontal_streaming_prepares_the_next_offscreen_column()
    {
        var column = string.Join(", ", Enumerable.Range(0, 14).Select(row => row % 4 + 1));
        var source = $$"""
                       void Main() {
                           Video.Init();
                           World.Column(0, {{column}});
                           World.Column(67, {{column}});
                           World.Map(68, 0, 14);
                           Camera.Init(68, 0, 14);
                           u8 x = 8;
                           u8 y = 1;
                           Camera.SetPosition(x, y);
                           Camera.Apply();
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
    [Trait("RetroSharp.TestOwnership", "SdkLowering")]
    public void Nes_streams_runtime_rows_when_vertical_camera_crosses_tall_world_boundary()
    {
        var tallColumn = string.Join(", ", Enumerable.Range(0, 61).Select(row => row % 4 + 1));
        var source = $$"""
                       void Main() {
                           Video.Init();
                           World.Column(0, {{tallColumn}});
                           World.Map(1, 0, 61);
                           Camera.Init(1, 0, 60);
                           u8 y = 8;
                           Camera.SetPosition(0, y);
                           Camera.Apply();
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
    [Trait("RetroSharp.TestOwnership", "SdkLowering")]
    public void Nes_automatic_camera_row_streaming_uses_the_callers_vblank()
    {
        var tallColumn = string.Join(", ", Enumerable.Range(0, 61).Select(row => row % 4 + 1));
        var source = $$"""
                       void Main() {
                           Video.Init();
                           World.Column(0, {{tallColumn}});
                           World.Map(1, 0, 61);
                           Camera.Init(1, 0, 60);
                           u8 y = 8;
                           Video.WaitVBlank();
                           Camera.SetPosition(0, y);
                           Camera.Apply();
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
    [Trait("RetroSharp.TestOwnership", "SdkLowering")]
    public void Nes_runtime_row_streaming_writes_contiguous_ppudata_segments()
    {
        var tallColumn = string.Join(", ", Enumerable.Range(0, 61).Select(row => row % 4 + 1));
        var source = $$"""
                       void Main() {
                           Video.Init();
                           World.Column(0, {{tallColumn}});
                           World.Map(1, 0, 61);
                           Camera.Init(1, 0, 60);
                           u8 y = 8;
                           Video.WaitVBlank();
                           Camera.SetPosition(0, y);
                           Camera.Apply();
                       }
                       """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.True(
            ContainsSequence(prg, [0xA5, 0xE2, 0x29, 0x1F, 0xC9, 0x00]),
            "Runtime row streaming should keep PPUDATA auto-incrementing within a nametable row and only reset PPUADDR when the target column crosses a 32-column nametable boundary.");
    }
    [Fact]
    [Trait("RetroSharp.TestOwnership", "SdkLowering")]
    public void Nes_runtime_row_streaming_is_split_across_vblanks()
    {
        var tallColumn = string.Join(", ", Enumerable.Range(0, 61).Select(row => row % 4 + 1));
        var source = $$"""
                       void Main() {
                           Video.Init();
                           World.Column(0, {{tallColumn}});
                           World.Map(1, 0, 61);
                           Camera.Init(1, 0, 60);
                           u8 y = 8;
                           Video.WaitVBlank();
                           Camera.SetPosition(0, y);
                           Camera.Apply();
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
    [Trait("RetroSharp.TestOwnership", "SdkLowering")]
    public void Compiles_four_screen_camera_path_from_world_map_to_nes_scroll()
    {
        var tallColumn = string.Join(", ", Enumerable.Range(0, 60).Select(row => row % 4 + 1));
        var source = $$"""
                       void Main() {
                           Video.Init();
                           World.Column(0, {{tallColumn}});
                           World.Column(63, {{tallColumn}});
                           World.Map(64, 0, 60);
                           Camera.Init(64, 0, 60);
                           u8 x = 0;
                           u8 y = 0;
                           while (true) {
                               Video.WaitVBlank();
                               x += 1;
                               y += 1;
                               Camera.SetPosition(x, y);
                               Camera.Apply();
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
    [Trait("RetroSharp.TestOwnership", "SdkLowering")]
    public void Nes_vertical_camera_large_delta_steps_one_pixel_instead_of_jumping()
    {
        var column = string.Join(", ", Enumerable.Range(0, 14).Select(row => row % 4 + 1));
        var source = $$"""
                       void Main() {
                           World.Column(0, {{column}});
                           World.Map(1, 0, 14);
                           Camera.Init(1, 0, 14);
                           u8 y = 56;
                           Camera.SetPosition(0, y);
                           Camera.Apply();
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
    [Trait("RetroSharp.TestOwnership", "SdkLowering")]
    public void Accepts_vertical_camera_stream_area_taller_than_four_screen_buffer()
    {
        var tallColumn = string.Join(", ", Enumerable.Range(0, 96).Select(row => row % 4 + 1));
        var source = $$"""
                       void Main() {
                           World.Column(0, {{tallColumn}});
                           World.Map(1, 0, 96);
                           Camera.Init(1, 0, 96);
                           while (true) {
                               Camera.SetPosition(0, 1);
                               Camera.Apply();
                           }
                       }
                       """;

        var rom = NesRomCompiler.CompileSource(source);

        Assert.Equal(0x08, rom[6] & 0x08);
    }
    [Fact]
    [Trait("RetroSharp.TestOwnership", "SdkLowering")]
    public void Rejects_vertical_camera_stream_start_outside_four_screen_buffer()
    {
        const string source = """
                              void Main() {
                                  World.Column(0, 1);
                                  World.Map(1, 0, 1);
                                  Camera.Init(1, 60, 1);
                                  while (true) {
                                      Camera.SetPosition(0, 1);
                                      Camera.Apply();
                                  }
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => NesRomCompiler.CompileSource(source));

        Assert.Contains("NES four-screen free scroll stream area must fit within the 60-row four-screen height", exception.Message);
    }

}
