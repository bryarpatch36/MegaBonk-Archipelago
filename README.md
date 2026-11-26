# Megabonk for Archipelago

Megabonk as a game in an [Archipelago](https://archipelago.gg) multiworld. It has two parts:

- **A Manual world.** Nothing is installed into the game. You play Megabonk normally, the items you are sent decide which characters, weapons, tomes and maps you may use, and you tick off what you have done in Archipelago's Manual Client.
- **MegabonkAP, an optional mod.** A BepInEx plugin that sends the checks for you as you play, locks what you do not hold yet, springs the traps you are sent and shows a tracker on screen. It has been played on the real game; a few parts are still untested: see [mod/README.md](mod/README.md).

This is an unofficial fan project. It is not affiliated with or endorsed by vedinad or the Archipelago project.

## Status

| Part | State |
|---|---|
| Generating a multiworld with every option combination | Works |
| Hosting a seed, connecting as the slot, sending checks, receiving items, reaching the goal | Works |
| Playing it on the real game with the mod: checks sent by themselves, locks, the tracker, DeathLink, the miniboss hunt | Works on the real game. Not yet confirmed there: the Graveyard checks, Spooky Steve, the challenge checks ([mod/README.md](mod/README.md)) |
| Traps (world 0.4.0, mod 0.6.0) | Generating them and delivering each exactly once works against a local Archipelago 0.6.8 server. What each does inside the game is not yet confirmed there ([mod/README.md](mod/README.md#traps)) |
| Playing it on the real game without the mod, ticking every check in the Manual Client | Works |

## What you need

- Archipelago 0.6.8 or newer, installed on your PC. The archipelago.gg website cannot generate custom worlds, so one player generates the multiworld locally.
- Megabonk, any platform.
- A save that already has most of the game unlocked. An Archipelago item only gives you *permission*; the game still has to offer the thing. See "Things your save has not unlocked" below.
- Recommended: the **Toggler** shop upgrade (the game offers it for Silver once you own 40 unlocks). It switches weapons and tomes off before a run, so the game keeps the rules for you.

## Install and play

1. Download `manual_megabonk_ap.apworld` from the [Releases page](https://github.com/bryarpatch36/MegaBonk-Archipelago/releases) and give it to every Megabonk player and to whoever generates.
2. In the Archipelago Launcher, click "Install APWorld", pick the file, and restart the Launcher.
3. Each Megabonk player needs a YAML with `game: Manual_Megabonk_AP`. [Bonker.yaml](Bonker.yaml) is ready to use (it is on the Releases page too): copy it into the `Players` folder and change `name` to your slot name.
4. Generate the multiworld locally, then host the result.
*Optional* Each Megabonk player opens "Manual Client" in the Launcher and connects with the game ID `Manual_Megabonk_AP`.
5. Before each run, look at the items you hold and set the Toggler to match. After the run, tick what you did in the client's "Manual" tab.

With the mod, step 5 is done for you: install it as [mod/README.md](mod/README.md) describes. The Manual Client can stay open next to it.

An APWorld is Python code that runs whenever Archipelago starts. Install this one, like any other, only if you trust where it came from; the full source is in this repository.

## Rules of play

- **Characters.** Start a run only with a character whose `Character - <name>` item you hold. You start with one random character.
- **Weapons.** At a level-up, pick only weapons whose `Weapon - <name>` item you hold. A character's own starting weapon is always allowed with that character, and upgrading it is always allowed.
- **Tomes.** At a level-up, pick only tomes whose `<name> Tome` item you hold. You start with two random tomes.
- **Nothing you hold is offered.** Use a Refresh, Skip or Banish if you have one. If you have none, take anything: it is not a rule break, but it does not count toward that weapon's or tome's check.
- **Maps and tiers.** `Progressive Forest Tier` and `Progressive Desert Tier` open one more tier of that map each time you receive one (first Tier 1, then Tier 2, then Tier 3). `Graveyard Access` opens the Graveyard. You start with Forest Tier 1.
- **Traps** (option `traps`, on by default). `Trap - Miniboss`, `Swarm`, `Elite Pack`, `Poison`, `Slow`, `Freeze`: it happens in your run when the item arrives. One that arrives while you are in a menu waits for your next run. Traps need the mod; without it they do nothing.
- **Everything else is free.** Items found in a run, shrines, chests, statues, the Microwave, Shady Guys and the shop's permanent upgrades are not restricted.
- **Goal.** Complete the chosen tier on both Forest and Desert: `tier 1 bosses`, `tier 2 bosses` (the default) or `tier 3 bosses`.
- **DeathLink** (off by default). The Manual Client gets a Death Link button: press it when you die, and when someone else dies, end your run.

Without the mod these rules are on your honour, as in any Manual game. The mod enforces them: a locked character, map or tier cannot be started, and a locked weapon or tome cannot be picked.

### Things your save has not unlocked

If you are sent a character, weapon, tome or map tier that your save has not unlocked yet, you cannot use it until you unlock it in the game the normal way. Three ways to deal with that:

- Play on a save with everything unlocked. This is the simplest.
- Otherwise, before generating, list the checks you cannot reach under `exclude_locations` in your YAML (for example `Vlad - Complete a Run`, `Vlad - Reach Level 30`, `Blood Magic - Reach Level 10`). Excluded checks only ever hold filler, so nobody waits on them.
- Install an everything unlock Mod using a mod manager. I used "UnlockEverything" by lukeod installed via r2modman. 

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
| Miniboss hunt: minibosses defeated in total over all your runs, at 1, 2, 3, 4, 5, 6, 8, 10, 12, 15, 20, 25, 30, 40 and 50. Ones called up at boss statues count. Needs the mod to count them | 15 | `include_miniboss_hunt` (default on; switch it off for a shorter solo seed) |
| Hidden bosses: the Suspicious Bush, the Bandit, Spooky Steve | 3 | `include_hidden_bosses` (default on) |
| Graveyard: collect the 4 Crypt Keys, defeat Big Bob | 2 | `include_graveyard` (default on; also removes Spooky Steve and `Graveyard Access`) |
| Forest challenges (13) and Desert challenges (9) | 22 | `include_challenges` (default off) |

With the defaults that is 125 checks. The item pool is 21 characters, 30 weapons, 23 tomes, 3 Forest tiers, 3 Desert tiers and `Graveyard Access` (81 items, 4 of them given at the start), and 12 traps: 3 Swarm, 2 each of Miniboss, Elite Pack, Poison and Slow, and 1 Freeze. The remaining checks hold `Bonk`, a filler item that does nothing.

`traps: false` in the YAML takes the 12 traps out. In a seed with fewer checks than items (everything else switched off leaves 84 checks for 88 items) Manual leaves out some traps to fit. Manual's own `filler_traps` option (0 by default) turns that percentage of the `Bonk` filler into more traps.

So that nobody is asked for a long run with nothing in hand, the generator only expects a Tier 2 completion (and Big Bob) once you hold 3 weapons and 5 tomes, and a Tier 3 completion (and Spooky Steve) once you hold 6 weapons and 9 tomes. The miniboss hunt follows the same steps: beyond 10 minibosses is expected with 3 weapons and 5 tomes, beyond 25 with 6 and 9.

## What this does not do

- **Nothing is automatic without the mod.** You tick every check yourself, and the game does not know what you hold: the Toggler covers weapons and tomes; characters and tiers are on your honour. The mod in [mod/](mod/README.md) sends the checks and enforces the rules.
- **Traps need the mod.** In the Manual Client a trap is an item like any other, and nothing happens.
- **The miniboss hunt needs the mod.** The game shows no total of minibosses defeated, so without the mod you would have to count them yourself; `include_miniboss_hunt: false` takes those checks out.
- **In-run items are not part of it.** The game's 85 pickup items are not in the item pool.

## Building and changing it

- `make_data.py` holds every list (characters and their starting weapons, weapons, tomes, bosses, challenges) and the level numbers (`CHARACTER_LEVEL`, `WEAPON_LEVEL`, `TOME_LEVEL`), and writes the world's `data/*.json` from them. Change a name or a number there, never in the JSON.
- `make_data.py` also writes `mod/src/MegabonkAP.Core/checks.json`, the same names for the mod, so the two cannot disagree.
- `build_apworld.py` packs `manual_megabonk_ap/` into `manual_megabonk_ap.apworld`. The built file is not committed: releases are built from the source.
- Bump `world_version` in `manual_megabonk_ap/archipelago.json` on every change, and give the new `.apworld` to every player before generating: all players need the same one.

```
python make_data.py
python build_apworld.py
```

## Problems and questions

Open an issue on the [Issues page](https://github.com/bryarpatch36/MegaBonk-Archipelago/issues). For anything about the mod, attach `BepInEx/LogOutput.log`: [mod/README.md](mod/README.md#reporting-a-problem) says what to check in it before posting.

## Credits

- [Manual for Archipelago](https://github.com/ManualForArchipelago/Manual): the world template and the Manual Client.
- Game data: [Bloxodes Megabonk database](https://bloxodes.com/wiki/megabonk) (characters, weapons, tomes) and the megabonk.org map, Graveyard and challenge guides.
- The mod's own credits are in [mod/README.md](mod/README.md#credits-and-license).

## License

MIT: see [LICENSE](LICENSE). The world is built from the Manual for Archipelago template and the mod's zip includes two libraries, all MIT as well: see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). Megabonk belongs to its developer; no game code or assets are in this repository.
