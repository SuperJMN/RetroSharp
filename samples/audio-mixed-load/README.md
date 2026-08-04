# Audio Mixed Load

Sample Layer: `target-acceptance`

This neutral Game Boy/NES canary keeps production BGM active while two spaced A-button
edges play a production one-shot SFX to completion without restarts. The same physical-frame loop polls input,
advances an airborne player, performs collision probes, moves two logical sprites,
follows the player with the packed camera, and services audio once.

The source uses only `RetroSharp.Portable2D`. Target-specific VGM/VGZ, sprite, audio,
banking, mapper, APU and DPCM behavior is selected by target asset variants and target
lowering; no hardware-specific gameplay API appears in the sample.

On NES, the two player-metasprite draw sites also form the stable shared-SDK-operation
canary: both keep the same compile-time asset/palette shape while passing runtime frame
and flip operands. The backend may therefore emit one fixed-resident draw body and call
it from both sites. Tests gate relative PRG savings, one logical tick per physical frame,
and the existing functional behavior; exact bytes, addresses, hashes, and cycles remain
diagnostic.

Build both tracked cartridges from the repository root:

```bash
tools/gameboy/generate_sample_roms.py samples/audio-mixed-load/audio-mixed-load.retrosharp.json
```

Controls:

- **Right**: move the player and camera through packed background work.
- **A**: start the airborne workload when grounded and play the SFX.

The exact physical-frame budgets, ordered APU/DPCM evidence, artifact hashes,
and emulator checkpoints are recorded in
[`docs/history/AudioMixedLoadFunctionalAcceptance.md`](../../docs/history/AudioMixedLoadFunctionalAcceptance.md).
