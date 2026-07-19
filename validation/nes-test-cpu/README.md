# NES validation CPU calibration

This directory records external calibration evidence for the in-process
`NesTestCpu` adapter. The values below were captured on 2026-07-19 with
`Nes.Mcp` 0.0.7.0 backed by AprNes.

## Retained-OAM publisher

The calibration used the tracked `samples/runner/bin/runner.nes` artifact
(SHA-256 `68e7cd55a237293d01254b79b0d6d8d27b06b05972526c95f7608cdcc145ec53`).
After stopping on the `$2003` write, `$4015` was cleared to exclude DMC stalls
from the isolated instruction budget. AprNes reported:

- `$2003` bus event: CPU cycle `14888109`;
- post-instruction CPU counter: `14888110`, PC `$C8C1`;
- 305 instructions through the 76-byte `$2004` loop: 1,065 cycles;
- `LDA #$00` plus `STA $2003` prefix: 6 cycles;
- complete publisher before `RTS`: **1,071 cycles**.

This independently matches
`NesTestCpuTimingTests.Seventy_six_byte_publisher_costs_1071_cycles_before_return`
and confirms that a PPU store is observed on its final bus cycle, rather than
at instruction start.

## Fixed-frame scenario refresh

Page-crossing corrections can move a logical observation by one instruction
at a physical frame boundary. The refreshed `tiled-vscroll` NES checkpoints
were therefore checked against the unchanged tracked ROM
`samples/tiled-vscroll/vscroll.nes` (SHA-256
`cbf9fbf1f5159a52258431403f7ef9fcf2532055808d5d6fa82bf159eaddde74`).
AprNes and the in-process adapter use frame origins two frames apart for this
ROM; the aligned observations were:

| Scenario frame | AprNes frame | `$0000-$0002` | Visible camera Y (`$03CD-$03CE`) |
| ---: | ---: | --- | ---: |
| 280 | 278 | `F0 01 17` | 240 |
| 320 | 318 | `D1 00 00` | 210 |
| 560 | 558 | `1F 01 00` | 30 |

The corresponding decimal signals are exactly the committed scenario oracle:
`240/1/23`, `209/0`, and `31/1`.
