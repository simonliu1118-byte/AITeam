from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
path = ROOT / "shared" / "cy-visual" / "desktop" / "CY_DESKTOP_VISUAL_GUIDE.md"
text = path.read_text(encoding="utf-8")

old = '''## 11.1 Dialog shell — `CORE — ADVISORY`\n\n- native title bar / window behavior first;\n- CY consistency applies to content typography / surface / inputs / buttons;\n- do not convert every dialog into borderless / custom chrome.\n'''
new = '''## 11.1 Dialog shell — `CORE`\n\n- native title bar / window behavior first;\n- the main application window carries the app's canonical family icon;\n- secondary dialogs, management windows and settings windows opened from the main application do **not** repeat the app icon in their title bar;\n- suppress the secondary-window app icon through the framework / OS-supported window API when available; do not introduce borderless or custom chrome solely to hide an icon;\n- native MessageBox keeps the OS-provided semantic icon / shell behavior and is not subject to the secondary-dialog no-app-icon rule;\n- CY consistency applies to content typography / surface / inputs / buttons;\n- do not convert every dialog into borderless / custom chrome.\n'''

count = text.count(old)
if count != 1:
    raise SystemExit(f"expected one Dialog shell block, got {count}")
text = text.replace(old, new, 1)
path.write_text(text, encoding="utf-8")
print("updated CY Desktop Visual Guide secondary dialog icon rule")
