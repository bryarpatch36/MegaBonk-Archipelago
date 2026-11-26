# MegabonkAP: the mod

A BepInEx plugin for Megabonk that connects to the Manual world's slot and sends its checks as you play, so nothing has to be ticked by hand. It also locks what you do not hold yet, springs the traps you are sent and shows a tracker on screen. It uses the same `.apworld` and YAML as the Manual game: the mod is a second client for the same slot, and the Manual Client can stay open next to it.

This is an unofficial fan project. It is not affiliated with or endorsed by vedinad or the Archipelago project.

## Status

Version 0.6.0. It has been played on the real game up to version 0.4.1. The game version it was written against is 1.0.69, with BepInEx 6.0.0-be.788 installed through r2modman. 0.5.0 changes names only: the game ID, the plugin ID and the settings file. 0.6.0 adds the traps and moving the tracker; neither has been played on the real game yet.

| Part | State |
|---|---|
| Connecting to Archipelago, sending checks, receiving items, the goal, keeping checks earned offline | Works against a local Archipelago 0.6.8 server and from inside the game |
| The plugin loading in the game and its hooks attaching | Works on the real game |
| Checks sent from a run, including completing a tier beyond Tier 1 | Works on the real game |
| Locks on characters, maps and tiers | Work on the real game |
| Locked weapons and tomes at a level-up | They stay on screen, dimmed, and the pick is refused: works on the real game. When every offer is locked, Skip, Refresh and Banish cost nothing and picking any offer skips the level-up: not confirmed on the game |
| The on-screen tracker | Works on the real game. Changing its view from a controller is not confirmed on the game |
| Moving the tracker (0.6.0): the move key, and a place of its own in the menus | Not confirmed on the game. The key is read the same way as the view key, which works there |
| Traps (0.6.0) | Receiving them and carrying each out exactly once, also after a restart and on another computer: works against a local Archipelago 0.6.8 server. What each does in the game is not confirmed on the game: see [Traps](#traps) |
| DeathLink | Works on the real game (0.3.3 and later) |
| The miniboss hunt | Works on the real game (0.4.1) |
| Graveyard checks (the Crypt Keys, Big Bob) | Not confirmed on the game |
| Spooky Steve | Not recognised yet: the game's name for him is unknown |
| Challenge checks | Not confirmed on the game |
| Weapon, tome and character levels | Whether the level the mod reads matches the level the game shows (a freshly picked weapon or tome may count from 0 or from 1) is not confirmed. The summary line in the log shows the levels the mod reads |

## Installing it with a mod manager

`MegabonkAP-<version>.zip` on the [Releases page](https://github.com/bryarpatch36/MegaBonk-Archipelago/releases) is the whole mod in one file, in the layout mod managers read. You also need the world's `.apworld` from the same page: see the [main README](../README.md).

1. **Back up your save and consider the leaderboard.** Megabonk has online leaderboards, and community guides say modded play can get scores flagged. Check the game's own setting for uploading scores before playing modded.
2. In r2modman, select Megabonk and a profile, then **Settings → Import local mod** and pick the zip. Gale has the same under **File → Import local mod**. BepInEx (`BepInExPack_IL2CPP`) is named as a dependency; install it from the Online tab if the manager does not add it.
3. Start the game once and close it. BepInEx sets itself up on that first start, which takes a few minutes, and the mod's settings file appears.
4. Set `Server` and `Slot` in the manager's config editor (`megabonk.archipelago.cfg`).
5. Start the game with the room running.

### Installing it by hand

A mod manager is not required. The mod is four `.dll` files that BepInEx loads from its `plugins` folder.

1. Install BepInEx 6 for IL2CPP into the game folder (the Megabonk community uses the bleeding-edge build, packaged on Thunderstore as `BepInExPack_IL2CPP`), start the game once and close it.
2. Unpack the zip and copy its four `.dll` files (`MegabonkAP.dll`, `MegabonkAP.Core.dll`, `Archipelago.MultiClient.Net.dll`, `Newtonsoft.Json.dll`) into a new folder `BepInEx/plugins/MegabonkAP/`. The other files in the zip are for mod managers and are not needed.
3. Start the game once more and close it, so the settings file appears, then set `Server` and `Slot` in `BepInEx/config/megabonk.archipelago.cfg` with a text editor:

   ```
   Server = archipelago.gg:38281
   Slot = Bonker
   ```

Install it one way or the other, not both, or the mod loads twice. The runs recorded in the status table above were made through r2modman; a by-hand install on a BepInEx set up without a mod manager uses the same files but has not been tried.

## Settings

`BepInEx/config/megabonk.archipelago.cfg`, created on first start.

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
| `Position` | `MiddleLeft` | Where the tracker sits during a run: `TopLeft`, `TopCenter`, `TopRight`, `MiddleLeft`, `MiddleRight`, `BottomLeft`, `BottomCenter` or `BottomRight` |
| `MenuPosition` | `TopCenter` | Where it sits in the menus, the character select included: the same names. A run and the menus each have their own place, because what is free in one is covered in the other. Which part of the character select is free was not looked up, so this default is a guess: the move key puts it right |
| `MoveKey` | `F7` | The key that moves the tracker to the next place, round the screen's edge. During a run it changes `Position`, in a menu `MenuPosition`; the choice is remembered. `None`: no key |
| `MoveControllerButtons` | `JoystickButton8+JoystickButton4` | Controller buttons that move the tracker when held together: by default clicking the left stick while holding the left bumper. `None`: no buttons |
| `FontSize`, `Width` | 20, 520 | Size of the text and width of the panel, as on a 1080p screen; both grow with the screen. On a handheld's small screen a larger `FontSize` (26 or so) may read better |
| `Probe` | on | Writes `BepInEx/MegabonkAP-probe.txt` at every start: the names in the game's code. It calls and changes nothing. Worth attaching to a report after a game update |

Checks earned while the server is unreachable are kept in `BepInEx/config/MegabonkAP.state.json` and sent on the next connection to the same seed and slot.

## Traps

A seed generated with `traps: true` (the default) has 12 trap items. When one arrives, the mod asks the game to do something to your run:

| Item | In the pool | What the mod asks the game for |
|---|---|---|
| `Trap - Miniboss` | 2 | One of the stage's own minibosses, a few steps from you (`EnemyManager.SpawnBoss`). Beating it counts like any other miniboss: for the miniboss hunt, and for its own check if it has one |
| `Trap - Swarm` | 3 | The swarm the stage has on its own timeline, now (`SummonerController.EventSwarm`). On a stage that has none, or while the game is holding its events back, 20 of the stage's enemies instead |
| `Trap - Elite Pack` | 2 | 6 of the stage's enemies, asked for as elites and placed by the game as it places its own (`EnemyManager.SpawnEnemy`) |
| `Trap - Poison` | 2 | Poison for 8 seconds (`PlayerStatusEffects.PoisonPlayer`) |
| `Trap - Slow` | 2 | Slowness for 10 seconds (`PlayerStatusEffects.SlowPlayer`) |
| `Trap - Freeze` | 1 | Frozen in place for 3 seconds (`PlayerStatusEffects.FreezePlayer`) |

- A trap needs a run with a living player. It arrives once the run has been going for 10 seconds, and never while the game is paused or a level-up is on screen. One that arrives in a menu waits for your next run: the tracker shows `Traps waiting: 2`.
- Traps come one at a time, in the order they arrived, at least 15 seconds apart.
- Each is carried out exactly once. Traps are numbered by their place among the items your slot has received; the highest number dealt with is kept in `MegabonkAP.state.json` and on the Archipelago server, so neither restarting the game nor playing on another computer brings one back.
- A trap the game cannot be made to carry out is spent all the same, and the log says which game name was missing.
- A trap can kill you, and with DeathLink on that death is sent to the others.

None of this has been seen in the game yet. The names and signatures of the six game methods are real: they were read from a reference copy of the game's interface (see [docs/hooks.md](docs/hooks.md)). What is not known until someone plays it: that the three durations are in seconds, how hard the game's poison hits, whether `SpawnEnemy` honours the elite flag, and how a miniboss placed this way behaves. A `LogOutput.log` from a run with traps settles it.

Locks and overlay can each be switched off if one misbehaves; the checks work without them. If a level-up ever cannot be left, set `Lock = false`.

## What the log says

Lines from the mod in `BepInEx/LogOutput.log` start with `[Info :MegabonkAP]` or `[Warning:MegabonkAP]`.

| Line | Meaning |
|---|---|
| `MegabonkAP 0.6.0 loaded: 21 of 21 hooks attached` | Every game method was found. Fewer: a `Hook missing:` line above it names each one and what the type has instead |
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
| `Trap sprung: Trap - Swarm.` | A trap was carried out: the game's method ran without an error. `Trap: the game placed a MinibossPig.` names the miniboss of a Miniboss trap |
| `Trap: this stage has no swarm on its timeline, so a crowd of 20 was called up instead.` | A Swarm trap fell back to placing enemies itself |
| `Trap - Slow did nothing: ...` | The game could not be made to do it; what follows names the missing method or member and lists what the object has. The trap is spent |
| `Trap - Slow has been waiting for over a minute: ...` | Written once, with the reason: during a run the game stayed paused, a level-up stayed open, or the player stayed dead. In the menus a trap waits without a word |
| `The server did not say which traps are already done; going by this computer's own record.` | The server's record could not be read; traps still work from `MegabonkAP.state.json` |
| `This run does not count: ...` | The rules refused the run (see `Strict` above); not an error |
| `Overlay: on screen.` | The tracker was drawn. `Overlay: ...` followed by a reason means it was not; after three failures it switches itself off and the game goes on |
| `Calcium is locked: you do not hold Character - Calcium.` | A lock refused a character, map or tier. The same line shows on the overlay |
| `Level-up weapons limited to the 3 of 14 you hold: ...` | Locked weapons (or tomes) were taken out of what the game may offer. Written when the outcome changes |
| `Could not read ... from X. It has: ...` | A member is missing or renamed; the list after it is what the object has |
| `The game named a weapon '...' that matches nothing` | An alias is missing: add it to `ALIASES` in `make_data.py` |

## Reporting a problem

Open an issue on the [Issues page](https://github.com/bryarpatch36/MegaBonk-Archipelago/issues) and attach `BepInEx/LogOutput.log` from the session that went wrong. Say which version of the game and of the mod you played, and what you expected to happen. After a game update, attach `BepInEx/MegabonkAP-probe.txt` as well.

The log names your server address and slot name (the `Connected to ... as ...` line). Look the file over and take out anything you would rather not post.

Reports of the unconfirmed parts in the status table are as welcome as bug reports: a log from a Graveyard run or a challenge run settles whether those checks work.

## Building it yourself

What you need: the [.NET SDK](https://dotnet.microsoft.com/download) 8 or newer and this repository. The game is not needed to build.

```
dotnet build mod/MegabonkAP.sln -c Release          # plugin files land in mod/dist/MegabonkAP/
python mod/make_package.py                          # builds the plugin and writes MegabonkAP-<version>.zip
```

To build straight into an installed game, point `MegabonkPath` at the folder that holds `BepInEx` (with r2modman that is the profile folder, for example `%AppData%\r2modmanPlus-local\Megabonk\profiles\Default`). BepInEx 6 for IL2CPP must already be installed there and the game started once:

```
dotnet build mod/src/MegabonkAP.Plugin -c Release -p:MegabonkPath="C:\path\to\folder-with-BepInEx"
```

This copies four files to `BepInEx/plugins/MegabonkAP/`.

`checks.json` is written by `make_data.py` in the folder above; change names there, never in the JSON. Both are written from the same lists, so the mod and the world cannot disagree on a check or item name.

## How it is put together

- `src/MegabonkAP.Core` holds everything that does not need the game: the Archipelago connection (`ArchipelagoLink`), the rules (`Tracker`), the names (`CheckList`, from `checks.json`, which `make_data.py` writes), and reading game objects by member name (`GameReader`, `Dyn`).
- `src/MegabonkAP.Plugin` is the BepInEx plugin. It contains no game types. At start it finds twenty-one game methods by name (`Hooks.cs`) and runs a little code after each, or before the few it may stop; that code hands the game's object to `GameReader`. The overlay is drawn the same way: `Screen` finds Unity's and TextMeshPro's classes by name.
- Because nothing is compiled against the game, a game update that renames a method costs one feature and a line in the log, not a mod that fails to load. The log names the missing member and lists what the object has instead.
- [docs/hooks.md](docs/hooks.md) lists every game name the mod relies on.

## Credits and license

- [Archipelago.MultiClient.Net](https://github.com/ArchipelagoMW/Archipelago.MultiClient.Net) for the connection, [BepInEx](https://github.com/BepInEx/BepInEx) and HarmonyX for loading and hooking.
- The first version's guesses at the game's names came from reading the source of [megabonk-together](https://github.com/Fcornaire/megabonk-together) (GPL-2.0), [Bonkipelago](https://github.com/ashulgach/Bonkipelago) and [megabonk_research](https://github.com/lukeod/megabonk_research) (MIT); no code was taken from them. Since 0.2.0 the names come from the mod's own probe of the game. The names the traps use (0.6.0) were read from the reference copy of the game's interface that megabonk-together builds against, and from how megabonk-together calls two of them; again no code was taken.
- The traps follow those of the author's [Left 4 Dead 2 world](https://github.com/bryarpatch36/Left4Dead2-Archipelago): a handful of kinds, 12 in the pool, each carried out exactly once.
- MIT License: see [LICENSE](../LICENSE) and [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md).
