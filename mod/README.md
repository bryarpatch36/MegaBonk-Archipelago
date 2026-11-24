# MegabonkAP: the mod

A BepInEx plugin for Megabonk that connects to the Manual world's slot and sends its checks as you play, so nothing has to be ticked by hand. It uses the same `.apworld` and YAML as the Manual game: the mod is a second client for the same slot, and the Manual Client can stay open next to it.

## Status

Version 0.4.1. Version 0.1.0 was run once on the real game (Megabonk 1.0.69, BepInEx 6.0.0-be.788 through r2modman, one Forest Tier 1 run): it loaded, every hook attached, it connected to the Archipelago server and read the map. It also showed three wrong guesses, which 0.2.0 corrects using the names the mod's own probe read from the game. 0.3.0 added locks (what you do not hold cannot be used) and an on-screen tracker; it was played on a ROG Ally: the tracker showed and the locks on characters and maps held, but level-ups still offered weapons and tomes that were not held. 0.3.1 to 0.3.4 fixed that and DeathLink, and all of it has since been played and reported working. 0.4.0 adds the miniboss hunt (15 checks for minibosses defeated in total, statue ones included; needs world 0.2.0), **which has not been run on the game yet.**

| Part | State |
|---|---|
| Which check belongs to which event, and whether it counts under the world's rules | Works: 159 automated tests |
| Connecting to Archipelago, sending checks, receiving items, the goal, keeping checks earned offline | Works against a local Archipelago 0.6.8 server (`tests/e2e_mod_core.py`), and connected from inside the game |
| The plugin loading in the game and its hooks attaching | Works (0.1.0: 9 of 9). 0.3.4 has 21 hooks |
| Reading the map of a run | Works on the real game |
| Reading the character, the tier, levels, bosses, run completion | Rewritten in 0.2.0 against the game's real names; tested against stand-in classes shaped like the game's; not yet seen on the game |
| DeathLink | Works both ways against a local Archipelago server. On the game (0.3.1, a seed with `death_link` on) neither direction worked; that run's log was not looked at. 0.3.3 no longer depends on the game calling `PlayerHealth.PlayerDied` and `PlayerHealth.Tick`: a few times a second it asks `PlayerHealth.IsDead()` and carries out a waiting kill, on the same per-frame turn the overlay uses. Not seen on the game. The overlay and the log say whether DeathLink is on, and why not |
| The miniboss hunt | On the game (0.4.0) minibosses counted only some of the time. One cause is fixed in 0.4.1: the game reuses its enemy objects, and a reused one was taken for a death already counted (this hit every enemy check, not only the hunt). 0.4.1 also counts any enemy the game flags `Boss` or `SummonerMiniboss`, or names `Miniboss...`, that is not a stage boss and not a hidden boss. Which flags statue minibosses carry has still not been seen: every boss death now writes `Boss death: <name>, flags <n>` to the log |
| Spooky Steve | Not recognised yet: the game's name for him is unknown |
| Locks on characters, maps and tiers | Work on the real game (0.3.0) |
| Locked weapons and tomes at a level-up | 0.3.0 tried to take them out of the offers and they were still offered; why is not known without that run's log. Since 0.3.1 they stay on screen, dimmed, and the pick is refused: this works on the real game. Until 0.3.4 a level-up whose offers were all locked let any be picked, which on the game happened with tomes once the held ones stopped being offered. 0.3.4 refuses those too: on such a level-up Skip, Refresh and Banish cost nothing, and picking any offer skips it. That part is tested against stand-ins, not seen on the game |
| The on-screen tracker | Works on the real game (0.3.0). Changing its view from a controller is new in 0.3.1 and not seen on the game |

## How it is put together

- `src/MegabonkAP.Core` holds everything that does not need the game: the Archipelago connection (`ArchipelagoLink`), the rules (`Tracker`), the names (`CheckList`, from `checks.json`, which `make_data.py` writes), and reading game objects by member name (`GameReader`, `Dyn`).
- `src/MegabonkAP.Plugin` is the BepInEx plugin. It contains no game types. At start it finds twenty-one game methods by name (`Hooks.cs`) and runs a little code after each, or before the few it may stop; that code hands the game's object to `GameReader`. The overlay is drawn the same way: `Screen` finds Unity's and TextMeshPro's classes by name.
- Because nothing is compiled against the game, a game update that renames a method costs one feature and a line in the log, not a mod that fails to load. The log names the missing member and lists what the object has instead.
- [docs/hooks.md](docs/hooks.md) lists every game name the mod relies on and what has been seen working. [docs/probes/](docs/probes/) keeps the probe of each game version looked at.

## Installing it with a mod manager

`MegabonkAP-<version>.zip` in the folder above is the whole mod in one file, in the layout mod managers read.

1. In r2modman, select Megabonk and a profile, then **Settings → Import local mod** and pick the zip. Gale has the same under **File → Import local mod**. BepInEx (`BepInExPack_IL2CPP`) is named as a dependency; install it from the Online tab if the manager does not add it.
2. If you installed an earlier build by hand, delete `BepInEx/plugins/MegabonkAP` in the profile folder first, or the mod loads twice.
3. Start the game once and close it, then set `Server` and `Slot` in the manager's config editor (`bbush.megabonk.archipelago.cfg`).

