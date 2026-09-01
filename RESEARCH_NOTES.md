# Research Notes — How This Was Figured Out

No official Adobe spec exists for the `.ffx` binary format. Everything in
`ffx_core/` was derived by hand-diffing real sample files. This document
records the derivation and, importantly, **the mistakes made along the
way** — several fixes looked correct in isolation but caused new failures,
and future contributors should read this before "simplifying" anything in
`pipeline.py`.

## Container format

`.ffx` files are RIFX (big-endian RIFF): `RIFX` + 4-byte big-endian size +
`FaFX` form tag, then a sequence of chunks. Each chunk is a 4-byte id + a
4-byte big-endian size + that many bytes of content, padded to an even
byte count. `LIST` chunks additionally have a 4-byte form tag before their
nested children (same nesting rules as top-level `RIFX`).

## The `head` chunk and the version gate

16 bytes, 4 big-endian uint32 fields: `[3, VERSION, X, 0x01000000]`. The
2nd field gates which AE version will open the file.

- **CS5.5 confirmed value: 78 (0x4E)** — derived by diffing the user's own
  native-CS5.5 preset (`Good_Quality_CC.ffx`, despite the misleading "CC"
  in its filename — CC there stood for "Color Correction", not the AE
  version) against a CC-saved preset. The CC file's value was 95 (0x5F);
  swapping only this byte was the first fix attempted.
- CC-era files in this project showed values 93, 94, and 95 — evidently
  varies per CC sub-version/build, not a single constant.
- **Mistake #1**: patching only this byte was not sufficient — the
  resulting file crashed AE on load. The version gate wasn't the only
  format difference between CC and CS5.5; see the string-encoding section
  below.

## Effect index (`besc` → `tdsp`/`tdsn`) and parameter blocks (`sspc`)

Discovered via full recursive chunk-tree dumps, then cross-referencing
against `strings`-style greps for known effect names.

- `LIST besc` is the single top-level container holding effectively
  everything: a `beso` header, then repeating `(LIST tdsp, tdsn)` pairs
  (the effect index), then a run of `LIST sspc` blocks (the actual
  parameter data, one per non-sentinel effect, in the same order as the
  index).
- `LIST tdsp` is **always exactly 172 bytes**, regardless of the effect's
  name length. This was the first clue that some fields inside it are
  fixed-width, not length-prefixed — which mattered a lot later (see
  Mistake #3).
  - Contains two `tdmn` chunks: `tdmn[0]` is always the literal string
    `"ADBE Effect Parade"`; `tdmn[1]` is the effect's real match-name
    (e.g. `"S_Sharpen"`). Both are **fixed 40-byte, null-padded, in both
    CC and CS5.5 format** — this field never needed conversion.
  - Contains two `tdix` chunks (4-byte uint32 each): `tdix[0]` is always
    `0xFFFFFFFF` (an unused marker); `tdix[1]` is a **sequential index**
    that ties this entry to its `sspc` parameter block by position.
- `tdsn` (a sibling immediately after each `tdsp`) holds the effect's
  custom/display name (e.g. `"S_FilmDamage 2"` for a second instance of
  the same effect on a layer).
- `LIST sspc` blocks hold the actual parameter data. `fnam` (a direct
  child) holds the effect's short name (e.g. `"Looks"`, distinct from the
  match-name `"MB LookSuite3"`).
- A final sentinel `tdsp` entry (`tdmn` = `"ADBE End of path sentinel"`,
  single `tdix` = `0xFFFFFFFF`) terminates the index. Never touch it.

## The Magic Bullet Looks crash, and effect removal

The user's first test file crashed AE even after the version patch. Effect
listing (via `tdmn`) revealed it used `MB LookSuite3` (Magic Bullet Looks)
and three Boris FX Sapphire effects (`S_FilmDamage`, `S_MathOps`,
`S_Sharpen`) plus native effects. The user confirmed Sapphire was
installed but Magic Bullet Looks was not — a plausible crash cause, since
a missing plugin binary can crash AE outright on project load rather than
just showing a red "missing effect" warning.

