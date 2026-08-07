# Validation Fixtures

Fixtures in this directory are versioned inputs for stable compiler, linker,
WorldPack, banking, profile, offset, and budget contracts. They are independent
of playable samples and should change only when the contract they represent is
intentionally revised.

`full-stage1-v1` freezes the stage 1 TMX, TSX, PNG, and a minimal Portable2D
link program that were current when the fixture was created. Tests that need a
complete stage must use this copy instead of `samples/runner`.

`nes-code-banking-v1` freezes a compact but linker-large NES program together
with a small Tiled world and pinned music. It is the stable canary for automatic
program R6 allocation, cross-bank control flow, WorldPack-first ownership, and
R7/fixed-runtime coexistence.

`nes-prg-board-escalation-v1` freezes an NES program whose gameplay stream
cannot fit the 64 KiB R6 pool. It is the stable canary for MMC3 PRG board
selection: the linker must escalate to the next larger board, keep the bank
roles unchanged, and still boot and tick safely.

`nes-oam-dma-v1` freezes a packed MMC3 NES scene with a static WorldPack commit
and one-vs-sixty retained hardware sprite publications. It is the stable budget
fixture for proving OAM publication cost stays flat as sprite count changes.

Normal runner edits never update fixtures or baselines. Add a new versioned
fixture when a contract needs a materially different stable input; do not
silently replace an existing version.
