from __future__ import annotations

import os
import sys

# Same fix as pyproject.toml's [tool.pytest.ini_options] pythonpath = ["."],
# kept here too as a fallback for any pytest invocation that doesn't pick up
# pyproject.toml's ini options (e.g. an older pytest, or a runner that
# passes -c to point at a different config file).
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
