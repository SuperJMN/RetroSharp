namespace RetroSharp.Core.Sdk.Tiled;

// Target-neutral logical model of an imported Tiled map. It holds parsed and
// validated structure: tileset references, raw layer GIDs at source-tile
// granularity, resolved collision flags for the playable world slice, and the
// geometry needed to lower the map to a tile machine. It deliberately holds no
// decoded pixels and no target tile ids; pixel generation, deduplication, and
// per-pixel layer composition stay in each target backend.
public sealed class LogicalTiledMap
{
    public LogicalTiledMap(
        IReadOnlyList<LogicalTileset> tilesets,
        uint[]? backgroundGids,
        uint[] worldGids,
        WorldTileFlags[] worldFlags,
        LogicalTiledMapGeometry geometry,
        IReadOnlyDictionary<string, IReadOnlyList<LogicalActorSpawn>>? actorSpawnLayers = null)
    {
        ArgumentNullException.ThrowIfNull(tilesets);
        ArgumentNullException.ThrowIfNull(worldGids);
        ArgumentNullException.ThrowIfNull(worldFlags);
        ArgumentNullException.ThrowIfNull(geometry);

        var sourceCells = checked(geometry.SourceWidth * geometry.SourceHeight);
        if (worldGids.Length != sourceCells)
        {
            throw new ArgumentException($"Logical Tiled map expected {sourceCells} world GID(s), got {worldGids.Length}.", nameof(worldGids));
        }

        if (backgroundGids is not null && backgroundGids.Length != sourceCells)
        {
            throw new ArgumentException($"Logical Tiled map expected {sourceCells} background GID(s), got {backgroundGids.Length}.", nameof(backgroundGids));
        }

        var sliceCells = checked(geometry.Width * geometry.Height);
        if (worldFlags.Length != sliceCells)
        {
            throw new ArgumentException($"Logical Tiled map expected {sliceCells} world flag(s), got {worldFlags.Length}.", nameof(worldFlags));
        }

        Tilesets = tilesets;
        BackgroundGids = backgroundGids;
        WorldGids = worldGids;
        WorldFlags = worldFlags;
        Geometry = geometry;
        ActorSpawnLayers = actorSpawnLayers ?? new Dictionary<string, IReadOnlyList<LogicalActorSpawn>>(StringComparer.Ordinal);
    }

    public IReadOnlyList<LogicalTileset> Tilesets { get; }

    // Background layer GIDs at source-tile granularity (SourceWidth * SourceHeight),
    // or null when the map declares no background layer.
    public uint[]? BackgroundGids { get; }

    // World layer GIDs at source-tile granularity (SourceWidth * SourceHeight).
    public uint[] WorldGids { get; }

    // Resolved collision flags for the expanded world slice (Width * Height).
    public WorldTileFlags[] WorldFlags { get; }

    public LogicalTiledMapGeometry Geometry { get; }

    public IReadOnlyDictionary<string, IReadOnlyList<LogicalActorSpawn>> ActorSpawnLayers { get; }
}

public sealed record LogicalActorSpawn(
    string Kind,
    int X,
    int Y,
    IReadOnlyDictionary<string, int> Fields);

// Geometry of an imported Tiled map. Source dimensions are in Tiled source tiles;
// expanded dimensions are in 8x8 cells, the portable tile unit shared by tile machines.
public sealed class LogicalTiledMapGeometry
{
    public LogicalTiledMapGeometry(
        int sourceWidth,
        int sourceHeight,
        int tileScaleX,
        int tileScaleY,
        int worldY,
        int worldHeight,
        int streamY,
        int width,
        int height,
        int expandedWorldY,
        int backgroundWidth,
        int backgroundHeight,
        int backgroundOffsetY)
    {
        SourceWidth = sourceWidth;
        SourceHeight = sourceHeight;
        TileScaleX = tileScaleX;
        TileScaleY = tileScaleY;
        WorldY = worldY;
        WorldHeight = worldHeight;
        StreamY = streamY;
        Width = width;
        Height = height;
        ExpandedWorldY = expandedWorldY;
        BackgroundWidth = backgroundWidth;
        BackgroundHeight = backgroundHeight;
        BackgroundOffsetY = backgroundOffsetY;
    }

    // Source-tile dimensions of the full map.
    public int SourceWidth { get; }

    public int SourceHeight { get; }

    // Number of 8x8 cells per source tile on each axis.
    public int TileScaleX { get; }

    public int TileScaleY { get; }

    // Source-tile row where the playable world slice starts, and its source-tile height.
    public int WorldY { get; }

    public int WorldHeight { get; }

    // Expanded (8x8-cell) world slice geometry.
    public int StreamY { get; }

    public int Width { get; }

    public int Height { get; }

    public int ExpandedWorldY { get; }

    // Expanded background layer geometry.
    public int BackgroundWidth { get; }

    public int BackgroundHeight { get; }

    public int BackgroundOffsetY { get; }
}
