# Free Scroll Sample

Sample Layer: `target-acceptance`

Target-acceptance sample for 2-axis camera scrolling. NES demonstrates the
four-screen 64x60 free-scroll buffer without runtime streaming for this bounded
map. Game Boy demonstrates the same `Camera.SetPosition(x, y)` source with
staggered column/row streaming, committing one background-buffer edge per VBlank.
The camera declares the complete 60-row source height so the streamed column
includes every tile row that can become partially visible.
Each simultaneous 8-pixel boundary request is retained for one source tick,
giving the staggered second axis its declared VBlank before motion continues.
Both targets prepare the next requested position before `Video.WaitVBlank()` and
call `Camera.Apply()` immediately after it so retained writes stay inside the
legal VRAM/PPU presentation window.
