"""
Phase 2 exit criteria tests. Each pipeline step is tested independently
where possible, plus an end-to-end test against real fixtures if any are
present in tests/fixtures/.
"""
from __future__ import annotations

import glob
import os
import struct

import pytest

from ffx_core import pipeline, riff

FIXTURES_DIR = os.path.join(os.path.dirname(__file__), "fixtures")


def _fixture_files():
    return sorted(glob.glob(os.path.join(FIXTURES_DIR, "*.ffx")))


def _make_utf8_prefixed(s: str) -> bytes:
    b = s.encode("latin1")
    return b"Utf8" + struct.pack(">I", len(b)) + b


def _make_leaf(cid: bytes, content: bytes) -> bytes:
    out = cid + struct.pack(">I", len(content)) + content
    if len(content) % 2 == 1:
        out += b"\x00"
    return out


def _make_list(form: bytes, children: bytes) -> bytes:
    body = form + children
    return b"LIST" + struct.pack(">I", len(body)) + body


def _make_tdsp(match_name: str, index: int) -> bytes:
    tdmn0 = _make_leaf(b"tdmn", b"ADBE Effect Parade" + b"\x00" * (40 - len("ADBE Effect Parade")))
    tdmn1 = _make_leaf(b"tdmn", match_name.encode("latin1") + b"\x00" * (40 - len(match_name)))
    tdix0 = _make_leaf(b"tdix", struct.pack(">I", 0xFFFFFFFF))
    tdix1 = _make_leaf(b"tdix", struct.pack(">I", index))
    tdsi = _make_list(b"tdsi", tdmn0 + tdix0) + _make_list(b"tdsi", tdmn1 + tdix1)
    # pad tdsp body to the real-world fixed 172 bytes isn't required for
    # these synthetic unit tests — only round-trip/format correctness is
    # being tested here, not the exact fixed-size layout.
    return _make_list(b"tdsp", tdsi)


def _minimal_synthetic_file(match_names: list[str]) -> bytes:
    """Build a minimal-but-structurally-valid synthetic .ffx-like file with
    N effects, each with a Utf8-prefixed tdsn/fnam, for pipeline unit
    testing without depending on any real fixture file."""
    head = _make_leaf(b"head", struct.pack(">4I", 3, 93, 0, 0x01000000))
    beso = _make_leaf(b"beso", b"\x00" * 56)

    besc_children = beso
    for i, name in enumerate(match_names):
        besc_children += _make_tdsp(name, i)
        besc_children += _make_leaf(b"tdsn", _make_utf8_prefixed(f"{name} display"))
    # sentinel
    tdmn_sentinel = _make_leaf(b"tdmn", b"ADBE End of path sentinel" + b"\x00" * (40 - len("ADBE End of path sentinel")))
    tdix_sentinel = _make_leaf(b"tdix", struct.pack(">I", 0xFFFFFFFF))
    besc_children += _make_list(b"tdsp", _make_list(b"tdsi", tdmn_sentinel + tdix_sentinel))

    for name in match_names:
        fnam = _make_leaf(b"fnam", _make_utf8_prefixed(name))
        besc_children += _make_list(b"sspc", fnam)

    besc = _make_list(b"besc", besc_children)
    body = b"FaFX" + head + besc
    return b"RIFX" + struct.pack(">I", len(body)) + body


def test_patch_version_unknown_target_raises():
    data = _minimal_synthetic_file(["S_Sharpen"])
    tree = riff.parse_file(data)
    with pytest.raises(ValueError):
        pipeline.patch_version(tree, "totally-made-up-version")


def test_patch_version_cs55():
    data = _minimal_synthetic_file(["S_Sharpen"])
    tree = riff.parse_file(data)
    pipeline.patch_version(tree, "cs5.5")
    head = tree["children"][0]
    vals = struct.unpack(">4I", head["content"])
    assert vals[1] == 78


def test_string_conversion_removes_utf8_prefix():
    data = _minimal_synthetic_file(["S_Sharpen"])
    tree = riff.parse_file(data)
    pipeline.convert_strings_to_target_format(tree)

    tdsns = riff.find_all(tree, b"tdsn")
    assert len(tdsns) == 1
    assert tdsns[0]["content"] == b"S_Sharpen display\x00"

    fnams = riff.find_all(tree, b"fnam")
    assert len(fnams) == 1
    assert len(fnams[0]["content"]) == pipeline.FNAM_FIXED_SIZE
    assert fnams[0]["content"].startswith(b"S_Sharpen\x00")


def test_remove_effects_and_renumber():
    data = _minimal_synthetic_file(["MB LookSuite3", "S_Sharpen", "ADBE Exposure2"])
    tree = riff.parse_file(data)

    removed = pipeline.remove_effects_by_match_name(tree, {"MB LookSuite3"})
    assert removed == ["MB LookSuite3"]

    remaining = [
        pipeline._tdmn_effect_name(c)
        for c in pipeline._find_besc(tree)["children"]
        if c.get("cid") == b"LIST" and c.get("form") == b"tdsp"
    ]
    assert "MB LookSuite3" not in remaining
    assert "S_Sharpen" in remaining
    assert "ADBE Exposure2" in remaining

    count = pipeline.renumber_indices(tree)
    assert count == 2  # S_Sharpen + ADBE Exposure2

    tdsps = [
        c for c in pipeline._find_besc(tree)["children"]
        if c.get("cid") == b"LIST" and c.get("form") == b"tdsp"
    ]
    indices = []
    for n in tdsps:
        if pipeline._tdmn_effect_name(n) is None:
            continue
        tdixs = riff.find_all(n, b"tdix")
        indices.append(struct.unpack(">I", tdixs[1]["content"])[0])
    assert indices == [0, 1]


def test_verify_flags_lingering_utf8():
    data = _minimal_synthetic_file(["S_Sharpen"])
    # a "converted" file that forgot to actually convert anything should
    # fail verification
    problems = pipeline.verify(data, data)
    assert any("Utf8" in p for p in problems)


def test_full_convert_end_to_end_synthetic():
    data = _minimal_synthetic_file(["MB LookSuite3", "S_Sharpen"])
    result = pipeline.convert(data, target="cs5.5", remove_match_names={"MB LookSuite3"})
    assert result.removed_effects == ["MB LookSuite3"]

    # re-parses cleanly and passes its own verification pass again
    problems = pipeline.verify(data, result.data)
    assert problems == []


@pytest.mark.parametrize("path", _fixture_files())
def test_full_convert_end_to_end_real_fixture(path):
    """Only runs if real fixture .ffx files are present in tests/fixtures/."""
    with open(path, "rb") as f:
        data = f.read()
    result = pipeline.convert(data, target="cs5.5")
    problems = pipeline.verify(data, result.data)
    assert problems == []
