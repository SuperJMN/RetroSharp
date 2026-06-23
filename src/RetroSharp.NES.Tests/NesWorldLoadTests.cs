namespace RetroSharp.NES.Tests;

using System.Buffers.Binary;
using System.IO.Compression;
using RetroSharp.Core.Sdk;
using RetroSharp.NES;
using RetroSharp.Parser;
using Xunit;

// Acceptance: a Tiled map imported through the shared target-neutral
// LogicalTiledMap lands on NES via world.Load, generating NES background CHR and
// the nametable from the same source a Game Boy world.Load would consume.
public sealed class NesWorldLoadTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "retrosharp-nes-worldload-" + Guid.NewGuid().ToString("N"));

    public NesWorldLoadTests() => Directory.CreateDirectory(directory);

    public void Dispose() => Directory.Delete(directory, recursive: true);

    [Fact]
    public void World_load_imports_a_tiled_map_into_nes_background_chr_and_nametable()
    {
        // Two 8x8 source tiles: gid 1 black (darkest tone), gid 2 white (lightest tone).
        WriteTilesheetPng(Path.Combine(directory, "tiles.png"), blackThenWhite: true);
        File.WriteAllText(Path.Combine(directory, "level.tmj"), """
        {
          "type": "map",
          "orientation": "orthogonal",
          "infinite": false,
          "width": 2,
          "height": 3,
          "tilewidth": 8,
          "tileheight": 8,
          "properties": [
            { "name": "retrosharpStreamY", "type": "int", "value": 0 },
            { "name": "retrosharpWorldY", "type": "int", "value": 1 },
            { "name": "retrosharpWorldHeight", "type": "int", "value": 2 }
          ],
          "tilesets": [
            {
              "firstgid": 1,
              "name": "tiles",
              "tilewidth": 8,
              "tileheight": 8,
              "tilecount": 2,
              "columns": 2,
              "image": "tiles.png",
              "imagewidth": 16,
              "imageheight": 8
            }
          ],
          "layers": [
            { "type": "tilelayer", "name": "world", "width": 2, "height": 3, "data": [0, 0, 1, 0, 0, 1] }
          ]
        }
        """);

        const string source = """
            void main() {
                video.Init();
                world.Load("level.tmj");
            }
            """;

        var program = BuildProgram(source);

        var worldMap = Assert.IsType<WorldMap2D>(program.WorldMap);
        Assert.Equal(2, worldMap.Width);
        Assert.Equal(2, worldMap.Height);
        // Black source tile (gid 1) generated as NES background tile 6; blank stays 0.
        Assert.Equal(6, worldMap.TileIdAt(0, 0));
        Assert.Equal(0, worldMap.TileIdAt(1, 0));
        Assert.Equal(0, worldMap.TileIdAt(0, 1));
        Assert.Equal(6, worldMap.TileIdAt(1, 1));

        var background = Assert.Single(program.GeneratedBackgroundTiles);
        Assert.Equal(6, background.FirstTile);
        // Darkest tone packs both bit planes set: a solid black 8x8 tile.
        Assert.Equal(Enumerable.Repeat((byte)0xFF, 16).ToArray(), background.Data);

        // The nametable references the generated background tile at the world origin.
        Assert.Equal(6, program.NameTable[0]);

        var rom = NesRomCompiler.CompileSource(source, directory);
        Assert.Equal(24592, rom.Length);
        Assert.Equal((byte)'N', rom[0]);
    }

    [Fact]
    public void Same_world_load_source_compiles_on_both_game_boy_and_nes()
    {
        WriteTilesheetPng(Path.Combine(directory, "tiles.png"), blackThenWhite: true);
        File.WriteAllText(Path.Combine(directory, "level.tmj"), """
        {
          "type": "map",
          "orientation": "orthogonal",
          "infinite": false,
          "width": 2,
          "height": 3,
          "tilewidth": 8,
          "tileheight": 8,
          "properties": [
            { "name": "retrosharpStreamY", "type": "int", "value": 0 },
            { "name": "retrosharpWorldY", "type": "int", "value": 1 },
            { "name": "retrosharpWorldHeight", "type": "int", "value": 2 }
          ],
          "tilesets": [
            {
              "firstgid": 1,
              "name": "tiles",
              "tilewidth": 8,
              "tileheight": 8,
              "tilecount": 2,
              "columns": 2,
              "image": "tiles.png",
              "imagewidth": 16,
              "imageheight": 8
            }
          ],
          "layers": [
            { "type": "tilelayer", "name": "world", "width": 2, "height": 3, "data": [0, 0, 1, 0, 0, 1] }
          ]
        }
        """);

        const string source = """
            void main() {
                video.Init();
                world.Load("level.tmj");
            }
            """;

        // The same target-neutral Tiled source lowers to a valid ROM on both machines.
        var gbRom = GameBoy.GameBoyRomCompiler.CompileSource(source, directory);
        var nesRom = NesRomCompiler.CompileSource(source, directory);

        Assert.NotEmpty(gbRom);
        Assert.Equal(24592, nesRom.Length);
        Assert.Equal((byte)'N', nesRom[0]);
    }

    [Fact]
    public void World_load_feeds_the_camera_scroll_path_on_both_targets()
    {
        WriteTilesheetPng(Path.Combine(directory, "tiles.png"), blackThenWhite: true);
        File.WriteAllText(Path.Combine(directory, "level.tmj"), """
        {
          "type": "map",
          "orientation": "orthogonal",
          "infinite": false,
          "width": 2,
          "height": 3,
          "tilewidth": 8,
          "tileheight": 8,
          "properties": [
            { "name": "retrosharpStreamY", "type": "int", "value": 0 },
            { "name": "retrosharpWorldY", "type": "int", "value": 1 },
            { "name": "retrosharpWorldHeight", "type": "int", "value": 2 }
          ],
          "tilesets": [
            {
              "firstgid": 1,
              "name": "tiles",
              "tilewidth": 8,
              "tileheight": 8,
              "tilecount": 2,
              "columns": 2,
              "image": "tiles.png",
              "imagewidth": 16,
              "imageheight": 8
            }
          ],
          "layers": [
            { "type": "tilelayer", "name": "world", "width": 2, "height": 3, "data": [0, 0, 1, 0, 0, 1] }
          ]
        }
        """);

        // A world imported with world.Load feeds the same camera scroll path that
        // world.Map does, so the camera lowers on both targets from one source.
        const string source = """
            void main() {
                video.Init();
                world.Load("level.tmj");
                camera.Init(2, 0, 2);
                loop {
                    video.WaitVBlank();
                    input.Poll();
                    let cx = button_hold_ticks(right);
                    camera.SetPosition(cx, 0);
                    camera.Apply();
                }
            }
            """;

        var gbRom = GameBoy.GameBoyRomCompiler.CompileSource(source, directory);
        var nesRom = NesRomCompiler.CompileSource(source, directory);

        Assert.NotEmpty(gbRom);
        Assert.Equal(24592, nesRom.Length);
        Assert.Equal((byte)'N', nesRom[0]);
    }

    private NesVideoProgram BuildProgram(string source)
    {
        var parse = new SomeParser().Parse(source);
        Assert.True(parse.IsSuccess, parse.IsFailure ? parse.Error : null);
        return NesVideoProgram.FromProgram(parse.Value, directory);
    }

    private static void WriteTilesheetPng(string path, bool blackThenWhite)
    {
        const int width = 16;
        const int height = 8;
        var rgba = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var black = (x < 8) == blackThenWhite;
                var value = (byte)(black ? 0x00 : 0xFF);
                var offset = (y * width + x) * 4;
                rgba[offset] = value;
                rgba[offset + 1] = value;
                rgba[offset + 2] = value;
                rgba[offset + 3] = 0xFF;
            }
        }

        File.WriteAllBytes(path, EncodeRgbaPng(width, height, rgba));
    }

    private static byte[] EncodeRgbaPng(int width, int height, byte[] rgba)
    {
        using var output = new MemoryStream();
        output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(0, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4, 4), height);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 6;  // color type RGBA
        WriteChunk(output, "IHDR", ihdr);

        var stride = width * 4;
        var raw = new byte[height * (stride + 1)];
        for (var y = 0; y < height; y++)
        {
            raw[y * (stride + 1)] = 0; // filter type 0
            Array.Copy(rgba, y * stride, raw, y * (stride + 1) + 1, stride);
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionMode.Compress, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        WriteChunk(output, "IDAT", compressed.ToArray());
        WriteChunk(output, "IEND", []);
        return output.ToArray();
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);

        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);

        var crc = Crc32(typeBytes, data);
        var crcBytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        output.Write(crcBytes);
    }

    private static uint Crc32(byte[] type, byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (var b in type.Concat(data))
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            }
        }

        return crc ^ 0xFFFFFFFF;
    }
}