`python mod/make_package.py` builds the plugin and writes the zip again; run it after changing the mod.

## Building it on the PC that has the game

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
| `MegabonkAP 0.4.1 loaded: 21 of 21 hooks attached` | Every game method was found. Fewer: a `Hook missing:` line above it names each one and what the type has instead |
| `Connected to ... as ...: N of M checks done.` | The Archipelago side works |
| `Run started: Forest Tier 1` | The run's map and tier were read |
| `Playing as Fox.` | The character was read, a moment after the run started |
| `Check earned:` / `Check sent:` | The whole chain works |
| `Stage boss dead. Seen this run: 812 enemy deaths (3 bosses), player level 34, weapons and tomes: ...` | Written at every stage boss; `You died. Seen this run: ...` at every death. It shows which hooks are alive: zero deaths, an unknown level or no weapons mean that hook is not firing |
| `Boss killed on Forest: the game calls it '...'` | A boss the mod has no check for. For the tier bosses that is expected |
| `DeathLink is on.` / `DeathLink is off: this seed was generated with death_link off.` | Written after connecting. The overlay's first line says the same. DeathLink needs `death_link: true` in the YAML before the seed is generated |
| `You died. Telling the others.` / `DeathLink: the game's KillPlayer was called.` | DeathLink going out, and coming in. `KillPlayer left the player alive, so their health was set to zero` means the game's own method did not kill and the fallback was used |
| `Boss death: MinibossPig, flags 2.` | Every death the game flags as a boss of some kind, with its flags (2 boss, 4 stage boss, 16 called up by a summoner, 32 final boss), and `not a miniboss` when it is not counted for the hunt |
| `Miniboss defeated (MinibossPig): 7 in this seed, next check at 8.` | A miniboss was counted for the hunt. The count is kept in `MegabonkAP.state.json` and, should that be lost, taken up again from the checks the server has |
| `This run does not count: ...` | The rules refused the run (see `Strict` below); not an error |
| `Overlay: on screen.` | The tracker was drawn. `Overlay: ...` followed by a reason means it was not; after three failures it switches itself off and the game goes on |
| `Calcium is locked: you do not hold Character - Calcium.` | A lock refused a character, map or tier. The same line shows on the overlay |
| `Level-up weapons limited to the 3 of 14 you hold: ...` | Locked weapons (or tomes) were taken out of what the game may offer. Written when the outcome changes |
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

### What 0.3.0 adds to that run

7. **The overlay.** Expect `Overlay: on screen.` in the log and a dark panel at the left edge, in the menu and in a run. `F8` steps through compact, full and hidden.
8. **A locked character.** In the character menu, confirm one you do not hold: nothing should happen, and the overlay should say why. Then confirm the one you hold.
9. **A locked tier.** Try Forest Tier 2, or Desert, without the items. If the menu ends up stuck after a refusal, say where: the game is stopped at `MapController.StartNewMap`, and what the menu does then is not known.
10. **Level-up offers.** A weapon or tome you do not hold should look faded and do nothing when picked, with the reason on the overlay. When every offer is locked the overlay says `Every offer here is locked. Skip costs nothing, or pick any offer to skip.`: check that the game's Skip button is free and usable there, and that picking an offer skips the level-up. If a level-up ever cannot be left, set `Lock = false` and send the log.
11. **The controller.** Click both sticks at once: the overlay should step to its next view.

Locks and overlay can each be switched off in the settings if one misbehaves; the checks work without them.

## Settings

`BepInEx/config/bbush.megabonk.archipelago.cfg`, created on first start.

| Setting | Default | Does |
|---|---|---|
| `Server` | `archipelago.gg:38281` | Address and port of the room |
| `Slot` | empty | Your slot name. Nothing is sent while it is empty |
| `Password` | empty | The room's password, if any |
| `Strict` | on | A check only counts when you hold what the world's rules ask for: the character, the map tier, the weapon (or it is your character's own) or the tome. Off: everything counts |
| `Lock` | on | With `Strict` on: what you do not hold cannot be used. A locked character, map or tier cannot be started, and locked weapons and tomes cannot be picked at a level-up; when a level-up offers nothing else, skipping it is free. Nothing is locked until the server has said what you hold. Off: anything can be used, it just does not count |
| `DeathLink` | on | Follows the `death_link` option of your YAML. Off: never |
| `Mode` | `Compact` | The on-screen tracker: `Compact`, `Full` (lists everything you hold, green when its checks are done) or `Hidden`. The key steps through them and the choice is remembered |
| `Key` | `F8` | The key that changes the view, as a Unity key name. `None`: no key |
| `ControllerButtons` | `JoystickButton8+JoystickButton9` | Controller buttons that change the view when held together: by default clicking both sticks at once. Unity numbers an Xbox-style pad's buttons 0 A, 1 B, 2 X, 3 Y, 4 LB, 5 RB, 6 View, 7 Menu, 8 left stick, 9 right stick. `None`: no buttons |
| `Position` | `MiddleLeft` | `TopLeft`, `MiddleLeft`, `BottomLeft`, `TopRight`, `MiddleRight` or `BottomRight` |
| `FontSize`, `Width` | 20, 520 | Size of the text and width of the panel, as on a 1080p screen; both grow with the screen. On a handheld's small screen a larger `FontSize` (26 or so) may read better |
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
