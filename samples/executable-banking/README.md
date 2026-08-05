# Executable Banking Sample

Sample Layer: `target-acceptance`

Build the NES canary from the repository root:

```bash
dotnet run --project src/RetroSharp.Cli/RetroSharp.Cli.csproj -- \
  --target nes \
  --out samples/executable-banking/executable-banking.nes \
  samples/executable-banking/executable-banking.rs
```

The nested inline helpers deliberately expand `Main` beyond fixed PRG capacity.
Automatic profile selection must therefore build this tracked sample with
`nes-mmc3-tvrom-codebank-v1`, while preserving the source-level unit and helper
structure. The sample is a build/link canary; it does not define a gameplay or
cycle-exact contract.
