namespace RetroSharp.Core.Tests;

using RetroSharp.Core.Sdk;
using Xunit;

public sealed class WorldMap2DTests
{
    [Fact]
    public void World_map_keeps_dimensions_tile_ids_and_flags()
    {
        var map = new WorldMap2D(
            width: 3,
            height: 2,
            tileIds: [1, 2, 3, 4, 5, 6],
            tileFlags:
            [
                WorldTileFlags.Empty,
                WorldTileFlags.Solid,
                WorldTileFlags.Hazard,
                WorldTileFlags.Platform,
                WorldTileFlags.Solid | WorldTileFlags.Hazard,
                WorldTileFlags.Empty,
            ]);

        Assert.Equal(3, map.Width);
        Assert.Equal(2, map.Height);
        Assert.Equal(6, map.TileCount);
        Assert.Equal(4, map.TileIdAt(0, 1));
        Assert.Equal(WorldTileFlags.Solid | WorldTileFlags.Hazard, map.FlagsAt(1, 1));
        Assert.Equal(new WorldMapTile(TileId: 3, Flags: WorldTileFlags.Hazard), map.TileAt(2, 0));
    }

    [Fact]
    public void World_map_rejects_invalid_dimensions_and_data_lengths()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorldMap2D(0, 1, [0], [WorldTileFlags.Empty]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorldMap2D(1, 0, [0], [WorldTileFlags.Empty]));
        Assert.Throws<ArgumentException>(() => new WorldMap2D(2, 2, [0, 1, 2], [WorldTileFlags.Empty, WorldTileFlags.Empty, WorldTileFlags.Empty, WorldTileFlags.Empty]));
        Assert.Throws<ArgumentException>(() => new WorldMap2D(2, 2, [0, 1, 2, 3], [WorldTileFlags.Empty, WorldTileFlags.Empty, WorldTileFlags.Empty]));
    }

    [Fact]
    public void World_map_rejects_invalid_columns_and_rows()
    {
        var map = new WorldMap2D(
            width: 2,
            height: 2,
            tileIds: [1, 2, 3, 4],
            tileFlags: [WorldTileFlags.Empty, WorldTileFlags.Solid, WorldTileFlags.Hazard, WorldTileFlags.Platform]);

        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = map.TileIdAt(-1, 0); });
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = map.TileIdAt(2, 0); });
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = map.FlagsAt(0, -1); });
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = map.FlagsAt(0, 2); });
    }
}
