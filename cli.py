#!/usr/bin/env python3
"""
Bare command-line tool for the FFX Compatibility pipeline — no GUI
dependency. This is the Phase 2 exit-criteria artifact: proves the
pipeline works standalone before any GUI work starts (see PHASES.md).

Usage:
    python cli.py list <file.ffx>
    python cli.py convert <in.ffx> <out.ffx> [--target cs5.5] [--remove "MB LookSuite3"]...
"""
from __future__ import annotations

import argparse
import sys

from ffx_core import pipeline, plugins


def cmd_list(args: argparse.Namespace) -> int:
    with open(args.file, "rb") as f:
        data = f.read()

    effects = pipeline.list_effects(data)
    table = plugins.load_table()

    print(f"{'#':<4} {'Match name':<28} {'Vendor':<20} {'Suite':<25} {'Status'}")
    print("-" * 100)
    i = 0
    for eff in effects:
        if eff["is_sentinel"]:
            continue
        i += 1
        match = plugins.resolve(eff["match_name"], table)
        if match.vendor is None:
            status = "UNKNOWN — not in lookup table"
        elif match.confirmed:
            status = "confirmed"
        else:
            status = "unverified"
        print(
            f"{i:<4} {eff['match_name']:<28} "
            f"{(match.vendor or '?'):<20} {(match.suite or '?'):<25} {status}"
        )
    return 0


def cmd_convert(args: argparse.Namespace) -> int:
    with open(args.input, "rb") as f:
        data = f.read()

    remove = set(args.remove) if args.remove else None

    try:
        result = pipeline.convert(data, target=args.target, remove_match_names=remove)
    except RuntimeError as e:
        print(f"Conversion FAILED verification: {e}", file=sys.stderr)
        return 1
    except ValueError as e:
        print(f"Conversion error: {e}", file=sys.stderr)
        return 1

    with open(args.output, "wb") as f:
        f.write(result.data)

    print(f"Wrote {args.output} ({len(result.data)} bytes)")
    if result.removed_effects:
        print(f"Removed effects: {', '.join(result.removed_effects)}")
    for w in result.warnings:
        print(f"Warning: {w}")
    print("Verification pass: OK (0 Utf8 tags remaining, indices contiguous, keyframe data unchanged)")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)

    p_list = sub.add_parser("list", help="List effects/plugins in a .ffx file")
    p_list.add_argument("file")
    p_list.set_defaults(func=cmd_list)

    p_convert = sub.add_parser("convert", help="Convert a .ffx file to a target AE version")
    p_convert.add_argument("input")
    p_convert.add_argument("output")
    p_convert.add_argument("--target", default="cs5.5", help="Target AE version (default: cs5.5)")
    p_convert.add_argument(
        "--remove", action="append", metavar="MATCH_NAME",
        help="Remove an effect by match-name (e.g. 'MB LookSuite3'). Repeatable.",
    )
    p_convert.set_defaults(func=cmd_convert)

    args = parser.parse_args()
    return args.func(args)


if __name__ == "__main__":
    sys.exit(main())
