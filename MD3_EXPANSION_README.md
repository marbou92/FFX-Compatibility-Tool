# MD3 Component Expansion — What Changed

Diff-only zip, per m3.material.io/components. Extract into `FfxTool.Gui/`:
- `Md3ComponentsExtended.cs` — NEW
- `ProfileTab.cs` — REPLACED
- `ConvertTab.cs` — REPLACED

## New components

**`Md3Switch`** (m3.material.io/components/switch) — a real MD3 track+thumb
toggle, hand-painted. This is deliberately a *different* component from a
checkbox, not just a re-skinned one — MD3 treats them as semantically
distinct (switch = immediate on/off state; checkbox = multi-select in a
list). Now used in `ProfileTab` for the per-vendor toggles, replacing the
plain `CheckBox`.

**`Md3Checkbox`** (m3.material.io/components/checkbox) — rounded-square
with a check glyph, for future use anywhere an actual multi-select list
checkbox is more appropriate than a switch (not wired into any tab yet
since the current checklist usages — `CheckedListBox` in ConvertTab — use
a different WinForms control entirely that doesn't support swapping in a
custom `CheckBox` subclass per-item without much more owner-draw work than
fits this pass).

**`Md3ComboBox`** (m3.material.io/components/menus#dropdown-menus) — an
owner-drawn dropdown with MD3's outlined-field shape and color tokens,
including a custom caret glyph replacing the native Windows combo arrow.
Now used in `ConvertTab` for the target-version selector.

## Same honesty note as every C# delivery in this project

Not compiled by me (still no .NET SDK in this sandbox) — brace-balance
checked only, which is a weak signal. `Md3Switch` and `Md3ComboBox` both
override `OnPaint`/`OnDrawItem`, which is the part most likely to need a
tweak if something renders wrong (e.g. clipping, or the owner-draw combo
box's dropdown list vs. closed-box painting looking inconsistent) — that
class of bug shows up visually, not as a build error, so click through
Convert and Plugin Profile tabs specifically after this build succeeds.

## Not changed this pass

- `ListerTab`'s `ListView` — WinForms `ListView` doesn't support per-cell
  custom painting without substantially more owner-draw work than fits
  here; left with its existing MD3-token-colored rows (background color
  per status) from the earlier pass rather than a full MD3 data-table
  treatment.
- `Md3Checkbox` isn't wired into any tab yet — added for future use.
