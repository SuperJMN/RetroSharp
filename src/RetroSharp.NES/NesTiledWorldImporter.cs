using RetroSharp.Core.Imaging;
using RetroSharp.Core.Sdk;
using RetroSharp.Core.Sdk.Tiled;

namespace RetroSharp.NES;

internal sealed record NesTiledWorld(
    int Width,
    int Height,
    int StreamY,
    int[] WorldTileIds,
    WorldTileFlags[] WorldFlags,
    byte[] GeneratedTileData);

// NES lowering of an imported Tiled map. It consumes the target-neutral
// RetroSharp.Core.Sdk.Tiled.LogicalTiledMap (shared with the Game Boy path) and
// owns the NES specifics: decoding tileset images, generating and deduplicating
// 2bpp planar CHR tiles, expanding source tiles into 8x8 cells, and composing the
// background under blank world cells. The current NES runtime streams horizontal
// worlds through a two-nametable 64-column buffer; vertical streaming remains out
// of scope.
internal static class NesTiledWorldImporter
{
    public static NesTiledWorld Load(string path, int firstGeneratedTile)
    {
        var logical = LogicalTiledMapImporter.Load(path);
        var geometry = logical.Geometry;
        var displayName = Path.GetFileName(path);

        if (geometry.StreamY < 0 || geometry.StreamY + geometry.Height > 30)
        {
            throw new InvalidOperationException($"Tiled map '{displayName}' world slice must fit the visible 30-row NES nametable.");
        }

        var tilesets = logical.Tilesets.Select(NesTileset.FromLogical).ToArray();
        var resolver = new NesTileResolver(tilesets, firstGeneratedTile);

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

        return new NesTiledWorld(
            geometry.Width,
            geometry.Height,
            geometry.StreamY,
            worldTileIds,
            logical.WorldFlags,
            resolver.GeneratedTileData);
    }

    private static int[] GenerateBackgroundTiles(uint[] backgroundGids, LogicalTiledMapGeometry geometry, string displayName, NesTileResolver resolver)
    {
        var width = geometry.SourceWidth;
        var height = geometry.SourceHeight;
        var tileScaleX = geometry.TileScaleX;
        var tileScaleY = geometry.TileScaleY;
        var expandedWidth = geometry.BackgroundWidth;
        var tiles = new int[expandedWidth * geometry.BackgroundHeight];
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
                        tiles[targetY * expandedWidth + targetX] = tileIds[tileY * tileScaleX + tileX];
                    }
                }
            }
        }

        return tiles;
    }

    private sealed class NesTileResolver(IReadOnlyList<NesTileset> tilesets, int firstGeneratedTile)
    {
        private readonly Dictionary<string, int> generatedTileIds = [];
        private readonly List<byte> generatedTileData = [];

        public byte[] GeneratedTileData => generatedTileData.ToArray();

        public int[] TileIdsFromTiledGid(uint gid, int tilesWide, int tilesHigh, string context)
        {
            if (tilesWide <= 0 || tilesHigh <= 0)
            {
                throw new InvalidOperationException($"{context} must expand to at least one NES tile.");
            }

            var cleanGid = LogicalTiledMapImporter.CleanTiledGid(gid, context);
            if (cleanGid == 0)
            {
                return new int[tilesWide * tilesHigh];
            }

            var tileset = FindTileset(cleanGid, context);
            var localId = checked((int)cleanGid - tileset.FirstGid);
            return tileset.BuildNesTiles(localId, tilesWide, tilesHigh, context)
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

            var tileId = firstGeneratedTile + generatedTileData.Count / 16;
            if (tileId > 255)
            {
                throw new InvalidOperationException($"{context} exceeds the NES 256 background tile index range.");
            }

            generatedTileIds.Add(key, tileId);
            generatedTileData.AddRange(pattern);
            return tileId;
        }

        private NesTileset FindTileset(uint cleanGid, string context)
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

    private sealed class NesTileset
    {
        private NesTileset(int firstGid, string name, int tileWidth, int tileHeight, int tileCount, int columns, PngImage? image)
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

        public static NesTileset FromLogical(LogicalTileset tileset)
        {
            var image = tileset.ImagePath is null ? null : PngImage.Read(tileset.ImagePath);
            return new NesTileset(tileset.FirstGid, tileset.Name, tileset.TileWidth, tileset.TileHeight, tileset.TileCount, tileset.Columns, image);
        }

        public IReadOnlyList<byte[]> BuildNesTiles(int localId, int tilesWide, int tilesHigh, string context)
        {
            if (Image is null)
            {
                throw new InvalidOperationException($"{context} references tileset '{Name}', but it has no image source.");
            }

            var canonical = CanonicalTileGenerator.BuildTiles(Image, localId, TileCount, Columns, TileWidth, TileHeight, tilesWide, tilesHigh, context);
            return canonical.Select(EncodeNesTile).ToArray();
        }

        // Encodes a canonical 8x8 four-tone pattern into the NES 2bpp planar tile
        // layout (bit plane 0 in bytes 0..7, bit plane 1 in bytes 8..15).
        private static byte[] EncodeNesTile(byte[] pattern)
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

                tile[row] = (byte)plane0;
                tile[row + 8] = (byte)plane1;
            }

            return tile;
        }
    }
}
