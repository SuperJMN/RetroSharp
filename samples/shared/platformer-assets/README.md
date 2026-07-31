# Stable Platformer Assets

These versioned maps, sprites, music, sound effects, and tilesets support focused
platformer and load canaries. Samples outside `samples/runner` should use these
copies so normal game edits cannot alter their validation inputs.

`maps/stage1.tmx` is the unshifted 156x20 stage used by the diagonal speed
sweep. It references the stable tileset in `tilesets/` and keeps the authored
top and bottom rows intact so `Camera.VerticalScrollMax()` represents the real
map boundary. The horizontal-only canaries retain their separately shifted
fixture because their 30-row camera window is a different acceptance scenario.

The runner keeps its own editable assets. Changing a runner asset does not
imply updating this directory; update a shared asset only when intentionally
revising the stable canary that consumes it.
