using RetroSharp.Core.Sdk;

namespace RetroSharp.GameBoy;

public sealed record GameBoyWorldPackInspection(
    WorldPack Pack,
    int SerializedBytes,
    int FirstGeneratedTileId,
    int GeneratedBackgroundTiles,
    int GeneratedBackgroundBytes);

public static class GameBoyWorldPackInspector
{
    public static GameBoyWorldPackInspection Inspect(string path)
    {
        var compiled = GameBoyTiledMapImporter.CompileWorldPack(
            path,
            GameBoyVideoProgram.FirstGeneratedBackgroundTile);
        return new GameBoyWorldPackInspection(
            compiled.Pack,
            compiled.SerializedBytes.Length,
            GameBoyVideoProgram.FirstGeneratedBackgroundTile,
            compiled.GeneratedTileData.Length / 16,
            compiled.GeneratedTileData.Length);
    }
}
