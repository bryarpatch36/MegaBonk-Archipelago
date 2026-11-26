"""Write the world's data files (items, locations, categories, options, game) from the lists below.

Run from anywhere:  python make_data.py
Then:               python build_apworld.py

The lists are the single place to change when the game adds a character, weapon, tome or map.
"""
import json
from pathlib import Path

WORLD = "manual_megabonk_ap"
data = Path(__file__).resolve().parent / WORLD / "data"

# character -> the weapon that character always starts a run with
CHARACTERS = {
    "Fox": "Firestaff",
    "Sir Oofie": "Sword",
    "Amog": "Poison Flask",
    "Athena": "Aegis",
    "Bandit": "Dexecutioner",
    "Birdo": "Tornado",
    "Bush": "Sniper Rifle",
    "Calcium": "Bone",
    "CL4NK": "Revolver",
    "Dicehead": "Dice",
    "Megachad": "Aura",
    "Monke": "Bananarang",
    "Ninja": "Katana",
    "Noelle": "Frostwalker",
    "Ogre": "Axe",
    "Roberto": "Scythe",
    "Robinette": "Bow",
    "Sir Chadwell": "Corrupted Sword",
    "Spaceman": "Black Hole",
    "Tony McZoom": "Wireless Dagger",
    "Vlad": "Blood Magic",
}

# weapons no character starts with
OTHER_WEAPONS = [
    "Flamewalker", "Lightning Staff", "Chunkers", "Space Noodle", "Slutty Rocket",
    "Shotgun", "Mines", "Dragon's Breath", "Hero Sword",
]

TOMES = [
    "Agility", "Armor", "Attraction", "Bloody", "Chaos", "Cooldown", "Cursed", "Damage",
    "Duration", "Evasion", "Golden", "Health", "Knockback", "Luck", "Precision",
    "Projectile Speed", "Quantity", "Regen", "Shield", "Silver", "Size", "Thorns", "XP",
]

# map -> the boss that ends a run of tier 1, 2, 3
TIER_BOSSES = {
    "Forest": ["Lil Bark", "Chadbark", "Bark Vader"],
    "Desert": ["Lil Anubis", "Anubruh", "Juge Anubis"],
}
MINIBOSSES = {
    "Forest": ["Stone Golem", "Chunkham the Terrible"],
    "Desert": ["Sand Golem", "Scorpionussy"],
}
HIDDEN_BOSSES = {
    "Forest": ["the Suspicious Bush"],
    "Desert": ["the Bandit"],
}
CHALLENGES = {
    "Forest": ["Fragile", "Turbo Mode", "Smol Boi", "AFK Gaming", "Blind", "Sticks and stones",
               "Weird Controls", "Speedrunner", "Speedrunner+", "Pacifist", "Oh no", "Oh shit",
               "What the fuck"],
    "Desert": ["Hyper", "The Floor is Lava", "Swarm", "Chonk Mode", "Speedrunner", "Speedrunner+",
               "Oh no", "Oh shit", "What the fuck"],
}

# Minibosses defeated in total, over every run of the seed, the ones called up at boss statues
# included: a check at each of these counts.
MINIBOSS_HUNT = [1, 2, 3, 4, 5, 6, 8, 10, 12, 15, 20, 25, 30, 40, 50]

CHARACTER_LEVEL = 30   # "<character> - Reach Level 30"
WEAPON_LEVEL = 10      # "<weapon> - Reach Level 10"
TOME_LEVEL = 8         # "<tome> Tome - Reach Level 8"

# What a longer run is expected to need, so the generator does not ask for Bark Vader with one tome.
POWER = {
    1: "",
    2: " and |@Weapons:3| and |@Tomes:5|",
    3: " and |@Weapons:6| and |@Tomes:9|",
}

ANY_MAP = "(|Progressive Forest Tier| or |Progressive Desert Tier| or |Graveyard Access|)"


def tier(map_name: str, n: int = 1) -> str:
    return f"|Progressive {map_name} Tier:{n}|"


items = []
for name in CHARACTERS:
    items.append({"name": f"Character - {name}", "category": ["Characters"], "count": 1, "progression": True})
for name in sorted(list(CHARACTERS.values()) + OTHER_WEAPONS):
    items.append({"name": f"Weapon - {name}", "category": ["Weapons"], "count": 1, "progression": True})
for name in TOMES:
    items.append({"name": f"{name} Tome", "category": ["Tomes"], "count": 1, "progression": True})
for map_name in TIER_BOSSES:
    items.append({"name": f"Progressive {map_name} Tier", "category": ["Maps"], "count": 3, "progression": True})
items.append({"name": "Graveyard Access", "category": ["Maps", "Graveyard"], "count": 1, "progression": True})

locations = []

# one run finished, and one level reached, per character
for name in CHARACTERS:
    have = f"|Character - {name}| and {ANY_MAP}"
    locations.append({"name": f"{name} - Complete a Run", "category": ["Characters"], "requires": have})
    locations.append({"name": f"{name} - Reach Level {CHARACTER_LEVEL}",
                      "category": ["Characters", "Character Levels"], "requires": have})

