using RetroSharp.Core.Imaging;
using RetroSharp.Core.Sdk;
using RetroSharp.Core.Sdk.Tiled;

namespace RetroSharp.NES;

internal sealed record NesTiledWorld(
    int Width,
    int Height,
    int StreamY,
    int BackgroundWidth,
    int BackgroundHeight,
    int BackgroundOffsetY,
    int[]? BackgroundTileIds,
    byte[]? BackgroundPaletteSlots,
    int[] WorldTileIds,
    byte[] WorldPaletteSlots,
    byte[] WorldSourceTiles,
    WorldTileFlags[] WorldFlags,
    byte[] GeneratedTileData,
    byte[] BackgroundPalette);

// NES lowering of an imported Tiled map. It consumes the target-neutral
// RetroSharp.Core.Sdk.Tiled.LogicalTiledMap (shared with the Game Boy path) and
// owns the NES specifics: decoding tileset images, generating and deduplicating
// 2bpp planar CHR tiles, expanding source tiles into 8x8 cells, and composing the
// background under blank world cells. The current NES runtime streams horizontal
// worlds through a two-nametable 64-column buffer, and vertical camera programs
// use the shared four-screen background buffer plus row streaming when needed.
internal static class NesTiledWorldImporter
{
    public static NesTiledWorld Load(string path, int firstGeneratedTile)
    {
        var logical = LogicalTiledMapImporter.Load(path);
        var geometry = logical.Geometry;
        var displayName = Path.GetFileName(path);

        var tilesets = logical.Tilesets.Select(NesTileset.FromLogical).ToArray();
        var palettePlan = NesBackgroundPalettePlan.Derive(logical, tilesets, displayName);
        var resolver = new NesTileResolver(tilesets, palettePlan, firstGeneratedTile);

        var expandedWidth = geometry.Width;
        var tileScaleX = geometry.TileScaleX;
        var tileScaleY = geometry.TileScaleY;

        var backgroundTiles = logical.BackgroundGids is null
            ? null
            : GenerateBackgroundTiles(logical.BackgroundGids, geometry, displayName, resolver);

        var worldTileIds = new int[expandedWidth * geometry.Height];
        var worldPaletteSlots = new byte[expandedWidth * geometry.Height];
        var worldSourceTiles = new byte[expandedWidth * geometry.Height];
        for (var y = 0; y < geometry.WorldHeight; y++)
        {
            var sourceY = geometry.WorldY + y;
            for (var x = 0; x < geometry.SourceWidth; x++)
            {
                var sourceIndex = sourceY * geometry.SourceWidth + x;
                var context = $"{displayName} world layer tile ({x}, {sourceY})";
                var cleanGid = LogicalTiledMapImporter.CleanTiledGid(logical.WorldGids[sourceIndex], context);
                var sourceTiles = resolver.TilesFromTiledGid(logical.WorldGids[sourceIndex], tileScaleX, tileScaleY, context);

                for (var tileY = 0; tileY < tileScaleY; tileY++)
                {
                    for (var tileX = 0; tileX < tileScaleX; tileX++)
                    {
                        var targetX = x * tileScaleX + tileX;
                        var targetY = y * tileScaleY + tileY;
                        var targetIndex = targetY * expandedWidth + targetX;
                        var tileId = sourceTiles.TileIds[tileY * tileScaleX + tileX];
                        var paletteSlot = sourceTiles.PaletteSlots[tileY * tileScaleX + tileX];
                        var fromWorldLayer = cleanGid != 0 && tileId != 0;
                        if (tileId == 0 && backgroundTiles is not null)
                        {
                            var backgroundY = geometry.ExpandedWorldY + targetY;
                            if (backgroundY >= 0 && backgroundY < geometry.BackgroundHeight)
                            {
                                var backgroundIndex = backgroundY * expandedWidth + targetX;
                                tileId = backgroundTiles.TileIds[backgroundIndex];
                                paletteSlot = backgroundTiles.PaletteSlots[backgroundIndex];
                                fromWorldLayer = false;
                            }
                        }

                        worldTileIds[targetIndex] = tileId;
                        worldPaletteSlots[targetIndex] = paletteSlot;
                        worldSourceTiles[targetIndex] = fromWorldLayer ? (byte)1 : (byte)0;
                    }
                }
            }
        }

        return new NesTiledWorld(
            geometry.Width,
            geometry.Height,
            geometry.StreamY,
            geometry.BackgroundWidth,
            geometry.BackgroundHeight,
            geometry.BackgroundOffsetY,
            backgroundTiles?.TileIds,
            backgroundTiles?.PaletteSlots,
            worldTileIds,
            worldPaletteSlots,
            worldSourceTiles,
            logical.WorldFlags,
            resolver.GeneratedTileData,
            palettePlan.PaletteRam);
    }

