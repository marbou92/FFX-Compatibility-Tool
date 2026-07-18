"""
Plugin Profile tab. A simple per-vendor checklist (see profile_store.py
for why it's grouped by vendor, not per-plugin). Auto-detect from a local
AE plugins folder is scaffolded but left as a manual "browse" step for now
— see the TODO below — since plugin folder layout varies enough across
OS/AE versions that guessing a default path wrongly is worse than asking.
"""

from __future__ import annotations
import os


from PySide6.QtWidgets import (
    QWidget, QVBoxLayout, QHBoxLayout, QCheckBox, QPushButton, QLabel, QFileDialog
)

from ffx_gui.profile_store import PluginProfile


class ProfileTab(QWidget):
    def __init__(self, profile: PluginProfile, on_change):
        super().__init__()
        self.profile = profile
        self.on_change = on_change
        self.checkboxes: dict[str, QCheckBox] = {}

        layout = QVBoxLayout(self)
        layout.addWidget(QLabel(
            "Check off every plugin vendor you have installed in your target "
            "After Effects version. Checking a vendor covers all of its "
            "effects (e.g. checking \"Boris FX\" covers both Sapphire and "
            "Continuum effects)."
        ))

        for vendor in profile.all_known_vendors():
            cb = QCheckBox(vendor)
            cb.setChecked(vendor in profile.owned_vendors)
            cb.stateChanged.connect(self._make_handler(vendor))
            self.checkboxes[vendor] = cb
            layout.addWidget(cb)

        layout.addStretch()

        scan_row = QHBoxLayout()
        scan_btn = QPushButton("Scan a plugins folder…")
        scan_btn.clicked.connect(self._scan_folder)
        scan_row.addWidget(scan_btn)
        scan_row.addWidget(QLabel(
            "Point this at your AE plugins directory to auto-check vendors "
            "whose files are found there."
        ))
        layout.addLayout(scan_row)

    def _make_handler(self, vendor: str):
        def handler(state):
            self.profile.set_owned(vendor, bool(state))
            self.profile.save()
            self.on_change()
        return handler

    def _scan_folder(self):
        folder = QFileDialog.getExistingDirectory(self, "Select your AE plugins folder")
        if not folder:
            return

        # Simple filename-substring heuristic per vendor. This is
        # intentionally conservative — a false "not found" just means the
        # user still has to check the box manually, which is safe; a false
        # positive would silently suppress a real missing-plugin warning,
        # which is not. Extend VENDOR_FILE_HINTS as real plugin filenames
        # are confirmed, the same way plugin_table.json grows.
        VENDOR_FILE_HINTS = {
            "Boris FX": ["sapphire", "continuum", "bcc"],
            "Red Giant / Maxon": ["magic bullet", "trapcode", "red giant"],
            "Video Copilot": ["element", "optical flares", "saber", "twitch"],
            "Plugin Everything": ["deep glow", "shadow studio"],
            "RE:Vision Effects": ["twixtor", "reelsmart"],
        }

        try:
            filenames = [f.lower() for f in os.listdir(folder)]
        except OSError:
            return

        for vendor, hints in VENDOR_FILE_HINTS.items():
            if vendor not in self.checkboxes:
                continue
            found = any(hint in fname for fname in filenames for hint in hints)
            if found:
                self.checkboxes[vendor].setChecked(True)
