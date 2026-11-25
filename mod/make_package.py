"""Build the plugin and pack it into one zip a mod manager can import (r2modman, Gale).

Run from anywhere:  python mod/make_package.py
Needs the .NET SDK 8 or newer. Writes MegabonkAP-<version>.zip in the folder above.

The zip has the Thunderstore layout: manifest.json, icon.png, README.md and the plugin's files.
In r2modman: Settings -> Import local mod -> pick the zip.
"""
import json
import re
import subprocess
import zipfile
from pathlib import Path

# The BepInEx pack the Megabonk community uses; the mod manager installs it with the mod.
BEPINEX = "BepInEx-BepInExPack_IL2CPP-6.0.788"

mod = Path(__file__).resolve().parent
project = mod / "src" / "MegabonkAP.Plugin" / "MegabonkAP.Plugin.csproj"
version = re.search(r"<Version>([^<]+)</Version>", project.read_text(encoding="utf-8")).group(1)

subprocess.run(["dotnet", "build", str(project), "-c", "Release", "--nologo", "-v", "quiet"], check=True)

manifest = {
    "name": "MegabonkArchipelago",
    "version_number": version,
    "website_url": "https://github.com/bryarpatch36/MegaBonk-Archipelago",
    "description": "Sends your Megabonk checks to an Archipelago multiworld automatically.",
    "dependencies": [BEPINEX],
}

dlls = sorted((mod / "dist" / "MegabonkAP").glob("*.dll"))
if not any(p.name == "MegabonkAP.dll" for p in dlls):
    raise SystemExit("the build left no MegabonkAP.dll in mod/dist/MegabonkAP")

target = mod.parent / f"MegabonkAP-{version}.zip"
with zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED) as zf:
    zf.writestr("manifest.json", json.dumps(manifest, indent=2) + "\n")
    zf.write(mod / "package" / "icon.png", "icon.png")
    zf.write(mod / "package" / "README.md", "README.md")
    # the licence of the mod and of the two libraries packed with it
    for notice in ("LICENSE", "THIRD-PARTY-NOTICES.md"):
        zf.write(mod.parent / notice, notice)
    for p in dlls:
        zf.write(p, p.name)

print(f"wrote {target.name}: {len(dlls)} plugin files, {target.stat().st_size} bytes")
