using RetroSharp.Core.Sdk;

namespace RetroSharp.NES;

public static class NesWorldPackInspector
{
    public static WorldPackInspection Inspect(string path) =>
        WorldPackInspector.Inspect(
            path,
            NesVideoProgram.FirstSpriteTile,
            (mapPath, firstGeneratedTile) => NesTiledWorldImporter.CompileWorldPack(mapPath, firstGeneratedTile),
            compiled => compiled.Pack,
            compiled => compiled.SerializedBytes,
            compiled => compiled.GeneratedTileData);
}
