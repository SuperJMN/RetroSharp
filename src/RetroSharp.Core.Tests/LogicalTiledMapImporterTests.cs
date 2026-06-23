namespace RetroSharp.Core.Tests;

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
}
