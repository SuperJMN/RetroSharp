# NES PRG board escalation v1 fixture

This fixture is the stable canary for MMC3 PRG board selection. It is the
`nes-code-banking-v1` shape scaled up: the same irreducible fold stream grown to
400 distinct branch folds, and it still owns a small Tiled `WorldPack` plus
pinned NES music. A steady frame loop then advances one logical tick per
physical frame.

That combination cannot link on the 64 KiB board. The pack claims one whole R6
bank, leaving three of the four R6 banks — 24 KiB — for a gameplay stream that
needs more. The final link must therefore keep climbing: exact mapper-0, then
the 64 KiB fixed-execution MMC3 profile, then 64 KiB code banking, and only
then the next board up.

On the selected 128 KiB board the R6 pool is banks `0, 3-13`. The pack keeps
bank 0, gameplay occupies the following banks in physical order, audio stays
pinned in R7, and reset, interrupts, DPCM, and banking helpers stay in the top
two fixed banks at `$C000-$FFFF`.

The heavy stream deliberately runs before `Video.Init()`, so the canary
stresses board selection rather than a one-off mid-frame commit. Its automated
observer owns link, boot, tick keep-up against physical frames, and zero unsafe
PPU/OAM writes.

The source names no bank, board, or mapper. Board choice stays a target-private
final-link decision.
