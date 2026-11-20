# The game names the mod relies on

Every name below is looked up at run time. None has been seen in the game by this project: they come from other mods' source code, read in November 2026. "Assumed" means no source shows it and it is a guess to confirm with the probe.

Sources:

- **MT**: [Fcornaire/megabonk-together](https://github.com/Fcornaire/megabonk-together), a netplay mod for BepInEx IL2CPP, last updated 2026-10-31. The most recent and the closest to how this mod loads.
- **BK**: [ashulgach/Bonkipelago](https://github.com/ashulgach/Bonkipelago), a MelonLoader prototype from 2025-10.
- **MR**: [lukeod/megabonk_research](https://github.com/lukeod/megabonk_research), notes from decompiled code, validated 2026-01-28.

## Methods hooked (`Hooks.cs`)

All are postfixes: they run after the game's method and change nothing.

| Method | Used for | Source |
|---|---|---|
| `MapController.StartNewMap(RunConfig)` | A run begins: character, map, tier | MT |
| `TrackStats.OnEnemyDied(Enemy, DamageContainer)` | Every enemy death: bosses, tier completion | MT |
| `Enemy.EnemyDied()` | Only if `TrackStats.OnEnemyDied` is missing | MT, BK |
| `PlayerXp.AddXp(int)` | The player's level | MT |
| `WeaponInventory.AddWeapon(WeaponData, List<StatModifier>)` | Weapon levels | MT |
| `TomeInventory.AddTome(TomeData, ...)` | Tome levels | MT |
| `LevelupScreen.OnClose()` | Weapon and tome levels again after each level-up | MT |
| `PlayerHealth.PlayerDied()` | Sending DeathLink | MT |
| `GraveyardBossRoom.Activate()` | The Crypt Keys check | MT (that it needs all four keys is assumed) |
| `GameManager.StopDungeon()` | A run is over | MT |

## Members read (`GameReader.cs`)

| From | Path | Used for | Source |
|---|---|---|---|
| `RunConfig` | `mapData.eMap` | The map: `EMap` has `None`, `Forest`, `Desert`, `Graveyard`, `Hell` | MT |
| `RunConfig` | `stageData`, `mapData.stages` | The tier: the position of the stage in the list | MT reads both; that this position is the tier is assumed |
| `RunConfig` | `characterData.eCharacter` and variants | The character | Assumed. `characterData.eCharacter` exists on the character menu's buttons (MT) |
| `RunConfig` | `challengeData` and variants | The challenge of the run | Assumed |
| `GameManager` | `Instance.player` | The player object | MT |
| player | `inventory.weaponInventory.weapons` | Dictionary of `EWeapon` to a weapon with `level` | MT |
| player | `inventory.tomeInventory.tomeLevels` | Dictionary of `ETome` to a level | MT |
| player | `characterData.eCharacter` and variants | The character, when the run settings do not have it | Assumed |
| `PlayerXp` | `level` and variants | The player's level | Assumed |
| `Enemy` | `enemyData.enemyName` | An `EEnemy` value, for example `MinibossPig`, `MinibossGolem`, `GhostGrave1` | MT |
| `Enemy` | `IsStageBoss()`, `IsFinalBoss()` | Whether this death ends a stage | MT |
| `Enemy` | `enemyFlag` | Only if those two methods are missing: `EEnemyFlag` with `StageBoss = 4`, `FinalBoss = 32` | MR |
| any game object | `Pointer` | Telling two wrappers of one object apart from two objects | Il2CppInterop |

## Names matched (`make_data.py`, `ALIASES` and `BOSS_WORDS`)

- **Weapons**: `EWeapon` from MR. All 30 resolve (a test checks this); five differ from the names players see: `Sniper`, `Rockets`, `Mine`, `CorruptSword`, `BluetoothDagger`.
- **Tomes**: `ETome` is only known to contain `Chaos` (MT). The aliases `Hp`, `Gold`, `Curse`, `Blood`, `Crit` are guesses.
- **Characters**: `ECharacter` values are unknown. Names are compared without case, spaces or punctuation, so `SirOofie` and `TonyMcZoom` resolve; `Clank` is a guessed alias for CL4NK.
- **Bosses**: matched by a word inside the `EEnemy` name, per map. `MinibossPig` and `MinibossGolem` are from MT; that the pig is Chunkham, that the Desert's golem shares the name, and the words for Scorpionussy, the Bush, the Bandit, Big Bob and Spooky Steve are assumed.

## What the probe is for

`BepInEx/MegabonkAP-probe.txt` lists, from the installed game: the members of every type in the tables above, the values of `EMap`, `ECharacter`, `EWeapon`, `ETome`, `EEnemy` and `EEnemyFlag`, and every type whose name contains win, victory, complete, gameover, challenge, tier, toggl, unlock, crypt or key. One read of that file settles most of the "assumed" entries here.
