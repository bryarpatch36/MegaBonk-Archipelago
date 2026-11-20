"""Generate a seed for every combination of options that changes the checks, and read the result.

Needs an Archipelago source checkout with this world's .apworld in its custom_worlds
folder, and a Python that has Archipelago's core requirements.

    python test_generate.py <archipelago source folder>

For each case it checks: generation succeeds with full accessibility (so the goal is reachable),
the number of checks matches the data files, the goal is the one asked for, and the starting
items are one Forest tier, one character and two tomes.
"""
import json
import os
import re
import subprocess
import sys
import tempfile
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
WORLD = "manual_megabonk_bbush"
GAME = "Manual_Megabonk_bbush"
DATA = ROOT / WORLD / "data"

locations = json.loads((DATA / "locations.json").read_text(encoding="utf-8"))
categories = json.loads((DATA / "categories.json").read_text(encoding="utf-8"))
options = json.loads((DATA / "options.json").read_text(encoding="utf-8"))["user"]
TOGGLES = sorted(options)
GOALS = [loc["name"] for loc in locations if loc.get("victory")]


def expected_checks(settings: dict) -> int:
    """Checks the seed should hold: every location that is not a goal and whose categories are all on."""
    count = 0
    for loc in locations:
        if loc.get("victory"):
            continue
        on = all(
            settings[option]
            for category in loc["category"]
            for option in categories.get(category, {}).get("yaml_option", [])
        )
        count += on
    return count


def cases():
    defaults = {name: options[name]["default"] for name in TOGGLES}
    yield "defaults", dict(defaults), "Tier 2 Bosses"
    yield "everything on", {name: True for name in TOGGLES}, "Tier 3 Bosses"
    yield "everything off", {name: False for name in TOGGLES}, "Tier 1 Bosses"
    for name in TOGGLES:                      # each switch flipped on its own
        flipped = dict(defaults)
        flipped[name] = not flipped[name]
        yield f"{name} flipped", flipped, "Tier 2 Bosses"
    for goal in GOALS:
        yield f"goal {goal}", dict(defaults), goal


def player_yaml(settings: dict, goal: str) -> str:
    lines = ["name: Bonker", f"game: {GAME}", f"{GAME}:", f"  goal: {goal.lower()}"]
    lines += [f"  {name}: {'true' if value else 'false'}" for name, value in settings.items()]
    return "\n".join(lines) + "\n"


def generate(source: Path, settings: dict, goal: str, seed: int) -> str:
    """Run Generate.py and return the spoiler log's text."""
    with tempfile.TemporaryDirectory() as work:
        players = Path(work) / "players"
        out = Path(work) / "out"
        players.mkdir()
        out.mkdir()
        (players / "Bonker.yaml").write_text(player_yaml(settings, goal), encoding="utf-8")
        env = dict(os.environ, SKIP_REQUIREMENTS_UPDATE="1")
        run = subprocess.run(
            [sys.executable, "Generate.py", "--player_files_path", str(players),
             "--outputpath", str(out), "--spoiler", "2", "--seed", str(seed)],
            cwd=source, env=env, capture_output=True, text=True,
        )
        if run.returncode != 0:
            raise AssertionError(f"generation failed:\n{run.stdout[-1500:]}\n{run.stderr[-3000:]}")
        archive = next(out.glob("AP_*.zip"))
        with zipfile.ZipFile(archive) as zf:
            names = zf.namelist()
            assert any(n.endswith(".archipelago") for n in names), names
            assert any(n.endswith(".apmanual") for n in names), names
            spoiler = next(n for n in names if n.endswith("_Spoiler.txt"))
            return zf.read(spoiler).decode("utf-8-sig")


def check(spoiler: str, settings: dict, goal: str) -> None:
    count = int(re.search(r"Location Count:\s+(\d+)", spoiler).group(1))
    want = expected_checks(settings)
    assert count == want, f"{count} checks, expected {want}"

    got_goal = re.search(r"^goal:\s+(.+)$", spoiler, re.M).group(1).strip()
    assert got_goal == goal, f"goal {got_goal!r}, expected {goal!r}"
    assert re.search(r"^Accessibility:\s+Full", spoiler, re.M), "accessibility is not full"

    start = [line.strip() for line in spoiler.split("Starting Items:")[1].split("Locations:")[0].splitlines() if line.strip()]
    assert start.count("Progressive Forest Tier") == 1, start
    assert sum(item.startswith("Character - ") for item in start) == 1, start
    assert sum(item.endswith(" Tome") for item in start) == 2, start
    assert len(start) == 4, start

    assert "Playthrough:" in spoiler, "no playthrough: the goal was not shown reachable"
    if not settings["include_graveyard"]:
        assert "Graveyard" not in spoiler.split("Locations:")[1], "Graveyard is off but still in the seed"
    if not settings["include_challenges"]:
        assert "Challenge - " not in spoiler, "challenges are off but still in the seed"


def main() -> int:
    if len(sys.argv) != 2:
        print(__doc__)
        return 2
    source = Path(sys.argv[1]).resolve()
    installed = source / "custom_worlds" / f"{WORLD}.apworld"
    built = ROOT / f"{WORLD}.apworld"
    if not installed.exists() or installed.read_bytes() != built.read_bytes():
        print(f"{installed} is missing or is not the .apworld in this folder; copy it there first")
        return 2

    failed = 0
    for seed, (label, settings, goal) in enumerate(cases(), start=1):
        try:
            check(generate(source, settings, goal, seed), settings, goal)
        except AssertionError as problem:
            failed += 1
            print(f"FAIL  {label}: {problem}")
        else:
            print(f"ok    {label}: {expected_checks(settings)} checks, goal {goal}")
    print("all cases passed" if not failed else f"{failed} case(s) failed")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
