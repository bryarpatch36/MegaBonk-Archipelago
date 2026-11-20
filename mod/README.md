# MegabonkAP: the mod

A BepInEx plugin for Megabonk that connects to the Manual world's slot and sends its checks as you play, so nothing has to be ticked by hand. It uses the same `.apworld` and YAML as the Manual game: the mod is a second client for the same slot, and the Manual Client can stay open next to it.

## Status

Version 0.2.0. Version 0.1.0 was run once on the real game (Megabonk 1.0.69, BepInEx 6.0.0-be.788 through r2modman, one Forest Tier 1 run): it loaded, every hook attached, it connected to the Archipelago server and read the map. It also showed three wrong guesses, which 0.2.0 corrects using the names the mod's own probe read from the game. **0.2.0 itself has not been run on the game yet.**

| Part | State |
|---|---|
| Which check belongs to which event, and whether it counts under the world's rules | Works: 95 automated tests |
| Connecting to Archipelago, sending checks, receiving items, the goal, keeping checks earned offline | Works against a local Archipelago 0.6.8 server (`tests/e2e_mod_core.py`), and connected from inside the game |
| The plugin loading in the game and its hooks attaching | Works (0.1.0: 9 of 9). 0.2.0 has 14 hooks, five of them new |
| Reading the map of a run | Works on the real game |
| Reading the character, the tier, levels, bosses, run completion | Rewritten in 0.2.0 against the game's real names; tested against stand-in classes shaped like the game's; not yet seen on the game |
| DeathLink | Sending and receiving are built (receiving calls the game's own `KillPlayer`); neither seen on the game |
| Spooky Steve | Not recognised yet: the game's name for him is unknown |
| Showing anything on screen, blocking a character or weapon you do not hold | Not built |

## How it is put together

- `src/MegabonkAP.Core` holds everything that does not need the game: the Archipelago connection (`ArchipelagoLink`), the rules (`Tracker`), the names (`CheckList`, from `checks.json`, which `make_data.py` writes), and reading game objects by member name (`GameReader`, `Dyn`).
- `src/MegabonkAP.Plugin` is the BepInEx plugin. It contains no game types. At start it finds fourteen game methods by name (`Hooks.cs`) and runs a little code after each; that code hands the game's object to `GameReader`.
- Because nothing is compiled against the game, a game update that renames a method costs one feature and a line in the log, not a mod that fails to load. The log names the missing member and lists what the object has instead.
- [docs/hooks.md](docs/hooks.md) lists every game name the mod relies on and what has been seen working. [docs/probes/](docs/probes/) keeps the probe of each game version looked at.

## Installing it on the PC that has the game

What you need there: Megabonk (Steam, Windows), the [.NET SDK](https://dotnet.microsoft.com/download) 8 or newer, and this folder.

1. **Back up your save and consider the leaderboard.** Megabonk has online leaderboards, and community guides say modded play can get scores flagged. Check the game's own setting for uploading scores before playing modded.
2. **Install BepInEx 6 for IL2CPP.** The Megabonk community uses the bleeding-edge build, packaged on Thunderstore as `BepInExPack_IL2CPP`; a mod manager (r2modman, Gale) installs it into a profile.
3. **Start the game once and close it.** BepInEx generates `BepInEx/interop/Assembly-CSharp.dll` on that first start, which takes a few minutes.
4. **Build the plugin into the game**, pointing `MegabonkPath` at the folder that holds `BepInEx` (with r2modman that is the profile folder, for example `%AppData%\r2modmanPlus-local\Megabonk\profiles\Default`):

   ```
   dotnet build mod/src/MegabonkAP.Plugin -c Release -p:MegabonkPath="C:\path\to\folder-with-BepInEx"
   ```

   This copies four files to `BepInEx/plugins/MegabonkAP/`. Do the same after every `git pull`.
5. **Start the game once more and close it**, so the settings file appears, then set your slot in `BepInEx/config/bbush.megabonk.archipelago.cfg`:

   ```
   Server = archipelago.gg:38281
   Slot = Bonker
   ```
6. **Play**, with a seed running. Lines from the mod in `BepInEx/LogOutput.log` start with `[Info :MegabonkAP]` or `[Warning:MegabonkAP]`.

### What the log should say, and what each line means

| Line | Meaning |
|---|---|
| `MegabonkAP 0.2.0 loaded: 14 of 14 hooks attached` | Every game method was found. Fewer: a `Hook missing:` line above it names each one and what the type has instead |
| `Connected to ... as ...: N of M checks done.` | The Archipelago side works |
| `Run started: Forest Tier 1` | The run's map and tier were read |
| `Playing as Fox.` | The character was read, a moment after the run started |
| `Check earned:` / `Check sent:` | The whole chain works |
| `Stage boss dead. Seen this run: 812 enemy deaths (3 bosses), player level 34, weapons and tomes: ...` | Written at every stage boss; `You died. Seen this run: ...` at every death. It shows which hooks are alive: zero deaths, an unknown level or no weapons mean that hook is not firing |
| `Boss killed on Forest: the game calls it '...'` | A boss the mod has no check for. For the tier bosses that is expected |
| `This run does not count: ...` | The rules refused the run (see `Strict` below); not an error |
| `Could not read ... from X. It has: ...` | A member is missing or renamed; the list after it is what the object has |
| `The game named a weapon '...' that matches nothing` | An alias is missing: add it to `ALIASES` in `make_data.py` |

### What the next run should settle

Play a Forest Tier 1 run to the boss as the character the seed started you with, then send `LogOutput.log`.

1. **The character.** Now taken from `MyPlayer.StartPlayer`. Expect `Playing as <name>.`
2. **The tier.** Now `RunConfig.mapTierIndex`, assumed to count from 0. If a Tier 1 run is logged as Tier 2, it counts from 1.
3. **Run completion.** Now the game's own `GameManager.OnStageBossDied` together with `MapController.IsTierFinalStage()`. Expect `Check earned: Forest - Complete Tier 1 (Lil Bark)` and `<character> - Complete a Run` when the boss dies.
4. **Levels.** Whether a freshly picked weapon or tome is level 0 or 1 decides whether "Level 10" is off by one. The summary line shows the levels the mod reads; compare them with the game's screen.
5. **Enemy deaths.** The summary's death count should be in the hundreds, and a Stone Golem or Chunkham kill should earn its check.
6. **Performance.** The enemy-death hook runs for every kill and a health hook runs every frame. If the frame rate drops in a late-game horde, say so.

Still to do after that, each needing its own run: a Tier 2 run (the tier is not finished at the first boss), the Graveyard (Crypt Keys, Big Bob, and what the game calls Spooky Steve), a challenge, and DeathLink between two players.

Not started: messages on screen, and refusing characters, weapons and tomes you do not hold (the game has `WeaponInventory.ToggleWeapon` and an Unlocks screen to look at).

## Settings

`BepInEx/config/bbush.megabonk.archipelago.cfg`, created on first start.

| Setting | Default | Does |
|---|---|---|
| `Server` | `archipelago.gg:38281` | Address and port of the room |
| `Slot` | empty | Your slot name. Nothing is sent while it is empty |
| `Password` | empty | The room's password, if any |
| `Strict` | on | A check only counts when you hold what the world's rules ask for: the character, the map tier, the weapon (or it is your character's own) or the tome. Off: everything counts |
| `DeathLink` | on | Follows the `death_link` option of your YAML. Off: never |
| `Probe` | on | Writes `BepInEx/MegabonkAP-probe.txt` at every start: the names in the game's code. It calls and changes nothing. Worth sending along after a game update |

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
- The first version's guesses at the game's names came from the source of [megabonk-together](https://github.com/Fcornaire/megabonk-together) (GPL-2.0), [Bonkipelago](https://github.com/ashulgach/Bonkipelago) and [megabonk_research](https://github.com/lukeod/megabonk_research) (MIT); no code was taken from them. Since 0.2.0 the names come from the mod's own probe of the game.
