# Game Boy Runner Diagnostics

Sample Layer: `target-acceptance`

These samples isolate runner features so visual or gameplay bugs can be bisected one layer at a time.

| Step | Source | Adds |
| --- | --- | --- |
| 00 | `00-static-background.rs` | Static Game Boy background tiles only. |
| 01 | `01-world-platforms.rs` | Playable world rows, platforms, holes, and hazards through `world_map(...)`. |
| 02a | `02-flat-ground-camera.rs` | Player sprite, input, camera, animation, jump, flat ground collision, and cyclic left/right camera movement; no platforms, holes, hazards, or enemies. |
| 02b | `02-player-camera.rs` | Full player collision layer: wrapped left/center/right foot probes, platforms, holes, hazards, jump, animation, and camera. |
| 03 | `03-enemy-sprites.rs` | Enemy logical sprite drawing, enemy animation, one static enemy, and one looping right-to-left enemy in isolation. |
| 04 | `../runner.rs` | Full runner scene with background, platforms, player, and enemies. |

Run the full diagnostic matrix from the repository root:

```bash
python3 -m pip install --target /tmp/retrosharp-pyboy-site pyboy pillow
PYTHONPATH=/tmp/retrosharp-pyboy-site python3 tools/gameboy/runner_diagnostics.py
```

Outputs are written to `artifacts/gameboy-runner-diagnostics/`.

When reporting a bug, use the first failing step and scenario name. For example: `02-flat-ground-camera-right` means the problem reproduces with only player/camera/flat-ground collision. `02-player-camera-right` means flat ground is not enough to reproduce the problem, but adding platforms, holes, hazards, and elevated collision is. `02-flat-ground-camera-left` validates that holding left at startup moves cyclically left instead of acting like a wall or moving the camera to the right. `02-flat-ground-camera-right-wrap` holds RIGHT long enough to cross the Game Boy scroll byte wrap at 255 -> 0.
