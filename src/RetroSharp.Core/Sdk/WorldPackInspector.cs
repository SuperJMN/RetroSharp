namespace RetroSharp.Core.Sdk;

// Target-neutral shape of a WorldPack inspection: the compiled pack itself,
// its serialized size, and the generated background-tile accounting used by
// budget reports. Both targets compile a WorldPack differently (their own
// Tiled importer and first-generated-tile source), but the inspected result
// is field-for-field identical, so only the compilation delegate varies.
public sealed record WorldPackInspection(
    WorldPack Pack,
    int SerializedBytes,
    int FirstGeneratedTileId,
    int GeneratedBackgroundTiles,
    int GeneratedBackgroundBytes);

public static class WorldPackInspector
{
    private const int PatternBytes = 16;

    public static WorldPackInspection Inspect<TCompiled>(
        string path,
        int firstGeneratedTileId,
        Func<string, int, TCompiled> compile,
        Func<TCompiled, WorldPack> pack,
        Func<TCompiled, byte[]> serializedBytes,
        Func<TCompiled, byte[]> generatedTileData)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(compile);
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(serializedBytes);
        ArgumentNullException.ThrowIfNull(generatedTileData);

        var compiled = compile(path, firstGeneratedTileId);
        var tileData = generatedTileData(compiled);
        return new WorldPackInspection(
            pack(compiled),
            serializedBytes(compiled).Length,
            firstGeneratedTileId,
            tileData.Length / PatternBytes,
            tileData.Length);
    }
}
