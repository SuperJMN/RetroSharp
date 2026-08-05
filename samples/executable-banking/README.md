# Executable Banking Sample

Sample Layer: `target-acceptance`

Build the NES canary from the repository root:

```bash
dotnet run --project src/RetroSharp.Cli/RetroSharp.Cli.csproj -- \
  --target nes \
  --out samples/executable-banking/executable-banking.nes \
  samples/executable-banking/executable-banking.rs
```

`Main` carries a generated stream of 385 distinct branch folds over a running
`u16` mixer. Every fold has its own constants, so the stream is irreducible: no
amount of body sharing or user-function outlining can shrink it, and the program
stays larger than fixed PRG capacity by construction. Automatic profile
selection must therefore build this tracked sample with
`nes-mmc3-tvrom-codebank-v1`. The sample is a build/link canary; it does not
define a gameplay or cycle-exact contract.

The source header records the state the stream ends with, so the canary can
assert that the whole banked stream really executed.
