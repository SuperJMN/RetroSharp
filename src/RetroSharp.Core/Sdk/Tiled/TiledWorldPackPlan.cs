namespace RetroSharp.Core.Sdk.Tiled;

using System.Collections.ObjectModel;

public readonly record struct TiledVisualTileReference(int TilesetIndex, int LocalTileId, uint CleanGid)
{
    public static TiledVisualTileReference Empty { get; } = new(-1, -1, 0);

    public bool IsEmpty => CleanGid == 0;
}

public sealed record TiledVisualMetatile(
    TiledVisualTileReference Background,
    TiledVisualTileReference World);

public sealed class TiledWorldPackPlan
{
    private readonly TiledVisualMetatile[] visualMetatiles;
    private readonly ushort[] visualIds;
    private readonly WorldPackCollisionProfile[] collisionProfiles;
    private readonly ushort[] collisionProfileIds;
    private readonly ReadOnlyCollection<TiledVisualMetatile> readOnlyVisualMetatiles;
    private readonly ReadOnlyCollection<ushort> readOnlyVisualIds;
    private readonly ReadOnlyCollection<WorldPackCollisionProfile> readOnlyCollisionProfiles;
    private readonly ReadOnlyCollection<ushort> readOnlyCollisionProfileIds;

    private TiledWorldPackPlan(
        int sourceWidth,
        int sourceHeight,
        int metatileWidth,
        int metatileHeight,
        TiledVisualMetatile[] visualMetatiles,
        ushort[] visualIds,
        WorldPackCollisionProfile[] collisionProfiles,
        ushort[] collisionProfileIds)
    {
        SourceWidth = sourceWidth;
        SourceHeight = sourceHeight;
        MetatileWidth = metatileWidth;
        MetatileHeight = metatileHeight;
        this.visualMetatiles = visualMetatiles;
        this.visualIds = visualIds;
        this.collisionProfiles = collisionProfiles;
        this.collisionProfileIds = collisionProfileIds;
        readOnlyVisualMetatiles = Array.AsReadOnly(this.visualMetatiles);
        readOnlyVisualIds = Array.AsReadOnly(this.visualIds);
        readOnlyCollisionProfiles = Array.AsReadOnly(this.collisionProfiles);
        readOnlyCollisionProfileIds = Array.AsReadOnly(this.collisionProfileIds);
    }

    public int SourceWidth { get; }

    public int SourceHeight { get; }

    public int MetatileWidth { get; }

    public int MetatileHeight { get; }

    public int HardwareWidth => checked(SourceWidth * MetatileWidth);

    public int HardwareHeight => checked(SourceHeight * MetatileHeight);

    public IReadOnlyList<TiledVisualMetatile> VisualMetatiles => readOnlyVisualMetatiles;

    public IReadOnlyList<ushort> VisualIds => readOnlyVisualIds;

    public IReadOnlyList<WorldPackCollisionProfile> CollisionProfiles => readOnlyCollisionProfiles;

    public IReadOnlyList<ushort> CollisionProfileIds => readOnlyCollisionProfileIds;

