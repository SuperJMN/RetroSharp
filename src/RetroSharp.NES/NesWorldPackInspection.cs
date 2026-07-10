using RetroSharp.Core.Sdk;

namespace RetroSharp.NES;

public sealed record NesWorldPackInspection(
    WorldPack Pack,
    int SerializedBytes,
    int FirstGeneratedTileId,
    int GeneratedBackgroundTiles,
    int GeneratedBackgroundBytes);

public static class NesWorldPackInspector
{
    public static NesWorldPackInspection Inspect(string path)
    {
        var compiled = NesTiledWorldImporter.CompileWorldPack(path, NesVideoProgram.FirstSpriteTile);
        return new NesWorldPackInspection(
            compiled.Pack,
            compiled.SerializedBytes.Length,
            NesVideoProgram.FirstSpriteTile,
            compiled.GeneratedTileData.Length / 16,
            compiled.GeneratedTileData.Length);
    }
}
