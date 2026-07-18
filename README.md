# FFX Compatibility Tool — C# / .NET Framework 4.8 port

A from-scratch port of the Python `ffx_core` engine to C#, targeting
**.NET Framework 4.8** specifically for real Windows 7 compatibility —
see the repo's earlier history for why: Python 3.9+, Qt6 (PySide6), and
even PySide2's available wheel range all independently stopped supporting
Win7, and chasing each one individually stopped being productive.

## Important: what's verified and what isn't yet

**I could not compile or run this C# code myself** — this sandbox has no
.NET SDK installed and no way to install one (network access here is
locked to a handful of package registries, not Microsoft's). Every line
was written by careful manual translation from the Python version that
*was* fully tested against your real preset files across this whole
project, but **the C# port itself has not been executed by anyone yet.**

`.github/workflows/test.yml` is set up to build and run the full test
suite (a port of every meaningful test from `tests/test_riff.py` and
`tests/test_pipeline.py`, including a real-file round-trip using the same
`sample_1.ffx` fixture) on `windows-latest` the moment you push this. That
CI run is the actual first real test of this code — please check it
before trusting the logic, the same discipline the Python version went
through before any of it got called "confirmed."

If it fails, the most likely culprits, roughly in order of likelihood:
1. A typo or off-by-one in the manual translation (most likely — this is
   hand-ported, not machine-translated)
2. `System.Text.Json` version pin needing adjustment for net48 compat
3. Something about the `<None Include>` linked-file paths for
   `plugin_table.json` / the `.ffx` fixture not resolving the way I
   expect across `dotnet build`'s output structure

None of these would be surprising for a first-pass port — flag whatever
the CI output shows and I'll fix it directly rather than guess further.

## Structure

```
FfxTool.Core/              # port of ffx_core — RiffNode.cs, Pipeline.cs, PluginLookup.cs
FfxTool.Core.Tests/         # xUnit port of test_riff.py / test_pipeline.py
data/plugin_table.json      # unchanged — same file the Python version uses
.github/workflows/test.yml  # dotnet build + test, runs on windows-latest
```

No GUI project yet — per the plan, Core gets verified first (via your CI
run), then a WinForms GUI project gets built on top of it, mirroring how
the Python version did Phase 1-2 (core+CLI) before Phase 3 (GUI).

## What was deliberately preserved from the Python version

Every hard-won detail from `RESEARCH_NOTES.md` carried over as-is:
- `fnam` chunks get padded to a fixed 48 bytes; `tdsn`/`pdnm` stay
  variable-length — these are NOT the same treatment (this distinction
  was Mistake #3 in the original derivation; getting it wrong crashes AE).
- Effect removal matches `sspc` blocks to `tdsp` entries by **position**,
  not name, and always renumbers `tdix` afterward.
- Keyframe (`lhd3`/`ldat`) and third-party plugin blob data is never
  touched by any pipeline step, and `Pipeline.Verify()` checks this holds
  after every conversion — same verification discipline as the Python
  version, not weakened for the port.

## Running locally (once you have the .NET SDK)

```bash
dotnet restore FfxTool.sln
dotnet build FfxTool.sln
dotnet test FfxTool.sln
```
