# NES OAM DMA canary v1

This fixture freezes a packed MMC3 NES scene with a static WorldPack commit and
retained sprite publication. It exists to prove that OAM publication cost is
independent of retained sprite count without depending on the editable runner.

`one-sprite.retrosharp.json` and `sixty-sprites.retrosharp.json` differ only in
how many visible retained hardware sprites they draw. Both use the versioned
`full-stage1-v1` world fixture and a copied 8x8 marker sprite asset.
