# FFX Compatibility Tool

Inspects, edits, and downgrades Adobe After Effects `.ffx` preset files so
they work on older AE versions (currently: CS5.5), with a plugin-dependency
checker built in.

See **`PHASES.md`** for the build roadmap and **`PROJECT_PLAN.md`** for the
full spec. This repo currently contains **Phase 1** (core RIFX
parser/serializer) and **Phase 2** (the conversion pipeline + CLI) —
no GUI yet.

## Quick start

```bash
pip install -r requirements.txt -r requirements-dev.txt

# List the effects/plugins in a preset:
python cli.py list path/to/preset.ffx

# Convert a preset to CS5.5:
python cli.py convert path/to/preset.ffx out.ffx --target cs5.5

# Convert AND strip an effect you don't have installed:
python cli.py convert in.ffx out.ffx --target cs5.5 --remove "MB LookSuite3"
```

## Running tests

```bash
pytest -v
```

Round-trip and pipeline tests run against synthetic files by default so CI
passes with zero setup. Drop real `.ffx` sample files into
`tests/fixtures/` to also exercise the full pipeline against real presets
(see `tests/fixtures/.gitkeep`).

## Project status

- ✅ Phase 1 — RIFX parser/serializer (lossless round-trip, tested)
- ✅ Phase 2 — Conversion pipeline + CLI (version patch, string-format
  conversion, effect removal, verification pass)
- ⬜ Phase 3 — PySide6 GUI
- ⬜ Phase 4 — Plugin ownership profile system
- ⬜ Phase 5 — Packaged releases (Windows/macOS) via GitHub Actions

## Why this exists / how it was figured out

No official spec exists for the `.ffx` binary format. Everything this tool
does was reverse-engineered against real sample files — see
`RESEARCH_NOTES.md` for the full derivation, including the debugging
process for a couple of non-obvious bugs (a version-only patch that
crashed AE, and a "names show as Utf1/Utf2" bug caused by treating a
fixed-width field as variable-length). Worth reading before changing
anything in `ffx_core/pipeline.py`.
