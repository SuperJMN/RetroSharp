# Music Assets

These assets are from Tronimal's Free Game Boy Music Pack and are licensed `CC-BY 2025` (Tronimal).

- `blue_ocean_remix.uge` ("02 Blue Ocean Remix") is the original hUGETracker song.
- `terminate.gbapu` is a Game Boy APU register trace **derived from** "08 Terminate" (`.uge`),
  captured from its playback with `gbsplay` and stored in the `retrosharp.gbapu` v2 format (the
  same authoring path used for the runner's `delight.gbapu`). It remains `CC-BY 2025` (Tronimal).

They demonstrate declaring and switching between multiple Game Boy BGM themes in different formats.
See `docs/GameBoyTarget.md` for the supported subset and the multiple-music-themes flow, and
`docs/GameBoyApuTraceFormat.md` for the `.gbapu` format.
