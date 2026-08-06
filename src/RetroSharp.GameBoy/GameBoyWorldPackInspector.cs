using RetroSharp.Core.Sdk;

namespace RetroSharp.GameBoy;

public static class GameBoyWorldPackInspector
{
    public static WorldPackInspection Inspect(string path) =>
        WorldPackInspector.Inspect(
            path,
            GameBoyVideoProgram.FirstGeneratedBackgroundTile,
            (mapPath, firstGeneratedTileId) => GameBoyTiledMapImporter.CompileWorldPack(mapPath, firstGeneratedTileId),
            compiled => compiled.Pack,
            compiled => compiled.SerializedBytes,
            compiled => compiled.GeneratedTileData);
}
