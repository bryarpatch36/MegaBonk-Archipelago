# Megabonk for Archipelago

Megabonk as a game in an [Archipelago](https://archipelago.gg) multiworld. It has two parts:

- **A Manual world.** Nothing is installed into the game. You play Megabonk normally, the items you are sent decide which characters, weapons, tomes and maps you may use, and you tick off what you have done in Archipelago's Manual Client.
- **MegabonkAP, a mod (in progress).** A BepInEx plugin that sends the checks for you as you play. It loads in the game, attaches its hooks and connects; reading the game correctly is still being worked through, one test run at a time: see [mod/README.md](mod/README.md).

This is an unofficial fan project. It is not affiliated with or endorsed by vedinad or the Archipelago project.

## Status

| Part | State |
|---|---|
| Generating a multiworld with every option combination | Works (automated test) |
| Hosting a seed, connecting as the slot, sending checks, receiving items, reaching the goal | Works against a local Archipelago 0.6.8 server (automated test, no game involved) |
| Playing it with the Manual Client and the real game | Not yet tried |
| Checks sent by themselves (the mod) | Loads in Megabonk 1.0.69, attaches its hooks and connects. No check has been sent from the game yet ([mod/README.md](mod/README.md)) |
| Rules enforced by the game | Not built |

The lists of characters, weapons and tomes come from a public Megabonk database as of November 2026, and all match the game's own code (version 1.0.69). The game's code also names four tomes the world does not have (Bounce, Balance, Gambler, Hoarder); whether they can be obtained has not been checked. Megabonk was not available on the PC this was built on, so three things were taken from guides rather than seen in the game and may need a correction after a first session: the challenge names, the Graveyard checks, and the level numbers the checks ask for (see "Building and changing it").

## What you need

- Archipelago 0.6.8 or newer, installed on your PC. The archipelago.gg website cannot generate custom worlds, so one player generates the multiworld locally.
- Megabonk, any platform.
- A save that already has most of the game unlocked. An Archipelago item only gives you *permission*; the game still has to offer the thing. See "Things your save has not unlocked" below.
- Recommended: the **Toggler** shop upgrade (the game offers it for Silver once you own 40 unlocks). It switches weapons and tomes off before a run, so the game keeps the rules for you.

## Install and play

1. Give `manual_megabonk_bbush.apworld` from this folder to every Megabonk player and to whoever generates.
2. In the Archipelago Launcher, click "Install APWorld", pick the file, and restart the Launcher.
3. Each Megabonk player needs a YAML with `game: Manual_Megabonk_bbush`. [Bonker.yaml](Bonker.yaml) is ready to use: copy it into the `Players` folder and change `name` to your slot name.
4. Generate the multiworld locally, then host the result.
5. Each Megabonk player opens "Manual Client" in the Launcher and connects with the game ID `Manual_Megabonk_bbush`.
6. Before each run, look at the items you hold and set the Toggler to match. After the run, tick what you did in the client's "Manual" tab.

An APWorld is Python code that runs whenever Archipelago starts. Install this one, like any other, only if you trust where it came from; the full source is in this folder.

## Rules of play

- **Characters.** Start a run only with a character whose `Character - <name>` item you hold. You start with one random character.
- **Weapons.** At a level-up, pick only weapons whose `Weapon - <name>` item you hold. A character's own starting weapon is always allowed with that character, and upgrading it is always allowed.
- **Tomes.** At a level-up, pick only tomes whose `<name> Tome` item you hold. You start with two random tomes.
- **Nothing you hold is offered.** Use a Refresh, Skip or Banish if you have one. If you have none, take anything: it is not a rule break, but it does not count toward that weapon's or tome's check.
- **Maps and tiers.** `Progressive Forest Tier` and `Progressive Desert Tier` open one more tier of that map each time you receive one (first Tier 1, then Tier 2, then Tier 3). `Graveyard Access` opens the Graveyard. You start with Forest Tier 1.
- **Everything else is free.** Items found in a run, shrines, chests, statues, the Microwave, Shady Guys and the shop's permanent upgrades are not restricted.
- **Goal.** Complete the chosen tier on both Forest and Desert: `tier 1 bosses`, `tier 2 bosses` (the default) or `tier 3 bosses`.
- **DeathLink** (off by default). The Manual Client gets a Death Link button: press it when you die, and when someone else dies, end your run.

These rules are on your honour, as in any Manual game.

### Things your save has not unlocked

If you are sent a character, weapon, tome or map tier that your save has not unlocked yet, you cannot use it until you unlock it in the game the normal way. Two ways to deal with that:

- Play on a save with everything unlocked. This is the simplest.
- Otherwise, before generating, list the checks you cannot reach under `exclude_locations` in your YAML (for example `Vlad - Complete a Run`, `Vlad - Reach Level 30`, `Blood Magic - Reach Level 10`). Excluded checks only ever hold filler, so nobody waits on them.

## Checks and options

| Check | Count | Option that switches it off |
|---|---|---|
| Complete a run with each character (any map, any tier) | 21 | always on |
| Reach Level 30 in a run with each character | 21 | `include_character_levels` (default on) |
| Get each weapon to Level 10 in a run | 30 | always on |
| Get each tome to Level 8 in a run | 23 | always on |
| Complete Forest Tier 1, 2, 3 (Lil Bark, Chadbark, Bark Vader) | 3 | always on |
| Complete Desert Tier 1, 2, 3 (Lil Anubis, Anubruh, Juge Anubis) | 3 | always on |
| Minibosses: Stone Golem, Chunkham the Terrible, Sand Golem, Scorpionussy | 4 | always on |
| Hidden bosses: the Suspicious Bush, the Bandit, Spooky Steve | 3 | `include_hidden_bosses` (default on) |
| Graveyard: collect the 4 Crypt Keys, defeat Big Bob | 2 | `include_graveyard` (default on; also removes Spooky Steve and `Graveyard Access`) |
| Forest challenges (13) and Desert challenges (9) | 22 | `include_challenges` (default off) |

With the defaults that is 110 checks. The item pool is 21 characters, 30 weapons, 23 tomes, 3 Forest tiers, 3 Desert tiers and `Graveyard Access` (81 items, 4 of them given at the start); the remaining checks hold `Bonk`, a filler item that does nothing.

So that nobody is asked for a long run with nothing in hand, the generator only expects a Tier 2 completion (and Big Bob) once you hold 3 weapons and 5 tomes, and a Tier 3 completion (and Spooky Steve) once you hold 6 weapons and 9 tomes.

## What this does not do

- **No automatic checks yet.** You tick every check yourself until the mod in [mod/](mod/README.md) has been tried in the game. Two other attempts at a Megabonk Archipelago mod are public, both unfinished as of November 2026: one repository of empty files, and Bonkipelago, a two-day prototype without a release or a world.
- **No enforcement.** The game does not know what you hold. The Toggler covers weapons and tomes; characters and tiers are on your honour.
- **In-run items are not part of it.** The game's 85 pickup items are not in the item pool.

## Building and changing it

- `make_data.py` holds every list (characters and their starting weapons, weapons, tomes, bosses, challenges) and the level numbers (`CHARACTER_LEVEL`, `WEAPON_LEVEL`, `TOME_LEVEL`), and writes the world's `data/*.json` from them. Change a name or a number there, never in the JSON.
- `make_data.py` also writes `mod/src/MegabonkAP.Core/checks.json`, the same names for the mod, so the two cannot disagree.
- `build_apworld.py` packs `manual_megabonk_bbush/` into `manual_megabonk_bbush.apworld`.
- Bump `world_version` in `manual_megabonk_bbush/archipelago.json` on every change, and give the new `.apworld` to every player before generating: all players need the same one.

```
python make_data.py
python build_apworld.py
```

Tests need an Archipelago source checkout with the `.apworld` copied into its `custom_worlds` folder, and a Python that has Archipelago's core requirements:

```
python tests/test_generate.py <archipelago source folder>
python tests/e2e_local_server.py <archipelago source folder> <seed zip> Bonker
```

`test_generate.py` generates a seed for every option combination and checks the number of checks, the goal and the starting items. `e2e_local_server.py` hosts a seed on a local server and plays the slot without the game. The mod has its own tests: see [mod/README.md](mod/README.md).

## Credits

- [Manual for Archipelago](https://github.com/ManualForArchipelago/Manual): the world template and the Manual Client.
- Game data: [Bloxodes Megabonk database](https://bloxodes.com/wiki/megabonk) (characters, weapons, tomes) and the megabonk.org map, Graveyard and challenge guides.
