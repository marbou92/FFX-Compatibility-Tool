# Nightly build

This branch carries the rolling nightly build of FFX Compatibility
Tool — a single `FFXCompatibilityTool-nightly.exe` replaced on
every push to `main`. It is the project's test channel: built
straight from the repository head, tests green before packaging,
but less proven than a tagged release.

The download link lives in the
[project README](https://github.com/marbou92/FFX-Compatibility-Tool#readme).
The exe's version (latest release followed by "-nightly") shows in
its Windows file properties and in the app's About page and status
bar.

Windows shows a "publisher could not be verified" warning on first
run — the app is unsigned open source. Click Run, or tick Unblock
in the exe's Properties. If it crashes, include
`%LOCALAPPDATA%\FFXCompatibilityTool\crash.log` when reporting.
Build 2026-09-26 — commit 23d10de2dc65fb3160db670677e624dd7073f12a.