# one check per weapon: usable through its own item, or through the character who starts with it
owner = {weapon: character for character, weapon in CHARACTERS.items()}
for name in sorted(list(CHARACTERS.values()) + OTHER_WEAPONS):
    usable = f"|Weapon - {name}|"
    if name in owner:
        usable = f"({usable} or |Character - {owner[name]}|)"
    locations.append({"name": f"{name} - Reach Level {WEAPON_LEVEL}", "category": ["Weapons"],
                      "requires": f"{usable} and {ANY_MAP}"})

for name in TOMES:
    locations.append({"name": f"{name} Tome - Reach Level {TOME_LEVEL}", "category": ["Tomes"],
                      "requires": f"|{name} Tome| and {ANY_MAP}"})

for map_name, bosses in TIER_BOSSES.items():
    for n, boss in enumerate(bosses, start=1):
        locations.append({"name": f"{map_name} - Complete Tier {n} ({boss})", "category": [map_name],
                          "requires": tier(map_name, n) + POWER[n]})
    for boss in MINIBOSSES[map_name]:
        locations.append({"name": f"{map_name} - Defeat {boss}", "category": [map_name],
                          "requires": tier(map_name)})
    for boss in HIDDEN_BOSSES[map_name]:
        locations.append({"name": f"{map_name} - Defeat {boss}", "category": [map_name, "Hidden Bosses"],
                          "requires": tier(map_name)})
    for name in CHALLENGES[map_name]:
        locations.append({"name": f"{map_name} Challenge - {name}", "category": [map_name, "Challenges"],
                          "requires": tier(map_name)})

locations += [
    {"name": "Graveyard - Collect the 4 Crypt Keys", "category": ["Graveyard"], "requires": "|Graveyard Access|"},
    {"name": "Graveyard - Defeat Big Bob", "category": ["Graveyard"],
     "requires": "|Graveyard Access|" + POWER[2]},
    {"name": "Graveyard - Defeat Spooky Steve", "category": ["Graveyard", "Hidden Bosses"],
     "requires": "|Graveyard Access|" + POWER[3]},
]

# a longer hunt is only expected of a player who holds enough to survive the statues
for n in MINIBOSS_HUNT:
    locations.append({"name": f"Miniboss Hunt - Defeat {n} Miniboss{'es' if n > 1 else ''}", "category": ["Miniboss Hunt"],
                      "requires": ANY_MAP + (POWER[1] if n <= 10 else POWER[2] if n <= 25 else POWER[3])})

# goals, easiest first; hooks/Options.py makes "Tier 2 Bosses" the default
for n in (1, 2, 3):
    locations.append({
        "name": f"Tier {n} Bosses",
        "category": ["Goal"],
        "requires": f"{tier('Forest', n)} and {tier('Desert', n)}{POWER[n]}",
        "victory": True,
    })

categories = {
    "Character Levels": {"yaml_option": ["include_character_levels"]},
    "Hidden Bosses": {"yaml_option": ["include_hidden_bosses"]},
    "Challenges": {"yaml_option": ["include_challenges"]},
    "Graveyard": {"yaml_option": ["include_graveyard"]},
    "Miniboss Hunt": {"yaml_option": ["include_miniboss_hunt"]},
}

options = {
    "user": {
        "include_character_levels": {
            "type": "Toggle",
            "display_name": "Character level checks",
            "description": [f"Adds a check for reaching Level {CHARACTER_LEVEL} in a run with each character."],
            "default": True,
        },
        "include_hidden_bosses": {
            "type": "Toggle",
            "display_name": "Hidden boss checks",
            "description": ["Adds a check for each optional boss: the Suspicious Bush (Forest), "
                            "the Bandit (Desert) and Spooky Steve (Graveyard)."],
            "default": True,
        },
        "include_challenges": {
            "type": "Toggle",
            "display_name": "Challenge checks",
            "description": ["Adds a check for each of the Forest and Desert challenges (22 checks)."],
            "default": False,
        },
        "include_graveyard": {
            "type": "Toggle",
            "display_name": "Graveyard",
            "description": ["Adds the Graveyard map: the Graveyard Access item and its checks."],
            "default": True,
        },
        "include_miniboss_hunt": {
            "type": "Toggle",
            "display_name": "Miniboss hunt checks",
            "description": [f"Adds {len(MINIBOSS_HUNT)} checks for minibosses defeated in total over all your runs, "
                            f"up to {MINIBOSS_HUNT[-1]}. Minibosses called up at boss statues count."],
            "default": True,
        },
    }
}

game = {
    "game": "Megabonk",
    "creator": "AP",
    "filler_item_name": "Bonk",
    "death_link": True,
    "starting_items": [
        {"items": ["Progressive Forest Tier"], "random": 1},
        {"item_categories": ["Characters"], "random": 1},
        {"item_categories": ["Tomes"], "random": 2},
    ],
}


