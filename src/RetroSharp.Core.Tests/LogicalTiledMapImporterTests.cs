namespace RetroSharp.Core.Tests;

using System.Text.Json;
using RetroSharp.Core.Sdk;
using RetroSharp.Core.Sdk.Tiled;
using Xunit;

public sealed class LogicalTiledMapImporterTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "retrosharp-logical-tiled-" + Guid.NewGuid().ToString("N"));

    public LogicalTiledMapImporterTests() => Directory.CreateDirectory(directory);

    public void Dispose() => Directory.Delete(directory, recursive: true);

    [Fact]
    public void Load_resolves_geometry_world_references_and_collision_flags_without_pixels()
    {
        var path = Path.Combine(directory, "level.tmj");
        File.WriteAllText(path, """
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
          "layers": [
            { "type": "tilelayer", "name": "world", "width": 2, "height": 3, "data": [0, 0, 5, 0, 0, 9] },
            { "type": "tilelayer", "name": "collision", "width": 2, "height": 3, "data": [0, 0, 1, 0, 0, 1] }
          ]
        }
        """);

        var map = LogicalTiledMapImporter.Load(path);

        Assert.Equal(2, map.Geometry.SourceWidth);
        Assert.Equal(3, map.Geometry.SourceHeight);
        Assert.Equal(1, map.Geometry.TileScaleX);
        Assert.Equal(1, map.Geometry.TileScaleY);
        Assert.Equal(2, map.Geometry.Width);
        Assert.Equal(2, map.Geometry.Height);
        Assert.Equal(0, map.Geometry.StreamY);
        Assert.Equal(1, map.Geometry.WorldY);
        Assert.Equal(2, map.Geometry.WorldHeight);

        // The neutral map keeps source-tile GID references, not lowered tile ids.
        Assert.Equal(new uint[] { 0, 0, 5, 0, 0, 9 }, map.WorldGids);
        Assert.Null(map.BackgroundGids);

        // Collision flags are resolved for the expanded world slice (source rows 1 and 2).
        Assert.Equal(
            new[] { WorldTileFlags.Solid, WorldTileFlags.Empty, WorldTileFlags.Empty, WorldTileFlags.Solid },
            map.WorldFlags);
    }

    [Fact]
    public void Load_reads_actor_spawns_from_object_layers_without_target_lowering()
    {
        var path = Path.Combine(directory, "actors.tmj");
        File.WriteAllText(path, """
        {
          "type": "map",
          "orientation": "orthogonal",
          "infinite": false,
          "width": 2,
          "height": 2,
          "tilewidth": 8,
          "tileheight": 8,
          "properties": [
            { "name": "retrosharpStreamY", "type": "int", "value": 0 }
          ],
          "layers": [
            { "type": "tilelayer", "name": "world", "width": 2, "height": 2, "data": [0, 0, 0, 0] },
            {
              "type": "objectgroup",
              "name": "actors",
              "objects": [
                {
                  "id": 1,
                  "type": "Goomba",
                  "x": 24,
                  "y": 40,
                  "properties": [
                    { "name": "facing", "type": "int", "value": 1 },
                    { "name": "health", "type": "int", "value": 2 }
                  ]
                },
                {
                  "id": 2,
                  "x": 280,
                  "y": 32,
                  "properties": [
                    { "name": "kind", "type": "string", "value": "Bat" }
                  ]
                }
              ]
            }
          ]
        }
        """);

        var map = LogicalTiledMapImporter.Load(path);

        var spawns = Assert.Contains("actors", map.ActorSpawnLayers);
        Assert.Collection(
            spawns,
            spawn =>
            {
                Assert.Equal("Goomba", spawn.Kind);
                Assert.Equal(24, spawn.X);
                Assert.Equal(40, spawn.Y);
                Assert.Equal(1, spawn.Fields["facing"]);
                Assert.Equal(2, spawn.Fields["health"]);
            },
            spawn =>
            {
                Assert.Equal("Bat", spawn.Kind);
                Assert.Equal(280, spawn.X);
                Assert.Equal(32, spawn.Y);
                Assert.Empty(spawn.Fields);
            });
    }

    [Fact]
    public void Explicit_collision_layer_overrides_tileset_metadata_in_the_world_pack_plan()
    {
        var path = Path.Combine(directory, "collision-override.tmj");
        File.WriteAllText(path, """
        {
          "type": "map",
          "orientation": "orthogonal",
          "infinite": false,
          "width": 2,
          "height": 1,
          "tilewidth": 8,
          "tileheight": 8,
          "properties": [
            { "name": "retrosharpStreamY", "type": "int", "value": 0 }
          ],
          "tilesets": [
            {
              "firstgid": 1,
              "name": "inline",
              "tilewidth": 8,
              "tileheight": 8,
              "tilecount": 1,
              "columns": 1,
              "tiles": [
                { "id": 0, "objectgroup": { "objects": [ { "width": 8, "height": 8 } ] } }
              ]
            }
          ],
          "layers": [
            { "type": "tilelayer", "name": "world", "width": 2, "height": 1, "data": [1, 1] },
            { "type": "tilelayer", "name": "collision", "width": 2, "height": 1, "data": [0, 2] }
          ]
        }
        """);

        var logical = LogicalTiledMapImporter.Load(path);
        var plan = TiledWorldPackPlan.Create(logical);
        var compiled = plan.Build(new byte[] { 0 }, targetCellStride: 1);

        Assert.Equal(new[] { WorldTileFlags.Empty, WorldTileFlags.Hazard }, logical.WorldFlags);
        Assert.Equal(2, plan.CollisionProfiles.Count);
        Assert.Equal(new ushort[] { 0, 1 }, plan.CollisionProfileIds);
        Assert.Equal(WorldTileFlags.Empty, compiled.Pack.CollisionAt(0, 0));
        Assert.Equal(WorldTileFlags.Hazard, compiled.Pack.CollisionAt(1, 0));
    }

    [Fact]
    public void Load_rounds_fractional_actor_coordinates_to_the_nearest_pixel()
    {
        var path = Path.Combine(directory, "fractional-actors.tmj");
        File.WriteAllText(path, """
        {
          "type": "map",
          "orientation": "orthogonal",
          "infinite": false,
          "width": 2,
          "height": 2,
          "tilewidth": 8,
          "tileheight": 8,
          "properties": [
            { "name": "retrosharpStreamY", "type": "int", "value": 0 }
          ],
          "layers": [
            { "type": "tilelayer", "name": "world", "width": 2, "height": 2, "data": [0, 0, 0, 0] },
            {
              "type": "objectgroup",
              "name": "actors",
              "objects": [
                { "id": 1, "type": "Goomba", "x": 168.5, "y": 40.5 }
              ]
            }
          ]
        }
        """);

        var map = LogicalTiledMapImporter.Load(path);

        var spawn = Assert.Single(Assert.Contains("actors", map.ActorSpawnLayers));
        Assert.Equal("Goomba", spawn.Kind);
        Assert.Equal(169, spawn.X);
        Assert.Equal(41, spawn.Y);
    }

    [Fact]
    public void Load_rejects_maps_without_a_world_layer()
    {
        var path = Path.Combine(directory, "no-world.tmj");
        File.WriteAllText(path, """
        {
          "type": "map",
          "orientation": "orthogonal",
          "infinite": false,
          "width": 1,
          "height": 1,
          "tilewidth": 8,
          "tileheight": 8,
          "properties": [ { "name": "retrosharpStreamY", "type": "int", "value": 0 } ],
          "layers": []
        }
        """);

        var error = Assert.Throws<InvalidOperationException>(() => LogicalTiledMapImporter.Load(path));
        Assert.Contains("requires a tile layer named 'world'", error.Message);
    }

    [Fact]
    public void Load_produces_the_same_logical_map_from_tmj_and_csv_tmx()
    {
        File.WriteAllText(
            Path.Combine(directory, "terrain.tsx"),
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <tileset version="1.10" tiledversion="1.12.2" name="terrain" tilewidth="16" tileheight="16" tilecount="2" columns="2">
              <tile id="0">
                <objectgroup><object id="1" x="0" y="0" width="16" height="16"/></objectgroup>
              </tile>
              <tile id="1">
                <properties><property name="retrosharpCollision" value="platform"/></properties>
              </tile>
            </tileset>
            """);

        var tmjPath = Path.Combine(directory, "level.tmj");
        File.WriteAllText(tmjPath, """
        {
          "type": "map",
          "orientation": "orthogonal",
          "infinite": false,
          "width": 2,
          "height": 2,
          "tilewidth": 16,
          "tileheight": 16,
          "properties": [
            { "name": "retrosharpStreamY", "type": "int", "value": 0 },
            { "name": "retrosharpWorldY", "type": "int", "value": 0 },
            { "name": "retrosharpWorldHeight", "type": "int", "value": 2 }
          ],
          "tilesets": [ { "firstgid": 1, "source": "terrain.tsx" } ],
          "layers": [
            {
              "type": "group",
              "name": "gameplay",
              "layers": [
                { "type": "tilelayer", "name": "background", "width": 2, "height": 2, "data": [0, 1, 0, 2] },
                { "type": "tilelayer", "name": "world", "width": 2, "height": 2, "data": [1, 2, 0, 1] },
                {
                  "type": "objectgroup",
                  "name": "actors",
                  "objects": [
                    {
                      "id": 7,
                      "class": "Goomba",
                      "x": 16.5,
                      "y": 31.5,
                      "properties": [
                        { "name": "active", "type": "bool", "value": true },
                        { "name": "health", "type": "int", "value": 2 }
                      ]
                    }
                  ]
                }
              ]
            }
          ]
        }
        """);

        var tmxPath = Path.Combine(directory, "level.tmx");
        File.WriteAllText(tmxPath, """
        <?xml version="1.0" encoding="UTF-8"?>
        <map version="1.10" tiledversion="1.12.2" orientation="orthogonal" width="2" height="2" tilewidth="16" tileheight="16" infinite="0">
          <properties>
            <property name="retrosharpStreamY" type="int" value="0"/>
            <property name="retrosharpWorldY" type="int" value="0"/>
            <property name="retrosharpWorldHeight" type="int" value="2"/>
          </properties>
          <tileset firstgid="1" source="terrain.tsx"/>
          <group name="gameplay">
            <layer name="background" width="2" height="2"><data encoding="csv">0,1,0,2</data></layer>
            <layer name="world" width="2" height="2"><data encoding="csv">1,2,0,1</data></layer>
            <objectgroup name="actors">
              <object id="7" class="Goomba" x="16.5" y="31.5">
                <properties>
                  <property name="active" type="bool" value="true"/>
                  <property name="health" type="int" value="2"/>
                </properties>
              </object>
            </objectgroup>
          </group>
        </map>
        """);

        var fromJson = LogicalTiledMapImporter.Load(tmjPath);
        var fromXml = LogicalTiledMapImporter.Load(tmxPath);

        AssertEquivalent(fromJson, fromXml);
        Assert.Equal(
            new[]
            {
                WorldTileFlags.Solid, WorldTileFlags.Solid, WorldTileFlags.Platform, WorldTileFlags.Platform,
                WorldTileFlags.Solid, WorldTileFlags.Solid, WorldTileFlags.Platform, WorldTileFlags.Platform,
                WorldTileFlags.Empty, WorldTileFlags.Empty, WorldTileFlags.Solid, WorldTileFlags.Solid,
                WorldTileFlags.Empty, WorldTileFlags.Empty, WorldTileFlags.Solid, WorldTileFlags.Solid,
            },
            fromXml.WorldFlags);
    }

    [Fact]
    public void Load_rejects_non_csv_tmx_tile_data()
    {
        var path = Path.Combine(directory, "base64.tmx");
        File.WriteAllText(path, """
        <?xml version="1.0" encoding="UTF-8"?>
        <map orientation="orthogonal" width="1" height="1" tilewidth="8" tileheight="8" infinite="0">
          <properties><property name="retrosharpStreamY" type="int" value="0"/></properties>
          <layer name="world" width="1" height="1"><data encoding="base64">AAAAAA==</data></layer>
        </map>
        """);

        var error = Assert.Throws<InvalidOperationException>(() => LogicalTiledMapImporter.Load(path));

        Assert.Contains("must use CSV-encoded TMX tile data without compression", error.Message);
    }

    [Fact]
    public void Load_imports_the_versioned_full_stage_fixture()
    {
        var map = LogicalTiledMapImporter.Load(RepositoryFile("validation/fixtures/full-stage1-v1/assets/stage1.tmx"));

        Assert.Equal(156, map.Geometry.SourceWidth);
        Assert.Equal(20, map.Geometry.SourceHeight);
        Assert.Equal(312, map.Geometry.Width);
        Assert.Equal(40, map.Geometry.Height);
        Assert.Equal(3120, map.WorldGids.Length);
        Assert.NotEmpty(map.Tilesets);
    }

    [Fact]
    public void Full_stage_fixture_tileset_declares_the_versioned_collision_flags()
    {
        File.Copy(RepositoryFile("validation/fixtures/full-stage1-v1/assets/stage1.tsx"), Path.Combine(directory, "stage1.tsx"));
        var expectedFlags = new Dictionary<int, WorldTileFlags>
        {
            [6] = WorldTileFlags.Solid,
            [7] = WorldTileFlags.Solid,
            [9] = WorldTileFlags.Solid,
            [10] = WorldTileFlags.Solid,
            [11] = WorldTileFlags.Platform,
            [12] = WorldTileFlags.Platform,
            [13] = WorldTileFlags.Platform,
            [29] = WorldTileFlags.Platform,
            [30] = WorldTileFlags.Platform,
            [31] = WorldTileFlags.Platform,
            [38] = WorldTileFlags.Solid,
            [40] = WorldTileFlags.Solid,
            [41] = WorldTileFlags.Solid,
            [42] = WorldTileFlags.Solid,
            [44] = WorldTileFlags.Solid,
            [50] = WorldTileFlags.Platform,
            [51] = WorldTileFlags.Platform,
            [52] = WorldTileFlags.Platform,
            [83] = WorldTileFlags.Solid,
            [102] = WorldTileFlags.Solid,
            [103] = WorldTileFlags.Solid,
            [104] = WorldTileFlags.Solid,
            [109] = WorldTileFlags.Solid,
        };
        var path = Path.Combine(directory, "collision-contract.tmx");
        File.WriteAllText(path, $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <map orientation="orthogonal" width="{{expectedFlags.Count}}" height="1" tilewidth="16" tileheight="16" infinite="0">
          <properties><property name="retrosharpStreamY" type="int" value="0"/></properties>
          <tileset firstgid="1" source="stage1.tsx"/>
          <layer name="world" width="{{expectedFlags.Count}}" height="1">
            <data encoding="csv">{{string.Join(',', expectedFlags.Keys.Select(id => id + 1))}}</data>
          </layer>
        </map>
        """);

        var map = LogicalTiledMapImporter.Load(path);

        foreach (var (entry, sourceX) in expectedFlags.Select((entry, index) => (entry, index)))
        {
            for (var hardwareY = 0; hardwareY < 2; hardwareY++)
            {
                for (var hardwareX = 0; hardwareX < 2; hardwareX++)
                {
                    Assert.Equal(entry.Value, map.WorldFlags[hardwareY * map.Geometry.Width + sourceX * 2 + hardwareX]);
                }
            }
        }
    }
    private static void AssertEquivalent(LogicalTiledMap expected, LogicalTiledMap actual)
    {
        Assert.Equal(expected.Geometry.SourceWidth, actual.Geometry.SourceWidth);
        Assert.Equal(expected.Geometry.SourceHeight, actual.Geometry.SourceHeight);
        Assert.Equal(expected.Geometry.TileScaleX, actual.Geometry.TileScaleX);
        Assert.Equal(expected.Geometry.TileScaleY, actual.Geometry.TileScaleY);
        Assert.Equal(expected.Geometry.WorldY, actual.Geometry.WorldY);
        Assert.Equal(expected.Geometry.WorldHeight, actual.Geometry.WorldHeight);
        Assert.Equal(expected.Geometry.StreamY, actual.Geometry.StreamY);
        Assert.Equal(expected.Geometry.Width, actual.Geometry.Width);
        Assert.Equal(expected.Geometry.Height, actual.Geometry.Height);
        Assert.Equal(expected.WorldGids, actual.WorldGids);
        Assert.Equal(expected.BackgroundGids, actual.BackgroundGids);
        Assert.Equal(expected.WorldFlags, actual.WorldFlags);

        var expectedTileset = Assert.Single(expected.Tilesets);
        var actualTileset = Assert.Single(actual.Tilesets);
        Assert.Equal(expectedTileset.FirstGid, actualTileset.FirstGid);
        Assert.Equal(expectedTileset.Name, actualTileset.Name);
        Assert.Equal(expectedTileset.TileWidth, actualTileset.TileWidth);
        Assert.Equal(expectedTileset.TileHeight, actualTileset.TileHeight);
        Assert.Equal(expectedTileset.TileCount, actualTileset.TileCount);

        var expectedSpawn = Assert.Single(Assert.Contains("actors", expected.ActorSpawnLayers));
        var actualSpawn = Assert.Single(Assert.Contains("actors", actual.ActorSpawnLayers));
        Assert.Equal(expectedSpawn.Kind, actualSpawn.Kind);
        Assert.Equal(expectedSpawn.X, actualSpawn.X);
        Assert.Equal(expectedSpawn.Y, actualSpawn.Y);
        Assert.Equal(expectedSpawn.Fields, actualSpawn.Fields);
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
}