Removing an effect requires deleting **three** things, not just its
name:
1. The `tdsp`+`tdsn` pair in the index (found via `tdmn[1]` match-name).
2. The corresponding `sspc` parameter block, matched **by position**
   (order in the `sspc` sequence corresponds to the order of non-sentinel
   `tdsp` entries) — matching by `fnam`'s short name alone is unreliable,
   since short names like `"Looks"` aren't guaranteed unique.
3. **Mistake #2**: after deleting entries, the surviving `tdix[1]` values
   are left with gaps (e.g. `1, 2, 3, 4, 5, 7, 8...` after removing
   indices 0 and 6). AE uses `tdix` to look up each effect's own
   parameter block; a gap causes it to read the *wrong* block for later
   effects — this manifested as displayed names/parameters not matching
   their actual effect, `not a crash`. Fixed by renumbering every
   remaining `tdix[1]` to be contiguous `0..N-1` after any removal.

## The string-encoding difference (the "Utf1/Utf2" bug)

Even after fixing the crash and the `tdix` gap, effect and parameter names
displayed as garbled placeholder text (`Utf`, `Utf1`, `Utf2`...) — AE's own
auto-dedupe suffixing kicking in because every name was literally reading
back as the same string.

Root cause, found by diffing a **CS5.5-native** reference file (built
directly by the user in CS5.5, never touched by CC) against the CC file:

- CC's `tdsn` and `fnam` chunks are encoded as `"Utf8"` (4 bytes) + a
  4-byte big-endian length + the string — a self-describing, variable-size
  format.
- CS5.5's native `tdsn` is **plain text + a single null terminator, no
  prefix, no length field, variable size**.
- CS5.5's native `fnam` is **plain text, null-padded to a fixed 48 bytes,
  no prefix**. This is *not* the same treatment as `tdsn` — `fnam` sits at
  a fixed byte offset inside `sspc`, and leaving it variable-length (as
  `tdsn` correctly is) shifts every field positioned after it, corrupting
  the rest of the block.
- **Mistake #3**: the first attempt stripped the `Utf8` prefix from every
  matching chunk uniformly (treating `fnam` the same as `tdsn`) — this
  produced a **hard crash**, worse than the cosmetic naming bug it was
  meant to fix, because it broke `sspc`'s fixed-offset internal layout.
  The fix required distinguishing `fnam` (fixed 48-byte, must pad) from
  `tdsn`/`pdnm` (variable, just strip-and-null-terminate).
- A third field type, `pdnm` (parameter display names — e.g. "Opacity",
  or a pipe-delimited dropdown option list like
  `"Off|Side By Side|Compare..."`), was found later via a full leaf-chunk
  scan for anything still carrying `Utf8`. It follows the same
  variable-length treatment as `tdsn`.

## Keyframes and third-party plugin data — deliberately untouched

`lhd3` (keyframe header: count + flags) and `ldat` (keyframe data: time,
value, and bezier tangent doubles for Graph Editor easing) were **never
modified** by any step in the pipeline, and verified byte-identical
before/after in every test file. This turned out to be correct — no
conversion was needed for keyframe data at all, only for the container's
name-string encoding.

Third-party plugins may carry their own **proprietary** parameter blob —
confirmed with RE:Vision Effects' Twixtor, which stores its internal speed
/ time-remap graph curve in an `sdat` chunk with no publicly known format.
Two attempts to reverse-engineer this were considered and explicitly
**not** attempted:
- No CC-vs-CS5.5 pair of the *same* Twixtor curve was available to diff
  (the only CS5.5-native Twixtor sample was built directly in CS5.5, with
  no CC-side equivalent to compare against).
- Blind-patching a plugin's private binary format without a way to verify
  the result carries real risk of silently corrupting the curve rather
  than failing loudly — worse than doing nothing.

The pipeline leaves these blobs completely untouched, and the verification
pass explicitly checks that they remain byte-identical after conversion.
This has been sufficient in every real test case so far — Twixtor's own
graph curve transferred correctly with zero modification needed, once the
container-level version/string fixes were in place.

## Summary of what NOT to do (learned the hard way)

- Don't patch only the version byte and assume that's sufficient.
- Don't remove an effect's index entry without also removing its matching
  `sspc` block and renumbering `tdix`.
- Don't treat `fnam` the same as `tdsn`/`pdnm` — it's fixed-width, they're
  not.
