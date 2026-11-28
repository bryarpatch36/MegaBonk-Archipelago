# The game names the mod relies on

Every name below is looked up at run time. They are the names of **game version 1.0.69**, read from the installed game by the mod's own probe (see "What the probe is for" below), so each name is known to exist in that version. Which features have been seen working on the real game is in the status table of the [mod's README](../README.md); the last column here says which mod version first used each method.

Three first guesses were wrong and were corrected in 0.2.0: the character is not in the run settings, the tier has its own field, and `GameManager.StopDungeon` is about crypts, not the end of a run.

## Methods hooked (`Hooks.cs`)

Most run after the game's method and only watch. Those that change the game are marked: `PlayerHealth.Tick` when a DeathLink has arrived, the three that run *before* the game's method and can stop it, and the two that edit a list the game returned. Each of them lets the game go on untouched when it fails or cannot read what it needs.

| Method | Used for | Since |
|---|---|---|
| `MapController.StartNewMap(RunConfig)` | A run begins: map, tier, challenge | 0.1.0 |
| `MapController.RestartRun()` | The same run again | 0.2.0 |
| `MyPlayer.StartPlayer(ECharacter, Vector3)` | The character being played | 0.2.0 |
| `TrackStats.OnEnemyDied(Enemy, DamageContainer)` | Every enemy death: minibosses, hidden bosses | 0.1.0 |
| `GameManager.OnStageBossDied()` | A stage's boss is dead; with `MapController.IsTierFinalStage()` that completes the tier and the run | 0.2.0 |
| `PlayerXp.AddXp(int)` | The player's level | 0.1.0 |
| `WeaponInventory.AddWeapon(WeaponData, List<StatModifier>)` | Weapon levels | 0.1.0 |
| `TomeInventory.AddTome(TomeData, List<StatModifier>, ERarity)` | Tome levels | 0.1.0 |
| `LevelupScreen.OnClose()` | Weapon and tome levels again after each level-up | 0.1.0 |
| `PlayerHealth.PlayerDied()` | Sending DeathLink, and the run summary line | 0.1.0 |
| `PlayerHealth.Tick()` | One more per-frame turn during a run (see the overlay's, below) | 0.2.0 |
| `GraveyardBossRoom.Activate()` | The Crypt Keys check (that activating takes all four keys is assumed) | 0.1.0 |
| `GraveyardBossRoom.OnEnemyDied(Enemy, DamageContainer)` | Big Bob: the dead enemy is the room's own `bossEnemy` | 0.2.0 |
| `ChallengesTracker.CompleteChallenge()` | Challenge checks | 0.2.0 |
| `Enemy.EnemyDied()` | Only if `TrackStats.OnEnemyDied` is missing | Fallback only |
| `MapController.StartNewMap(RunConfig)`, before | **Stops** a run on a map or tier that is locked | 0.3.0 |
| `CharacterMenu.OnConfirmCharacter(MyButtonCharacter)`, before | **Stops** confirming a locked character | 0.3.0 |
| `SelectionGroupToggleSingleButtonTier.OnClick()`, before | **Stops** picking a locked tier (the button's `runConfig`) | 0.3.0 |
| `UnlockUtility.GetAvailableWeapons()` | **Removes** weapons not held from the returned `List<WeaponData>`. That the level-up offers are drawn from it is assumed from the name | 0.3.0 |
| `UnlockUtility.GetAvailableTomes()` | **Removes** tomes not held from the returned `HashSet<TomeData>` | 0.3.0 |
| `UpgradePicker.ShuffleUpgrades(EEncounter)` | A level-up's offers are on screen: **dims** the locked ones (a `CanvasGroup` on the button, alpha 0.3). Looked at again a few times a second while the picker is shown | 0.3.1 |
| `UpgradePicker.SelectUpgrade(IUpgradable, List<StatModifier>, UpgradeButton, ERarity)`, before | **Stops** picking a locked weapon or tome, unless the picker is in banish mode or the other offers cannot be read. When every offer is locked, the pick is stopped and the mod calls `LevelupScreen.Skip()` at its next turn, then `CloseLevelupScreen()` if the offers are still there a second later | 0.3.1; the skip since 0.3.4 |
| `LevelupScreen.GetShopToolPrice(int)` | **Returns 0** while every offer on screen is locked, so Skip (and Refresh, Banish: the game has one price method for all three) is free there; `RefreshUtilityButtons()` is called when that begins. That this method prices the Skip button is assumed from its name | 0.3.4 |

The traps (0.6.0) need no hook of their own: they are carried out from the per-frame turn below.

On the real game the two `GetAvailable...` hooks did not keep locked weapons and tomes out of the offers; whether they failed to fire or edit a list the offers are not drawn from is not known. They are kept because they do no harm. What enforces the lock at a level-up is `UpgradePicker.ShuffleUpgrades` and `UpgradePicker.SelectUpgrade`.

For the overlay, one of these gives the mod its turn every frame; whichever exist are hooked: `UnityEngine.EventSystems.EventSystem.Update()`, `UnityEngine.Canvas.SendWillRenderCanvases()`, `WindowManager.Update()`, and `PlayerHealth.Tick()` during a run.

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
| `Enemy` | `enemyData.enemyName` (`EEnemy`), `enemyFlag` (`EEnemyFlag`) | Which enemy died and whether it is a boss. For the miniboss hunt: flag `Boss` (2) or `SummonerMiniboss` (16) or a name starting with `Miniboss`, neither `StageBoss` (4) nor `FinalBoss` (32), and not the Bush or the Bandit. A death of the same object (`Pointer`) within a second is one death; later it is a new enemy, because the game reuses its enemy objects |
| `GraveyardBossRoom` | `bossEnemy` | The Graveyard's boss |
| `ChallengesTracker` (static) | `GetCurrentChallenge()` | The challenge just completed |
| `PlayerHealth` | `IsDead()`, `KillPlayer()`; `hp`, `shield`, `overheal`, `CheckDead()` if that does not kill | DeathLink, both ways, looked at a few times a second. Found through `GameManager.Instance.player.inventory.playerHealth`, else `MyPlayer.Instance`, else `MapController.inventory` |
| any game object | `Pointer` | Telling two wrappers of one object apart from two objects |
| `MyButtonCharacter` | `characterData.eCharacter`, else any member of type `ECharacter`; else `CharacterMenu.selectedCharacter` | The character being confirmed. `MyButtonCharacter` is not in the probe, so its member is a guess with two fallbacks |
| `WeaponData`, `TomeData` | `eWeapon` / `eTome`, else any member of type `EWeapon` / `ETome` | Which weapon or tome an offer is. Neither type is in the probe; the guess follows `CharacterData.eCharacter` and `MapData.eMap` |
| `UpgradePicker` | `buttons`, `banishMode`, `window.activeInHierarchy` | The offers on screen. `UpgradeButton` is not in the 1.0.69 probe: what it offers is found as any member of the game's own classes whose type is `WeaponData`, `TomeData`, `UnlockableBase` or has "upgrad" in its name, asked for its real type with `TryCast` |
| the offer lists | `GetEnumerator()`, `Contains`, `Remove`, `Add` | Taking locked things out, and putting them back if the game hands out the same list again after an item arrived |

## Methods called for traps (`GameReader.cs`)

These names were not read by the mod's probe of 1.0.69. They come from the reference copy of the game's `Assembly-CSharp` that [megabonk-together](https://github.com/Fcornaire/megabonk-together) builds against (names and signatures only: such a copy holds no code). Which game version it was made from is not stated there; the 21 methods hooked above and the members read below are all in it under the same names. megabonk-together itself calls `SpawnBoss(kind, 0, Boss, position, size)` and `EventSwarm(event)`. From 0.6.0 the probe lists these types, so a probe from the installed game will confirm or correct them. None has been seen working from this mod on the real game.

| Trap | Called | With |
|---|---|---|
| Slow, Freeze, Poison | `PlayerStatusEffects.SlowPlayer(float time)`, `FreezePlayer(float time)`, `PoisonPlayer(float duration)` on the player's `inventory.statusEffects` | 10, 3 and 8. That the number is seconds is assumed from the parameter names |
| Swarm | `SummonerController.EventSwarm(TimelineEvent)` on `EnemyManager.Instance.summonerController` | The first event of the stage's own `timeline.events` whose `eTimelineEvent` is `ESwarm`: the object the game itself passes when the timeline reaches it. Not called when `AreEventsDisabled()` is true or the stage has no such event: then a crowd, as below, of 20 with no flag |
| Elite Pack | `EnemyManager.SpawnEnemy(EnemyData, int summonerId, bool forceSpawn, EEnemyFlag flag, bool useDirectionBias)`, 6 times | `DataManager.Instance.GetEnemyData(EEnemy)` of a random one of the stage's enemies (`summonerController.stageSummoner.currentEnemies`, else `stageSummoner.enemyPool`, else `timeline.startEnemies`), summoner 0, forced, `Elite` (1), direction bias on. This form lets the game choose the place. That it honours the `Elite` flag is assumed |
| Miniboss | `EnemyManager.SpawnBoss(EEnemy, int summonerId, EEnemyFlag, Vector3 pos, float extraSizeMultiplier)` | A random one of `summonerController.timeline.minibosses`, else `summonerController.minibossPool`, else any `EEnemy` whose name starts with `Miniboss`; summoner 0, `Boss` (2), 3 to the side of and 1 above `MyPlayer.GetFeetPosition()`, size 1 |

Before a trap is carried out: `PlayerHealth.IsDead()` is false, `MyTime.paused` is false, `GameManager.Instance.IsPlaying()` is true, and no level-up is on screen. Each of the three reads is skipped when the member is not there.

A run is told from the menus by the player object being alive: `UnityEngine.Object.op_Implicit(Object)`, Unity's own "does this still exist", asked of `GameManager.Instance.player` (else `MyPlayer.Instance`). After a run the game may still point at a player it has destroyed. This decides which of the overlay's two places is used, and no trap is carried out without it.

## The overlay (`Screen.cs`)

Unity and TextMeshPro members used, all by name: `GameObject(string)`, `AddComponent<T>()`, `SetActive`, `transform`; `Object.DontDestroyOnLoad`; `Canvas.renderMode`, `sortingOrder`; `CanvasScaler.uiScaleMode`, `referenceResolution`, `matchWidthOrHeight`; `Image.color`, `raycastTarget`, `rectTransform`; `RectTransform.SetParent`, `anchorMin`, `anchorMax`, `pivot`, `anchoredPosition`, `sizeDelta`, `offsetMin`, `offsetMax`; `TextMeshProUGUI.text`, `fontSize`, `alignment`, `enableWordWrapping`, `richText`, `font`, `GetPreferredValues(string, float, float)`; `Input.GetKeyDown(KeyCode)`, `Input.GetKey(KeyCode)`. The overlay has been seen on screen on the real game.

Moving it (0.6.0) sets the same four members it is first placed with (`anchorMin`, `anchorMax`, `pivot`, `anchoredPosition`) on the panel that is already there; nothing is built again. The move key is read like the view key.

## Names matched (`make_data.py`)

How the names in the game's enums (version 1.0.69) line up with the world's names:

- **`EWeapon`**: all 30 real weapons match; `Test` and `None` are passed over.
- **`ECharacter`**: all 21 match (`Cl4nk`, `SirOofie`, `TonyMcZoom`, `SirChadwell` by ignoring case and spaces).
- **`ETome`**: 23 match (`Regeneration` is Regen, `Blood` is Bloody). **Four tomes are in the game's code and not in the world: `Bounce`, `Balance`, `Gambler`, `Hoarder`.** The mod passes over them. They are not offered in a run (checked in the game's tome list), so they stay out of the world.
- **`EEnemy`**: bosses are matched by exact name, because `Golem` and `Scorpion` are ordinary enemies: `MinibossPig` (Chunkham the Terrible), `MinibossGolem` (Stone Golem), `Bush`, `MinibossGolemSand` (Sand Golem), `MinibossScorpion` (Scorpionussy), `Bandit`. That the pig is Chunkham is assumed.
- **Spooky Steve** has no way to be recognised yet. Candidates in `EEnemy`: `GhostInvincible`, `Ghostham`. The mod logs every boss it does not know (`Boss killed on Graveyard: the game calls it '...'`), which will name him.
- **Tier bosses** need no names: `Ent1`–`Ent3` and `Pharaoh1`–`Pharaoh3` are presumably them, but the tier check comes from the stage-boss event.

## What the probe is for

`BepInEx/MegabonkAP-probe.txt` lists, from the installed game: the members of the types the mod uses (static ones marked from format 2 on), the values of the enums it matches, and every type whose name suggests something still needed. It calls and changes nothing in the game. Probe files are not kept in this repository. After a game update, compare the new probe with the one from before the update: renamed members show up there, and as `Hook missing:` or `Could not read ...` lines in the log.
