using RetroSharp.Core.Imaging;
using RetroSharp.Core.Sdk;
using RetroSharp.Core.Sdk.Tiled;

namespace RetroSharp.GameBoy;

internal sealed record GameBoyTiledMap(
    int Width,
    int Height,
    int StreamY,
    int BackgroundWidth,
    int BackgroundHeight,
    int BackgroundOffsetY,
    byte[]? BackgroundTileIds,
    int[] WorldTileIds,
    WorldTileFlags[] WorldFlags,
    byte[] GeneratedTileData);

// Game Boy lowering of an imported Tiled map. The target-neutral structure
// (parsing, tilesets, geometry, world slice, and collision flags) is produced by
// RetroSharp.Core.Sdk.Tiled.LogicalTiledMapImporter. This stage owns only the
// Game Boy specifics: decoding tileset images, generating and deduplicating 2bpp
// tile patterns, expanding source tiles into 8x8 cells, and composing the
// background under blank world cells.
internal static class GameBoyTiledMapImporter
{
    public static GameBoyTiledMap Load(string path, int firstGeneratedTileId = 6)
    {
        var logical = LogicalTiledMapImporter.Load(path);
        var geometry = logical.Geometry;
        var displayName = Path.GetFileName(path);

        if (geometry.StreamY is < 0 or > 31)
        {
            throw new InvalidOperationException($"Tiled map '{displayName}' property 'retrosharpStreamY' must be between 0 and 31.");
        }

        if (geometry.StreamY + geometry.Height > 32)
        {
            throw new InvalidOperationException($"Tiled map '{displayName}' world slice exceeds the Game Boy background tilemap height.");
        }

        var tilesets = logical.Tilesets.Select(GameBoyTileset.FromLogical).ToArray();
        var resolver = new GameBoyTileResolver(tilesets, firstGeneratedTileId);

        var expandedWidth = geometry.Width;
        var tileScaleX = geometry.TileScaleX;
        var tileScaleY = geometry.TileScaleY;

        var backgroundTiles = logical.BackgroundGids is null
            ? null
            : GenerateBackgroundTiles(logical.BackgroundGids, geometry, displayName, resolver);

        var worldTileIds = new int[expandedWidth * geometry.Height];
        for (var y = 0; y < geometry.WorldHeight; y++)
        {
            var sourceY = geometry.WorldY + y;
            for (var x = 0; x < geometry.SourceWidth; x++)
            {
                var sourceIndex = sourceY * geometry.SourceWidth + x;
                var context = $"{displayName} world layer tile ({x}, {sourceY})";
                var tileIds = resolver.TileIdsFromTiledGid(logical.WorldGids[sourceIndex], tileScaleX, tileScaleY, context);

                for (var tileY = 0; tileY < tileScaleY; tileY++)
                {
                    for (var tileX = 0; tileX < tileScaleX; tileX++)
                    {
                        var targetX = x * tileScaleX + tileX;
                        var targetY = y * tileScaleY + tileY;
                        var targetIndex = targetY * expandedWidth + targetX;
                        var tileId = tileIds[tileY * tileScaleX + tileX];
                        if (tileId == 0 && backgroundTiles is not null)
                        {
                            var backgroundY = geometry.ExpandedWorldY + targetY;
                            if (backgroundY >= 0 && backgroundY < geometry.BackgroundHeight)
                            {
                                tileId = backgroundTiles[backgroundY * expandedWidth + targetX];
                            }
                        }

                        worldTileIds[targetIndex] = tileId;
                    }
                }
            }
        }

        return new GameBoyTiledMap(
            geometry.Width,
            geometry.Height,
            geometry.StreamY,
            geometry.BackgroundWidth,
            geometry.BackgroundHeight,
            geometry.BackgroundOffsetY,
            backgroundTiles,
            worldTileIds,
            logical.WorldFlags,
            resolver.GeneratedTileData);
    }

