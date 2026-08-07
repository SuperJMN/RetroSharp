# NES spawn outline v1

Stable fixture for NES actor spawn activation placement. The one-spawn and eight-spawn sources differ only in the Tiled object layer they authorize, so tests can assert that authored spawn content changes fixed tables/body size without growing the hot frame phase.

The `prefix-*` sources deliberately call `Actors.SpawnWindow` between `Video.WaitVBlank()` and `Camera.Apply()` so NES video-safe reporting has to account for the complete outlined activation body instead of only the call-site `JSR`.
