#!/usr/bin/env python3
"""Package only mod-owned runtime files, never game or Harmony dependencies."""
from pathlib import Path
import hashlib
import json
import zipfile


ROOT = Path(__file__).resolve().parents[1]
VERSIONS = ("1.4", "1.5", "1.6")
ASSEMBLIES = ("TechTraderPrevention.dll", "TechTraderPrevention.Core.dll")


def main():
    entries = []
    for folder in ("About", "Languages"):
        directory = ROOT / folder
        if not directory.is_dir():
            raise SystemExit(f"Missing package directory: {folder}")
        entries.extend(p for p in directory.rglob("*") if p.is_file())
    if not (ROOT / "About" / "About.xml").is_file():
        raise SystemExit("Missing About/About.xml")
    for version in VERSIONS:
        for assembly in ASSEMBLIES:
            path = ROOT / version / "Assemblies" / assembly
            if not path.is_file():
                raise SystemExit(f"Build required first: {path.relative_to(ROOT)}")
            entries.append(path)
    for name in ("LoadFolders.xml", "README.md"):
        path = ROOT / name
        if path.is_file():
            entries.append(path)
    # README links these player-facing compatibility and validation notes.
    entries.extend(path for path in (ROOT / "docs").glob("*.md") if path.is_file())
    output_dir = ROOT / "dist"
    output_dir.mkdir(exist_ok=True)
    destination = output_dir / "TechTraderPrevention-2.0.0.zip"
    manifest = []
    with zipfile.ZipFile(destination, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as package:
        for path in sorted(entries):
            content = path.read_bytes()
            relative = path.relative_to(ROOT).as_posix()
            # Fixed timestamps make repeat builds of identical contents reproducible.
            info = zipfile.ZipInfo(f"TechTraderPrevention/{relative}", date_time=(2023, 5, 17, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o644 << 16
            package.writestr(info, content)
            manifest.append({"path": relative, "sha256": hashlib.sha256(content).hexdigest(), "bytes": len(content)})
    manifest_path = output_dir / "manifest.json"
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(f"Created {destination.relative_to(ROOT)} ({len(manifest)} files)")
    print(f"SHA256 {hashlib.sha256(destination.read_bytes()).hexdigest()}")


if __name__ == "__main__":
    main()
