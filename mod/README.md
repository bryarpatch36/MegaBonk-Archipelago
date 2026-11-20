# MegabonkAP: the mod

A BepInEx plugin for Megabonk that connects to the Manual world's slot and sends its checks as you play, so nothing has to be ticked by hand. It uses the same `.apworld` and YAML as the Manual game: the mod is a second client for the same slot, and the Manual Client can stay open next to it.

## Status

Built on a PC without Megabonk. **The plugin has never been loaded into the game.**

| Part | State |
|---|---|
| Which check belongs to which event, and whether it counts under the world's rules | Works: 74 automated tests |
| Connecting to Archipelago, sending checks, receiving items, the goal, keeping checks earned offline | Works against a local Archipelago 0.6.8 server (`tests/e2e_mod_core.py`) |
| Reading the game's objects (run settings, enemies, weapon and tome levels) | Works against stand-in classes that carry the game's member names; the names themselves come from other mods' source and are unconfirmed |
| The plugin loading in the game and its hooks attaching | Compiles; not yet tried |
| DeathLink | Sending is built; on receiving one the mod only writes a line to the log |
| Showing anything on screen, blocking a character or weapon you do not hold | Not built |

## How it is put together

- `src/MegabonkAP.Core` holds everything that does not need the game: the Archipelago connection (`ArchipelagoLink`), the rules (`Tracker`), the names (`CheckList`, from `checks.json`, which `make_data.py` writes), and reading game objects by member name (`GameReader`, `Dyn`).
- `src/MegabonkAP.Plugin` is the BepInEx plugin. It contains no game types. At start it finds nine game methods by name (`Hooks.cs`) and runs a little code after each; that code hands the game's object to `GameReader`.
- Because nothing is compiled against the game, a game update that renames a method costs one feature and a line in the log, not a mod that fails to load. The log names the missing member and lists what the object has instead.
- [docs/hooks.md](docs/hooks.md) lists every game name the mod relies on and where it comes from.

## First session on the PC that has the game

