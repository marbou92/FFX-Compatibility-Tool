"""
FFX Compatibility Tool — desktop GUI entry point.

Three tabs, matching PROJECT_PLAN.md Section 4:
  1. Effect Lister  — open a .ffx, see every effect + resolved vendor/suite
  2. Plugin Profile — check off which plugin vendors you have installed
  3. Convert        — pick a target version, optionally strip effects
                       flagged as missing from your profile, convert

All actual file logic lives in ffx_core (Phase 1-2) — this file and its
sibling modules are UI only. Run with: python -m ffx_gui
"""

from __future__ import annotations
import sys

from PySide2.QtWidgets import QApplication, QMainWindow, QTabWidget

from ffx_gui.tab_lister import ListerTab
from ffx_gui.tab_profile import ProfileTab
from ffx_gui.tab_convert import ConvertTab
from ffx_gui.profile_store import PluginProfile


class MainWindow(QMainWindow):
    def __init__(self):
        super().__init__()
        self.setWindowTitle("FFX Compatibility Tool")
        self.resize(900, 600)

        self.profile = PluginProfile.load()

        tabs = QTabWidget()
        self.lister_tab = ListerTab(self.profile)
        self.profile_tab = ProfileTab(self.profile, on_change=self._on_profile_changed)
        self.convert_tab = ConvertTab(self.profile)

        tabs.addTab(self.lister_tab, "Effect Lister")
        tabs.addTab(self.profile_tab, "Plugin Profile")
        tabs.addTab(self.convert_tab, "Convert")
        self.setCentralWidget(tabs)

    def _on_profile_changed(self):
        # Both other tabs read from the same PluginProfile instance, so a
        # profile edit should immediately affect how they flag effects.
        self.lister_tab.refresh()
        self.convert_tab.refresh()


def main() -> int:
    app = QApplication(sys.argv)
    window = MainWindow()
    window.show()
    return app.exec_()


if __name__ == "__main__":
    sys.exit(main())