# ---------------------------------------------------------------------------------------------
# The same lists, shaped for the mod (mod/src/MegabonkAP.Core/checks.json): which check belongs
# to which thing in the game, so the mod and the world can never disagree on a name.
# ---------------------------------------------------------------------------------------------

# What the game calls things in its own code, where that differs from the name players see.
# Names are compared without case, spaces or punctuation, so only real differences are listed.
# Source: the enums of game version 1.0.69, as the mod's probe read them from the game.
ALIASES = {
    "weapon": {
        "Sniper": "Sniper Rifle",
        "Rockets": "Slutty Rocket",
        "Mine": "Mines",
        "CorruptSword": "Corrupted Sword",
        "BluetoothDagger": "Wireless Dagger",
    },
    "tome": {"Regeneration": "Regen", "Blood": "Bloody"},
    "character": {},
    "map": {},
}

# Names the game has that the world does not: the mod passes over them without a word.
# The four tomes are in the game's code (1.0.69) but cannot be picked in a run, so the world leaves them out.
IGNORED = {
    "weapon": ["Test", "None"],
    "tome": ["Bounce", "Balance", "Gambler", "Hoarder"],
    "character": [],
    "map": ["None", "Hell"],
}

# The game's own name of an enemy (EEnemy) -> the map and boss whose check its death earns.
# Exact names: "Golem" and "Scorpion" are ordinary enemies, only the Miniboss ones count.
BOSS_ENEMIES = {
    "MinibossPig": ("Forest", "Chunkham the Terrible"),
    "MinibossGolem": ("Forest", "Stone Golem"),
    "Bush": ("Forest", "Suspicious Bush"),
    "MinibossGolemSand": ("Desert", "Sand Golem"),
    "MinibossScorpion": ("Desert", "Scorpionussy"),
    "Bandit": ("Desert", "Bandit"),
}
# Bosses the mod recognises by what happens rather than by an enemy name.
NAMED_BOSSES = {"Big Bob": "Graveyard", "Spooky Steve": "Graveyard"}


def boss_check(map_name: str, boss: str) -> str:
    return next(loc["name"] for loc in locations
                if loc["name"] in (f"{map_name} - Defeat {boss}", f"{map_name} - Defeat the {boss}"))


mod_maps = {}
for map_name, bosses in TIER_BOSSES.items():
    mod_maps[map_name] = {
        "item": f"Progressive {map_name} Tier",
        "tiers": [f"{map_name} - Complete Tier {n} ({boss})" for n, boss in enumerate(bosses, start=1)],
        "challenges": {name: f"{map_name} Challenge - {name}" for name in CHALLENGES[map_name]},
    }
mod_maps["Graveyard"] = {
    "item": "Graveyard Access",
    "tiers": [],
    "challenges": {},
    "keys": "Graveyard - Collect the 4 Crypt Keys",
}

mod_checks = {
    "game": "Manual_Megabonk_AP",
    "levels": {"character": CHARACTER_LEVEL, "weapon": WEAPON_LEVEL, "tome": TOME_LEVEL},
    "characters": {
        name: {"item": f"Character - {name}", "weapon": weapon,
               "run": f"{name} - Complete a Run", "level": f"{name} - Reach Level {CHARACTER_LEVEL}"}
        for name, weapon in CHARACTERS.items()
    },
    "weapons": {
        name: {"item": f"Weapon - {name}", "level": f"{name} - Reach Level {WEAPON_LEVEL}"}
        for name in sorted(list(CHARACTERS.values()) + OTHER_WEAPONS)
    },
    "tomes": {
        name: {"item": f"{name} Tome", "level": f"{name} Tome - Reach Level {TOME_LEVEL}"}
        for name in TOMES
    },
    "maps": mod_maps,
    "enemies": {enemy: boss_check(where, boss) for enemy, (where, boss) in BOSS_ENEMIES.items()},
    "bosses": {boss: boss_check(where, boss) for boss, where in NAMED_BOSSES.items()},
    "miniboss_hunt": {str(n): f"Miniboss Hunt - Defeat {n} Miniboss{'es' if n > 1 else ''}" for n in MINIBOSS_HUNT},
    "ignored": IGNORED,
    "goals": [f"Tier {n} Bosses" for n in (1, 2, 3)],   # in the order of the world's goal option
    "aliases": ALIASES,
}

def write(name: str, content) -> None:
    text = json.dumps(content, indent=2, ensure_ascii=False) + "\n"
    (data / name).write_text(text, encoding="utf-8", newline="\n")


mod_core = Path(__file__).resolve().parent / "mod" / "src" / "MegabonkAP.Core"
mod_core.mkdir(parents=True, exist_ok=True)
(mod_core / "checks.json").write_text(
    json.dumps(mod_checks, indent=2, ensure_ascii=False) + "\n", encoding="utf-8", newline="\n")


write("items.json", items)
write("locations.json", locations)
write("categories.json", categories)
write("options.json", options)
write("game.json", game)
write("regions.json", {})

pool = sum(item["count"] for item in items)
print(f"{len(items)} item names ({pool} in the pool), {len(locations)} locations")