- Don't attempt to reverse-engineer a third-party plugin's own parameter
  blob without a same-curve CC/target-version pair to diff against.
- Always run the full verification pass (zero `Utf8` tags, contiguous
  `tdix`, unchanged keyframe/blob data) — several of the above mistakes
  produced a file that "looked" fine (parsed without error) but was wrong
  in ways only visible by actually opening it in AE.

## Keyframe records are per-DIMENSION (lhd3 field [3]) — round-23 graph fix

The graph pane's "spike then decay" artifact on some effects (BCC points
especially) was the keyframe reader treating a 2D stream as 1D:

- `lhd3` is 52 bytes of big-endian uint32s. Proven layout:
  `[0] 0x00D00BEE` magic, `[2]` keyframe count, `[3]` **dimension count**,
  `[4]` record size (48), the rest zeros/flags on current samples. Every
  stream in `sample_1.ffx` is 1D and carries `[3] = 1`.
- `ldat` holds `count x dims` records, interleaved per keyframe
  (dim0, dim1, dim0, dim1, ...), each record the proven 48-byte layout
  (time, interp in/out, value, in-slope, in-influence, out-slope,
  out-influence, 2 bytes pad).
- Reading a 2D stream as 1D plots X and Y as if they were consecutive
  keyframes — the value graph zig-zags between the dimensions and the
  speed graph spikes and decays; shapes AE never draws.
- The dimension declaration is only trusted after two checks: the record
  count must tile the ldat exactly, and every dimension of one keyframe
  must share its time (the structural fingerprint of interleaving).
  Anything else falls back to the 1D read. `[3] = 0` (synthetic test
  files) reads as 1.
- Round 23 plotted only dimension 0 (the row/tooltip named 2D streams
  "X of 2D (Y = ...)"); round 25 decodes dimension 1's value AND its
  own tangent block, so the value graph draws AE's X+Y curve pair and
  the speed graph draws the combined magnitude (round-25 section below).
- **Padded records carry the tangents too (round 26).** Every tangent
  offset lives in the record's FIRST 48 bytes; a writer that pads the
  record beyond byte 48 keeps the proven layout intact, and the padding
  proves it: when every byte past byte 48 is zero, the tangent block
  decodes with the same confidence as a 48-byte record. Round 25 and
  earlier decoded tangents ONLY at recSize == 48, so a padded stream
  silently drew clean straight lines where AE shows eased curves — the
  "the curve math doesn't look like AE" report that no presentation fix
  could reach. A record whose tail is NOT zero (an unknown layout whose
  +16 could be a second value double) still takes the honest linear
  read; the fixture's 48-byte streams are untouched (byte-identical
  decode, proven old-vs-new).

## The pard param-flags word (+4) and AE-hidden rows — round-23 panel fix

The 148-byte `pard` descriptor in `LIST parT` starts with a big-endian
uint32 **flags word at offset +4** (the control kind is the uint32 at +12,
its low byte at +15):

- **Bit 0x200 hides a parameter.** Proven on `sample_1.ffx`: BCC's three
  "placeholder" rows carry 0x220, Sapphire's opaque "mocha" blob 0x208 —
  and every visibly rendered parameter of all three vendors leaves bit
  0x200 clear. Visible rows carry 0x8 / 0x20 / 0x2 freely, so only 0x200
  may hide a row.
- BCC's own "Hidden" slider carries 0x8 like visible sliders, so the flag
  alone cannot identify it; it is hidden by its exact display name, along
  with the "placeholder" padders.
- ARB_DATA parameters (kind 11) render no UI in AE at all — hidden
  unconditionally (Sapphire "mocha", BCC "Mocha Data0").
- Net effect on the fixture: BCC's Effect Controls drop 5 junk rows
  (3x placeholder, "Hidden", "Mocha Data0") and Sapphire's mocha blob row,
  matching what AE's own panel shows.

## Nested tdgp groups: the naming order, and the tdmn BEFORE the group (round 26)

A nested `LIST tdgp` usually carries its display name in a `tdsn` leaf.
Round 26 established the FULL naming order AE's own writer uses:

