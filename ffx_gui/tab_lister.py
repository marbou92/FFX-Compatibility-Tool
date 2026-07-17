"""
Effect Lister tab. Open a .ffx, see every effect it uses, its resolved
vendor/suite from the plugin_table.json lookup, and — if the user's
Plugin Profile says a vendor isn't owned — a clear warning before they
ever try opening the file in AE. This is the feature meant to catch
exactly the kind of crash that motivated this whole project (a preset
using a plugin that turned out not to be installed).
"""
from PySide6.QtWidgets import (
    QWidget, QVBoxLayout, QHBoxLayout, QPushButton, QLabel,
    QTableWidget, QTableWidgetItem, QFileDialog, QHeaderView,
)
from PySide6.QtGui import QColor

from ffx_core import pipeline, plugins as plugins_module
from ffx_gui.profile_store import PluginProfile

WARNING_COLOR = QColor("#7a2020")
UNKNOWN_COLOR = QColor("#5a5a2a")


class ListerTab(QWidget):
    def __init__(self, profile: PluginProfile):
        super().__init__()
        self.profile = profile
        self.current_path: str | None = None
        self.current_effects: list[dict] = []

        layout = QVBoxLayout(self)

        top_row = QHBoxLayout()
        self.open_btn = QPushButton("Open .ffx file…")
        self.open_btn.clicked.connect(self._open_file)
        self.file_label = QLabel("No file loaded")
        top_row.addWidget(self.open_btn)
        top_row.addWidget(self.file_label, stretch=1)
        layout.addLayout(top_row)

        self.table = QTableWidget(0, 4)
        self.table.setHorizontalHeaderLabels(["#", "Match name", "Vendor / Suite", "Status"])
        self.table.horizontalHeader().setSectionResizeMode(1, QHeaderView.Stretch)
        self.table.horizontalHeader().setSectionResizeMode(2, QHeaderView.Stretch)
        self.table.setEditTriggers(QTableWidget.NoEditTriggers)
        layout.addWidget(self.table)

        self.summary_label = QLabel("")
        layout.addWidget(self.summary_label)

    def _open_file(self):
        path, _ = QFileDialog.getOpenFileName(self, "Open .ffx preset", "", "After Effects Presets (*.ffx)")
        if not path:
            return
        self.load_file(path)

    def load_file(self, path: str):
        self.current_path = path
        self.file_label.setText(path)
        with open(path, "rb") as f:
            data = f.read()
        self.current_effects = pipeline.list_effects(data)
        self.refresh()

    def refresh(self):
        """Re-render the table. Called on file load, and again whenever
        the Plugin Profile tab changes — a vendor flip should immediately
        update which rows show a missing-plugin warning."""
        table_data = plugins_module.load_table()
        real_effects = [e for e in self.current_effects if not e["is_sentinel"]]

        self.table.setRowCount(len(real_effects))
        missing_count = 0
        unknown_count = 0

        for row, eff in enumerate(real_effects):
            match = plugins_module.resolve(eff["match_name"], table_data)
            owned = self.profile.owns(match.vendor)

            if match.vendor is None:
                status = "Unknown plugin — not in lookup table"
                color = UNKNOWN_COLOR
                unknown_count += 1
            elif owned is False:
                status = "NOT in your profile — likely to fail"
                color = WARNING_COLOR
                missing_count += 1
            elif owned is True:
                status = "You have this" + ("" if match.confirmed else " (unverified prefix)")
                color = None
            else:
                status = "Native / always available"
                color = None

            vendor_suite = f"{match.vendor or '?'} — {match.suite or '?'}"

            self.table.setItem(row, 0, QTableWidgetItem(str(row + 1)))
            self.table.setItem(row, 1, QTableWidgetItem(eff["match_name"]))
            self.table.setItem(row, 2, QTableWidgetItem(vendor_suite))
            status_item = QTableWidgetItem(status)
            if color:
                status_item.setBackground(color)
            self.table.setItem(row, 3, status_item)

        parts = [f"{len(real_effects)} effect(s)"]
        if missing_count:
            parts.append(f"{missing_count} flagged as missing from your profile")
        if unknown_count:
            parts.append(f"{unknown_count} unrecognized")
        self.summary_label.setText(" · ".join(parts))
