"""
Generic RIFX (big-endian RIFF) container parser/serializer.

No After Effects-specific logic lives here. This module only understands
the container format: chunk id (4 bytes) + big-endian uint32 size + content
(+ 1 pad byte if size is odd). LIST/RIFX chunks additionally have a 4-byte
form tag before their nested children.

Correctness bar: parse(data) then serialize(tree) must reproduce `data`
exactly, byte for byte, when no edits are made. See tests/test_riff.py.
"""
from __future__ import annotations

import struct
from typing import Optional

CONTAINER_IDS = (b"RIFX", b"LIST")


def parse(data: bytes, offset: int = 0, end: Optional[int] = None) -> list[dict]:
    """Parse a byte range into a list of sibling chunk nodes.

    Each node is a dict:
      container node: {'cid': bytes, 'form': bytes, 'children': [node, ...]}
      leaf node:       {'cid': bytes, 'form': None, 'content': bytes}
    """
    if end is None:
        end = len(data)
    results = []
    pos = offset
    while pos < end - 8:
        cid = data[pos:pos + 4]
        size = struct.unpack(">I", data[pos + 4:pos + 8])[0]
        content_start = pos + 8
        raw_end = content_start + size
        if raw_end > end:
            raise ValueError(
                f"Chunk {cid!r} at offset {pos} claims size {size}, "
                f"which overruns the parent's bounds (end={end})."
            )
        if cid in CONTAINER_IDS:
            form = data[content_start:content_start + 4]
            node = {
                "cid": cid,
                "form": form,
                "children": parse(data, content_start + 4, raw_end),
            }
        else:
            node = {
                "cid": cid,
                "form": None,
                "content": bytes(data[content_start:raw_end]),
            }
        results.append(node)
        pos = raw_end
        if size % 2 == 1:
            pos += 1  # RIFF chunks pad to an even boundary
    return results


def parse_file(data: bytes) -> dict:
    """Parse a whole .ffx file into its top-level RIFX node.

    Real-world .ffx files sometimes have extra bytes after the RIFX chunk
    ends (observed: a trailing `<?xp...` XMP-style packet). Those trailing
    bytes are preserved verbatim on the returned node under the special
    key "_trailer" so that parse_file() + serialize() remains lossless —
    never drop them.
    """
    if data[0:4] != b"RIFX":
        raise ValueError("Not a valid RIFX file (must start with 'RIFX').")
    size = struct.unpack(">I", data[4:8])[0]
    riff_end = 8 + size
    if riff_end % 2 == 1:
        riff_end += 1  # trailing pad byte belongs to the RIFX chunk itself
    if riff_end > len(data):
        raise ValueError("RIFX chunk size overruns the file length — truncated or corrupt file.")

    top = parse(data, 0, riff_end)
    if len(top) != 1 or top[0]["cid"] != b"RIFX":
        raise ValueError("Not a valid RIFX file (expected a single top-level RIFX chunk).")

    node = top[0]
    node["_trailer"] = bytes(data[riff_end:])
    return node


def serialize(node: dict) -> bytes:
    """Serialize a node (and its children) back into raw bytes.

    If `node` is a top-level RIFX node produced by parse_file() and carries
    a "_trailer" key (see parse_file's docstring), those trailing bytes are
    appended after the RIFX chunk itself — required for lossless round-trip
    on real .ffx files, which often have a trailing XMP-style packet.
    """
    if node["form"] is not None:
        body = node["form"] + b"".join(serialize(c) for c in node["children"])
    else:
        body = node["content"]
    out = node["cid"] + struct.pack(">I", len(body)) + body
    if len(out) % 2 == 1:
        out += b"\x00"
    trailer = node.get("_trailer")
    if trailer:
        out += trailer
    return out


def find_all(node: dict, cid: bytes) -> list[dict]:
    """Recursively find every descendant leaf/container chunk with a given cid."""
    out = []
    if node["form"] is not None:
        for c in node["children"]:
            if c["cid"] == cid:
                out.append(c)
            out += find_all(c, cid)
    return out


def find_all_lists(node: dict, form: bytes) -> list[dict]:
    """Recursively find every descendant LIST chunk with a given form tag."""
    out = []
    if node["form"] is not None:
        for c in node["children"]:
            if c["cid"] == b"LIST" and c.get("form") == form:
                out.append(c)
            out += find_all_lists(c, form)
    return out


def leaf_content(node: dict) -> bytes:
    """Get the raw content of a leaf chunk (raises if node is a container)."""
    if node["form"] is not None:
        raise ValueError(f"{node['cid']!r} is a container chunk, not a leaf.")
    return node["content"]
