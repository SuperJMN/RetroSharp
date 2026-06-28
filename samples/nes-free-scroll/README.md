# Free Scroll Sample

Sample Layer: `target-acceptance`

Target-acceptance sample for 2-axis camera scrolling. NES demonstrates the
four-screen 64x60 free-scroll buffer without runtime streaming for this bounded
map. Game Boy demonstrates the same `camera.SetPosition(x, y)` source with
staggered column/row streaming, committing one visible edge per VBlank.
