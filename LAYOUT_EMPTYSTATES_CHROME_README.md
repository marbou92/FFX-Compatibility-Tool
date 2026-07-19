# Layout Audit, Empty States, Window Chrome — What Changed

Full replacement of `FfxTool.Gui/`. This is items (4), (5), (6) —
completing the 6-phase plan.

## (4) Layout audit

Two concrete inconsistencies found and fixed, plus a new shared token to
prevent the same class of drift going forward:

- **Card widths didn't match across tabs**: Plugin Profile's vendor cards
  were 420px, Settings' cards were 520px — no shared source of truth, just
  two different numbers picked independently. Added
  `Md3Tokens.ContentMaxWidth = 520` and pointed both at it.
- **Inconsistent margin** on the "Open .ffx file" button between
  `ListerTab` (had an extra unexplained bottom margin) and `ConvertTab`
  (didn't) — same button, same role, different spacing for no reason.
  Normalized.

## (5) Empty states (`Md3EmptyState.cs`, new)

Effect Lister's list and Convert's effect checklist both previously
showed a bare empty box with zero guidance before a file was loaded — not
a bug, but a real gap. Added a reusable empty-state component (icon +
title + message), wired into both:
- Effect Lister: shows before any file is opened, explains what the tab
  does.
- Convert: shows in the checklist area until a file is loaded.

Both toggle automatically via the same `Refresh_()` methods that already
existed — no new lifecycle logic needed, just an added visibility switch.

## (6) Window chrome (`Md3TitleBar.cs`, new) — the riskiest part of this pass

Every screenshot you sent showed the native Windows Aero title bar,
which breaks MD3 immersion completely. This replaces it with a fully
custom-drawn title bar (drag, minimize, maximize/restore, close) and a
`WM_NCHITTEST` override in `MainForm` for edge/corner resize.

**Why this is the riskiest change in the whole project so far, and what I
did to contain that risk:**

- Used the two well-established, "let Windows do the real work" patterns
  for this instead of anything custom-built from scratch:
  - **Drag**: `ReleaseCapture()` + `SendMessage(WM_NCLBUTTONDOWN,
    HTCAPTION)` — a standard technique that tells the OS "treat this as a
    title-bar click," and the OS's own window-drag logic takes over. Not
    manually tracking mouse deltas and repositioning the window every
    frame (which is the fragile way to do this).
  - **Resize**: `WM_NCHITTEST` override returning the correct
    edge/corner hit-test constant based on cursor position — same
    principle, OS handles the actual resize.
- **Maximize is deliberately NOT using `FormWindowState.Maximized`** —
  with `FormBorderStyle.None`, that combination is a well-known WinForms
  gotcha where the maximized window covers the Windows taskbar. Instead,
  "maximize" manually sets `Bounds` to `Screen.WorkingArea` (which
  excludes the taskbar) and remembers the previous bounds to restore.
  This avoids the gotcha entirely rather than working around it after
  the fact.
- Explicitly avoided anything touching DWM composition, window blur, or
  non-rectangular window shapes — those are the areas most likely to
  behave differently across Windows versions, which is exactly the kind
  of platform-specific risk this whole project has been trying to avoid
  (see: the entire Python→C# saga earlier in this build).

**`AppHeader.cs` removed** — it's fully superseded by `Md3TitleBar` (which
does everything `AppHeader` did — show the title — plus drag/minimize/
maximize/close), so I deleted it rather than leaving dead code sitting
next to its replacement.

## Honesty note — read this one before building

This is the pass I'd trust least of everything delivered so far, and I
want to be direct about why: custom window chrome is fundamentally
different from everything else in this project. Every previous fix was a
custom-painted *control* inside a normal window — worst case, something
looks wrong. This changes how the *window itself* behaves at the OS
level. If something's subtly wrong here, the failure modes are more
disruptive: the window might not drag correctly, might not resize from
every edge, might behave oddly when snapped to a screen edge (Windows'
Aero Snap), or might interact unexpectedly with Win7's specific DWM
behavior in a way I can't verify without actually running it there.

I chose the two most standard, most-documented techniques available
specifically to minimize this risk rather than write anything from
scratch, and reasoned through the taskbar-covering gotcha proactively
rather than hitting it and patching after — but this is genuinely the
one change in this whole project I'd want tested most thoroughly before
trusting it, including specifically: drag from the title bar, resize from
all 4 edges and all 4 corners, maximize/restore, and minimize, each
checked individually rather than just "does the app open."

If this causes real problems on your Win7 machine, the safe fallback is
reverting just `MainForm.cs` and `Md3TitleBar.cs` to restore native
window chrome — everything else in this pass (layout audit, empty
states) is independent and doesn't need to be reverted with it.
