# FFX Compatibility Tool

Inspects, edits, and downgrades Adobe After Effects `.ffx` preset files so
they work on older AE versions (currently: CS5.5), with a plugin-dependency
checker that flags effects you don't have installed *before* AE crashes on
them.

No official spec exists for the `.ffx` binary format — everything this tool
does was reverse-engineered by hand against real sample files. See
**`RESEARCH_NOTES.md`** for the full derivation, including the mistakes made
along the way (worth reading before changing `ffx_core/pipeline.py`).

## Features

- **Effect Lister** — see every effect in a preset, resolved to its vendor
  (Boris FX, Red Giant/Maxon, Video Copilot, Plugin Everything, RE:Vision
  Effects, or native Adobe), with missing-plugin warnings.
- **Plugin Profile** — tell the app which plugin vendors you actually have
  installed, either by checking them off manually or by scanning your AE
  plugins folder.
- **Version downgrade** — converts a preset's internal name-string encoding
  and version marker to a target AE version's native format. Currently
  supports CS5.5; adding another target requires deriving its version-byte
  value the same way CS5.5's was (see `RESEARCH_NOTES.md`).
- **Effect removal** — strip an effect you don't have, with automatic index
  renumbering so the remaining effects don't get corrupted.
- **Keyframes and third-party plugin data are never touched** — only the
  container-level version marker and name strings are converted; animation
  curves and plugin-internal parameter blobs (e.g. Twixtor's speed graph)
  pass through byte-for-byte unchanged.
- **Verification pass on every conversion** — checks for zero remaining
  old-format string tags, contiguous effect indices, and unchanged
  keyframe/parameter data, and reports the result rather than a silent
  pass/fail.

## Quick start (from source)

```bash
pip install -r requirements.txt -r requirements-dev.txt

# GUI:
python -m ffx_gui

# CLI:
python cli.py list path/to/preset.ffx
python cli.py convert path/to/preset.ffx out.ffx --target cs5.5
python cli.py convert in.ffx out.ffx --target cs5.5 --remove "MB LookSuite3"
```

## Running tests

```bash
pytest -v
```

Round-trip and pipeline tests run against a synthetic file by default, plus
a real sample file committed at `tests/fixtures/sample_1.ffx`. Add more real
`.ffx` files to that folder to exercise the pipeline further — the test
suite picks up any `*.ffx` file placed there automatically.

## Downloading a build

Tagged releases (`vX.Y.Z`) are built automatically for Windows and macOS via
GitHub Actions — see the Releases page. See `.github/workflows/build.yml`
for the packaging steps if you'd rather build locally with PyInstaller.

## Project status

- ✅ Phase 1 — RIFX parser/serializer (lossless round-trip, tested)
- ✅ Phase 2 — Conversion pipeline + CLI
- ✅ Phase 3 — PySide6 GUI (Effect Lister / Plugin Profile / Convert tabs)
- ✅ Phase 4 — Plugin ownership profile system (delivered as part of
  Phase 3 — manual checklist + folder-scan auto-detect + automatic
  per-file cross-check, all in `ffx_gui/profile_store.py` and
  `ffx_gui/tab_profile.py`)
- ✅ Phase 5 — Packaged releases (Windows/macOS) via GitHub Actions

See `PROJECT_PLAN.md` for the full original spec and `PHASES.md` for how it
was broken down. Both are historical planning documents at this point —
this README is the source of truth for current status.

## Known limitations

- Only CS5.5 is a confirmed downgrade target. Adding another target AE
  version requires a real sample pair to derive its version-byte value —
  see `RESEARCH_NOTES.md`'s section on the `head` chunk.
- Third-party plugins' own internal parameter formats (e.g. Twixtor's speed
  curve) are treated as opaque and never modified. If a plugin's blob
  itself needs version translation (not yet observed — the container-level
  fixes have been sufficient in every real test case so far), that would be
  a separate, much harder per-plugin research effort.
- The plugin vendor/prefix lookup table (`data/plugin_table.json`) and the
  folder-scan filename hints (`ffx_gui/tab_profile.py`) are both seeded
  thin, by design — meant to grow from real files and real plugin
  installations rather than a one-time scrape. Contributions welcome.

## License

MIT — see `LICENSE`. Adobe, After Effects, and third-party plugin vendor
names referenced in this project (Boris FX, Red Giant/Maxon, Video Copilot,
Plugin Everything, RE:Vision Effects, etc.) are trademarks of their
respective owners, referenced solely for interoperability/identification.
This project is not affiliated with or endorsed by Adobe or any plugin
vendor it detects.
