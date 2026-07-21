# Collapsible Nav Rail — What Changed

Full replacement of `FfxTool.Gui/`. Built from interpreting your rough
Figma-export sketch (icon-only stacked buttons on the far left edge, gear
separated near the bottom) as MD3's real **collapsed Navigation Rail**
state (m3.material.io/components/navigation-rail defines both expanded
and collapsed as valid states of the same component — this isn't a
deviation from spec, it's using a documented alternate state of it).

## What's new

- **Toggle button** at the top of the rail (simple 3-line menu glyph) —
  click to switch between expanded (200px, icon+label) and collapsed
  (72px, icon-only, MD3's actual spec width for this state).
- **Tooltips on hover** when collapsed — since labels disappear, hovering
  an icon shows its name, matching MD3's own expectation for icon-only nav.
- **Settings is now "pinned"** — rendered in its own group at the bottom
  of the rail with a visual divider line separating it from Effect
  Lister/Plugin Profile/Convert, matching the separation in your sketch.
  `NavRail.AddItem()` now takes an optional `pinned: true` parameter for
  this.
- **Content area reclaims the space** — when collapsed, the content
  column grows by ~128px live (wired via a new `CollapsedChanged` event
  from `NavRail` that `MainForm` uses to resize the layout column on the
  spot, no restart needed).
- **Preference persists** across launches (`NavRailPrefs.cs`, new — same
  small-JSON-in-AppData pattern as `PluginProfile`).

## Two bugs I caught in my own code before shipping this

Worth knowing about, same as always: `ItemBoundsY()` takes an `int`
index, but while wiring the new pinned-group logic I initially called it
with the `NavItem` object itself in two places (the divider-gap
calculation and the item-bounds calculation) — a real type mismatch that
would have failed to compile. Caught by re-reading the file immediately
after writing it, not by a build (still can't compile here). Both call
sites now correctly pass the loop index instead of the item object.

## Honesty note, same as always

Not compiled by me. This one's lower-risk than the window-chrome pass —
it's back to "custom-painted control," not "OS-level window behavior" —
but it's a substantial rewrite of `NavRail.cs`'s internals (positioning
logic changed from a flat list to a main/pinned split), so I'd still
click-test both states specifically: toggle collapsed/expanded a few
times, confirm the content area actually resizes, and confirm Settings'
divider line and pinned position look right in both states.
