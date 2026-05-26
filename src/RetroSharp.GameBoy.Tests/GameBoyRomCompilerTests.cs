namespace RetroSharp.GameBoy.Tests;

using RetroSharp.GameBoy;
using Xunit;

public class GameBoyRomCompilerTests
{
    [Fact]
    public void Compiles_video_api_calls_to_a_game_boy_rom()
    {
        const string source = """
                              void main() {
                                  video_init();
                                  palette_set(0, 0);
                                  palette_set(1, 1);
                                  palette_set(2, 2);
                                  palette_set(3, 3);
                                  tilemap_set(8, 7, 1);
                                  tilemap_set(9, 7, 2);
                                  tilemap_set(10, 7, 1);
                                  tilemap_set(9, 8, 3);
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
    public void Compiles_runtime_sprite_loop_to_a_game_boy_rom()
    {
        const string source = """
                              void main() {
                                  video_init();
                                  palette_set(0, 0);
                                  palette_set(1, 1);
                                  palette_set(2, 2);
                                  palette_set(3, 3);
                                  i16 x = 8;
                                  while (true) {
                                      video_wait_vblank();
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
    public void Compiles_scroll_set_to_game_boy_scroll_register_writes()
    {
        const string source = """
                              void main() {
                                  video_init();
                                  i16 camera = 0;
                                  while (true) {
                                      video_wait_vblank();
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
    public void Compiles_tilemap_fill_column_to_runtime_vram_writes()
    {
        const string source = """
                              void main() {
                                  video_init();
                                  i16 column = 20;
                                  while (true) {
                                      video_wait_vblank();
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
                              void main() {
                                  video_init();
                                  i16 camera = 0;
                                  i16 fine = 0;
                                  i16 streamColumn = 20;
                                  i16 marker = 0;
                                  i16 frame = 0;
                                  while (true) {
                                      video_wait_vblank();
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
                              void main() {
                                  video_init();
                                  map_column(0, 1, 2, 3, 4);
                                  map_column(1, 5, 6, 7, 8);
                                  i16 targetColumn = 20;
                                  i16 mapColumn = 0;
                                  while (true) {
                                      video_wait_vblank();
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
        Assert.True(ContainsSequence(rom, [0xFA, 0x01, 0xC0, 0x5F, 0x16, 0x00, 0x21]), "ROM should load the source map column into DE and a row-table address into HL.");
        Assert.True(ContainsSequence(rom, [0x19, 0x7E, 0x47]), "ROM should read a tile from the map row table and preserve it in B.");
        Assert.True(ContainsSequence(rom, [0xFA, 0x00, 0xC0, 0xC6, 0x60, 0x6F, 0x26, 0x99, 0x78, 0x77]), "ROM should stream row 11 into the target background column.");
    }

    [Fact]
    public void Compiles_long_if_body_with_map_streaming()
    {
        const string source = """
                              void main() {
                                  video_init();
                                  map_column(0, 0, 0, 3, 5);
                                  map_column(1, 0, 5, 3, 5);
                                  i16 fine = 0;
                                  i16 streamColumn = 20;
                                  i16 mapColumn = 0;
                                  while (true) {
                                      video_wait_vblank();
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

    private static bool ContainsSequence(byte[] bytes, byte[] sequence)
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
                return true;
            }
        }

        return false;
    }
}
