# Stable Platformer Assets

These versioned sprites, music, sound effects, and tilesets support focused
platformer and load canaries. Samples outside `samples/runner` should use these
copies so normal game edits cannot alter their validation inputs.

The runner keeps its own editable assets. Changing a runner asset does not
imply updating this directory; update a shared asset only when intentionally
revising the stable canary that consumes it.
