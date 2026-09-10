# FFX Compatibility Tool

[![CI](https://github.com/marbou92/FFX-Compatibility-Tool/actions/workflows/test.yml/badge.svg?label=CI)](https://github.com/marbou92/FFX-Compatibility-Tool/actions/workflows/test.yml)
[![Nightly](https://github.com/marbou92/FFX-Compatibility-Tool/actions/workflows/nightly.yml/badge.svg?label=Nightly)](https://github.com/marbou92/FFX-Compatibility-Tool/actions/workflows/nightly.yml)
[![Release](https://github.com/marbou92/FFX-Compatibility-Tool/actions/workflows/build.yml/badge.svg?label=Release)](https://github.com/marbou92/FFX-Compatibility-Tool/actions/workflows/build.yml)

Converts After Effects **.ffx** presets for cross-version compatibility —
a verified full downgrade to CS5.5's native format, or native-format effect
removal for every AE version after CS5.5 (CS6 through 2025) — strips effects
whose plugins you don't own, and shows you exactly what is inside a preset —
all offline, in one portable exe. Built for **.NET Framework 4.8 / WPF**: runs
on Windows 7 SP1 through Windows 11, no installer, no runtime download on
Windows 10/11. Made by **marbou92**.

## Install

1. Grab the **FFXCompatibilityTool-*.exe** from the
   [latest release](https://github.com/marbou92/FFX-Compatibility-Tool/releases/latest)
   — that single file is the whole app.
2. Run it. Everything lives inside the exe — no installer, no zip, no
   DLL pile, no data folder.
3. Windows 7 only: install the .NET 4.8 runtime once —
   [download](https://dotnet.microsoft.com/download/dotnet-framework/net48).

Windows warns "publisher could not be verified" on first run — the app is
unsigned open source, and Windows says that about every fresh download.
Check the SHA-256 on the release page, click **Run**, and if the prompt
gets old: right-click the exe → Properties → **Unblock**.

Want new features early? The rolling
[nightly](https://github.com/marbou92/FFX-Compatibility-Tool/releases/tag/nightly)
pre-release is rebuilt from every push to main.

## Convert

- Drop presets — or a whole folder — anywhere on the window, or browse.
- A folder opens as a file manager with the source subfolder layout; every
  row fills in with its live conversion status. Presets referencing plugins
  you don't own get a warning badge — double-click a preset to toggle its
  effects yourself.
- The **expand button** (top of the file manager) opens it across the whole
  page — Esc or the button puts it back.
- Five output modes: a `converted` subfolder, a version suffix beside the
  originals, overwrite, or one ZIP / one folder that mirrors the subfolders.
  Every run is a clean rebuild and writes a `conversion-report.csv`.

## Effect Lister

- Open a preset to read it the way AE's Effect Controls panel draws it:
  parameter groups, popups, sliders, color swatches, stopwatch states,
  keyframe navigators — plus a compatibility list of what's missing on this
  machine.
- Open a folder to browse the same tree: click a folder and hand exactly that
  subfolder to Convert, or ctrl/shift-click presets and hand over a
  selection.
- **Folder report** deep-reads every preset into one table, exportable as CSV.

## Settings

Plugin profile (which suites you own), four palettes light and dark, storage
cleanup, and **Check for Updates** — the only network call the app ever makes.

## Problems?

If the app crashes it shows a readable report and saves the full trace to
`%LOCALAPPDATA%\FFXCompatibilityTool\crash.log`. **Report on GitHub** in
that dialog opens a prefilled issue with the trace on your clipboard — or
open one yourself at the
[issue tracker](https://github.com/marbou92/FFX-Compatibility-Tool/issues).
No telemetry, no crash uploads: nothing leaves your machine unless you send
it yourself.

## Build from source

```
dotnet restore FfxTool.sln
dotnet test FfxTool.sln --configuration Release
dotnet build FfxTool.sln --configuration Release
# → FfxTool.Gui\bin\Release\net48\FFXCompatibilityTool.exe — one file
#   (the DLLs and seed tables are merged/embedded into it; Debug stays
#   unpacked)
```

CI runs on every push: **CI** (test.yml), **Nightly** (nightly.yml — the
rolling pre-release), **Release** (build.yml — push a `v*` tag, or use its
Run workflow page with a version number to publish exactly that version).
The format research the engine is built on lives in RESEARCH_NOTES.md.

## License

MIT — see [LICENSE](LICENSE). Adobe After Effects, the .ffx format and the
plugin names in data/plugin_table.json are trademarks of their owners,
referenced for interoperability only; this project is not affiliated with or
endorsed by Adobe or any plugin vendor.
