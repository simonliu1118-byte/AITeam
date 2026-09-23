from __future__ import annotations

from pathlib import Path

import cairosvg
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
SIZES = (16, 24, 32, 48, 64, 128, 256)
SOURCES = {
    "INV": ROOT / "apps" / "invoice" / "INV.svg",
    "ACC": ROOT / "apps" / "accounting" / "ACC.svg",
    "ENV": ROOT / "apps" / "envelope" / "ENV.svg",
    "Auto": ROOT / "apps" / "erp-autoinput" / "Auto.svg",
    "WM": ROOT / "apps" / "watermark" / "WM.svg",
    "CVT": ROOT / "apps" / "converter" / "CVT.svg",
    "CAL": ROOT / "apps" / "tri-invoice-calc" / "CAL.svg",
}


def export_one(identifier: str, source: Path) -> None:
    target_dir = source.parent
    png_path = target_dir / f"{identifier}_256.png"
    ico_path = target_dir / f"{identifier}.ico"

    svg = source.read_bytes()
    cairosvg.svg2png(
        bytestring=svg,
        write_to=str(png_path),
        output_width=256,
        output_height=256,
    )

    image = Image.open(png_path).convert("RGBA")
    image.save(ico_path, format="ICO", sizes=[(size, size) for size in SIZES])

    check = Image.open(ico_path)
    actual = set(check.info.get("sizes", ()))
    expected = {(size, size) for size in SIZES}
    if actual != expected:
        raise RuntimeError(f"{identifier}: ICO sizes mismatch: {sorted(actual)}")


if __name__ == "__main__":
    for identifier, source in SOURCES.items():
        export_one(identifier, source)
        print(f"exported {identifier}")
