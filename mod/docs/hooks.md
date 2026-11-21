# The game names the mod relies on

Every name below is looked up at run time. They are the names of **game version 1.0.69**, taken from [probes/probe-1.0.69.txt](probes/probe-1.0.69.txt), which the mod itself wrote from the installed game on 2026-11-20. So each name is known to exist. What is not yet known for most of them is whether the mod's *use* of it is right: the last column says what has been seen on the real game.

The first run on the real game (mod 0.1.0, one Forest Tier 1 run) showed: all hooks of 0.1.0 attached, the Archipelago connection worked, and the map was read. It also showed three wrong guesses, corrected in 0.2.0: the character is not in the run settings, the tier has its own field, and `GameManager.StopDungeon` is about crypts, not the end of a run.

## Methods hooked (`Hooks.cs`)

All run after the game's method. Only `PlayerHealth.Tick` ever changes anything, and only when a DeathLink has arrived.

| Method | Used for | Seen on the real game |
|---|---|---|
| `MapController.StartNewMap(RunConfig)` | A run begins: map, tier, challenge | Attaches and fires; the map was read |
| `MapController.RestartRun()` | The same run again | New in 0.2.0 |
| `MyPlayer.StartPlayer(ECharacter, Vector3)` | The character being played | New in 0.2.0 |
| `TrackStats.OnEnemyDied(Enemy, DamageContainer)` | Every enemy death: minibosses, hidden bosses | Attaches; no death was confirmed in the log |
| `GameManager.OnStageBossDied()` | A stage's boss is dead; with `MapController.IsTierFinalStage()` that completes the tier and the run | New in 0.2.0 |
| `PlayerXp.AddXp(int)` | The player's level | Attaches; unconfirmed |
| `WeaponInventory.AddWeapon(WeaponData, List<StatModifier>)` | Weapon levels | Attaches and fires |
| `TomeInventory.AddTome(TomeData, List<StatModifier>, ERarity)` | Tome levels | Attaches; unconfirmed |
| `LevelupScreen.OnClose()` | Weapon and tome levels again after each level-up | Attaches; unconfirmed |
| `PlayerHealth.PlayerDied()` | Sending DeathLink, and the run summary line | Attaches; unconfirmed |
| `PlayerHealth.Tick()` | On a received DeathLink, calls the game's `PlayerHealth.KillPlayer()` | New in 0.2.0 |
| `GraveyardBossRoom.Activate()` | The Crypt Keys check (that activating takes all four keys is assumed) | Attaches; unconfirmed |
| `GraveyardBossRoom.OnEnemyDied(Enemy, DamageContainer)` | Big Bob: the dead enemy is the room's own `bossEnemy` | New in 0.2.0 |
| `ChallengesTracker.CompleteChallenge()` | Challenge checks | New in 0.2.0 |
| `Enemy.EnemyDied()` | Only if `TrackStats.OnEnemyDied` is missing | Not used |

## Members read (`GameReader.cs`)

| From | Member | Used for |
|---|---|---|
| `RunConfig` | `mapData.eMap` | The map: `EMap` is `None`, `Forest`, `Desert`, `Hell`, `Graveyard` |
| `RunConfig` | `mapTierIndex` | The tier, assumed to count from 0 (Tier 1 = 0) |
| `RunConfig` | `challenge` (`ChallengeData`: `GetDisplayName()`, `name`) | The run's challenge |
| `MapController` (static) | `runConfig`, `inventory`, `IsTierFinalStage()` | A restarted run; the inventory; whether a dead stage boss was the tier's last |
| `GameManager` | `Instance.player` | The player (`MyPlayer`): `character`, `inventory` |
| `WeaponInventory` | `weapons.ContainsKey(EWeapon)`, `GetWeaponLevel(EWeapon)` | Each weapon's level, asked for every `EWeapon` in turn |
| `TomeInventory` | `HasTome(ETome)`, `GetTomeLevel(ETome)` | Each tome's level |
| `PlayerXp` | `level` | The player's level |
| `Enemy` | `enemyData.enemyName` (`EEnemy`), `enemyFlag` (`EEnemyFlag`) | Which enemy died and whether it is a boss |
| `GraveyardBossRoom` | `bossEnemy` | The Graveyard's boss |
| `ChallengesTracker` (static) | `GetCurrentChallenge()` | The challenge just completed |
| `PlayerHealth` | `KillPlayer()` | DeathLink |
| any game object | `Pointer` | Telling two wrappers of one object apart from two objects |

## Names matched (`make_data.py`)

A test reads the enums out of the probe and fails if any value is neither known nor deliberately passed over, or if anything in the world cannot be reached from a game name.

- **`EWeapon`**: all 30 real weapons match; `Test` and `None` are passed over.
- **`ECharacter`**: all 21 match (`Cl4nk`, `SirOofie`, `TonyMcZoom`, `SirChadwell` by ignoring case and spaces).
- **`ETome`**: 23 match (`Regeneration` is Regen, `Blood` is Bloody). **Four tomes are in the game's code and not in the world: `Bounce`, `Balance`, `Gambler`, `Hoarder`.** The mod passes over them. They are not offered in a run (checked in the game's tome list), so they stay out of the world.
- **`EEnemy`**: bosses are matched by exact name, because `Golem` and `Scorpion` are ordinary enemies: `MinibossPig` (Chunkham the Terrible), `MinibossGolem` (Stone Golem), `Bush`, `MinibossGolemSand` (Sand Golem), `MinibossScorpion` (Scorpionussy), `Bandit`. That the pig is Chunkham is assumed.
- **Spooky Steve** has no way to be recognised yet. Candidates in `EEnemy`: `GhostInvincible`, `Ghostham`. The mod logs every boss it does not know (`Boss killed on Graveyard: the game calls it '...'`), which will name him.
- **Tier bosses** need no names: `Ent1`–`Ent3` and `Pharaoh1`–`Pharaoh3` are presumably them, but the tier check comes from the stage-boss event.

## What the probe is for

`BepInEx/MegabonkAP-probe.txt` lists, from the installed game: the members of the types the mod uses (static ones marked from format 2 on), the values of the enums it matches, and every type whose name suggests something still needed. After a game update, put the new probe next to the old one in `probes/` and compare.
