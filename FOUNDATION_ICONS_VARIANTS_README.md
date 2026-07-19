# Foundation Tokens, Icons, Component Variants — What Changed

Full replacement of `FfxTool.Gui/`. This is items (1), (2), (3) from the
plan — (4) layout audit, (5) empty states, (6) window chrome are next,
per your own sequencing.

## (1) Foundation tokens

- **`Md3Tokens.cs`**: expanded from 6 type-scale fonts to the complete
  MD3 scale — Display/Headline/Title/Body/Label × Large/Medium/Small (15
  total). Also added the state-layer opacity constants
  (`HoverStateAlpha`/`PressStateAlpha`/`FocusStateAlpha`) and a
  `Md3StateLayer.Paint()` helper — MD3's real hover/press mechanism is a
  semi-transparent overlay of the state color, not a flat color swap,
  which is what buttons now actually do (see below).
- **`ThemeManager.cs`**: added the two missing surface tiers
  (`SurfaceContainerLowest`, `SurfaceContainerLow`) to complete MD3's real
  5-tier surface system. These are *derived* (blended between the
  existing `Surface`/`SurfaceContainer` per palette) rather than 16 more
  hand-picked hex values — correct ordering mattered more here than exact
  hue, and hand-picking 16 more colors under this scope would have been a
  much larger error surface.
- **Elevation hierarchy now actually applied**: nav rail moved from
  `SurfaceContainer` to `SurfaceContainerLow`, establishing window
  background → nav rail → cards as three distinct tonal steps instead of
  nav rail and cards using the same tone.

## (2) Icons (`Md3Icons.cs`, new)

The app had **zero icons anywhere** before this pass — a big tell for
"doesn't look like a real MD3 app," since MD3 leans heavily on
iconography. WinForms has no icon-font support, so these are hand-drawn
vector primitives (lines/arcs/paths) on MD3's standard 24×24 icon grid,
not bitmaps. 10 icons: folder-open, convert, settings-gear, check,
warning, palette, sun, moon, effect-list, plugin.

Wired in:
- **Nav rail**: every item now has an icon next to its label (folder for
  Effect Lister... wait, Effect Lister uses the list icon, Plugin Profile
  uses the plug icon, Convert uses the convert icon, Settings uses the
  gear).
- **Buttons**: "Open .ffx file" (folder-open), "Convert" (convert arrow),
  "Scan a plugins folder" (folder-open) all now show an icon before the
  label text.

## (3) Component variants

**Buttons** (`Md3ButtonVariant`: Filled/Tonal/Outlined/Text) — previously
every button in the app was Filled, including secondary actions, which
gives a screen no visual hierarchy. Applied meaningfully: primary actions
("Open .ffx file", "Convert") stay **Filled**; the secondary "Scan a
plugins folder" action is now **Outlined**, correctly reading as
lower-priority than the primary actions on the same screen. (Tonal/Text
variants exist and are ready to use but nothing in the current app calls
for them yet — didn't force usage just to demo them.)

Also fixed while rebuilding this: buttons now use a real state-layer
hover effect (semi-transparent overlay, per spec) instead of the old
`FlatAppearance.MouseOverBackColor` flat-swap approach, and support an
optional icon rendered before the label.

**Cards** (`Md3CardVariant`: Elevated/Filled/Outlined) — previously every
card in the app looked identical. Settings' Appearance/About cards are
now **Elevated** (slightly higher surface tone — MD3's own
tonal-elevation concept, used instead of a drop shadow since WinForms
shadows are unreliable across OS versions including Win7, same reasoning
already applied to buttons). Plugin Profile's vendor rows stay **Filled**
— they're equal-weight list items, not something that needs to visually
compete for attention the way Settings' two distinct sections do.

## Honesty note, same as always

Not compiled by me. Brace-balance checked on every file (all passed), and
I specifically grep-checked for stale references to removed APIs
(`FlatAppearance.MouseOverBackColor`, which the button rewrite no longer
uses) to catch the kind of leftover-reference bug that's bitten this
project before. `Md3Icons.cs` is the piece I'd trust least without seeing
it render — vector path math is easy to get subtly wrong (a line slightly
off, an arc angle backwards) in a way that only shows up visually, not as
a compile error. Worth specifically looking at each icon once this
builds, not just confirming they appear at all.
