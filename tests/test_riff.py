"""
Phase 1 exit criteria: parsing then re-serializing an unmodified .ffx file
must reproduce it byte-for-byte. This is the correctness bar the whole
pipeline depends on — every later edit assumes serialize() is lossless
for anything it doesn't touch.
"""
from __future__ import annotations

import glob
import os
import struct

import pytest

from ffx_core import riff

FIXTURES_DIR = os.path.join(os.path.dirname(__file__), "fixtures")


def _fixture_files():
    return sorted(glob.glob(os.path.join(FIXTURES_DIR, "*.ffx")))


@pytest.mark.parametrize("path", _fixture_files())
def test_round_trip_is_byte_identical(path):
    with open(path, "rb") as f:
        original = f.read()
    tree = riff.parse_file(original)
    rebuilt = riff.serialize(tree)
    assert rebuilt == original, f"Round-trip mismatch for {path}"


def test_round_trip_requires_at_least_one_fixture():
    files = _fixture_files()
    assert files, (
        "No .ffx sample files found in tests/fixtures/. "
        "Add at least one real preset (sanitized/renamed if needed) "
        "so this suite actually proves something."
    )


def test_synthetic_minimal_rifx():
    """A hand-built minimal RIFX file, independent of any real sample,
    so the parser/serializer is exercised even with zero fixtures present."""
    # RIFX header + form
    inner_chunk = b"head" + struct.pack(">I", 4) + b"\x00\x00\x00\x01"
    body = b"FaFX" + inner_chunk
    data = b"RIFX" + struct.pack(">I", len(body)) + body

    tree = riff.parse_file(data)
    assert tree["cid"] == b"RIFX"
    assert tree["form"] == b"FaFX"
    assert len(tree["children"]) == 1
    assert tree["children"][0]["cid"] == b"head"
    assert tree["children"][0]["content"] == b"\x00\x00\x00\x01"

    rebuilt = riff.serialize(tree)
    assert rebuilt == data


def test_synthetic_odd_size_padding():
    """Odd-sized chunk content must get a single pad byte, and that pad
    byte must not be treated as part of the next chunk's content."""
    leaf = b"abcd" + struct.pack(">I", 3) + b"xyz" + b"\x00"  # padded
    body = b"FaFX" + leaf
    data = b"RIFX" + struct.pack(">I", len(body)) + body

    tree = riff.parse_file(data)
    assert tree["children"][0]["content"] == b"xyz"
    rebuilt = riff.serialize(tree)
    assert rebuilt == data


def test_find_all_recursive():
    inner = b"tdmn" + struct.pack(">I", 4) + b"name"
    nested_list = b"LIST" + struct.pack(">I", 4 + len(inner)) + b"tdsp" + inner
    body = b"FaFX" + nested_list
    data = b"RIFX" + struct.pack(">I", len(body)) + body

    tree = riff.parse_file(data)
    found = riff.find_all(tree, b"tdmn")
    assert len(found) == 1
    assert found[0]["content"] == b"name"
