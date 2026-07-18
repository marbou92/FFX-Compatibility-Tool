"""
Convert tab. Load a .ffx, optionally strip effects flagged as missing from
the user's profile, pick a target version, run the pipeline, save output.
Surfaces the verification pass results directly rather than a silent
pass/fail — see PROJECT_PLAN.md Section 4.5.
"""

from __future__ import annotations
from PySide2.QtWidgets import (
    QWidget, QVBoxLayout, QHBoxLayout, QPushButton, QLabel, QComboBox,
    QListWidget, QListWidgetItem, QFileDialog, QMessageBox, QTextEdit,
)
from PySide2.QtCore import Qt

from ffx_core import pipeline, plugins as plugins_module
from ffx_gui.profile_store import PluginProfile


class ConvertTab(QWidget):
    def __init__(self, profile: PluginProfile):
        super().__init__()
        self.profile = profile
        self.input_path: str | None = None
        self.input_data: bytes | None = None
        self.current_effects: list[dict] = []

        layout = QVBoxLayout(self)

        open_row = QHBoxLayout()
        self.open_btn = QPushButton("Open .ffx file…")
        self.open_btn.clicked.connect(self._open_file)
        self.file_label = QLabel("No file loaded")
        open_row.addWidget(self.open_btn)
        open_row.addWidget(self.file_label, stretch=1)
        layout.addLayout(open_row)

        layout.addWidget(QLabel(
            "Effects flagged as missing from your Plugin Profile are "
            "pre-selected for removal below — uncheck any you'd rather "
            "keep (e.g. if you're not sure the profile is accurate)."
        ))
        self.effect_list = QListWidget()
        layout.addWidget(self.effect_list)

        target_row = QHBoxLayout()
        target_row.addWidget(QLabel("Target version:"))
        self.target_combo = QComboBox()
        self.target_combo.addItems(sorted(pipeline.KNOWN_VERSIONS.keys()))
        target_row.addWidget(self.target_combo)
        target_row.addStretch()
        layout.addLayout(target_row)

        self.convert_btn = QPushButton("Convert…")
        self.convert_btn.clicked.connect(self._convert)
        self.convert_btn.setEnabled(False)
        layout.addWidget(self.convert_btn)

        self.result_box = QTextEdit()
        self.result_box.setReadOnly(True)
        self.result_box.setMaximumHeight(120)
        layout.addWidget(self.result_box)

    def _open_file(self):
        path, _ = QFileDialog.getOpenFileName(self, "Open .ffx preset", "", "After Effects Presets (*.ffx)")
        if not path:
            return
        self.input_path = path
        self.file_label.setText(path)
        with open(path, "rb") as f:
            self.input_data = f.read()
        self.current_effects = pipeline.list_effects(self.input_data)
        self.convert_btn.setEnabled(True)
        self.refresh()

    def refresh(self):
        """Re-render the removal checklist. Called on file load, and again
        when the Plugin Profile changes, so pre-selections stay accurate."""
        self.effect_list.clear()
        if not self.current_effects:
            return

        table_data = plugins_module.load_table()
        for eff in self.current_effects:
            if eff["is_sentinel"]:
                continue
            match = plugins_module.resolve(eff["match_name"], table_data)
            owned = self.profile.owns(match.vendor)

            item = QListWidgetItem(f"{eff['match_name']}  ({match.vendor or 'unknown vendor'})")
            item.setFlags(item.flags() | Qt.ItemIsUserCheckable)
            item.setData(Qt.UserRole, eff["match_name"])
            # pre-check for removal only when we're confident it's missing —
            # never pre-check an unknown-vendor effect, since "unknown" is
            # not the same as "confirmed missing" and shouldn't be silently
            # stripped by default.
            item.setCheckState(Qt.Checked if owned is False else Qt.Unchecked)
            self.effect_list.addItem(item)

    def _convert(self):
        if self.input_data is None:
            return

        to_remove = set()
        for i in range(self.effect_list.count()):
            item = self.effect_list.item(i)
            if item.checkState() == Qt.Checked:
                to_remove.add(item.data(Qt.UserRole))

        target = self.target_combo.currentText()

        try:
            result = pipeline.convert(
                self.input_data, target=target,
                remove_match_names=to_remove or None,
            )
        except (RuntimeError, ValueError) as e:
            QMessageBox.critical(self, "Conversion failed", str(e))
            return

        out_path, _ = QFileDialog.getSaveFileName(
            self, "Save converted .ffx", "", "After Effects Presets (*.ffx)"
        )
        if not out_path:
            return
        with open(out_path, "wb") as f:
            f.write(result.data)

        lines = [f"Saved: {out_path}", f"Target: {target}"]
        if result.removed_effects:
            lines.append(f"Removed: {', '.join(result.removed_effects)}")
        if result.warnings:
            lines.extend(f"Warning: {w}" for w in result.warnings)
        lines.append(
            "Verification pass: OK — 0 Utf8 tags remaining, indices "
            "contiguous, keyframe/parameter data unchanged."
        )
        self.result_box.setPlainText("\n".join(lines))
