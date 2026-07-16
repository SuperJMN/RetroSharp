# Archived Z80 compiler path

Status: retired from the active solution on 2026-07-17.

RetroSharp currently supports direct Game Boy and NES cartridge compilation.
The older parser-to-three-address-IR-to-Z80 path was removed because neither
active target consumed that IR or backend. This document preserves the exact
recovery points and the external forks that path used.

## Recovery point

The last `master` commit containing the complete Z80 path and both submodule
gitlinks is:

```text
1fba42e1aaa1dd8ec7df8ce2a0eeda96b07a1428
```

That tree contains the retired `RetroSharp`,
`RetroSharp.Generation.Intermediate`, `RetroSharp.Z80`,
`RetroSharp.Z80.Tests`, `TestZ80Assembler`, and Z80 debug projects, plus the
CLI dispatch that assembled and executed Z80 output.

To inspect or revive the exact archived toolchain without disturbing the
current checkout:

```bash
git worktree add ../RetroSharp-z80-archive -b spike/revive-z80 \
  1fba42e1aaa1dd8ec7df8ce2a0eeda96b07a1428
git -C ../RetroSharp-z80-archive submodule update --init --recursive
```

Port changes forward deliberately from that worktree. Restoring the old
solution or CLI wholesale would also restore its obsolete default `z80`
target and its parallel compiler architecture.

## Retired submodules

The fork relationships below were verified against GitHub and the pinned
trees on 2026-07-17.

### `libs/6502DotNet`

- RetroSharp fork: <https://github.com/SuperJMN/6502.Net>
- Upstream: <https://github.com/informedcitizenry/6502.Net>
- Pinned commit: `c96d8d8de028fbed9ec6efe8122a229b9d34cead`
- Pinned subject: `build: remove System.Drawing dependency`
- Role in RetroSharp: `Sixty502DotNet.Shared.Z80Assembler` assembled the Z80
  text emitted by `RetroSharp.Z80`; despite the repository name, the active NES
  compiler did not use this submodule.
- Important fork work: the fork added `Z80Assembler`, assembly debug metadata,
  assembler error handling and lexer diagnostics, then modernized the projects
  for .NET 8/tool packaging and removed the `System.Drawing` dependency.

This was the substantially tuned fork. At retirement its branch contained 28
commits not present in the then-current upstream history, including the
RetroSharp-specific Z80 assembler work.

### `libs/Z80DotNet`

- RetroSharp fork: <https://github.com/SuperJMN/Z80dotNet>
- Upstream: <https://github.com/Konamiman/Z80dotNet>
- Pinned commit: `708f27ef079bd6dca1ca1193a90ab010b739cc98`
- Pinned subject: `chore: stop tracking generated obj files`
- Role in RetroSharp: `Konamiman.Z80dotNet.Z80Processor` executed assembled
  binaries in the legacy CLI and Z80 end-to-end tests.
- Important fork work: .NET 8 migration, modern NuGet/package metadata,
  DotnetDeployer pipeline setup, and generated-file cleanup.

At retirement this fork contained 12 commits not present in the
then-current upstream history. Its changes were mostly maintenance and
packaging rather than RetroSharp-specific CPU behavior.

## Current replacement path

The active production seam is `TargetFrontendPreparation`: source packages,
parsing, target selection, import validation, SDK/Actor Framework lowering,
`let` inference, and function-contract validation are shared. Game Boy and NES
then construct their own video programs and lower directly to cartridge bytes.
There is no active shared three-address backend IR.
