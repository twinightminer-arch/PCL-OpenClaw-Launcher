#!/usr/bin/env python3
"""Inject the wallpaper layer into the built OpenClaw Control UI.

The Control UI ships as a prebuilt bundle (dist/control-ui) with no plugin
extension point for the frontend, so this patch adds one script tag to
index.html. It is idempotent and keeps a .bak-wallpaper backup of the
original file. Re-run it after upgrading OpenClaw (which rewrites dist/).
"""

import argparse
import os
import sys

TAG = '<script src="./wallpaper/wallpaper.js" defer></script>'


def default_root() -> str:
    return os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "..", "..")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--control-ui-root",
        default=r"E:\openclaw\dist\control-ui",
        help="Control UI asset root (default: E:\\openclaw\\dist\\control-ui)",
    )
    parser.add_argument("--uninstall", action="store_true", help="Remove the injected tag")
    args = parser.parse_args()

    index_path = os.path.join(args.control_ui_root, "index.html")
    if not os.path.exists(index_path):
        print(f"ERROR: index.html not found at {index_path}")
        return 1

    with open(index_path, "r", encoding="utf-8") as handle:
        html = handle.read()

    if args.uninstall:
        if TAG not in html:
            print("Nothing to remove; tag not present.")
            return 0
        html = html.replace("\n    " + TAG, "").replace(TAG, "")
        with open(index_path, "w", encoding="utf-8") as handle:
            handle.write(html)
        print(f"Removed wallpaper script tag from {index_path}")
        return 0

    if TAG in html:
        print(f"Already patched: {index_path}")
        return 0

    backup = index_path + ".bak-wallpaper"
    if not os.path.exists(backup):
        with open(backup, "w", encoding="utf-8") as handle:
            handle.write(html)
        print(f"Backup written: {backup}")

    marker = "</head>"
    if marker not in html:
        print("ERROR: no </head> marker in index.html")
        return 1

    html = html.replace(marker, f"    {TAG}\n  {marker}", 1)
    with open(index_path, "w", encoding="utf-8") as handle:
        handle.write(html)
    print(f"Patched {index_path}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
