from __future__ import annotations

#!/usr/bin/env python3
"""
Top-level entry point used for PyInstaller packaging.

`python -m ffx_gui` (see README) is the normal way to run the app from
source — it correctly resolves ffx_gui's internal relative imports because
it's invoked as a package. PyInstaller freezes a single script, though, and
pointing it directly at ffx_gui/__main__.py can break those same relative
imports depending on how it's frozen. This tiny wrapper avoids that by
being a normal top-level script that imports the package properly, so
`pyinstaller run_gui.py` and `python -m ffx_gui` behave identically.
"""
from ffx_gui.__main__ import main
import sys

if __name__ == "__main__":
    sys.exit(main())
