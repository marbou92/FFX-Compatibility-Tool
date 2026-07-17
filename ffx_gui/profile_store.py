"""
The user's "which plugin vendors do I actually have installed" profile.
Persisted as a small JSON file in the platform's user config directory so
it survives between app launches. See PROJECT_PLAN.md Section 4.2.

Deliberately grouped by VENDOR, not by individual plugin — checking
"Boris FX" once should cover every Sapphire (`S_`) and Continuum (`BCC`)
effect, matching how the plugin_table.json prefixes are organized.
"""
from __future__ import annotations

import json
import os
from dataclasses import dataclass, field

from ffx_core import plugins as plugins_module


def _config_path() -> str:
    if sys_platform_is_windows():
        base = os.environ.get("APPDATA", os.path.expanduser("~"))
    elif sys_platform_is_mac():
        base = os.path.expanduser("~/Library/Application Support")
    else:
        base = os.environ.get("XDG_CONFIG_HOME", os.path.expanduser("~/.config"))
    d = os.path.join(base, "FFXCompatibilityTool")
    os.makedirs(d, exist_ok=True)
    return os.path.join(d, "plugin_profile.json")


def sys_platform_is_windows() -> bool:
    import sys
    return sys.platform.startswith("win")


def sys_platform_is_mac() -> bool:
    import sys
    return sys.platform == "darwin"


@dataclass
class PluginProfile:
    owned_vendors: set[str] = field(default_factory=set)
    _path: str = field(default_factory=_config_path)

    @classmethod
    def load(cls) -> "PluginProfile":
        path = _config_path()
        if os.path.exists(path):
            try:
                with open(path, "r", encoding="utf-8") as f:
                    data = json.load(f)
                return cls(owned_vendors=set(data.get("owned_vendors", [])), _path=path)
            except (json.JSONDecodeError, OSError):
                pass  # fall through to a fresh empty profile rather than crash
        return cls(_path=path)

    def save(self) -> None:
        with open(self._path, "w", encoding="utf-8") as f:
            json.dump({"owned_vendors": sorted(self.owned_vendors)}, f, indent=2)

    def all_known_vendors(self) -> list[str]:
        table = plugins_module.load_table()
        vendors = {entry["vendor"] for entry in table if entry["vendor"] != "Adobe"}
        return sorted(vendors)

    def owns(self, vendor: str | None) -> bool | None:
        """True/False if we have an opinion, None if vendor is unknown
        (e.g. Adobe native, or an unrecognized match-name) — None means
        'no missing-plugin warning applies', not 'confirmed missing'."""
        if vendor is None or vendor == "Adobe":
            return None
        return vendor in self.owned_vendors

    def set_owned(self, vendor: str, owned: bool) -> None:
        if owned:
            self.owned_vendors.add(vendor)
        else:
            self.owned_vendors.discard(vendor)
