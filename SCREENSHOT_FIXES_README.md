# Screenshot-Driven Fixes — What Changed

Full replacement of `FfxTool.Gui/` again. This pass fixed **4 concrete
bugs I could actually see in your screenshots**, not aesthetic guesses.

## Bug 1: Black jagged artifacts on every button

Visible in Images 1, 2, and 4 — a black notch at the bottom-left corner of
"Open .ffx file", "Convert", and "Scan a plugins folder". Cause:
`Md3Button` never called `SetStyle(ControlStyles.UserPaint, true)`, so
Windows was still painting its own default button background underneath
my custom pill shape — visible wherever the pill's rounded corners didn't
fully cover the button's square bounds. Fixed by forcing `UserPaint` and
explicitly clearing to the parent's actual background color before
drawing the pill.

## Bug 2: Plugin Profile tab nearly unreadable

Image 4 — vendor names overlapping/garbled behind the switch graphics.
Root cause was actually introduced by *my own earlier fix*: when I fixed
`Md3Card` to stop reading its stale `BackColor` property and paint
`ThemeManager.Current.SurfaceContainer` directly instead, I didn't notice
that `Md3Switch`/`Md3Checkbox` were relying on reading `Parent.BackColor`
as their own background-clear color — which was now stale/wrong since
`Md3Card` no longer kept that property in sync. Fixed by having the
switch/checkbox read `ThemeManager.Current.SurfaceContainer` directly too,
instead of trusting a property on their parent.

## Bug 3: Native dropdown arrow leaking through

Image 1 — a small native-looking arrow box visible at the edge of the
target-version dropdown despite my custom painting. Real cause, confirmed
by this actually failing visually rather than just "needs more paint
code": WinForms `ComboBox` renders its closed-box chrome through the OS's
native composite layer, not through a normal `OnPaint` call — no amount of
`OnPaint` override fully replaces it. Fixed properly this time by **not
fighting it** — built `Md3Dropdown.cs`, a from-scratch control (not a
`ComboBox` subclass at all) that's just a themed clickable box opening a
small custom popup window with a hand-painted option list. Nothing native
left to leak through. Wired into `ConvertTab` in place of the old
`Md3ComboBox`, which is now deleted entirely rather than left as dead
broken code.

## Bug 4: Effect Lister's list was completely unstyled native Windows chrome

Image 3 — sunken 3D column headers, default list background, nothing
matching the rest of the app. This one wasn't a bug in previous code so
much as a gap — `ListView` was never given any custom painting at all.
Fixed using `ListView`'s actual supported owner-draw API (`OwnerDraw =
true` + `DrawColumnHeader`/`DrawItem`/`DrawSubItem` events) — unlike
`ComboBox`, this is a real, reliable, documented WinForms mechanism for
full custom rendering, not something fighting the OS. Header is now flat
with MD3 colors, selected rows use the primary-container tone, and the
existing missing/unknown-plugin row highlighting is preserved correctly
alongside it.

## What's the same as before

Nav rail (with its Label Medium type-scale fix and pill animation),
Settings tab, theme system, and all 8 palettes are unchanged from the
last two passes — this was purely bug-fixing on top of that foundation,
driven by what your screenshots actually showed rather than more
speculative "polish."

## Honesty note — same as always, but this time the evidence is stronger

I still can't compile or run this myself. What's different this time:
every fix above traces to something I could actually **see broken** in a
real screenshot, not a hypothesis about what might be wrong. That's a
meaningfully stronger signal than my usual "brace-balance checked, hope it
works" — but it's still not a compile, and `Md3Dropdown` is entirely new
code (a popup `Form` positioned relative to screen coordinates via
`PointToScreen`), which is the part I'd most want you to click-test
specifically: open the Convert tab's dropdown, confirm it opens in the
right place and closes correctly on selection or on clicking away.