    private static byte[] GenerateBackgroundTiles(uint[] backgroundGids, LogicalTiledMapGeometry geometry, string displayName, GameBoyTileResolver resolver)
    {
        var width = geometry.SourceWidth;
        var height = geometry.SourceHeight;
        var tileScaleX = geometry.TileScaleX;
        var tileScaleY = geometry.TileScaleY;
        var expandedWidth = geometry.BackgroundWidth;
        var tiles = new byte[expandedWidth * geometry.BackgroundHeight];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var sourceIndex = y * width + x;
                var tileIds = resolver.TileIdsFromTiledGid(backgroundGids[sourceIndex], tileScaleX, tileScaleY, $"{displayName} background layer tile ({x}, {y})");
                for (var tileY = 0; tileY < tileScaleY; tileY++)
                {
                    for (var tileX = 0; tileX < tileScaleX; tileX++)
                    {
                        var targetX = x * tileScaleX + tileX;
                        var targetY = y * tileScaleY + tileY;
                        tiles[targetY * expandedWidth + targetX] = (byte)tileIds[tileY * tileScaleX + tileX];
                    }
                }
            }
        }

        return tiles;
    }

    private sealed class GameBoyTileResolver(IReadOnlyList<GameBoyTileset> tilesets, int firstGeneratedTileId)
    {
        private readonly Dictionary<string, int> generatedTileIds = [];
        private readonly List<byte> generatedTileData = [];

        public byte[] GeneratedTileData => generatedTileData.ToArray();

        public int[] TileIdsFromTiledGid(uint gid, int tilesWide, int tilesHigh, string context)
        {
            if (tilesWide <= 0 || tilesHigh <= 0)
            {
                throw new InvalidOperationException($"{context} must expand to at least one Game Boy tile.");
            }

            var cleanGid = LogicalTiledMapImporter.CleanTiledGid(gid, context);
            if (cleanGid == 0)
            {
                return new int[tilesWide * tilesHigh];
            }

            var tileset = FindTileset(cleanGid, context);
            var localId = checked((int)cleanGid - tileset.FirstGid);
            return tileset.BuildGameBoyTiles(localId, tilesWide, tilesHigh, context)
                .Select(pattern => TileIdForPattern(pattern, context))
                .ToArray();
        }

        private int TileIdForPattern(byte[] pattern, string context)
        {
            if (pattern.All(value => value == 0))
            {
                return 0;
            }

            var key = Convert.ToHexString(pattern);
            if (generatedTileIds.TryGetValue(key, out var existing))
            {
                return existing;
            }

            var tileId = firstGeneratedTileId + generatedTileData.Count / 16;
            if (tileId > 255)
            {
                throw new InvalidOperationException($"{context} exceeds the Game Boy 256 tile index range.");
            }

            generatedTileIds.Add(key, tileId);
            generatedTileData.AddRange(pattern);
            return tileId;
        }

        private GameBoyTileset FindTileset(uint cleanGid, string context)
        {
            for (var i = tilesets.Count - 1; i >= 0; i--)
            {
                var tileset = tilesets[i];
                var gid = checked((int)cleanGid);
                if (gid >= tileset.FirstGid && gid < tileset.FirstGid + tileset.TileCount)
                {
                    return tileset;
                }
            }

            throw new InvalidOperationException($"{context} references tile gid {cleanGid}, which is outside every tileset.");
        }
    }

    private sealed class GameBoyTileset
    {
        private GameBoyTileset(int firstGid, string name, int tileWidth, int tileHeight, int tileCount, int columns, PngImage? image)
        {
            FirstGid = firstGid;
            Name = name;
            TileWidth = tileWidth;
            TileHeight = tileHeight;
            TileCount = tileCount;
            Columns = columns;
            Image = image;
        }

        public int FirstGid { get; }

        public string Name { get; }

        public int TileWidth { get; }

        public int TileHeight { get; }

        public int TileCount { get; }

        public int Columns { get; }

        public PngImage? Image { get; }

        public static GameBoyTileset FromLogical(LogicalTileset tileset)
        {
            var image = tileset.ImagePath is null ? null : PngImage.Read(tileset.ImagePath);
            return new GameBoyTileset(tileset.FirstGid, tileset.Name, tileset.TileWidth, tileset.TileHeight, tileset.TileCount, tileset.Columns, image);
        }

        public IReadOnlyList<byte[]> BuildGameBoyTiles(int localId, int tilesWide, int tilesHigh, string context)
        {
            if (Image is null)
            {
                throw new InvalidOperationException($"{context} references tileset '{Name}', but it has no image source.");
            }

            var canonical = CanonicalTileGenerator.BuildTiles(Image, localId, TileCount, Columns, TileWidth, TileHeight, tilesWide, tilesHigh, context);
            return canonical.Select(EncodeGameBoyTile).ToArray();
        }

        // Encodes a canonical 8x8 four-tone pattern into the Game Boy 2bpp tile byte
        // layout (two interleaved bit planes per row).
        private static byte[] EncodeGameBoyTile(byte[] pattern)
        {
            var tile = new byte[16];
            for (var row = 0; row < 8; row++)
            {
                var plane0 = 0;
                var plane1 = 0;
                for (var col = 0; col < 8; col++)
                {
                    var tone = pattern[row * 8 + col];
                    var bit = 7 - col;
                    if ((tone & 1) != 0) plane0 |= 1 << bit;
                    if ((tone & 2) != 0) plane1 |= 1 << bit;
                }

                tile[row * 2] = (byte)plane0;
                tile[row * 2 + 1] = (byte)plane1;
            }

            return tile;
        }
    }
}