1. the `tdsn` inside the group;
2. **the `tdmn` directly BEFORE the group** — the group's match name.
   The fixture pairs `'ADBE Effect Built In Params'` with the
   "Compositing Options" tdgp and `'ADBE Effect Mask Parade'` with its
   internal wrapper six times, always as the immediately preceding
   sibling. That tdmn belongs to the GROUP, not to the next parameter:
   leaving it pending mis-paired the next `tdbs` that carried no tdmn of
   its own (its parT kind/flags/menu then came from the group's
   descriptor — a visible row could inherit the group's 0x200 hidden bit
   and vanish, or a GROUP marker misfire), and an unnamed group hoisted
   every parameter inside it one level up. The walker now consumes the
   tdmn at the tdgp, uses it as a name candidate, and clears it.
3. a `tdmn` INSIDE the group resolving to the parT descriptor's display
   name (round 23's fallback — and the last resort: on a tdsn-less group
   it used to name the GROUP after its first inner PARAMETER, a header
   AE never shows).

Anonymous wrappers (no name anywhere) keep the parent path — they ARE
the parent visually.

## Graph Editor rendering references (round-23; corrected rounds 24/25)

The round-23 reading of a "light editor" with unselected direction
lines and a faint speed fill was wrong - the round-24 screenshot pair
(value graph, speed graph) shows AE's REFERENCE shots wearing one dark
skin. Round 25 settles the theme question the way AE itself does: AE's
Graph Editor follows AE's UI brightness preference, so OUR editor
follows the app theme - dark app = AE's dark palette, light app = the
app's own light tokens. The round-24 shape grammar stands:

- dark editor (BOTH modes): field #656565, grid #595959, zero line
  brighter, curve #CBCBCB, picked key #FFEE00, current-time line
  #FC0000, ruler labels bare numbers - the readout above the plot
  carries the unit ("N units" / "N units/sec", AE's own grammar).
- light editor (BOTH modes, round 25): the app's own tokens - B.Surface
  field, B.OutlineVariant grid, B.Primary curve and key fill,
  B.OnSurfaceVariant labels; AE's #FFEE00 picked key and #FC0000
  current-time line stay fixed (they read on any brightness).
- value graph: direction lines exist ONLY on the picked key
  (unselected keys show none; round-23 drew every bezier key's lines -
  disproven by the round-24 screenshot). The value carry beyond the
  keyed span is DASHED (AE's dashed stub after the last key). No glow,
  no fill: a plain 2px line.
- speed graph: SIGNED derivative with NO area fill (the round-23 faint
  fill was wrong - AE's field stays uniformly dark under the curve).
  Each segment renders as ONE analytic arc (dv/dt evaluated straight
  from the segment's control points); keyframe speed discontinuities
  get TRUE VERTICAL jump lines at the key's time (the reference's
  vertical plunge at the left edge and rise at the right edge are the
  carry-to-segment and segment-to-carry jumps); zero carries fill the
  window outside the keyed span. Ruler: ~6 lines, bare numbers.
- speed-editor key icons are shaped by interpolation (circle = bezier /
  easy ease, hollow square = linear, half square = hold); the value
  editor draws squares regardless. The PICKED key's icon is painted
  editor yellow; AE draws no selection ring around it.


## Multidimensional graphs draw AE's per-dimension pair (round 25)

Round-25 research pins AE's Graph Editor behavior for a 2D property
(Position, a POINT control):

- VALUE graph: ONE CURVE PER DIMENSION - "when you animate Position,
  the value graph shows two separate lines, X and Y" (the two curves
  overlay in one editor; Separate Dimensions splits them apart). The
  Y curve needs Y's OWN tangent block - the 48-byte ldat record
  carries it one record after dimension 0's - so the parser decodes
  dimension 1's slopes/influences/interpolation too, and PresetCurve
  builds Y segments through the same proven geometry.
- SPEED graph: ONE COMBINED curve - "the speed graph combines them
  into one single line representing the object's overall speed", the
  magnitude sqrt(vx^2 + vy^2) of the per-dimension signed rates. 1D
  streams keep the signed speed (the -500 dip reference).
- Dimension 1's tangent block gets the same garbage envelope as
  dimension 0 (ClampHandle2), so one absurd Y slope cannot bend the
  Y curve into an arc AE never draws.
