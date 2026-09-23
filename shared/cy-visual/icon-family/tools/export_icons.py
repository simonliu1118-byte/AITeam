from __future__ import annotations

import hashlib
import json
import struct
from pathlib import Path

import cairosvg
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
SIZES = (16, 24, 32, 48, 64, 128, 256)
SOURCES = {
    "INV": ("CYInvoice", "invoice", "#006EFE"),
    "ACC": ("CYAccounting", "accounting", "#03A844"),
    "ENV": ("CYEnvelope", "envelope", "#761EE1"),
    "Auto": ("CYERPAutoInput", "erp-autoinput", "#FE6A02"),
    "WM": ("CYWatermark", "watermark", "#019EB9"),
    "CVT": ("CYConverter", "converter", "#F09D03"),
    "CAL": ("CYTriplicateCalculator", "tri-invoice-calc", "#FD4944"),
}


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def git_blob_sha1(path: Path) -> str:
    data = path.read_bytes()
    return hashlib.sha1(f"blob {len(data)}\0".encode() + data).hexdigest()


def write_png_ico(entries: list[tuple[int, Path]], target: Path) -> None:
    """Build an ICO containing the exact PNG rendered for each native size."""
    payloads = [path.read_bytes() for _, path in entries]
    offset = 6 + 16 * len(entries)
    directory = []
    for (size, _), data in zip(entries, payloads):
        width = 0 if size == 256 else size
        height = 0 if size == 256 else size
        directory.append(
            struct.pack("<BBBBHHII", width, height, 0, 0, 1, 32, len(data), offset)
        )
        offset += len(data)
    target.write_bytes(
        struct.pack("<HHH", 0, 1, len(entries)) + b"".join(directory) + b"".join(payloads)
    )


def export_one(identifier: str, app: str, folder: str, accent: str) -> dict:
    source = ROOT / "apps" / folder / f"{identifier}.svg"
    target_dir = source.parent
    png_dir = target_dir / "png"
    png_dir.mkdir(exist_ok=True)

    png_entries: list[tuple[int, Path]] = []
    png_manifest = {}
    svg = source.read_bytes()
    for size in SIZES:
        target = png_dir / f"{size}.png"
        cairosvg.svg2png(
            bytestring=svg,
            write_to=str(target),
            output_width=size,
            output_height=size,
        )
        with Image.open(target) as image:
            image.load()
            if image.size != (size, size):
                raise RuntimeError(f"{identifier}: PNG {size} rendered as {image.size}")
            mode = image.mode
        png_entries.append((size, target))
        png_manifest[str(size)] = {
            "bytes": target.stat().st_size,
            "sha256": sha256(target),
            "git_blob_sha1": git_blob_sha1(target),
            "dimensions": [size, size],
            "mode": mode,
        }

    ico_path = target_dir / f"{identifier}.ico"
    write_png_ico(png_entries, ico_path)
    with Image.open(ico_path) as image:
        actual = sorted(image.info.get("sizes", ()))
    expected = sorted((size, size) for size in SIZES)
    if actual != expected:
        raise RuntimeError(f"{identifier}: ICO sizes mismatch: {actual}")

    return {
        "app": app,
        "folder": folder,
        "accent": accent,
        "svg": {
            "bytes": source.stat().st_size,
            "sha256": sha256(source),
            "git_blob_sha1": git_blob_sha1(source),
        },
        "png": png_manifest,
        "ico": {
            "bytes": ico_path.stat().st_size,
            "sha256": sha256(ico_path),
            "git_blob_sha1": git_blob_sha1(ico_path),
            "entries": [list(size) for size in actual],
        },
    }


def main() -> None:
    master = ROOT / "master" / "CY_ICON_MASTER.svg"
    manifest = {
        "format_version": 1,
        "sizes": list(SIZES),
        "master": {
            "bytes": master.stat().st_size,
            "sha256": sha256(master),
            "git_blob_sha1": git_blob_sha1(master),
        },
        "apps": {},
    }
    for identifier, (app, folder, accent) in SOURCES.items():
        manifest["apps"][identifier] = export_one(identifier, app, folder, accent)
        print(f"exported {identifier}")

    manifest_path = ROOT / "ASSET_MANIFEST.json"
    manifest_path.write_text(
        json.dumps(manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
    )
    print(f"wrote {manifest_path}")


if __name__ == "__main__":
    main()
