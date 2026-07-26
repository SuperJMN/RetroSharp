# Game Boy Packed-Camera Cadence Acceptance

Status: accepted evidence for issue #332.
Captured: 2026-07-13.

This checkpoint uses the autonomous
`samples/tiled-vscroll/vscroll.rs` production build. The final-link report owns
`worldpack:default` and contains no `legacy-world-data:default` segment, so the
measurement exercises the packed `WorldPack` reader and staged camera rather
than the raw row-table route.

## Ninety-frame comparison

SameBoy/GameboyMcp loaded three independently built 32 KiB DMG ROMs and ran
exactly 90 physical frames from reset with no input:

| Build | SHA-256 | Source gameplay ticks (`$C000`) | `SCY` | Visible Y (`$C14F`) | Request/prepare/resident/commit/release |
| --- | --- | ---: | ---: | ---: | --- |
| Baseline `95f1668` | `81202fc93a90670bea5a1512f6e49b8ab2fa8785b6f84c6ea6fb47b2d2ba2c93` | 88 | 87 | n/a (raw route) | n/a |
| `master` before #332 | `55823302258b05ed8e7a65e2a0a03fb4b39cbb1a15eec1ad96dc74c79b637282` | 11 | 10 | 10 | `1/1/1/1/1` |
| #332 packed fix | `9f1dae19dfc15f3589bd07e2add35929531f21977a4b47987223becce147cd5c` | 87 | 86 | 86 | `10/10/10/10/10` |

The one-tick baseline phase difference is startup validation, not recurring
gameplay loss. The fixed packed build completes ten later staged edges and
keeps source, visible state, and hardware scroll advancing at normal cadence;
the broken build completes only one edge and advances the source eleven times.
At frame 90 the LCD remains enabled (`LCDC=$97`), whole-pack validation is
cached successful (`$C1FB=1`), and the bank/decode/directory forbidden-work
counters are all zero (`$C157`, `$C158`, `$C19C`).

The complete 160x144 DMG shade read contained all four shade values with
histogram `7260/3060/8040/4680`, ruling out a blank or inert display.

## Cold-edge lifecycle budget

For the first cold row edge, GameboyMcp observed these distinct events:

| Event | Physical frame | Cumulative cycles | Observable state |
| --- | ---: | ---: | --- |
| Request | 11 | 826,444 | request count becomes 1; source tick 8; `SCY=7` |
| Prepare | 11 | 826,944 | prepare count becomes 1; source tick remains 8 |
| Resident | 12 | 889,692 | resident count becomes 1; source tick remains 8; `SCY=7` |
| Commit | 12 | 891,648 | commit count becomes 1; hardware remains at `SCY=7` |
| Release/visible | 12 | 893,796 | release count becomes 1; visible Y becomes 8 |
| Hardware publication | 12 | 893,884 | `SCY` becomes 8 |

Request-to-resident costs 63,248 cycles, below one 70,224-cycle DMG frame.
The automated production-path regression repeats the measurement through the
first fourteen cold, chunk-boundary, and bank-sensitive edges and fails if any
edge exceeds one physical frame. It separately asserts that hardware
publication cannot precede residency and that no bank selection, directory
read, or decode enters the VBlank commit phase.

## Preserved contracts

- Startup compares the complete v1 header byte-for-byte and verifies a
  position-sensitive 32-bit rolling fingerprint over the complete linked
  WorldPack before enabling the LCD. A failed pack caches malformed state and
  never reaches partial edge publication; regressions also cover compensating
  mutations which preserved the old 16-bit word sum.
- The staged RLE path enforces stored-length consumption, packet fit, and ID
  bounds. The staged raw path validates every visual or collision ID before it
  publishes a cache slot, and expansion/profile lookup retains a defensive
  range check. A malformed payload therefore cannot become visible even if it
  collides with the lightweight preflight fingerprint. Edge traversal reuses
  the decoded chunk, cell, metatile expansion address, and bank session across
  consecutive tiles.
- Each VBlank still commits at most one immutable 19-tile column or 21-tile
  row. Same-axis order, diagonal column/row staggering, reversal cancellation,
  exact tags, and bank restoration retain their existing tests.
- Packed preparation waits keep the audio helper at exactly one update per
  physical frame; gameplay cadence is measured independently from physical
  frames and lifecycle transitions. The explicit raw-world golden remains
  byte-for-byte unchanged.
