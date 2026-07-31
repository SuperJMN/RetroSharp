# Validation Fixtures

Fixtures in this directory are versioned inputs for stable compiler, linker,
WorldPack, banking, profile, offset, and budget contracts. They are independent
of playable samples and should change only when the contract they represent is
intentionally revised.

`full-stage1-v1` freezes the stage 1 TMX, TSX, PNG, and a minimal Portable2D
link program that were current when the fixture was created. Tests that need a
complete stage must use this copy instead of `samples/runner`.

Normal runner edits never update fixtures or baselines. Add a new versioned
fixture when a contract needs a materially different stable input; do not
silently replace an existing version.
