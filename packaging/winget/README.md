# winget packaging

Ready-to-submit manifests for the Windows Package Manager. winget listings
do not live in this repository — they are pull requests to the community
catalog at https://github.com/microsoft/winget-pkgs.

## First submission (0.1.0)

1. Fork `microsoft/winget-pkgs` and create a branch.
2. Copy the three `Marbou92.FFXCompatibilityTool*.yaml` files into
   `manifests/m/Marbou92/FFXCompatibilityTool/0.1.0/` (same names).
3. Open a pull request. The winget validator bot checks the manifest
   against the real installer URL and SHA — both are already filled in
   from the published v0.1.0 asset, so it should pass on the first try.

## Every release after that

1. Download the new `FFXCompatibilityTool-<version>.exe` from the release
   page and compute its hash: `certutil -hashfile <file> SHA256`.
2. Copy the three manifests into `0.<new version>/`, update
   `PackageVersion` and `InstallerSha256` (and `InstallerUrl`) in each.
3. Pull request. (The `winget create` command can also generate these
   files from a URL directly.)

## Notes

- `InstallerType: portable` is right for this project: the exe IS the
  whole app, no installer. winget places it on the user's PATH under its
  `Commands` name.
- `Architecture: x86` — the exe is a single managed (AnyCPU → 32-bit
  fallback) assembly; winget requires a concrete architecture and x86
  runs everywhere.
