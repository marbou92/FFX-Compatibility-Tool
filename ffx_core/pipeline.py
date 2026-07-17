"""
The CS5.5-downgrade conversion pipeline.

Every step here was derived and verified against real .ffx sample files —
see RESEARCH_NOTES.md for the full derivation. Do not "simplify" any of
these steps without re-testing against real files; several looked correct
in isolation but caused crashes or corrupted names when done incompletely
(see RESEARCH_NOTES.md's account of the naming-bug debugging process).

Rules this module always follows:
  - Keyframe data (lhd3/ldat) and third-party plugin blobs (e.g. Twixtor's
    sdat) are NEVER modified, under any circumstance.
  - Every conversion ends with a verification pass (verify()) before the
    caller is allowed to treat the output as done.
"""
from __future__ import annotations

import struct
from dataclasses import dataclass, field

from ffx_core import riff

# Confirmed version-byte values for the `head` chunk's 2nd uint32 field.
# Only CS5.5 has been confirmed against a real native sample so far.
# To add a new target: get a same-preset pair (one CC-saved, one saved
# natively in the target version), diff their `head` chunks, and add the
# confirmed value here — do not guess.
KNOWN_VERSIONS = {
    "cs5.5": 78,
}

FNAM_FIXED_SIZE = 48  # CS5.5's fixed-width field size for `fnam` chunks


def _decode_utf8_prefixed(raw: bytes) -> bytes:
    """Strip CC's `Utf8` + 4-byte-length prefix, returning the raw string
    bytes (including whatever trailing null the original had). If `raw`
    isn't prefixed this way, return it unchanged — some files may already
    be in the target's plain format."""
    if raw[:4] == b"Utf8" and len(raw) >= 8:
        strlen = struct.unpack(">I", raw[4:8])[0]
        if 8 + strlen <= len(raw):
            return raw[8:8 + strlen]
    return raw


def _tdmn_effect_name(tdsp_node: dict) -> str | None:
    """Get a tdsp entry's real effect match-name (the 2nd tdmn chunk).
    Returns None for the sentinel entry, which only has 1 tdmn."""
    tdmns = riff.find_all(tdsp_node, b"tdmn")
    if len(tdmns) < 2:
        return None
    return tdmns[1]["content"].split(b"\x00")[0].decode("latin1", errors="replace")


def _fnam_effect_name(sspc_node: dict) -> str:
    fnams = [c for c in sspc_node["children"] if c["cid"] == b"fnam"]
    raw = fnams[0]["content"]
    decoded = _decode_utf8_prefixed(raw)
    return decoded.split(b"\x00")[0].decode("latin1", errors="replace")


def _find_besc(riff_node: dict) -> dict:
    besc = riff_node["children"][1]
    if besc.get("form") != b"besc":
        raise ValueError("Expected the file's 2nd top-level chunk to be `LIST besc`.")
    return besc


@dataclass
class ConversionResult:
    data: bytes
    removed_effects: list[str] = field(default_factory=list)
    warnings: list[str] = field(default_factory=list)


def list_effects(data: bytes) -> list[dict]:
    """Return every effect in a .ffx file: match-name, display name, and
    whether it's the harmless sentinel entry. Does not modify anything."""
    tree = riff.parse_file(data)
    besc = _find_besc(tree)
    out = []
    for c in besc["children"]:
        if c.get("cid") == b"LIST" and c.get("form") == b"tdsp":
            name = _tdmn_effect_name(c)
            out.append({"match_name": name, "is_sentinel": name is None})
    return out


