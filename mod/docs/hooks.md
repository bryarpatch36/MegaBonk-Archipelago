# The game names the mod relies on

Every name below is looked up at run time. They are the names of **game version 1.0.69**, taken from [probes/probe-1.0.69.txt](probes/probe-1.0.69.txt), which the mod itself wrote from the installed game on 2026-11-20. So each name is known to exist. What is not yet known for most of them is whether the mod's *use* of it is right: the last column says what has been seen on the real game.

The first run on the real game (mod 0.1.0, one Forest Tier 1 run) showed: all hooks of 0.1.0 attached, the Archipelago connection worked, and the map was read. It also showed three wrong guesses, corrected in 0.2.0: the character is not in the run settings, the tier has its own field, and `GameManager.StopDungeon` is about crypts, not the end of a run.

## Methods hooked (`Hooks.cs`)

Most run after the game's method and only watch. Those that change the game are marked: `PlayerHealth.Tick` when a DeathLink has arrived, the three that run *before* the game's method and can stop it, and the two that edit a list the game returned. Each of them lets the game go on untouched when it fails or cannot read what it needs.

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
| `PlayerHealth.Tick()` | One more per-frame turn during a run (see the overlay's, below) | New in 0.2.0 |
| `GraveyardBossRoom.Activate()` | The Crypt Keys check (that activating takes all four keys is assumed) | Attaches; unconfirmed |
| `GraveyardBossRoom.OnEnemyDied(Enemy, DamageContainer)` | Big Bob: the dead enemy is the room's own `bossEnemy` | New in 0.2.0 |
| `ChallengesTracker.CompleteChallenge()` | Challenge checks | New in 0.2.0 |
| `Enemy.EnemyDied()` | Only if `TrackStats.OnEnemyDied` is missing | Not used |
| `MapController.StartNewMap(RunConfig)`, before | **Stops** a run on a map or tier that is locked | New in 0.3.0 |
| `CharacterMenu.OnConfirmCharacter(MyButtonCharacter)`, before | **Stops** confirming a locked character | New in 0.3.0 |
| `SelectionGroupToggleSingleButtonTier.OnClick()`, before | **Stops** picking a locked tier (the button's `runConfig`) | New in 0.3.0 |
| `UnlockUtility.GetAvailableWeapons()` | **Removes** weapons not held from the returned `List<WeaponData>`. That the level-up offers are drawn from it is assumed from the name | New in 0.3.0 |
| `UnlockUtility.GetAvailableTomes()` | **Removes** tomes not held from the returned `HashSet<TomeData>` | New in 0.3.0 |
| `UpgradePicker.ShuffleUpgrades(EEncounter)` | A level-up's offers are on screen: **dims** the locked ones (a `CanvasGroup` on the button, alpha 0.3). Looked at again a few times a second while the picker is shown | New in 0.3.1 |
| `UpgradePicker.SelectUpgrade(IUpgradable, List<StatModifier>, UpgradeButton, ERarity)`, before | **Stops** picking a locked weapon or tome, unless the picker is in banish mode or the other offers cannot be read. When every offer is locked, the pick is stopped and the mod calls `LevelupScreen.Skip()` at its next turn, then `CloseLevelupScreen()` if the offers are still there a second later | 0.3.1; works on the game. The skip is new in 0.3.4 |
| `LevelupScreen.GetShopToolPrice(int)` | **Returns 0** while every offer on screen is locked, so Skip (and Refresh, Banish: the game has one price method for all three) is free there; `RefreshUtilityButtons()` is called when that begins. That this method prices the Skip button is assumed from its name | New in 0.3.4 |

On the real game (0.3.0) the two `GetAvailable...` hooks did not keep locked weapons and tomes out of the offers. Whether they attached, fired, or edit a list the offers are not drawn from is in that run's log, which was not looked at; they are kept because they do no harm.

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

## The overlay (`Screen.cs`)

Unity and TextMeshPro members used, all by name: `GameObject(string)`, `AddComponent<T>()`, `SetActive`, `transform`; `Object.DontDestroyOnLoad`; `Canvas.renderMode`, `sortingOrder`; `CanvasScaler.uiScaleMode`, `referenceResolution`, `matchWidthOrHeight`; `Image.color`, `raycastTarget`, `rectTransform`; `RectTransform.SetParent`, `anchorMin`, `anchorMax`, `pivot`, `anchoredPosition`, `sizeDelta`, `offsetMin`, `offsetMax`; `TextMeshProUGUI.text`, `fontSize`, `alignment`, `enableWordWrapping`, `richText`, `font`, `GetPreferredValues(string, float, float)`; `Input.GetKeyDown(KeyCode)`. None has been seen working on the game. The game itself uses TextMeshPro and `OnGUI`, so the classes are there; whether every member survived the game's build is what the first run shows.

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
