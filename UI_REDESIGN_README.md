# UI/Layout Redesign — What Changed

Diff-only zip. Extract into your `FfxTool.Gui/` folder, overwriting the
existing files with the same names. Two brand-new files
(`NavRail.cs`, `AppHeader.cs`) are added alongside.

## What changed and why

**1. Native TabControl → MD3 navigation rail (`NavRail.cs`, new)**
WinForms' built-in `TabControl` can't be restyled at all — no custom
colors, no custom shape, it just renders as the OS's default tab chrome
(which on Win7 looks especially dated). Replaced it with a custom-painted
vertical nav rail: pill-shaped selected-state indicator, MD3 color tokens,
click handling done manually since this isn't a real WinForms component,
it's a `Panel` with `OnPaint` + `OnMouseClick` overrides.

**2. Added a top app bar (`AppHeader.cs`, new)**
A simple title strip with a bottom hairline — MD3's "top app bar" pattern.
Previously the only "header" was the OS window title bar.

**3. `MainForm.cs` — rebuilt around `TableLayoutPanel`**
Old version used `TabControl` + manual page-add. New version: a 2-row
`TableLayoutPanel` (header, then body), where the body is itself a 2-column
`TableLayoutPanel` (nav rail, then a content host `Panel` that swaps which
tab's control is `Visible`). This replaces `TabControl`'s built-in page
switching with manual visibility toggling driven by `NavRail.SelectionChanged`.

**4. All three tabs — rebuilt around layout panels instead of pixel math**
The old `ListerTab`/`ProfileTab`/`ConvertTab` positioned every control with
hand-calculated `Location = new Point(x, y)` coordinates and manual
`Anchor` combinations. This is fragile — resizing the window could leave
controls overlapping or with dead space, and adding/removing a control
meant re-deriving every Y-offset after it by hand.

Replaced with:
- `ListerTab`: `TableLayoutPanel` with 3 rows (header / list — fills
  remaining space / summary), `FlowLayoutPanel` for the header row's
  button+label pair.
- `ProfileTab`: `FlowLayoutPanel` (`TopDown`) for the vendor card list
  instead of a manual Y-increment loop.
- `ConvertTab`: `TableLayoutPanel` with 5 rows, using `Percent` row
  heights (55%/45% split) for the effect checklist and result box so
  *both* grow proportionally when the window resizes, instead of the old
  fixed pixel `Size`.

## Same honesty note as every previous C# delivery

I still can't compile this myself (no .NET SDK in this sandbox). I did
run a basic brace-balance sanity check across every changed file (all
passed), but that's a long way from "this compiles" — treat your next
`dotnet build`/CI run as the real test, same as always. Given this is a
layout-heavy change, the most likely failure mode if something's wrong is
a control not appearing where expected or a `TableLayoutPanel` row not
sizing the way I intend — not a build error, most of this is
straightforward WinForms API usage. Worth actually clicking through all
three tabs and resizing the window once it builds, not just confirming
it launches.

## Files in this zip

```
FfxTool.Gui/
  NavRail.cs      # NEW — MD3 nav rail
  AppHeader.cs     # NEW — MD3 top app bar
  MainForm.cs       # REPLACED
  ListerTab.cs       # REPLACED
  ProfileTab.cs       # REPLACED
  ConvertTab.cs        # REPLACED
```

`Md3Tokens.cs` and `Md3Controls.cs` are unchanged — this redesign reuses
the same color/spacing tokens and button/card/chip controls from before,
just arranges them more robustly.