def remove_effects_by_match_name(riff_node: dict, match_names_to_remove: set[str]) -> list[str]:
    """Preferred effect-removal function. Removes tdsp+tdsn index entries
    AND their corresponding sspc data blocks, matched by *order*, which is
    how AE itself associates them (see tdix in RESEARCH_NOTES.md)."""
    besc = _find_besc(riff_node)
    children = besc["children"]

    # First pass: figure out which sspc blocks correspond (by position) to
    # tdsp entries being removed.
    tdsp_order = [c for c in children if c.get("cid") == b"LIST" and c.get("form") == b"tdsp"]
    sspc_order = [c for c in children if c.get("cid") == b"LIST" and c.get("form") == b"sspc"]

    real_tdsp = [c for c in tdsp_order if _tdmn_effect_name(c) is not None]
    # sspc blocks are in the same order as the *real* (non-sentinel) tdsp entries
    assert len(real_tdsp) == len(sspc_order), (
        f"Effect index count ({len(real_tdsp)}) doesn't match parameter "
        f"block count ({len(sspc_order)}) — file structure is not what "
        f"this pipeline expects; aborting rather than guessing."
    )

    sspc_to_remove = set()
    removed_names = []
    for tdsp_node, sspc_node in zip(real_tdsp, sspc_order):
        name = _tdmn_effect_name(tdsp_node)
        if name in match_names_to_remove:
            sspc_to_remove.add(id(sspc_node))
            removed_names.append(name)

    new_children = []
    skip_next_tdsn = False
    for c in children:
        if skip_next_tdsn and c["cid"] == b"tdsn":
            skip_next_tdsn = False
            continue
        if c.get("cid") == b"LIST" and c.get("form") == b"tdsp":
            if _tdmn_effect_name(c) in match_names_to_remove:
                skip_next_tdsn = True
                continue
        if c.get("cid") == b"LIST" and c.get("form") == b"sspc":
            if id(c) in sspc_to_remove:
                continue
        new_children.append(c)

    besc["children"] = new_children
    return removed_names


def renumber_indices(riff_node: dict) -> int:
    """Renumber every non-sentinel tdsp entry's tdix[1] to be contiguous
    0..N-1, in order. Must be called after any effect removal — AE uses
    this index to associate an effect entry with its parameter block, and
    a gap causes wrong names/parameters to display (not always a crash).

    Returns the count of entries renumbered.
    """
    besc = _find_besc(riff_node)
    tdsps = [c for c in besc["children"] if c.get("cid") == b"LIST" and c.get("form") == b"tdsp"]
    idx = 0
    for n in tdsps:
        if _tdmn_effect_name(n) is None:
            continue  # sentinel entry — leave untouched
        tdixs = riff.find_all(n, b"tdix")
        tdixs[1]["content"] = struct.pack(">I", idx)
        idx += 1
    return idx


def convert_strings_to_target_format(riff_node: dict) -> None:
    """Convert tdsn/pdnm/fnam from CC's Utf8-prefixed encoding to the
    target's native plain-string format. Safe to call even if a file is
    already in the target format (no-op for chunks that aren't prefixed).
    """
    def strip_variable(node: dict, cid_target: bytes) -> None:
        if node["form"] is not None:
            for c in node["children"]:
                strip_variable(c, cid_target)
        elif node["cid"] == cid_target:
            raw = node["content"]
            if raw[:4] == b"Utf8":
                decoded = _decode_utf8_prefixed(raw)
                node["content"] = decoded + b"\x00"

    # tdsn and pdnm: strip prefix, keep as variable-length null-terminated
    strip_variable(riff_node, b"tdsn")
    strip_variable(riff_node, b"pdnm")

    # fnam: strip prefix AND pad/truncate to the fixed 48-byte field CS5.5
    # expects. This is NOT the same treatment as tdsn/pdnm — fnam sits at
    # a fixed offset inside sspc and leaving it variable-length shifts
    # every field after it, which crashes AE. See RESEARCH_NOTES.md.
    for sspc_node in riff.find_all_lists(riff_node, b"sspc"):
        fnams = [c for c in sspc_node["children"] if c["cid"] == b"fnam"]
        if not fnams:
            continue
        raw = fnams[0]["content"]
        if raw[:4] == b"Utf8":
            name = _decode_utf8_prefixed(raw)
            padded = name[:FNAM_FIXED_SIZE - 1] + b"\x00" * (
                FNAM_FIXED_SIZE - len(name[:FNAM_FIXED_SIZE - 1])
            )
            fnams[0]["content"] = padded


