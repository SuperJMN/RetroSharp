namespace RetroSharp.Core.Sdk;

// Portable world collision resource: dimensions plus per-tile WorldTileFlags. It
// deliberately does NOT carry background tile numbers, which are target-lowered
// render data owned by each target (see WorldTileGrid).
public sealed class WorldMap2D
{
    private readonly WorldTileFlags[] tileFlags;

    public WorldMap2D(int width, int height, IEnumerable<WorldTileFlags> tileFlags)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "World map width must be positive.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "World map height must be positive.");
        }

        ArgumentNullException.ThrowIfNull(tileFlags);

        Width = width;
        Height = height;
        TileCount = checked(width * height);

        this.tileFlags = tileFlags.ToArray();

        if (this.tileFlags.Length != TileCount)
        {
            throw new ArgumentException($"World map expected {TileCount} tile flag value(s), got {this.tileFlags.Length}.", nameof(tileFlags));
        }
    }

    public int Width { get; }

    public int Height { get; }

    public int TileCount { get; }

    public WorldTileFlags FlagsAt(int x, int y) => tileFlags[IndexOf(x, y)];

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
