# FFX Compatibility Tool

[![CI](https://github.com/marbou92/FFX-Compatibility-Tool/actions/workflows/test.yml/badge.svg?label=CI)](https://github.com/marbou92/FFX-Compatibility-Tool/actions/workflows/test.yml)
[![Nightly](https://github.com/marbou92/FFX-Compatibility-Tool/actions/workflows/nightly.yml/badge.svg?label=Nightly)](https://github.com/marbou92/FFX-Compatibility-Tool/actions/workflows/nightly.yml)
[![Release](https://github.com/marbou92/FFX-Compatibility-Tool/actions/workflows/build.yml/badge.svg?label=Release)](https://github.com/marbou92/FFX-Compatibility-Tool/actions/workflows/build.yml)

A small Windows desktop tool for After Effects **.ffx** presets: it
downgrades presets to older After Effects versions, removes effects whose
plugins you don't own, and lets you inspect what is really inside a preset —
all offline, in one portable exe.

Built for **.NET Framework 4.8 / WPF**, so it runs on Windows 7 SP1 through
Windows 11. No installer, no runtime download on Windows 10/11.

## Download & install

Grab **FFXCompatibilityTool-windows.zip** from the
[latest release](https://github.com/marbou92/FFX-Compatibility-Tool/releases/latest),
unzip it anywhere and run **FfxTool.Gui.exe**.

- **Windows 10/11** — .NET Framework 4.8 is preinstalled, nothing to install.
- **Windows 7 SP1** — needs the .NET 4.8 runtime installed once
  ([download](https://dotnet.microsoft.com/download/dotnet-framework/net48)).

Every release zip carries its SHA-256 on the release page. The app's own
**Settings → About → Check for Updates** resolves the project's latest
release on GitHub — that single lookup is the only network traffic the app
ever makes. A rolling **nightly** pre-release (built from every push to
main) is published at the `nightly` tag for testing new features early.

## What it does

### Convert

- Drop presets — or a whole folder of them — anywhere onto the window, or
  browse. A folder shows up as an **explorer-style file manager**: the
  source folder's subfolders nest the presets exactly like on disk
  (folders expand, presets sit inside their folder), and each row fills
  in with its live conversion status (`Converting…` → `OK` / `WARN` /
  `FAILED`). **Bigger list** opens the file manager across the whole
  page — same card, same behaviours, far more rows — and puts it back
  exactly where it was.
- Presets whose effects reference plugins that are unknown or not selected
  in your plugin profile get a **warning badge** in the file manager —
  hover it for the exact list, and double-click the preset to open the
  effect checklist and toggle those effects yourself. The flags follow
  your profile live.
- A single preset shows its effects as a checklist against your plugin
  profile: what you don't own is pre-marked for removal.
- Conversion applies the target version, optionally removes the marked
  effects, and **verifies** the output (structure, effect indices and
  keyframe data) before anything is written.
- Folder mode converts every `.ffx` in one pass — one console line per
  file — with five output modes:
  - **Subfolder "converted" inside the source** — one converted copy per
    preset, side by side.
  - **Beside originals, with a version suffix** — `MyPreset_cs55.ffx`
    next to `MyPreset.ffx`.
  - **Overwrite the original files** — asks first; the originals cannot
    be recovered.
  - **One ZIP that mirrors the subfolders** — `Presets (converted).zip`
    beside the source folder, holding the converted presets inside their
    real subfolders and nothing else (no invented folders, ever).
  - **One folder that mirrors the subfolders** — the same tree as a
    plain folder next to the source.
- The derived outputs (subfolder, suffix, ZIP, mirrored folder) are
  **clean rebuilds**: every run produces exactly that run's result, so a
  preset that fails or was removed from the queue can never leave an
  older converted copy behind. They can never overwrite the input file.
- Derived outputs also carry a **`conversion-report.csv`** — one quoted
  row per preset: file, source subfolder, status, effects kept/removed
  and decode notes — UTF-8 with BOM, opens straight in Excel.

### Effect Lister

- Open a preset to read it the way AE's Effect Controls panel draws it:
  parameter groups, popups, sliders with ranges, color swatches, stopwatch
  states, keyframe navigators.
- The split inspector adds a compatibility list (which effects are likely
  missing on this machine) plus a keyframe view with AE-style timecodes and
  the value/speed graph pair.
- Open a folder and the workspace becomes an **explorer-style file
  manager** with the same subfolder tree: click a preset to open it with
  the full anatomy, or use **All presets** to come back to the tree.
  Clicking a **folder** targets the handoff at it — the button reads
  `Convert folder “Sub” (N)…` and sends exactly that subfolder with its
  layout. Ctrl+click picks several presets and Shift+click picks a range —
  **Convert selection…** sends exactly those to Convert, and with
  nothing picked it sends the whole folder with its subfolder layout.
  The two file managers share one look but not one behaviour: Convert's
  runs the batch (statuses, plugin flags, double-click to edit, bigger
  list), the Lister's browses and hands off.
  The **Folder report** deep-reads every preset into one
  table — status, effect/parameter/animated counts, size, decode notes —
  exportable as CSV.
- **Convert this preset…** hands the preset you are reading straight to
  the Convert section.

### Settings

- **Plugin profile** — which plugin suites you own; the compatibility list
  and the "remove effects missing from my profile" option key off it.
- **Appearance** — four color palettes, light and dark, applied live.
- **Storage** — inspect and delete the plugin-scan catalog and the
  recently-opened history. After updating the tool, run the plugin scan
  once more: the catalog harvest now reads much deeper into big plugin
  packs (Boris Continuum and friends), which fixes the
  "installed: something.aex" lines that used to name the wrong file.
- **About** — build version and Check for Updates.

## Privacy

No telemetry, no analytics, no crash uploads. Crash reports and session
logs stay in `%LOCALAPPDATA%\FFXCompatibilityTool` (and
`%APPDATA%\FFXCompatibilityTool` for the journal) and never leave the
machine unless you paste them somewhere yourself.

## Building from source

```
dotnet restore FfxTool.sln
dotnet test FfxTool.sln --configuration Release
dotnet build FfxTool.sln --configuration Release
# → FfxTool.Gui\bin\Release\net48\FfxTool.Gui.exe (+ dependency DLLs + data\)
```

Building `net48` needs the .NET Framework 4.8 targeting pack (GitHub's
`windows-latest` runners have it preinstalled). CI (`.github/workflows/test.yml`)
builds and tests every push. `.github/workflows/build.yml` publishes
releases two ways: push a `v*` tag, or open the workflow's **Run workflow**
page, type a version number (e.g. `0.2.0`) and run — it tests, packages
the Release output, stamps the exe's version to match, creates the tag
and publishes the release with a description written automatically from
the commit history. Leave the version box empty to build an artifact
without publishing anything. `.github/workflows/nightly.yml` keeps the
rolling nightly channel fed: every push to main plus a daily scheduled
build is published as a pre-release at the `nightly` tag.

## Repository layout

```
FfxTool.Core/        # the RIFX/FaFX engine: parsing, conversion, verification
FfxTool.Core.Tests/  # xUnit suite (round-trip tests on real fixtures)
FfxTool.Gui/         # the WPF app (MD3-styled): Convert, Effect Lister, Settings
data/                # plugin_table.json + effect_names.json (recognition tables)
```

`RESEARCH_NOTES.md` documents the file-format findings the engine is built
on — chunk layout, effect/parameter descriptors, keyframe timing — with the
evidence for each.

## Preset compatibility notes

Hard-won rules from the format research, preserved by the engine:

- `fnam` chunks are padded to a fixed 48 bytes; `tdsn`/`pdnm` stay
  variable-length — treating these the same crashes After Effects.
- Effect removal matches `sspc` blocks to `tdsp` entries by **position**,
  never by name, and renumbers `tdix` afterward.
- The keyframe tick is 1/1024 of a 30 fps frame (30720 ticks/second).
- Keyframe streams and third-party plugin blobs are never rewritten;
  `Pipeline.Verify()` checks that holds after every conversion.

## License

MIT — see [LICENSE](LICENSE).

Trademark note: Adobe After Effects, the .ffx preset format, and the
third-party plugin names referenced in data/plugin_table.json (Boris FX,
Red Giant/Maxon, Video Copilot, Plugin Everything, RE:Vision Effects,
etc.) are trademarks of their respective owners, referenced here solely
for interoperability and identification. This project is not affiliated
with or endorsed by Adobe or any of the plugin vendors it detects.
