namespace RetroSharp.NES.Tests;

using System.Buffers.Binary;
using System.IO.Compression;
using RetroSharp.Core.Sdk;
using RetroSharp.NES;
using RetroSharp.Parser;
using RetroSharp.Sdk;
using Xunit;

// Acceptance: a Tiled map imported through the shared target-neutral
// LogicalTiledMap lands on NES via World.Load, generating NES background CHR and
// the nametable from the same source a Game Boy World.Load would consume.
public sealed class NesWorldLoadTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "retrosharp-nes-worldload-" + Guid.NewGuid().ToString("N"));

    public NesWorldLoadTests() => Directory.CreateDirectory(directory);

    public void Dispose() => Directory.Delete(directory, recursive: true);

    [Fact]
    public void World_load_imports_a_tiled_map_into_nes_background_chr_and_nametable()
    {
        // Two 8x8 source tiles: gid 1 contains two colors, gid 2 is unused.
        WriteTwoToneTilesheetPng(Path.Combine(directory, "tiles.png"));
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
            void Main() {
                Video.Init();
                World.Load("level.tmj");
            }
            """;

        var program = BuildProgram(source);

        var worldMap = Assert.IsType<WorldMap2D>(program.WorldMap);
        var worldTileGrid = Assert.IsType<WorldTileGrid>(program.WorldTileGrid);
        Assert.Equal(2, worldMap.Width);
        Assert.Equal(2, worldMap.Height);
        // The colored source tile (gid 1) generates a NES background tile; blank stays 0.
        var generatedTileId = worldTileGrid.TileIdAt(0, 0);
        Assert.NotEqual(0, generatedTileId);
        Assert.Equal(0, worldTileGrid.TileIdAt(1, 0));
        Assert.Equal(0, worldTileGrid.TileIdAt(0, 1));
        Assert.Equal(generatedTileId, worldTileGrid.TileIdAt(1, 1));

        var background = Assert.Single(program.GeneratedBackgroundTiles);
        Assert.Equal(generatedTileId, background.FirstTile);
        Assert.Contains(background.Data, value => value != 0);

        // The nametable references the generated background tile at the world origin.
        Assert.Equal(generatedTileId, program.NameTable[0]);

        var rom = NesRomCompiler.CompileSource(source, directory);
        Assert.Equal(40976, rom.Length);
        Assert.Equal((byte)'N', rom[0]);
    }

    [Fact]
    public void World_load_uses_nes_tileset_png_variant_when_present()
    {
        WriteTilesheetPng(Path.Combine(directory, "tiles.png"), blackThenWhite: true);
        WriteTilesheetPng(Path.Combine(directory, "tiles.nes.png"), blackThenWhite: false);
        File.WriteAllText(Path.Combine(directory, "level.tsx"), """
        <?xml version="1.0" encoding="UTF-8"?>
        <tileset version="1.10" tiledversion="1.12.2" name="Level" tilewidth="8" tileheight="8" tilecount="2" columns="2">
         <image source="tiles.png" width="16" height="8"/>
        </tileset>
        """);
        File.WriteAllText(Path.Combine(directory, "level.tmj"), """
        {
          "type": "map",
          "orientation": "orthogonal",
          "infinite": false,
          "width": 1,
          "height": 1,
          "tilewidth": 8,
          "tileheight": 8,
          "properties": [
            { "name": "retrosharpStreamY", "type": "int", "value": 0 },
            { "name": "retrosharpWorldY", "type": "int", "value": 0 },
            { "name": "retrosharpWorldHeight", "type": "int", "value": 1 }
          ],
          "tilesets": [
            { "firstgid": 1, "source": "level.tsx" }
          ],
          "layers": [
            { "type": "tilelayer", "name": "world", "width": 1, "height": 1, "data": [1] }
          ]
        }
        """);

        const string source = """
            void Main() {
                Video.Init();
                World.Load("level.tmj");
            }
            """;

        var program = BuildProgram(source);
        var worldMap = Assert.IsType<WorldMap2D>(program.WorldMap);
        var worldTileGrid = Assert.IsType<WorldTileGrid>(program.WorldTileGrid);

        Assert.Equal(0, worldTileGrid.TileIdAt(0, 0));
        Assert.Empty(program.GeneratedBackgroundTiles);
        Assert.Equal(0, program.NameTable[0]);
    }

    [Fact]
    public void World_load_derives_nes_background_palette_from_tileset_png_variant()
    {
        WriteSolidTilesheetPng(
            Path.Combine(directory, "tiles.png"),
            (0xFF, 0xFF, 0xFF),
            (0xBC, 0xBC, 0xBC),
            (0x75, 0x75, 0x75),
            (0x00, 0x00, 0x00));
        WriteSolidTilesheetPng(
            Path.Combine(directory, "tiles.nes.png"),
            (0xAB, 0xE7, 0xFF),
            (0x83, 0xD3, 0x13),
            (0xC8, 0x4C, 0x0C),
            (0xAB, 0x00, 0x13));
        File.WriteAllText(Path.Combine(directory, "level.tsx"), """
        <?xml version="1.0" encoding="UTF-8"?>
        <tileset version="1.10" tiledversion="1.12.2" name="Level" tilewidth="8" tileheight="8" tilecount="4" columns="4">
         <image source="tiles.png" width="32" height="8"/>
        </tileset>
        """);
        File.WriteAllText(Path.Combine(directory, "level.tmj"), """
        {
          "type": "map",
          "orientation": "orthogonal",
          "infinite": false,
          "width": 4,
          "height": 1,
          "tilewidth": 8,
          "tileheight": 8,
          "properties": [
            { "name": "retrosharpStreamY", "type": "int", "value": 0 },
            { "name": "retrosharpWorldY", "type": "int", "value": 0 },
            { "name": "retrosharpWorldHeight", "type": "int", "value": 1 }
          ],
          "tilesets": [
            { "firstgid": 1, "source": "level.tsx" }
          ],
          "layers": [
            { "type": "tilelayer", "name": "world", "width": 4, "height": 1, "data": [1, 2, 3, 4] }
          ]
        }
        """);

        const string source = """
            void Main() {
                Video.Init();
                World.Load("level.tmj");
            }
            """;

        var program = BuildProgram(source);

        var backgroundPalette = program.Palette.Take(16).ToArray();
        Assert.Equal(0x31, backgroundPalette[0]);
        Assert.Contains((byte)0x29, backgroundPalette);
        Assert.Contains((byte)0x17, backgroundPalette);
        Assert.Contains((byte)0x05, backgroundPalette);
    }

    [Fact]
    public void World_load_derives_nes_background_palette_from_referenced_tiles_only()
    {
        WriteSolidTilesheetPng(
            Path.Combine(directory, "tiles.png"),
            (0xFF, 0xFF, 0xFF), (0xFF, 0xFF, 0xFF), (0xFF, 0xFF, 0xFF),
            (0xBC, 0xBC, 0xBC), (0xBC, 0xBC, 0xBC), (0xBC, 0xBC, 0xBC),
            (0x75, 0x75, 0x75), (0x75, 0x75, 0x75), (0x75, 0x75, 0x75),
            (0x00, 0x00, 0x00), (0x00, 0x00, 0x00), (0x00, 0x00, 0x00),
            (0xFF, 0xFF, 0xFF),
            (0xBC, 0xBC, 0xBC),
            (0x75, 0x75, 0x75),
            (0x00, 0x00, 0x00));
        WriteSolidTilesheetPng(
            Path.Combine(directory, "tiles.nes.png"),
            (0xFF, 0xFF, 0xFF), (0xFF, 0xFF, 0xFF), (0xFF, 0xFF, 0xFF),
            (0xBC, 0xBC, 0xBC), (0xBC, 0xBC, 0xBC), (0xBC, 0xBC, 0xBC),
            (0x75, 0x75, 0x75), (0x75, 0x75, 0x75), (0x75, 0x75, 0x75),
            (0x00, 0x00, 0x00), (0x00, 0x00, 0x00), (0x00, 0x00, 0x00),
            (0xAB, 0xE7, 0xFF),
            (0x83, 0xD3, 0x13),
            (0xC8, 0x4C, 0x0C),
            (0xAB, 0x00, 0x13));
        File.WriteAllText(Path.Combine(directory, "level.tsx"), """
        <?xml version="1.0" encoding="UTF-8"?>
        <tileset version="1.10" tiledversion="1.12.2" name="Level" tilewidth="8" tileheight="8" tilecount="16" columns="16">
         <image source="tiles.png" width="128" height="8"/>
        </tileset>
        """);
        File.WriteAllText(Path.Combine(directory, "level.tmj"), """
        {
          "type": "map",
          "orientation": "orthogonal",
          "infinite": false,
          "width": 4,
          "height": 1,
          "tilewidth": 8,
          "tileheight": 8,
          "properties": [
            { "name": "retrosharpStreamY", "type": "int", "value": 0 },
            { "name": "retrosharpWorldY", "type": "int", "value": 0 },
            { "name": "retrosharpWorldHeight", "type": "int", "value": 1 }
          ],
          "tilesets": [
            { "firstgid": 1, "source": "level.tsx" }
          ],
          "layers": [
            { "type": "tilelayer", "name": "world", "width": 4, "height": 1, "data": [13, 14, 15, 16] }
          ]
        }
        """);

        const string source = """
            void Main() {
                Video.Init();
                World.Load("level.tmj");
            }
            """;

        var program = BuildProgram(source);

        var backgroundPalette = program.Palette.Take(16).ToArray();
        Assert.Equal(0x31, backgroundPalette[0]);
        Assert.Contains((byte)0x29, backgroundPalette);
        Assert.Contains((byte)0x17, backgroundPalette);
        Assert.Contains((byte)0x05, backgroundPalette);
    }

    [Fact]
    public void World_load_derives_nes_background_palette_from_imported_world_slice()
    {
        WriteSolidTilesheetPng(
            Path.Combine(directory, "tiles.png"),
            (0xFF, 0xFF, 0xFF), (0xFF, 0xFF, 0xFF), (0xFF, 0xFF, 0xFF),
            (0xBC, 0xBC, 0xBC), (0xBC, 0xBC, 0xBC), (0xBC, 0xBC, 0xBC),
            (0x75, 0x75, 0x75), (0x75, 0x75, 0x75), (0x75, 0x75, 0x75),
            (0x00, 0x00, 0x00), (0x00, 0x00, 0x00), (0x00, 0x00, 0x00),
            (0xFF, 0xFF, 0xFF),
            (0xBC, 0xBC, 0xBC),
            (0x75, 0x75, 0x75),
            (0x00, 0x00, 0x00));
        WriteSolidTilesheetPng(
            Path.Combine(directory, "tiles.nes.png"),
            (0xFF, 0xFF, 0xFF), (0xFF, 0xFF, 0xFF), (0xFF, 0xFF, 0xFF),
            (0xBC, 0xBC, 0xBC), (0xBC, 0xBC, 0xBC), (0xBC, 0xBC, 0xBC),
            (0x75, 0x75, 0x75), (0x75, 0x75, 0x75), (0x75, 0x75, 0x75),
            (0x00, 0x00, 0x00), (0x00, 0x00, 0x00), (0x00, 0x00, 0x00),
            (0xAB, 0xE7, 0xFF),
            (0x83, 0xD3, 0x13),
            (0xC8, 0x4C, 0x0C),
            (0xAB, 0x00, 0x13));
        File.WriteAllText(Path.Combine(directory, "level.tsx"), """
        <?xml version="1.0" encoding="UTF-8"?>
        <tileset version="1.10" tiledversion="1.12.2" name="Level" tilewidth="8" tileheight="8" tilecount="16" columns="16">
         <image source="tiles.png" width="128" height="8"/>
        </tileset>
        """);
        File.WriteAllText(Path.Combine(directory, "level.tmj"), """
        {
          "type": "map",
          "orientation": "orthogonal",
          "infinite": false,
          "width": 16,
          "height": 2,
          "tilewidth": 8,
          "tileheight": 8,
          "properties": [
            { "name": "retrosharpStreamY", "type": "int", "value": 0 },
            { "name": "retrosharpWorldY", "type": "int", "value": 1 },
            { "name": "retrosharpWorldHeight", "type": "int", "value": 1 }
          ],
          "tilesets": [
            { "firstgid": 1, "source": "level.tsx" }
          ],
          "layers": [
            {
              "type": "tilelayer",
              "name": "world",
              "width": 16,
              "height": 2,
              "data": [
                1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 0, 0, 0, 0,
                13, 14, 15, 16, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0
              ]
            }
          ]
        }
        """);

        const string source = """
            void Main() {
                Video.Init();
                World.Load("level.tmj");
            }
            """;

        var program = BuildProgram(source);

        var backgroundPalette = program.Palette.Take(16).ToArray();
        Assert.Equal(0x31, backgroundPalette[0]);
        Assert.Contains((byte)0x29, backgroundPalette);
        Assert.Contains((byte)0x17, backgroundPalette);
        Assert.Contains((byte)0x05, backgroundPalette);
    }

    [Fact]
    public void World_load_writes_nes_background_attribute_slots_for_distinct_tile_palettes()
    {
        WriteTwoPaletteTilesheetPng(Path.Combine(directory, "tiles.png"));
        File.WriteAllText(Path.Combine(directory, "level.tsx"), """
        <?xml version="1.0" encoding="UTF-8"?>
        <tileset version="1.10" tiledversion="1.12.2" name="Level" tilewidth="16" tileheight="16" tilecount="2" columns="2">
         <image source="tiles.png" width="32" height="16"/>
        </tileset>
        """);
        File.WriteAllText(Path.Combine(directory, "level.tmj"), """
        {
          "type": "map",
          "orientation": "orthogonal",
          "infinite": false,
          "width": 2,
          "height": 1,
          "tilewidth": 16,
          "tileheight": 16,
          "properties": [
            { "name": "retrosharpStreamY", "type": "int", "value": 0 },
            { "name": "retrosharpWorldY", "type": "int", "value": 0 },
            { "name": "retrosharpWorldHeight", "type": "int", "value": 1 }
          ],
          "tilesets": [
            { "firstgid": 1, "source": "level.tsx" }
          ],
          "layers": [
            { "type": "tilelayer", "name": "world", "width": 2, "height": 1, "data": [1, 2] }
          ]
        }
        """);

        const string source = """
            void Main() {
                Video.Init();
                World.Load("level.tmj");
            }
            """;

        var program = BuildProgram(source);
        var firstAttributeByte = program.NameTable[960];
        var leftQuadrantSlot = firstAttributeByte & 0x03;
        var rightQuadrantSlot = (firstAttributeByte >> 2) & 0x03;

        Assert.NotEqual(leftQuadrantSlot, rightQuadrantSlot);
    }

    [Fact]
    public void World_load_seeds_background_rows_above_the_streamed_world_band()
    {
        WriteTwoToneTilesheetPng(Path.Combine(directory, "tiles.png"));
        File.WriteAllText(Path.Combine(directory, "level.tmj"), """
        {
          "type": "map",
          "orientation": "orthogonal",
          "infinite": false,
          "width": 1,
          "height": 2,
          "tilewidth": 8,
          "tileheight": 8,
          "properties": [
            { "name": "retrosharpStreamY", "type": "int", "value": 1 },
            { "name": "retrosharpWorldY", "type": "int", "value": 1 },
            { "name": "retrosharpWorldHeight", "type": "int", "value": 1 }
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
            { "type": "tilelayer", "name": "background", "width": 1, "height": 2, "data": [1, 0] },
            { "type": "tilelayer", "name": "world", "width": 1, "height": 2, "data": [0, 0] }
          ]
        }
        """);

        const string source = """
            void Main() {
                Video.Init();
                World.Load("level.tmj");
            }
            """;

        var program = BuildProgram(source);

        Assert.NotEqual(0, program.NameTable[0]);
    }

    [Fact]
    public void Nes_palette_matching_preserves_cyan_sky_hue()
    {
        Assert.Equal(0x2C, NesPalette.NearestIndex(0x56, 0xED, 0xFF));
    }

    [Fact]
    public void World_load_attribute_palette_ties_prefer_lower_row_terrain()
    {
        WriteSkyAndGrassTilesheetPng(Path.Combine(directory, "tiles.png"));
        File.WriteAllText(Path.Combine(directory, "level.tmj"), """
        {
          "type": "map",
          "orientation": "orthogonal",
          "infinite": false,
          "width": 8,
          "height": 2,
          "tilewidth": 8,
          "tileheight": 8,
          "properties": [
            { "name": "retrosharpStreamY", "type": "int", "value": 1 },
            { "name": "retrosharpWorldY", "type": "int", "value": 1 },
            { "name": "retrosharpWorldHeight", "type": "int", "value": 1 }
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
            {
              "type": "tilelayer",
              "name": "background",
              "width": 8,
              "height": 2,
              "data": [
                1, 1, 1, 1, 1, 1, 1, 1,
                0, 0, 0, 0, 0, 0, 0, 0
              ]
            },
            {
              "type": "tilelayer",
              "name": "world",
              "width": 8,
              "height": 2,
              "data": [
                0, 0, 0, 0, 0, 0, 0, 0,
                2, 2, 0, 0, 0, 0, 0, 0
              ]
            }
          ]
        }
        """);

        const string source = """
            void Main() {
                Video.Init();
                World.Load("level.tmj");
            }
            """;

        var program = BuildProgram(source);
        var slot = program.NameTable[960] & 0x03;
        var slotPalette = program.Palette.Skip(slot * 4).Take(4).ToArray();

        Assert.Contains(NesPalette.NearestIndex(0x0F, 0x98, 0x22), slotPalette);
    }

    [Fact]
    public void World_load_attribute_palette_ties_keep_upper_world_object_over_ground()
    {
        WritePipeAndTerrainTilesheetPng(Path.Combine(directory, "tiles.png"));
        File.WriteAllText(Path.Combine(directory, "level.tmj"), """
        {
          "type": "map",
          "orientation": "orthogonal",
          "infinite": false,
          "width": 8,
          "height": 2,
          "tilewidth": 8,
          "tileheight": 8,
          "properties": [
            { "name": "retrosharpStreamY", "type": "int", "value": 0 },
            { "name": "retrosharpWorldY", "type": "int", "value": 0 },
            { "name": "retrosharpWorldHeight", "type": "int", "value": 2 }
          ],
          "tilesets": [
            {
              "firstgid": 1,
              "name": "tiles",
              "tilewidth": 8,
              "tileheight": 8,
              "tilecount": 3,
              "columns": 3,
              "image": "tiles.png",
              "imagewidth": 24,
              "imageheight": 8
            }
          ],
          "layers": [
            {
              "type": "tilelayer",
              "name": "background",
              "width": 8,
              "height": 2,
              "data": [
                1, 1, 1, 1, 1, 1, 1, 1,
                1, 1, 1, 1, 1, 1, 1, 1
              ]
            },
            {
              "type": "tilelayer",
              "name": "world",
              "width": 8,
              "height": 2,
              "data": [
                2, 2, 0, 0, 0, 0, 0, 0,
                3, 3, 0, 0, 0, 0, 0, 0
              ]
            }
          ]
        }
        """);

        const string source = """
            void Main() {
                Video.Init();
                World.Load("level.tmj");
            }
            """;

        var program = BuildProgram(source);
        var slot = program.NameTable[960] & 0x03;
        var slotPalette = program.Palette.Skip(slot * 4).Take(4).ToArray();

        Assert.Contains(NesPalette.NearestIndex(0x10, 0xFF, 0x31), slotPalette);
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
            void Main() {
                Video.Init();
                World.Load("level.tmj");
            }
            """;

        // The same target-neutral Tiled source lowers to a valid ROM on both machines.
        var gbRom = GameBoyRomCompiler.CompileSource(source, directory);
        var nesRom = NesRomCompiler.CompileSource(source, directory);

        Assert.NotEmpty(gbRom);
        Assert.Equal(40976, nesRom.Length);
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

        // A world imported with World.Load feeds the same camera scroll path that
        // World.Map does, so the camera lowers on both targets from one source.
        const string source = """
            void Main() {
                Video.Init();
                World.Load("level.tmj");
                Camera.Init(2, 0, 2);
                while (true) {
                    Video.WaitVBlank();
                    Input.Poll();
                    let cx = button_hold_ticks(right);
                    Camera.SetPosition(cx, 0);
                    Camera.Apply();
                }
            }
            """;

        var gbRom = GameBoyRomCompiler.CompileSource(source, directory);
        var nesRom = NesRomCompiler.CompileSource(source, directory);

        Assert.NotEmpty(gbRom);
        Assert.Equal(40976, nesRom.Length);
        Assert.Equal((byte)'N', nesRom[0]);
    }

    [Fact]
    public void World_load_tall_tiled_map_feeds_nes_runtime_row_streaming()
    {
        WriteTwoToneTilesheetPng(Path.Combine(directory, "tiles.png"));
        var rows = string.Join(", ", Enumerable.Range(0, 61).Select(row => row % 2 + 1));
        File.WriteAllText(Path.Combine(directory, "level.tmj"), $$"""
        {
          "type": "map",
          "orientation": "orthogonal",
          "infinite": false,
          "width": 1,
          "height": 61,
          "tilewidth": 8,
          "tileheight": 8,
          "properties": [
            { "name": "retrosharpStreamY", "type": "int", "value": 0 },
            { "name": "retrosharpWorldY", "type": "int", "value": 0 },
            { "name": "retrosharpWorldHeight", "type": "int", "value": 61 }
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
            { "type": "tilelayer", "name": "world", "width": 1, "height": 61, "data": [{{rows}}] }
          ]
        }
        """);

        const string source = """
            void Main() {
                Video.Init();
                World.Load("level.tmj");
                Camera.Init(1, 0, 60);
                u8 y = 8;
                Camera.SetPosition(0, y);
                Camera.Apply();
            }
            """;

        var rom = NesRomCompiler.CompileSource(source, directory);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.Equal(0x08, rom[6] & 0x08);
        Assert.True(
            ContainsSequence(prg, [0xA4, 0xE3, 0xB1, 0xE8, 0x8D, 0x07, 0x20]),
            "A Tiled World.Load row beyond the initial four-screen surface should stream through the runtime-selected world row pointer.");
        Assert.True(
            ContainsSequence(prg, [0xA9, 0x09, 0x85, 0xEF]),
            "A Tiled World.Load streamed row should refresh the worst-case 9 touched row-attribute bytes.");
    }

    private NesVideoProgram BuildProgram(string source)
    {
        var parse = new SomeParser().Parse(
            SdkLibrarySource.Merge(
                NesTarget.Intrinsics,
                source,
                libraryImportPaths: [SdkImportResolver.Portable2D]));
        Assert.True(parse.IsSuccess, parse.IsFailure ? parse.Error : null);
        var targetProgram = TargetProgramSelector.Select(parse.Value, NesTarget.Intrinsics);
        var actorProgram = ActorFrameworkLowerer.Lower(targetProgram, NesTarget.Capabilities, supportsUpdate: true, supportsDraw: true, directory);
        var lowered = SdkSourcePackageFacadeLowerer.Lower(actorProgram);
        return NesVideoProgram.FromProgram(lowered, directory);
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

    private static void WriteTwoToneTilesheetPng(string path)
    {
        const int width = 16;
        const int height = 8;
        var rgba = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var value = (byte)(x < 4 ? 0x00 : 0xFF);
                var offset = (y * width + x) * 4;
                rgba[offset] = value;
                rgba[offset + 1] = value;
                rgba[offset + 2] = value;
                rgba[offset + 3] = 0xFF;
            }
        }

        File.WriteAllBytes(path, EncodeRgbaPng(width, height, rgba));
    }

    private static void WriteTwoPaletteTilesheetPng(string path)
    {
        const int width = 32;
        const int height = 16;
        var rgba = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                (byte R, byte G, byte B) color;
                if (x < 16)
                {
                    color = x is >= 4 and < 12 && y is >= 4 and < 12
                        ? ((byte)0xFF, (byte)0xFF, (byte)0xFF)
                        : ((byte)0x56, (byte)0xED, (byte)0xFF);
                }
                else
                {
                    color = y < 4
                        ? ((byte)0x10, (byte)0xFF, (byte)0x31)
                        : ((byte)0xF0, (byte)0xC7, (byte)0x27);
                    if (x is 16 or 31 || y == 15)
                    {
                        color = (0x00, 0x00, 0x00);
                    }
                }

                var offset = (y * width + x) * 4;
                rgba[offset] = color.R;
                rgba[offset + 1] = color.G;
                rgba[offset + 2] = color.B;
                rgba[offset + 3] = 0xFF;
            }
        }

        File.WriteAllBytes(path, EncodeRgbaPng(width, height, rgba));
    }

    private static void WriteSkyAndGrassTilesheetPng(string path)
    {
        const int width = 16;
        const int height = 8;
        var rgba = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                (byte R, byte G, byte B) color;
                if (x < 8)
                {
                    color = x < 4
                        ? ((byte)0x56, (byte)0xED, (byte)0xFF)
                        : ((byte)0xFF, (byte)0xFF, (byte)0xFF);
                }
                else
                {
                    color = y < 2
                        ? ((byte)0x0F, (byte)0x98, (byte)0x22)
                        : ((byte)0xF0, (byte)0xC7, (byte)0x27);
                    if (y == 0 && x % 2 == 0)
                    {
                        color = (0x00, 0x00, 0x00);
                    }
                }

                var offset = (y * width + x) * 4;
                rgba[offset] = color.R;
                rgba[offset + 1] = color.G;
                rgba[offset + 2] = color.B;
                rgba[offset + 3] = 0xFF;
            }
        }

        File.WriteAllBytes(path, EncodeRgbaPng(width, height, rgba));
    }

    private static void WritePipeAndTerrainTilesheetPng(string path)
    {
        const int width = 24;
        const int height = 8;
        var rgba = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                (byte R, byte G, byte B) color;
                if (x < 8)
                {
                    color = x < 4
                        ? ((byte)0x56, (byte)0xED, (byte)0xFF)
                        : ((byte)0xFF, (byte)0xFF, (byte)0xFF);
                }
                else if (x < 16)
                {
                    color = ((byte)0x10, (byte)0xFF, (byte)0x31);
                    if (x is 8 or 15 || y is 0 or 7)
                    {
                        color = (0x00, 0x97, 0x00);
                    }
                    if ((x is 9 or 14) && (y is 1 or 6))
                    {
                        color = (0x00, 0x00, 0x00);
                    }
                }
                else
                {
                    color = y < 2
                        ? ((byte)0x0F, (byte)0x98, (byte)0x22)
                        : ((byte)0xF0, (byte)0xC7, (byte)0x27);
                    if (y >= 4 && (x + y) % 3 == 0)
                    {
                        color = (0xFF, 0x9B, 0x3B);
                    }
                    if (y == 0 && x % 2 == 0)
                    {
                        color = (0x00, 0x00, 0x00);
                    }
                }

                var offset = (y * width + x) * 4;
                rgba[offset] = color.R;
                rgba[offset + 1] = color.G;
                rgba[offset + 2] = color.B;
                rgba[offset + 3] = 0xFF;
            }
        }

        File.WriteAllBytes(path, EncodeRgbaPng(width, height, rgba));
    }

    private static void WriteSolidTilesheetPng(string path, params (int R, int G, int B)[] tileColors)
    {
        var width = tileColors.Length * 8;
        const int height = 8;
        var rgba = new byte[width * height * 4];
        for (var tile = 0; tile < tileColors.Length; tile++)
        {
            var color = tileColors[tile];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < 8; x++)
                {
                    var offset = (y * width + tile * 8 + x) * 4;
                    rgba[offset] = (byte)color.R;
                    rgba[offset + 1] = (byte)color.G;
                    rgba[offset + 2] = (byte)color.B;
                    rgba[offset + 3] = 0xFF;
                }
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

    private static bool ContainsSequence(IReadOnlyList<byte> bytes, IReadOnlyList<byte> sequence)
    {
        if (bytes.Count < sequence.Count)
        {
            return false;
        }

        return Enumerable.Range(0, bytes.Count - sequence.Count + 1)
            .Any(offset => sequence.Select((expected, index) => bytes[offset + index] == expected).All(match => match));
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
