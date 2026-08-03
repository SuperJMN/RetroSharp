# NES code-banking v1 fixture

This fixture is the stable executable-banking canary. Its compact hierarchy of
inline receiver helpers expands to 3,456 runtime increments without adding a
function ABI. It also owns a small Tiled `WorldPack` and pinned NES music, so a
successful link must reserve the WorldPack's whole R6 bank first, place gameplay
across at least two remaining R6 banks, keep audio in R7, and leave reset,
interrupts, DPCM, and banking helpers fixed.

The source does not name or select a bank. The normal NES final-link ladder must
choose `nes-mmc3-tvrom-codebank-v1` only after the exact mapper-0 and existing
MMC3 data-only attempts prove that the movable gameplay stream cannot remain
fixed.