    public static TiledWorldPackPlan Create(LogicalTiledMap logical)
    {
        ArgumentNullException.ThrowIfNull(logical);

        var geometry = logical.Geometry;
        var sourceWidth = geometry.SourceWidth;
        var sourceHeight = geometry.WorldHeight;
        var sourceCellCount = checked(sourceWidth * sourceHeight);
        var visualCells = new TiledVisualMetatile[sourceCellCount];
        var profileCells = new WorldTileFlags[sourceCellCount][];

        for (var localY = 0; localY < sourceHeight; localY++)
        {
            var sourceY = checked(geometry.WorldY + localY);
            for (var sourceX = 0; sourceX < sourceWidth; sourceX++)
            {
                var sourceIndex = checked(sourceY * sourceWidth + sourceX);
                var packIndex = checked(localY * sourceWidth + sourceX);
                visualCells[packIndex] = new TiledVisualMetatile(
                    ResolveReference(logical, logical.BackgroundGids?[sourceIndex] ?? 0, $"background ({sourceX}, {sourceY})"),
                    ResolveReference(logical, logical.WorldGids[sourceIndex], $"world ({sourceX}, {sourceY})"));

                var flags = new WorldTileFlags[checked(geometry.TileScaleX * geometry.TileScaleY)];
                for (var subcellY = 0; subcellY < geometry.TileScaleY; subcellY++)
                {
                    for (var subcellX = 0; subcellX < geometry.TileScaleX; subcellX++)
                    {
                        var hardwareX = checked(sourceX * geometry.TileScaleX + subcellX);
                        var hardwareY = checked(localY * geometry.TileScaleY + subcellY);
                        flags[subcellY * geometry.TileScaleX + subcellX] =
                            logical.WorldFlags[hardwareY * geometry.Width + hardwareX];
                    }
                }

                profileCells[packIndex] = flags;
            }
        }

        var visualMetatiles = visualCells
            .Distinct()
            .Order(TiledVisualMetatileComparer.Instance)
            .ToArray();
        if (visualMetatiles.Length > ushort.MaxValue)
        {
            throw new InvalidOperationException($"Tiled WorldPack has {visualMetatiles.Length} visual metatiles, exceeding the v1 limit of {ushort.MaxValue}.");
        }

        var visualIdsByMetatile = visualMetatiles
            .Select((metatile, id) => (metatile, id: checked((ushort)id)))
            .ToDictionary(item => item.metatile, item => item.id);
        var visualIds = visualCells.Select(metatile => visualIdsByMetatile[metatile]).ToArray();

        var emptyProfile = new WorldTileFlags[checked(geometry.TileScaleX * geometry.TileScaleY)];
        var profileBytes = profileCells
            .Append(emptyProfile)
            .GroupBy(ProfileKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var orderedProfileKeys = profileBytes.Keys.Order(StringComparer.Ordinal).ToArray();
        if (orderedProfileKeys.Length > ushort.MaxValue)
        {
            throw new InvalidOperationException($"Tiled WorldPack has {orderedProfileKeys.Length} collision profiles, exceeding the v1 limit of {ushort.MaxValue}.");
        }

        var profileIdsByKey = orderedProfileKeys
            .Select((key, id) => (key, id: checked((ushort)id)))
            .ToDictionary(item => item.key, item => item.id, StringComparer.Ordinal);
        var collisionProfiles = orderedProfileKeys
            .Select(key => new WorldPackCollisionProfile(profileBytes[key]))
            .ToArray();
        var collisionProfileIds = profileCells.Select(flags => profileIdsByKey[ProfileKey(flags)]).ToArray();

        return new TiledWorldPackPlan(
            sourceWidth,
            sourceHeight,
            geometry.TileScaleX,
            geometry.TileScaleY,
            visualMetatiles,
            visualIds,
            collisionProfiles,
            collisionProfileIds);
    }

    public SerializedWorldPack Build(ReadOnlyMemory<byte> targetExpansions, int targetCellStride) =>
        WorldPackSerializer.Build(this, targetExpansions, targetCellStride);

    private static TiledVisualTileReference ResolveReference(LogicalTiledMap logical, uint gid, string context)
    {
        var cleanGid = LogicalTiledMapImporter.CleanTiledGid(gid, context);
        if (cleanGid == 0)
        {
            return TiledVisualTileReference.Empty;
        }

        var tileset = LogicalTiledMapImporter.FindTileset(logical.Tilesets, cleanGid, context);
        var tilesetIndex = -1;
        for (var index = 0; index < logical.Tilesets.Count; index++)
        {
            if (ReferenceEquals(logical.Tilesets[index], tileset))
            {
                tilesetIndex = index;
                break;
            }
        }

        if (tilesetIndex < 0)
        {
            throw new InvalidOperationException($"{context} resolved to a tileset outside importer order.");
        }

        return new TiledVisualTileReference(
            tilesetIndex,
            checked((int)cleanGid - tileset.FirstGid),
            cleanGid);
    }

    private static string ProfileKey(IEnumerable<WorldTileFlags> flags) =>
        Convert.ToHexString(flags.Select(flag => (byte)flag).ToArray());

    private sealed class TiledVisualMetatileComparer : IComparer<TiledVisualMetatile>
    {
        public static TiledVisualMetatileComparer Instance { get; } = new();

        public int Compare(TiledVisualMetatile? left, TiledVisualMetatile? right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left is null)
            {
                return -1;
            }

            if (right is null)
            {
                return 1;
            }

            var background = CompareReference(left.Background, right.Background);
            return background != 0 ? background : CompareReference(left.World, right.World);
        }

        private static int CompareReference(TiledVisualTileReference left, TiledVisualTileReference right)
        {
            var tileset = left.TilesetIndex.CompareTo(right.TilesetIndex);
            return tileset != 0 ? tileset : left.LocalTileId.CompareTo(right.LocalTileId);
        }
    }
}