What you need there: Megabonk (Steam, Windows), the [.NET SDK](https://dotnet.microsoft.com/download) 8 or newer, and this folder.

1. **Back up your save and consider the leaderboard.** Megabonk has online leaderboards, and community guides say modded play can get scores flagged. Check the game's own setting for uploading scores before playing modded.
2. **Install BepInEx 6 for IL2CPP** into the game folder (the folder with `Megabonk.exe`). The Megabonk community uses the bleeding-edge build, packaged on Thunderstore as `BepInExPack_IL2CPP`; mod managers (r2modman, Gale) install it for you into a profile.
3. **Start the game once and close it.** BepInEx generates `BepInEx/interop/Assembly-CSharp.dll` on that first start, which takes a few minutes.
4. **Build the plugin into the game:**

   ```
   dotnet build mod/src/MegabonkAP.Plugin -c Release -p:MegabonkPath="C:\path\to\Megabonk"
   ```

   This copies four files to `BepInEx/plugins/MegabonkAP/`. With a mod manager, point `MegabonkPath` at the profile folder that holds `BepInEx`.
5. **Start the game once more and close it**, so the settings file appears, then set your slot in `BepInEx/config/bbush.megabonk.archipelago.cfg`:

   ```
   Server = archipelago.gg:38281
   Slot = Bonker
   ```
6. **Start the game and play one Forest Tier 1 run** to the boss, with a seed running.
7. **Collect two files** from the `BepInEx` folder: `LogOutput.log` and `MegabonkAP-probe.txt`. Lines from the mod start with `[Info :MegabonkAP]` or `[Warning:MegabonkAP]`.

### What the log should say, and what each line means

| Line | Meaning |
|---|---|
| `MegabonkAP 0.1.0 loaded: 9 of 9 hooks attached` | Every game method was found. Fewer: a `Hook missing:` line above it names each one and what the type has instead |
| `Connected to ... as ...: N of M checks done.` | The Archipelago side works |
| `Run started: Fox on Forest Tier 1` | The run's character, map and tier were read. `unknown character`, `unknown map` or no tier: a `Could not read ...` line says which member was missing |
| `Stage boss killed on Forest: the game calls it '...'` | Deaths are seen and the boss flag is read |
| `Check earned:` / `Check sent:` | The whole chain works |
| `This run does not count: ...` | The rules refused the run (see `Strict` below); not an error |
| `The game named a weapon '...' that matches nothing` | An alias is missing: add it to `ALIASES` in `make_data.py` |

### Known unknowns, in the order to settle them

1. **Do the hooks attach at all?** The plugin patches by name with `object`-typed parameters (`__instance`, `__args`). That is standard Harmony, but untried under BepInEx IL2CPP here.
2. **The tier.** Assumed to be the position of `RunConfig.stageData` in `RunConfig.mapData.stages`. If the log shows the wrong tier, the probe's `RunConfig`, `MapData` and `StageData` sections show what to read instead.
3. **The character.** Tried on the run settings, then on `GameManager.Instance.player`. The probe's `RunConfig`, `MyPlayer` and `CharacterData` sections settle it.
4. **When a run is won.** Assumed: when as many stage bosses have died as the tier's number (and Big Bob on the Graveyard). If the game has a real "run completed" method, the probe's last section (types named win, victory, complete, ...) will show it, and it should replace the counting.
5. **Enemy names.** Minibosses are matched by a word in the game's name (`pig`, `golem`, `scorp`, `bush`, `bandit`, `bob`, `steve`). `EEnemy` in the probe has the real list.
6. **Weapon and tome levels.** Read from `weaponInventory.weapons` and `tomeInventory.tomeLevels`. Whether a freshly picked weapon is level 0 or 1 decides whether "Level 10" is off by one.
7. **Player level.** Read as `level` on `PlayerXp`.
8. **Challenges.** The challenge of a run is guessed at `RunConfig.challengeData`; nothing confirms it.
9. **Crypt Keys.** The check is sent when `GraveyardBossRoom.Activate` runs, assumed to need all four keys.
10. **Performance.** The enemy-death hook runs for every kill and reads two members by name. If the frame rate drops in a late-game horde, that is the place.

After those: receiving a DeathLink (ending the run), messages on screen, and refusing characters, weapons and tomes you do not hold (the game's Toggler has a `WeaponInventory.ToggleWeapon` to look at).

## Settings

`BepInEx/config/bbush.megabonk.archipelago.cfg`, created on first start.

| Setting | Default | Does |
|---|---|---|
| `Server` | `archipelago.gg:38281` | Address and port of the room |
| `Slot` | empty | Your slot name. Nothing is sent while it is empty |
| `Password` | empty | The room's password, if any |
| `Strict` | on | A check only counts when you hold what the world's rules ask for: the character, the map tier, the weapon (or it is your character's own) or the tome. Off: everything counts |
| `DeathLink` | on | Follows the `death_link` option of your YAML. Off: never |
| `Probe` | on | Writes `BepInEx/MegabonkAP-probe.txt` at every start: the names in the game's code. It calls and changes nothing |

Checks earned while the server is unreachable are kept in `BepInEx/config/MegabonkAP.state.json` and sent on the next connection to the same seed and slot.

## Building and testing without the game

```
dotnet build mod/MegabonkAP.sln -c Release          # plugin files land in mod/dist/MegabonkAP/
dotnet test  mod/tests/MegabonkAP.Tests             # rules, names, reading stand-in game objects
python tests/e2e_mod_core.py <archipelago source>   # the mod's core against a local Archipelago server
```

`checks.json` is written by `make_data.py` in the folder above; change names there, never in the JSON. A test fails if the mod and the world disagree on a single check or item name.

## Credits

- [Archipelago.MultiClient.Net](https://github.com/ArchipelagoMW/Archipelago.MultiClient.Net) for the connection, [BepInEx](https://github.com/BepInEx/BepInEx) and HarmonyX for loading and hooking.
- The game's method and member names were learned from the source of [megabonk-together](https://github.com/Fcornaire/megabonk-together) (GPL-2.0) and [Bonkipelago](https://github.com/ashulgach/Bonkipelago), an earlier Archipelago prototype, and the weapon list from [megabonk_research](https://github.com/lukeod/megabonk_research) (MIT). No code was taken from them.
