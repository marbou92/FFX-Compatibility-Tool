# FfxTool.Gui — WinForms GUI (with MD3-inspired styling)

Diff-only zip: extract `FfxTool.Gui/` into your existing C# repo root
alongside `FfxTool.Core/` and `FfxTool.Core.Tests/`. Also update `FfxTool.sln`
and `.github/workflows/test.yml` per below.

## Same honesty note as the Core port

I still don't have a .NET SDK in this sandbox, so **this GUI code has not
been compiled by me** — only hand-reviewed. The Core library it depends on
*is* now proven (your CI run confirmed it green), so the risk here is
narrower than last time, but treat the first `dotnet build` after adding
this the same way: as the real test, not a formality.

Likely first-build issues, roughly in order of likelihood:
1. A `net48`/WinForms designer-generated-code convention I skipped (this
   is hand-written, not designer-generated — plain constructor code
   instead of a `.Designer.cs` split, which is valid but less common)
2. `System.Drawing.Drawing2D`/`GraphicsPath` usage in `Md3Controls.cs` —
   straightforward WinForms APIs, low risk, but worth checking first if
   anything fails
3. Anchor/Dock layout math being slightly off visually (functional bug
   risk is low here — worst case is clipped/misaligned controls, not a
   crash)

## About "installing" the Material 3 skill

You asked me to install `github.com/hamen/material-3-skill` — I couldn't:
it installs into Claude Code via `npx skills add`, which only works in
that environment, and I'm running in claude.ai's chat interface with
read-only access to Anthropic's own preloaded skills. It also targets
Compose/Flutter/Web, not WinForms, so it wouldn't have driven API choices
here even if I could load it.

What I did instead: hand-applied MD3's actual published design tokens
(color roles, spacing scale, corner radii — see `Md3Tokens.cs`) via
custom-painted WinForms controls (`Md3Controls.cs`), since WinForms has no
native Material Design support at all, from any library, for any .NET
version. This is a manual style layer, not a real MD3 component library
port — good enough for a distinct, intentional look, not pixel-perfect
MD3 compliance.

## Files in this zip

```
FfxTool.Gui/
  FfxTool.Gui.csproj    # WinForms, net48
  Program.cs            # entry point
  MainForm.cs           # 3-tab shell (Effect Lister / Plugin Profile / Convert)
  Md3Tokens.cs           # MD3 color/spacing/type tokens
  Md3Controls.cs         # custom-painted button/card/chip controls
  PluginProfile.cs       # port of ffx_gui/profile_store.py
  ListerTab.cs            # port of ffx_gui/tab_lister.py
  ProfileTab.cs            # port of ffx_gui/tab_profile.py
  ConvertTab.cs            # port of ffx_gui/tab_convert.py
```

## Update FfxTool.sln

Add a third project entry (same pattern as the existing two) pointing at
`FfxTool.Gui\FfxTool.Gui.csproj`, with a new GUID and corresponding
`ProjectConfigurationPlatforms` lines. If you're using Visual Studio,
easier to just right-click the solution → Add → Existing Project instead
of hand-editing the `.sln` file.

## Update .github/workflows/test.yml

No changes strictly required — `dotnet build FfxTool.sln` will pick up
the new project automatically once it's added to the `.sln`, and building
it is itself a useful smoke test even without UI automation tests (WinForms
UI testing is a separate, heavier setup not included here).

## Running it locally (once built)

```
dotnet run --project FfxTool.Gui
```
