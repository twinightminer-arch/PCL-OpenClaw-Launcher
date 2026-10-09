# OCL 0.2.0 Rebuild Artifacts

This directory contains the source snapshot and Windows installer for the OCL 0.2.0 rebuild.

- `OCL-0.2.0-source.zip`: complete `source-v0.2.0` source tree used for the local build.
- `OCL-0.2.0-Setup.exe`: Inno Setup package. The installed copy was placed in `E:\\OCL`; the existing desktop `OCL.lnk` now targets that installation.
- `SHA256SUMS.txt`: SHA-256 checksums for both artifacts.

Local checks: 92 launcher integration checks passed; Wallpaper Engine plugin TypeScript build and test passed; launcher build completed with 0 warnings and 0 errors. The installer was run locally and the executable, bundled wallpaper extension, and shortcut target were verified. GUI interaction and real gateway/console startup were not smoke-tested, so this branch does not claim a fully verified public release.
