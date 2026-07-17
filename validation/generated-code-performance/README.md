# Generated-code performance baseline

This directory owns the `GCP-0.1` Actor Framework characterization fixture and
its refreshable exact report. It is validation input, not a sample or a public
benchmark API.

`fixture.retrosharp.json`, `src/main.rs`, and `assets/` are a complete valid
two-target project. The validation materializer preserves that full Actor
Framework program and changes only the isolated discovery dimension:

- `wide-spawn-{3,8,16,24,32,48,64,96,128}` keeps pool capacity 2 and places
  `Goomba` objects at `x = 1000 + i * 200`, `y = 8`;
- `active-pool-{1,2,4,8}` gives the pool and object layer the same count and
  places objects at `x = 16 + i * 16`, `y = 8`.

Every logical iteration retains this order: `Actors.SpawnLayer`, `Update`,
`TouchTiles`, `LandOnTiles`, `Draw`. The one-piece actor declaration means the
pool capacity is also the declared hardware-sprite and worst-case scanline
count; each report row records that declaration beside the target limit.

Each materialized project is compiled by `CliRunner` to a temporary ROM. The
target test also compiles the same composed source through the target compiler
report seam, requires byte equality with the CLI ROM, and obtains the selected
cartridge profile. No synthetic ROM is checked in.

The observation protocol is 20 physical warm-up frames followed by 100
physical observation frames on `GameBoyTestCpu` or `NesTestCpu`. A logical tick
is the delta of the CPU's completed VBlank/frame waits. `longest-miss` is the
longest consecutive physical-frame run without a new completed wait.
`observation-cycles` is the modeled test-CPU cycle delta across only the 100
observed frames; it is not wall-clock time or a whole-program WCET claim. Game
Boy's normal reset baseline is 0 and NES's boot reset baseline is 1. Failure to
reach warm-up, reset-vector re-entry, or zero completed waits is an explicit
protocol failure and cannot become a performance row.

`baseline.tsv` contains exactly 26 data rows: the 13 fixtures for Game Boy and
then the same 13 for NES. Tests run every target matrix twice and require
identical ROM hashes and serialized rows. The file is an intentional
characterization snapshot, not a permanent success threshold for the current
cliffs. A later optimization refreshes it deliberately by reviewing the full
new report; it must not preserve slow tick counts merely to keep this file
unchanged.

The 2026-07-17 GCP-1.1 refresh records the expected ROM-byte changes from
constant-cost spawn columns. NES now sustains 100/100 through the 128-record
wide-spawn case. Game Boy sustains 100/100 through 64 records and 50/100 at 96
and 128 records; its remaining complete-layer activation scan is owned by
AF-5.10 / #244. Active-pool cadence is unchanged because GCP-1.1 does not alter
pool-phase traversal.

The later 2026-07-17 AF-5.10 / #244 refresh records the ROM-byte/hash changes
from camera-window spawn candidate indexes. The wide-spawn matrix now sustains
100/100 through the 128-record case on both Game Boy and NES. Active-pool rows
remain unchanged; their remaining eight-slot cadence work belongs to the
GCP-2.x actor-phase/struct-addressing path.

The TSV serializer always writes literal LF line endings so the exact report is
independent of the host operating system.

GCP-2.3 deliberately refreshes the NES rows after bounding runtime
struct-array address materialization. Capacities 1, 2, and 4 retain 100/100;
capacity 8 improves from 50/100 with a one-frame longest miss to 100/100 with
no miss. The NES ROM container remains 40,976 bytes, while the active-pool
fixed payload is 3,132 bytes smaller at every recorded capacity. Game Boy rows
remain the independent target snapshot.

GCP-3.2 deliberately refreshes the Game Boy rows after bounding screen-space
camera AABB collision checks on small maps with compile-time row facts. The
canonical active-pool map has an empty row and a fully solid row for the
requested flags, so `Camera.ScreenAabbTiles(...)` and
`Camera.ScreenAabbHitTop(...)` can skip flag-table column probes when the row
alone determines the result. The Game Boy `active-pool-8` row improves from
77/100 with a one-frame longest miss to 100/100 with no miss; the wide-spawn
128 row remains 100/100. NES rows remain the independent target snapshot from
GCP-2.3.
