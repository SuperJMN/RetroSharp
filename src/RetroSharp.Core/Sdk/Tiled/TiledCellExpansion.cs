namespace RetroSharp.Core.Sdk.Tiled;

// Receives one already-expanded hardware subcell position: its 0-based offset
// within the source cell's tile-scale block (subcellIndex, row-major), its
// position in the expanded grid (targetX, targetY), and the flattened index
// (targetIndex) for an expanded-width array. Each target owns what value it
// writes there and how wide that value is; only the walk itself is shared.
public delegate void TiledSubcellWriter(int subcellIndex, int targetX, int targetY, int targetIndex);

// Shared source-to-hardware tile expansion geometry. A Tiled source cell
// expands into tileScaleX * tileScaleY hardware 8x8 cells, laid out row-major
// within the block and placed at (sourceX*tileScaleX + subcellX,
// sourceY*tileScaleY + subcellY) in the expanded grid. Both targets walk this
// same shape for the background layer, the playable world slice, and the
// WorldPack visual-metatile expansion; this is that one shared walk. What data
// a target resolves per source cell, and how many bytes it writes per subcell,
// stay target owned.
public static class TiledCellExpansion
{
    public static void ForEachSubcell(
        int sourceX,
        int sourceY,
        int tileScaleX,
        int tileScaleY,
        int expandedWidth,
        TiledSubcellWriter writeCell)
    {
        ArgumentNullException.ThrowIfNull(writeCell);

        for (var subcellY = 0; subcellY < tileScaleY; subcellY++)
        {
            for (var subcellX = 0; subcellX < tileScaleX; subcellX++)
            {
                var targetX = checked(sourceX * tileScaleX + subcellX);
                var targetY = checked(sourceY * tileScaleY + subcellY);
                var targetIndex = checked(targetY * expandedWidth + targetX);
                var subcellIndex = subcellY * tileScaleX + subcellX;
                writeCell(subcellIndex, targetX, targetY, targetIndex);
            }
        }
    }
}
