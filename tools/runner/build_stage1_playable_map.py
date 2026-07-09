#!/usr/bin/env python3
"""Build the runner's playable Game Boy/NES map from the full stage1 design.

The full authored level `stage1.tmj` (a raw Tiled export, 156x20 @16px source
cells) is larger than the RetroSharp 8-bit targets can stream: the Game Boy uses
8-bit background column indices (map width must stay < 256 hw columns) and both
targets keep world Y collision reads in an 8-bit range (map height must stay
<= 255 px). NES NROM also bounds map data + music inside a 32 KiB PRG budget.

This tool derives `stage1.playable.tmj`, the trimmed map the runner actually
loads. It:
  * keeps the first `WIDTH` source columns (the level start),
  * keeps the bottom `KEEP_ROWS` source rows so the ground sits on the map's
    bottom row and the level's platforms fill the rows above it,
  * (optionally) pads `PAD_ROWS` solid rows below the ground,
  * renames the single tile layer to `world` and adds the `retrosharp*` map
    properties the importer requires.

The runner anchors the world's bottom to each target's screen bottom via
`Camera.VerticalScrollMax()`, so the ground frames at the bottom on both the
Game Boy (144 px) and NES (240 px) screens without exposing area below the map.

Usage (from repo root):
    tools/runner/build_stage1_playable_map.py
"""
import json
import os

MAPS = "samples/runner/assets/maps"
SRC = os.path.join(MAPS, "stage1.tmj")            # full authored design (raw Tiled export)
OUT = os.path.join(MAPS, "stage1.playable.tmj")   # trimmed runtime map the runner loads

WIDTH = 88      # source columns kept -> 176 hw columns (widest that fits NES NROM + full music)
KEEP_ROWS = 15  # bottom source rows kept: ground row + platforms above, filling the 30 hw-row screen
PAD_ROWS = 0    # no underground fill; the ground sits at the map (and screen) bottom
PAD_GID = 104   # ground tile used to fill the underground rows (unused when PAD_ROWS == 0)


def main():
    d = json.load(open(SRC))
    src_w, src_h = d["width"], d["height"]
    width = min(WIDTH, src_w)
    row_start = max(0, src_h - KEEP_ROWS)
    layer = next(l for l in d["layers"] if l.get("type") == "tilelayer")
    data = layer["data"]

    world_data = []
    for row in range(row_start, src_h):
        base = row * src_w
        world_data.extend(data[base:base + width])
    for _ in range(PAD_ROWS):
        world_data.extend([PAD_GID] * width)
    out_h = (src_h - row_start) + PAD_ROWS

    world = {
        "data": world_data,
        "height": out_h,
        "id": 1,
        "name": "world",
        "opacity": 1,
        "type": "tilelayer",
        "visible": True,
        "width": width,
        "x": 0,
        "y": 0,
    }
    playable = {
        "compressionlevel": -1,
        "width": width,
        "height": out_h,
        "infinite": False,
        "orientation": "orthogonal",
        "renderorder": "right-down",
        "tiledversion": d.get("tiledversion", "1.12.2"),
        "tileheight": d["tileheight"],
        "tilewidth": d["tilewidth"],
        "type": "map",
        "version": d.get("version", "1.10"),
        "nextlayerid": 3,
        "nextobjectid": 1,
        "properties": [
            {"name": "retrosharpStreamY", "type": "int", "value": 0},
            {"name": "retrosharpWorldY", "type": "int", "value": 0},
            {"name": "retrosharpWorldHeight", "type": "int", "value": out_h},
        ],
        "tilesets": [{"firstgid": 1, "source": "stage1.tsx"}],
        "layers": [world],
    }
    with open(OUT, "w") as f:
        json.dump(playable, f)
    print(f"wrote {OUT}: {width}x{out_h} source cells = {width * 2}x{out_h * 2} hw tiles")


if __name__ == "__main__":
    main()