def patch_version(riff_node: dict, target: str) -> None:
    """Patch the head chunk's version field to a known target version."""
    if target not in KNOWN_VERSIONS:
        raise ValueError(
            f"No confirmed version-byte value for target '{target}'. "
            f"Known targets: {sorted(KNOWN_VERSIONS)}. "
            f"See RESEARCH_NOTES.md for how to derive a new one — do not guess."
        )
    head_node = riff_node["children"][0]
    if head_node["cid"] != b"head":
        raise ValueError("Expected the file's 1st top-level chunk to be `head`.")
    vals = list(struct.unpack(">4I", head_node["content"]))
    vals[1] = KNOWN_VERSIONS[target]
    head_node["content"] = struct.pack(">4I", *vals)


def verify(original_data: bytes, converted_data: bytes) -> list[str]:
    """The mandatory post-conversion safety pass. Returns a list of
    problems found (empty list = all checks passed). Never skip this."""
    problems = []

    new_tree = riff.parse_file(converted_data)

    # 1. No remaining Utf8-tagged chunks anywhere
    def count_utf8(node: dict) -> int:
        c = 0
        if node["form"] is not None:
            for ch in node["children"]:
                c += count_utf8(ch)
        elif node["content"][:4] == b"Utf8":
            c += 1
        return c

    utf8_remaining = count_utf8(new_tree)
    if utf8_remaining:
        problems.append(f"{utf8_remaining} chunk(s) still carry the Utf8 prefix.")

    # 2. tdix values are contiguous starting at 0
    besc = _find_besc(new_tree)
    tdsps = [c for c in besc["children"] if c.get("cid") == b"LIST" and c.get("form") == b"tdsp"]
    seen_indices = []
    for n in tdsps:
        if _tdmn_effect_name(n) is None:
            continue
        tdixs = riff.find_all(n, b"tdix")
        seen_indices.append(struct.unpack(">I", tdixs[1]["content"])[0])
    if seen_indices != list(range(len(seen_indices))):
        problems.append(f"tdix values are not contiguous: {seen_indices}")

    # 3. Keyframe data (lhd3/ldat) unchanged, chunk-for-chunk, wherever it
    #    still exists (removed effects legitimately remove their own
    #    keyframes — we only check that surviving chunks are untouched).
    orig_tree = riff.parse_file(original_data)
    orig_lhd3 = {bytes(c["content"]) for c in riff.find_all(orig_tree, b"lhd3")}
    new_lhd3 = {bytes(c["content"]) for c in riff.find_all(new_tree, b"lhd3")}
    if not new_lhd3.issubset(orig_lhd3):
        problems.append("Keyframe header (lhd3) data changed unexpectedly.")

    orig_ldat = {bytes(c["content"]) for c in riff.find_all(orig_tree, b"ldat")}
    new_ldat = {bytes(c["content"]) for c in riff.find_all(new_tree, b"ldat")}
    if not new_ldat.issubset(orig_ldat):
        problems.append("Keyframe data (ldat) changed unexpectedly.")

    return problems


def convert(
    data: bytes,
    target: str = "cs5.5",
    remove_match_names: set[str] | None = None,
) -> ConversionResult:
    """Run the full pipeline: optional effect removal, index renumbering,
    string-format conversion, version patch, re-serialize, verify.
    """
    tree = riff.parse_file(data)
    warnings: list[str] = []
    removed: list[str] = []

    if remove_match_names:
        removed = remove_effects_by_match_name(tree, remove_match_names)
        missing = remove_match_names - set(removed)
        if missing:
            warnings.append(
                f"Requested removal of effects not found in file: {sorted(missing)}"
            )

    renumber_indices(tree)
    convert_strings_to_target_format(tree)
    patch_version(tree, target)

    out_bytes = riff.serialize(tree)

    problems = verify(data, out_bytes)
    if problems:
        raise RuntimeError(
            "Conversion failed its verification pass:\n  - " + "\n  - ".join(problems)
        )

    return ConversionResult(data=out_bytes, removed_effects=removed, warnings=warnings)
