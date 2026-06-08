# RetroSharp Samples

Samples are classified by architectural layer in `samples/manifest.json`.

| Layer | Meaning |
| --- | --- |
| `portable-sdk` | A source sample that is allowed to prove cross-target SDK portability. It must not call target intrinsics or transitional helpers. |
| `target-intrinsic` | A target-specific sample that demonstrates raw setup or hardware-shaped calls. It is not evidence that the API is portable. |
| `target-capability-spike` | A target-specific spike for a capability-gated SDK feature whose cross-target contract is not complete yet. |
| `target-acceptance` | A target-specific acceptance sample for a runnable scenario. It can use transitional calls while they are explicitly documented. |

The portable quarantine check in `RetroSharp.Core.Tests` reads the manifest and rejects transitional or target-intrinsic calls inside `portable-sdk` samples.
