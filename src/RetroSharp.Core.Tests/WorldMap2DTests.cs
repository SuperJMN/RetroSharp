namespace RetroSharp.Core.Tests;

using RetroSharp.Core.Sdk;
using Xunit;

public sealed class WorldMap2DTests
{
    [Fact]
    public void World_map_keeps_dimensions_and_flags()
    {
        var map = new WorldMap2D(
            width: 3,
            height: 2,
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
        Assert.Equal(WorldTileFlags.Hazard, map.FlagsAt(2, 0));
        Assert.Equal(WorldTileFlags.Solid | WorldTileFlags.Hazard, map.FlagsAt(1, 1));
    }

    [Fact]
    public void World_map_rejects_invalid_dimensions_and_data_lengths()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorldMap2D(0, 1, [WorldTileFlags.Empty]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorldMap2D(1, 0, [WorldTileFlags.Empty]));
        Assert.Throws<ArgumentException>(() => new WorldMap2D(2, 2, [WorldTileFlags.Empty, WorldTileFlags.Empty, WorldTileFlags.Empty]));
        Assert.Throws<ArgumentException>(() => new WorldMap2D(2, 2, [WorldTileFlags.Empty, WorldTileFlags.Empty, WorldTileFlags.Empty, WorldTileFlags.Empty, WorldTileFlags.Empty]));
    }

    [Fact]
    public void World_map_rejects_invalid_columns_and_rows()
    {
        var map = new WorldMap2D(
            width: 2,
            height: 2,
            tileFlags: [WorldTileFlags.Empty, WorldTileFlags.Solid, WorldTileFlags.Hazard, WorldTileFlags.Platform]);

        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = map.FlagsAt(-1, 0); });
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = map.FlagsAt(2, 0); });
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = map.FlagsAt(0, -1); });
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = map.FlagsAt(0, 2); });
    }

    [Fact]
    public void World_map_reports_whether_any_requested_flags_exist()
    {
        var map = new WorldMap2D(
            width: 2,
            height: 2,
            tileFlags: [WorldTileFlags.Empty, WorldTileFlags.Solid, WorldTileFlags.Hazard, WorldTileFlags.Empty]);

        Assert.True(map.ContainsAnyFlags(WorldTileFlags.Solid));
        Assert.True(map.ContainsAnyFlags(WorldTileFlags.Solid | WorldTileFlags.Platform));
        Assert.False(map.ContainsAnyFlags(WorldTileFlags.Platform));
        Assert.False(map.ContainsAnyFlags(WorldTileFlags.Empty));
    }

    [Fact]
    public void World_map_reports_row_flag_coverage()
    {
        var map = new WorldMap2D(
            width: 3,
            height: 3,
            tileFlags:
            [
                WorldTileFlags.Empty,
                WorldTileFlags.Empty,
                WorldTileFlags.Empty,
                WorldTileFlags.Solid,
                WorldTileFlags.Solid | WorldTileFlags.Hazard,
                WorldTileFlags.Solid,
                WorldTileFlags.Empty,
                WorldTileFlags.Platform,
                WorldTileFlags.Empty,
            ]);

        Assert.False(map.RowContainsAnyFlags(0, WorldTileFlags.Solid));
        Assert.True(map.RowContainsAnyFlags(1, WorldTileFlags.Solid));
        Assert.True(map.RowContainsAnyFlags(2, WorldTileFlags.Platform));
        Assert.False(map.RowContainsAnyFlags(2, WorldTileFlags.Solid));
        Assert.False(map.RowContainsAnyFlags(1, WorldTileFlags.Empty));

        Assert.True(map.RowAllColumnsContainAnyFlags(1, WorldTileFlags.Solid));
        Assert.True(map.RowAllColumnsContainAnyFlags(1, WorldTileFlags.Solid | WorldTileFlags.Hazard));
        Assert.False(map.RowAllColumnsContainAnyFlags(1, WorldTileFlags.Hazard));
        Assert.False(map.RowAllColumnsContainAnyFlags(2, WorldTileFlags.Platform));
        Assert.False(map.RowAllColumnsContainAnyFlags(1, WorldTileFlags.Empty));

        Assert.Throws<ArgumentOutOfRangeException>(() => map.RowContainsAnyFlags(-1, WorldTileFlags.Solid));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.RowAllColumnsContainAnyFlags(3, WorldTileFlags.Solid));
    }
}

public sealed class WorldTileGridTests
{
    [Fact]
    public void World_tile_grid_keeps_dimensions_and_tile_ids()
    {
        var grid = new WorldTileGrid(width: 3, height: 2, tileIds: [1, 2, 3, 4, 5, 6]);

        Assert.Equal(3, grid.Width);
        Assert.Equal(2, grid.Height);
        Assert.Equal(6, grid.TileCount);
        Assert.Equal(3, grid.TileIdAt(2, 0));
        Assert.Equal(4, grid.TileIdAt(0, 1));
    }

    [Fact]
    public void World_tile_grid_rejects_invalid_dimensions_and_data_lengths()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorldTileGrid(0, 1, [0]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorldTileGrid(1, 0, [0]));
        Assert.Throws<ArgumentException>(() => new WorldTileGrid(2, 2, [0, 1, 2]));
        Assert.Throws<ArgumentException>(() => new WorldTileGrid(2, 2, [0, 1, 2, 3, 4]));
    }

    [Fact]
    public void World_tile_grid_rejects_invalid_columns_and_rows()
    {
        var grid = new WorldTileGrid(width: 2, height: 2, tileIds: [1, 2, 3, 4]);

        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = grid.TileIdAt(-1, 0); });
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = grid.TileIdAt(2, 0); });
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = grid.TileIdAt(0, -1); });
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = grid.TileIdAt(0, 2); });
    }
}