    private static ResolvedTiles GenerateBackgroundTiles(uint[] backgroundGids, LogicalTiledMapGeometry geometry, string displayName, NesTileResolver resolver)
    {
        var width = geometry.SourceWidth;
        var height = geometry.SourceHeight;
        var tileScaleX = geometry.TileScaleX;
        var tileScaleY = geometry.TileScaleY;
        var expandedWidth = geometry.BackgroundWidth;
        var tiles = new int[expandedWidth * geometry.BackgroundHeight];
        var paletteSlots = new byte[expandedWidth * geometry.BackgroundHeight];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var sourceIndex = y * width + x;
                var sourceTiles = resolver.TilesFromTiledGid(backgroundGids[sourceIndex], tileScaleX, tileScaleY, $"{displayName} background layer tile ({x}, {y})");
                for (var tileY = 0; tileY < tileScaleY; tileY++)
                {
                    for (var tileX = 0; tileX < tileScaleX; tileX++)
                    {
                        var targetX = x * tileScaleX + tileX;
                        var targetY = y * tileScaleY + tileY;
                        var sourceTileIndex = tileY * tileScaleX + tileX;
                        var targetIndex = targetY * expandedWidth + targetX;
                        tiles[targetIndex] = sourceTiles.TileIds[sourceTileIndex];
                        paletteSlots[targetIndex] = sourceTiles.PaletteSlots[sourceTileIndex];
                    }
                }
            }
        }

        return new ResolvedTiles(tiles, paletteSlots);
    }

    private sealed record ResolvedTiles(int[] TileIds, byte[] PaletteSlots);

    private sealed class NesBackgroundPalettePlan
    {
        private readonly Dictionary<uint, TilePaletteAssignment> assignments;
        private readonly TilePaletteAssignment fallbackAssignment;

        private NesBackgroundPalettePlan(byte[] paletteRam, Dictionary<uint, TilePaletteAssignment> assignments, TilePaletteAssignment fallbackAssignment)
        {
            PaletteRam = paletteRam;
            this.assignments = assignments;
            this.fallbackAssignment = fallbackAssignment;
        }

        public byte[] PaletteRam { get; }

        public TilePaletteAssignment AssignmentFor(uint cleanGid)
        {
            return assignments.TryGetValue(cleanGid, out var assignment)
                ? assignment
                : fallbackAssignment;
        }

        public static NesBackgroundPalettePlan Derive(LogicalTiledMap logical, IReadOnlyList<NesTileset> tilesets, string displayName)
        {
            var placementWeights = PalettePlacementWeights(logical, displayName);
            var profiles = new Dictionary<uint, Dictionary<int, ColorCount>>();
            foreach (var cleanGid in placementWeights.Keys)
            {
                profiles.Add(cleanGid, CountTileColors(tilesets, cleanGid, $"{displayName} palette tile gid {cleanGid}"));
            }

            var globalCounts = WeightedColorCounts(profiles, placementWeights);
            if (globalCounts.Count == 0)
            {
                var fallbackColors = new[]
                {
                    new SourceColor(0xFF, 0xFF, 0xFF),
                    new SourceColor(0xBC, 0xBC, 0xBC),
                    new SourceColor(0x75, 0x75, 0x75),
                    new SourceColor(0x00, 0x00, 0x00),
                };
                return new NesBackgroundPalettePlan(
                    [0x30, 0x10, 0x00, 0x0F, 0x30, 0x10, 0x00, 0x0F, 0x30, 0x10, 0x00, 0x0F, 0x30, 0x10, 0x00, 0x0F],
                    [],
                    new TilePaletteAssignment(0, fallbackColors));
            }

            var universal = SelectUniversalColor(globalCounts);
            var candidates = PaletteCandidates(profiles, universal);
            var selected = SelectPalettes(profiles, placementWeights, universal, candidates);
            var paletteRam = BuildPaletteRam(universal, selected);

            var fallbackAssignment = new TilePaletteAssignment(0, AssignmentColors(universal, selected.Count == 0 ? [] : selected[0]));
            var assignments = new Dictionary<uint, TilePaletteAssignment>();
            foreach (var (cleanGid, profile) in profiles)
            {
                if (selected.Count == 0)
                {
                    assignments.Add(cleanGid, fallbackAssignment);
                    continue;
                }

                var slot = Enumerable.Range(0, selected.Count)
                    .OrderBy(index => TileError(profile, universal, selected[index]))
                    .ThenBy(index => index)
                    .First();
                assignments.Add(cleanGid, new TilePaletteAssignment(slot, AssignmentColors(universal, selected[slot])));
            }

            return new NesBackgroundPalettePlan(paletteRam, assignments, fallbackAssignment);
        }

        private static Dictionary<uint, int> PalettePlacementWeights(LogicalTiledMap logical, string displayName)
        {
            var result = new Dictionary<uint, int>();
            var geometry = logical.Geometry;
            AddPlacementWeights(result, logical.BackgroundGids, geometry.SourceWidth, 0, geometry.SourceHeight, displayName, "background");

            for (var y = geometry.WorldY; y < geometry.WorldY + geometry.WorldHeight; y++)
            {
                for (var x = 0; x < geometry.SourceWidth; x++)
                {
                    var index = y * geometry.SourceWidth + x;
                    var worldGid = CleanLayerGid(logical.WorldGids[index], displayName, "world", x, y);
                    var backgroundGid = logical.BackgroundGids is null
                        ? 0
                        : CleanLayerGid(logical.BackgroundGids[index], displayName, "background", x, y);
                    var visualGid = worldGid == 0 ? backgroundGid : worldGid;
                    if (visualGid != 0)
                    {
                        result[visualGid] = result.TryGetValue(visualGid, out var count) ? count + 1 : 1;
                    }
                }
            }

            return result;
        }

        private static void AddPlacementWeights(Dictionary<uint, int> result, uint[]? gids, int width, int startY, int height, string displayName, string layerName)
        {
            if (gids is null)
            {
                return;
            }

            for (var y = startY; y < startY + height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var cleanGid = CleanLayerGid(gids[y * width + x], displayName, layerName, x, y);
                    if (cleanGid != 0)
                    {
                        result[cleanGid] = result.TryGetValue(cleanGid, out var count) ? count + 1 : 1;
                    }
                }
            }
        }

        private static uint CleanLayerGid(uint gid, string displayName, string layerName, int x, int y)
        {
            return LogicalTiledMapImporter.CleanTiledGid(gid, $"{displayName} {layerName} layer tile ({x}, {y})");
        }

        private static Dictionary<int, ColorCount> CountTileColors(IReadOnlyList<NesTileset> tilesets, uint cleanGid, string context)
        {
            var tileset = FindTileset(tilesets, cleanGid, context);
            return tileset.CountColors(cleanGid, context);
        }

        private static NesTileset FindTileset(IReadOnlyList<NesTileset> tilesets, uint cleanGid, string context)
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

        private static Dictionary<int, WeightedColorCount> WeightedColorCounts(
            IReadOnlyDictionary<uint, Dictionary<int, ColorCount>> profiles,
            IReadOnlyDictionary<uint, int> placementWeights)
        {
            var result = new Dictionary<int, WeightedColorCount>();
            foreach (var (cleanGid, profile) in profiles)
            {
                var weight = placementWeights[cleanGid];
                foreach (var (key, count) in profile)
                {
                    var weightedCount = (long)count.Count * weight;
                    result[key] = result.TryGetValue(key, out var existing)
                        ? existing.Add(weightedCount)
                        : new WeightedColorCount(count.Color, weightedCount);
                }
            }

            return result;
        }

        private static SourceColor SelectUniversalColor(IReadOnlyDictionary<int, WeightedColorCount> counts)
        {
            return counts.Values
                .OrderByDescending(count => count.Count)
                .ThenByDescending(count => NesPalette.Luminance(count.Color.R, count.Color.G, count.Color.B))
                .ThenBy(count => count.Color.ToRgbKey())
                .First()
                .Color;
        }

        private static IReadOnlyList<SourceColor[]> PaletteCandidates(
            IReadOnlyDictionary<uint, Dictionary<int, ColorCount>> profiles,
            SourceColor universal)
        {
            var seen = new HashSet<string>();
            var candidates = new List<SourceColor[]>();
            foreach (var profile in profiles.Values)
            {
                var colors = profile.Values
                    .Where(count => count.Color != universal)
                    .OrderByDescending(count => count.Count)
                    .ThenByDescending(count => NesPalette.Luminance(count.Color.R, count.Color.G, count.Color.B))
                    .ThenBy(count => count.Color.ToRgbKey())
                    .Take(3)
                    .Select(count => count.Color)
                    .ToArray();
                if (colors.Length == 0)
                {
                    continue;
                }

                var key = string.Join(",", colors.Select(color => color.ToRgbKey().ToString("X6")));
                if (seen.Add(key))
                {
                    candidates.Add(colors);
                }
            }

            return candidates;
        }

        private static List<SourceColor[]> SelectPalettes(
            IReadOnlyDictionary<uint, Dictionary<int, ColorCount>> profiles,
            IReadOnlyDictionary<uint, int> placementWeights,
            SourceColor universal,
            IReadOnlyList<SourceColor[]> candidates)
        {
            var selected = new List<SourceColor[]>();
            while (selected.Count < 4 && selected.Count < candidates.Count)
            {
                var baseError = TotalError(profiles, placementWeights, universal, selected);
                var bestGain = 0L;
                SourceColor[]? bestCandidate = null;
                foreach (var candidate in candidates)
                {
                    if (selected.Any(existing => SamePalette(existing, candidate)))
                    {
                        continue;
                    }

                    var error = TotalError(profiles, placementWeights, universal, selected.Append(candidate));
                    var gain = baseError - error;
                    if (gain > bestGain)
                    {
                        bestGain = gain;
                        bestCandidate = candidate;
                    }
                }

                if (bestCandidate is null)
                {
                    break;
                }

                selected.Add(bestCandidate);
            }

            return selected;
        }

        private static long TotalError(
            IReadOnlyDictionary<uint, Dictionary<int, ColorCount>> profiles,
            IReadOnlyDictionary<uint, int> placementWeights,
            SourceColor universal,
            IEnumerable<SourceColor[]> palettes)
        {
            var paletteList = palettes.ToArray();
            var total = 0L;
            foreach (var (cleanGid, profile) in profiles)
            {
                var tileError = paletteList.Length == 0
                    ? TileError(profile, universal, [])
                    : paletteList.Min(palette => TileError(profile, universal, palette));
                total += tileError * placementWeights[cleanGid];
            }

            return total;
        }

        private static long TileError(IReadOnlyDictionary<int, ColorCount> profile, SourceColor universal, IReadOnlyList<SourceColor> palette)
        {
            var colors = AssignmentColors(universal, palette);
            var error = 0L;
            foreach (var count in profile.Values)
            {
                error += (long)count.Count * colors.Min(color => count.Color.DistanceSquared(color));
            }

            return error;
        }

        private static bool SamePalette(IReadOnlyList<SourceColor> left, IReadOnlyList<SourceColor> right)
        {
            return left.Count == right.Count && left.Zip(right).All(pair => pair.First == pair.Second);
        }

        private static byte[] BuildPaletteRam(SourceColor universal, IReadOnlyList<SourceColor[]> selected)
        {
            var universalIndex = NesPalette.NearestIndex(universal.R, universal.G, universal.B);
            var result = new byte[16];
            for (var slot = 0; slot < 4; slot++)
            {
                result[slot * 4] = universalIndex;
                var colors = slot < selected.Count ? selected[slot] : [];
                for (var i = 0; i < 3; i++)
                {
                    result[slot * 4 + i + 1] = i < colors.Length
                        ? NesPalette.NearestIndex(colors[i].R, colors[i].G, colors[i].B)
                        : (byte)0x0F;
                }
            }

            return result;
        }

        private static SourceColor[] AssignmentColors(SourceColor universal, IReadOnlyList<SourceColor> colors)
        {
            return [universal, .. colors.Take(3)];
        }
    }

    private sealed record TilePaletteAssignment(int Slot, SourceColor[] Colors);

    private sealed class NesTileResolver(IReadOnlyList<NesTileset> tilesets, NesBackgroundPalettePlan palettePlan, int firstGeneratedTile)
    {
        private readonly Dictionary<string, int> generatedTileIds = [];
        private readonly List<byte> generatedTileData = [];

        public byte[] GeneratedTileData => generatedTileData.ToArray();

        public ResolvedTiles TilesFromTiledGid(uint gid, int tilesWide, int tilesHigh, string context)
        {
            if (tilesWide <= 0 || tilesHigh <= 0)
            {
                throw new InvalidOperationException($"{context} must expand to at least one NES tile.");
            }

            var cleanGid = LogicalTiledMapImporter.CleanTiledGid(gid, context);
            if (cleanGid == 0)
            {
                return new ResolvedTiles(new int[tilesWide * tilesHigh], new byte[tilesWide * tilesHigh]);
            }

            var tileset = FindTileset(cleanGid, context);
            var localId = checked((int)cleanGid - tileset.FirstGid);
            var assignment = palettePlan.AssignmentFor(cleanGid);
            var tileIds = tileset.BuildNesTiles(localId, tilesWide, tilesHigh, assignment, context)
                .Select(pattern => TileIdForPattern(pattern, context))
                .ToArray();
            return new ResolvedTiles(tileIds, Enumerable.Repeat((byte)assignment.Slot, tileIds.Length).ToArray());
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
            var imagePath = tileset.ImagePath is null
                ? null
                : PlatformAssetPathResolver.ResolvePngVariant(tileset.ImagePath, "nes");
            var image = imagePath is null ? null : PngImage.Read(imagePath);
            return new NesTileset(tileset.FirstGid, tileset.Name, tileset.TileWidth, tileset.TileHeight, tileset.TileCount, tileset.Columns, image);
        }

        public IReadOnlyList<byte[]> BuildNesTiles(int localId, int tilesWide, int tilesHigh, TilePaletteAssignment assignment, string context)
        {
            if (Image is null)
            {
                throw new InvalidOperationException($"{context} references tileset '{Name}', but it has no image source.");
            }

            return BuildPaletteMappedTiles(localId, tilesWide, tilesHigh, assignment, context)
                .Select(EncodeNesTile)
                .ToArray();
        }

        public Dictionary<int, ColorCount> CountColors(uint cleanGid, string context)
        {
            if (Image is null)
            {
                return [];
            }

            var localId = checked((int)cleanGid - FirstGid);
            ValidateLocalId(localId, context);
            var colorCounts = new Dictionary<int, ColorCount>();
            var sourceX = localId % Columns * TileWidth;
            var sourceY = localId / Columns * TileHeight;
            for (var y = 0; y < TileHeight; y++)
            {
                for (var x = 0; x < TileWidth; x++)
                {
                    CountPixel(sourceX + x, sourceY + y, colorCounts);
                }
            }

            return colorCounts;
        }

        private IReadOnlyList<byte[]> BuildPaletteMappedTiles(int localId, int tilesWide, int tilesHigh, TilePaletteAssignment assignment, string context)
        {
            ValidateLocalId(localId, context);
            var sourceX = localId % Columns * TileWidth;
            var sourceY = localId / Columns * TileHeight;
            var targetPixelWidth = tilesWide * 8;
            var targetPixelHeight = tilesHigh * 8;
            var tiles = new List<byte[]>(tilesWide * tilesHigh);
            for (var tileY = 0; tileY < tilesHigh; tileY++)
            {
                for (var tileX = 0; tileX < tilesWide; tileX++)
                {
                    var pattern = new byte[64];
                    for (var outY = 0; outY < 8; outY++)
                    {
                        var targetY = tileY * 8 + outY;
                        var yStart = sourceY + targetY * TileHeight / targetPixelHeight;
                        var yEnd = sourceY + Math.Min(TileHeight, Math.Max((targetY + 1) * TileHeight / targetPixelHeight, targetY * TileHeight / targetPixelHeight + 1));
                        for (var outX = 0; outX < 8; outX++)
                        {
                            var targetX = tileX * 8 + outX;
                            var xStart = sourceX + targetX * TileWidth / targetPixelWidth;
                            var xEnd = sourceX + Math.Min(TileWidth, Math.Max((targetX + 1) * TileWidth / targetPixelWidth, targetX * TileWidth / targetPixelWidth + 1));
                            pattern[outY * 8 + outX] = (byte)NearestPaletteIndex(AverageColor(xStart, xEnd, yStart, yEnd, assignment.Colors[0]), assignment.Colors);
                        }
                    }

                    tiles.Add(pattern);
                }
            }

            return tiles;
        }

        private void ValidateLocalId(int localId, string context)
        {
            if (localId < 0 || localId >= TileCount)
            {
                throw new InvalidOperationException($"{context} references tile id {localId}, which is outside the tileset.");
            }

            var sourceX = localId % Columns * TileWidth;
            var sourceY = localId / Columns * TileHeight;
            if (sourceX + TileWidth > Image!.Width || sourceY + TileHeight > Image.Height)
            {
                throw new InvalidOperationException($"{context} references tile id {localId}, which exceeds the tileset image.");
            }
        }

        private void CountPixel(int x, int y, Dictionary<int, ColorCount> colorCounts)
        {
            var offset = Image!.PixelOffset(x, y);
            if (Image.RgbaPixels[offset + 3] < 128)
            {
                return;
            }
            var color = new SourceColor(
                Image.RgbaPixels[offset],
                Image.RgbaPixels[offset + 1],
                Image.RgbaPixels[offset + 2]);
            var key = color.ToRgbKey();
            colorCounts[key] = colorCounts.TryGetValue(key, out var count)
                ? count.Increment()
                : new ColorCount(color, 1);
        }

        private SourceColor AverageColor(int xStart, int xEnd, int yStart, int yEnd, SourceColor fallback)
        {
            var totalR = 0;
            var totalG = 0;
            var totalB = 0;
            var count = 0;
            for (var y = yStart; y < yEnd; y++)
            {
                for (var x = xStart; x < xEnd; x++)
                {
                    var offset = Image!.PixelOffset(x, y);
                    if (Image.RgbaPixels[offset + 3] < 128)
                    {
                        continue;
                    }

                    totalR += Image.RgbaPixels[offset];
                    totalG += Image.RgbaPixels[offset + 1];
                    totalB += Image.RgbaPixels[offset + 2];
                    count++;
                }
            }

            return count == 0
                ? fallback
                : new SourceColor((byte)(totalR / count), (byte)(totalG / count), (byte)(totalB / count));
        }

        private static int NearestPaletteIndex(SourceColor color, IReadOnlyList<SourceColor> palette)
        {
            return palette
                .Select((candidate, index) => (index, distance: color.DistanceSquared(candidate)))
                .OrderBy(item => item.distance)
                .ThenBy(item => item.index)
                .First()
                .index;
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

    private readonly record struct SourceColor(byte R, byte G, byte B)
    {
        public int ToRgbKey() => (R << 16) | (G << 8) | B;

        public int DistanceSquared(SourceColor other)
        {
            var dr = R - other.R;
            var dg = G - other.G;
            var db = B - other.B;
            return (dr * dr) + (dg * dg) + (db * db);
        }
    }

    private readonly record struct ColorCount(SourceColor Color, int Count)
    {
        public ColorCount Increment() => this with { Count = Count + 1 };
    }

    private readonly record struct WeightedColorCount(SourceColor Color, long Count)
    {
        public WeightedColorCount Add(long count) => this with { Count = Count + count };
    }
}
