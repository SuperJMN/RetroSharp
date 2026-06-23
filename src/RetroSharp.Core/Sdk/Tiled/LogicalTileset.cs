namespace RetroSharp.Core.Sdk.Tiled;

// Target-neutral descriptor of a Tiled tileset. It carries the structural
// metadata and a resolved image path, but no decoded pixels and no
// target-specific tile encoding. A target backend turns these references into
// its own tile data (Game Boy 2bpp patterns, NES CHR, ...).
public sealed class LogicalTileset
{
    public LogicalTileset(
        int firstGid,
        string name,
        int tileWidth,
        int tileHeight,
        int tileCount,
        int columns,
        string? imagePath,
        IReadOnlyDictionary<int, WorldTileFlags> tileFlags)
    {
        FirstGid = firstGid;
        Name = name;
        TileWidth = tileWidth;
        TileHeight = tileHeight;
        TileCount = tileCount;
        Columns = columns;
        ImagePath = imagePath;
        TileFlags = tileFlags;
    }

    public int FirstGid { get; }

    public string Name { get; }

    public int TileWidth { get; }

    public int TileHeight { get; }

    public int TileCount { get; }

    public int Columns { get; }

    // Absolute path to the tileset image, or null when the tileset declares no image.
    public string? ImagePath { get; }

    public IReadOnlyDictionary<int, WorldTileFlags> TileFlags { get; }

    public WorldTileFlags FlagsForTile(int localId) => TileFlags.GetValueOrDefault(localId, WorldTileFlags.Empty);
}
