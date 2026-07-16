namespace RetroSharp.GameBoy.Tests;

using Xunit;
using static RetroSharp.GameBoy.Tests.GameBoySdkOperationBoundaryTests;

public sealed class GameBoySdkCameraRuntimeLoweringTests
{
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
}
