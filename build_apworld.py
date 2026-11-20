"""Package the manual_megabonk_bbush folder into manual_megabonk_bbush.apworld.

Run from anywhere:  python build_apworld.py
"""
import zipfile
from pathlib import Path

NAME = "manual_megabonk_bbush"
root = Path(__file__).resolve().parent
src = root / NAME
target = root / f"{NAME}.apworld"

files = sorted(
    p for p in src.rglob("*")
    if p.is_file() and "__pycache__" not in p.parts and p.suffix != ".pyc"
)
with zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED) as zf:
    for p in files:
        zf.write(p, f"{NAME}/{p.relative_to(src).as_posix()}")

print(f"wrote {target.name}: {len(files)} files, {target.stat().st_size} bytes")
