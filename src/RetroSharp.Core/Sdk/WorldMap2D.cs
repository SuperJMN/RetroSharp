namespace RetroSharp.Core.Sdk;

public sealed class WorldMap2D
{
    private readonly int[] tileIds;
    private readonly WorldTileFlags[] tileFlags;

    public WorldMap2D(int width, int height, IEnumerable<int> tileIds, IEnumerable<WorldTileFlags> tileFlags)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "World map width must be positive.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "World map height must be positive.");
        }

        ArgumentNullException.ThrowIfNull(tileIds);
        ArgumentNullException.ThrowIfNull(tileFlags);

        Width = width;
        Height = height;
        TileCount = checked(width * height);

        this.tileIds = tileIds.ToArray();
        this.tileFlags = tileFlags.ToArray();

        if (this.tileIds.Length != TileCount)
        {
            throw new ArgumentException($"World map expected {TileCount} tile id value(s), got {this.tileIds.Length}.", nameof(tileIds));
        }

        if (this.tileFlags.Length != TileCount)
        {
            throw new ArgumentException($"World map expected {TileCount} tile flag value(s), got {this.tileFlags.Length}.", nameof(tileFlags));
        }
    }

    public int Width { get; }

    public int Height { get; }

    public int TileCount { get; }

    public int TileIdAt(int x, int y) => tileIds[IndexOf(x, y)];

    public WorldTileFlags FlagsAt(int x, int y) => tileFlags[IndexOf(x, y)];

    public WorldMapTile TileAt(int x, int y)
    {
        var index = IndexOf(x, y);
        return new WorldMapTile(tileIds[index], tileFlags[index]);
    }

    private int IndexOf(int x, int y)
    {
        if (x < 0 || x >= Width)
        {
            throw new ArgumentOutOfRangeException(nameof(x), x, $"World map column must be between 0 and {Width - 1}.");
        }

        if (y < 0 || y >= Height)
        {
            throw new ArgumentOutOfRangeException(nameof(y), y, $"World map row must be between 0 and {Height - 1}.");
        }

        return y * Width + x;
    }
}
