"""
Plugin match-name lookup: resolve an effect's match-name (e.g. "S_Sharpen")
to a vendor/suite, using a small seed table (data/plugin_table.json) that's
meant to grow from real files rather than be scraped wholesale up front.

See PROJECT_PLAN.md Section 3 for how the seed table was derived, and
Section 4.2 for how the app is meant to build a per-user "which plugins do
I actually have" profile on top of this.
"""
from __future__ import annotations

import json
import os
from dataclasses import dataclass

_DATA_PATH = os.path.join(os.path.dirname(__file__), "..", "data", "plugin_table.json")


@dataclass
class PluginMatch:
    match_name: str
    vendor: str | None
    suite: str | None
    prefix_matched: str | None
    confirmed: bool  # True if this exact prefix was verified against a real file


def load_table(path: str | None = None) -> list[dict]:
    with open(path or _DATA_PATH, "r", encoding="utf-8") as f:
        return json.load(f)


def resolve(match_name: str, table: list[dict] | None = None) -> PluginMatch:
    """Look up a match-name against the prefix table. Longest-prefix match
    wins so e.g. `BCC3Directional Blur` doesn't accidentally match a
    shorter, less specific `BCC` entry ahead of a more specific one."""
    table = table if table is not None else load_table()

    if match_name == "ADBE Effect Parade" or match_name == "ADBE End of path sentinel":
        # structural markers, not real effects — never surface these to a user
        return PluginMatch(match_name, "Adobe", "structural marker", None, True)

    best: dict | None = None
    for entry in table:
        prefix = entry["prefix"]
        if match_name.startswith(prefix):
            if best is None or len(prefix) > len(best["prefix"]):
                best = entry

    if best is None:
        return PluginMatch(match_name, None, None, None, False)

    return PluginMatch(
        match_name=match_name,
        vendor=best["vendor"],
        suite=best["suite"],
        prefix_matched=best["prefix"],
        confirmed=best.get("confirmed", False),
    )


def resolve_many(match_names: list[str], table: list[dict] | None = None) -> list[PluginMatch]:
    table = table if table is not None else load_table()
    return [resolve(name, table) for name in match_names]
