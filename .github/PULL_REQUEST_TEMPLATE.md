## Roadmap Task

- Task id:
- Issue:
- Layer: language | sdk-2d | target-intrinsic | documentation | validation

## Scope

- What changed:
- What stayed intentionally unchanged:

## Gameplay Acceptance

Complete when gameplay performance is affected; otherwise write `Not applicable`.

- Player action and scene:
- Target and physical observer/emulator:
- Named visible or audible defect:
- Safety constraints:
- Perceptual terminal result:
- Explicit precision/non-gameplay non-goals:
- Review budget used: 0 or 1 review round; 0 or 1 correction round

Once the perceptual terminal is met, record precision, metric, architecture, and
unrelated findings as follow-ups unless they provide direct blocking evidence
defined by `AGENTS.md`.

## Verification

- [ ] Focused validation for the changed owner:
  `<command and result>`
- [ ] `dotnet test RetroSharp.sln -m:1`, when production or shared .NET behavior changed
- [ ] Game Boy runner build, when affected:
  `dotnet run --project src/RetroSharp.Cli/RetroSharp.Cli.csproj -- --target gb --out samples/runner/bin/runner.gb samples/runner/runner.retrosharp.json`
- [ ] `git diff --check`
- [ ] Documentation updated, when public API or roadmap status changed.

## Agent Handoff

- Dependencies satisfied:
- Follow-up task ids:
- Notes for the integrator:
