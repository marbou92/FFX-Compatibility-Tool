# Spec-Verified MD3 Fixes — What Changed

Full replacement of `FfxTool.Gui/` again (same as the last zip) — this
pass corrects real, concrete inconsistencies against the **actual MD3
spec data**, not another aesthetic pass.

## What I couldn't do

You asked me to install `github.com/hamen/material-3-skill` and use it.
I confirmed (again, by actually re-fetching the repo, not just recalling
the earlier check) that it's fundamentally a Claude Code plugin — installs
via `npx skills add` or `/plugin install`, neither of which exist in this
chat interface — and its actual guidance is **Compose-first, Flutter
secondary, Web explicitly "limited/maintenance mode."** There is no
WinForms/.NET path in it at all. I could not install it, and even fully
installed, it wouldn't have driven WinForms-specific decisions.

## What I did instead

I fetched the skill's own `typography-and-shape.md` reference file
directly — which is public, distilled from m3.material.io, and contains
real spec tables (type scale, corner-radius scale, component→shape
mapping, component→type mapping). I used that to find and fix **actual
verified bugs**, not guesses:

### 1. Buttons were the wrong shape entirely

Per spec's component-shape table: **"Buttons (all types): full"** — a
true pill/stadium shape. `Md3Button` was using a fixed 16px corner radius
(`Md3Tokens.CornerLarge`), which is the spec value for FABs and nav
drawers, not buttons. Every button in the app (Convert, Open file, Scan
folder, etc.) was visually wrong. Fixed: `Md3Button` now computes a true
pill shape from its own height, matching the spec exactly.

### 2. Status chip had the shape backwards

Per spec: **"Chips: small"** (8px corner) — a rounded rectangle, not a
pill. `Md3StatusChip` was doing the opposite of what buttons should have
been doing: a full pill shape, borrowed from the button treatment I'd
mistakenly applied everywhere. Fixed: now uses the correct 8px corner.
(Not currently instantiated in any tab, same as before, but correct now
for whenever it is.)

### 3. Navigation rail was using the wrong type scale

Per spec's component-type table: **"Navigation label: Label Medium."**
`NavRail` was using Title Medium for the selected item and Body Large for
unselected — both wrong; those are card-title and body-text styles, not
navigation. Added a proper `Md3Tokens.LabelMedium` token (spec: 12sp/500
weight) and switched the rail to use Label Large (selected) / Label
Medium (unselected), both from the correct type category.

## Also carried forward from the previous pass

The theme system, Settings tab, 4 palettes × light/dark, and nav rail
animation from the last zip are all still here, unchanged — this pass
only touched shape/type-scale correctness on top of that foundation.

## Same honesty note, and one thing worth being extra careful about here

Not compiled by me, brace-balance checked (passed on every file). One
thing worth flagging specifically: while fixing `Md3Button`'s shape I made
an editing mistake mid-pass — an overly broad find-replace briefly
corrupted `Md3Card`'s doc comment — which I caught by re-reading the file
immediately after and fixed before finalizing. I'm mentioning this not to
be dramatic about it, but because it's a real example of exactly the kind
of small mistake that's easy to make during a multi-file patching pass
and easy to miss if you don't re-verify — worth you doing the same
"re-read after editing" discipline if you make further changes to these
files yourself.

Please build, then specifically look at: any button (should now be a true
pill, not slightly-rounded-rectangle), and the nav rail's text size
(should look a bit smaller/more label-like now, not headline-sized).
