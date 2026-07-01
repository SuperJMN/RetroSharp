namespace RetroSharp.Core.Sdk;

// A grid of already-lowered, target-specific background tile numbers produced by a
// target's world importer and consumed by that target's rendering path. It is kept
// separate from the portable WorldMap2D so the portable collision resource does not
// carry target tile payload. This is target render data, not a portable SDK promise.
public sealed class WorldTileGrid
{
    private readonly int[] tileIds;

    public WorldTileGrid(int width, int height, IEnumerable<int> tileIds)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "World tile grid width must be positive.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "World tile grid height must be positive.");
        }

        ArgumentNullException.ThrowIfNull(tileIds);

        Width = width;
        Height = height;
        TileCount = checked(width * height);

        this.tileIds = tileIds.ToArray();

        if (this.tileIds.Length != TileCount)
        {
            throw new ArgumentException($"World tile grid expected {TileCount} tile id value(s), got {this.tileIds.Length}.", nameof(tileIds));
        }
    }

    public int Width { get; }

    public int Height { get; }

    public int TileCount { get; }

    public int TileIdAt(int x, int y) => tileIds[IndexOf(x, y)];

    private int IndexOf(int x, int y)
    {
        if (x < 0 || x >= Width)
        {
            throw new ArgumentOutOfRangeException(nameof(x), x, $"World tile grid column must be between 0 and {Width - 1}.");
        }

        if (y < 0 || y >= Height)
        {
            throw new ArgumentOutOfRangeException(nameof(y), y, $"World tile grid row must be between 0 and {Height - 1}.");
        }

        return y * Width + x;
    }
}
