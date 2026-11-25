using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace MegabonkAP.Core
{
    /// <summary>
    /// Takes the game's own objects from the plugin's hooks, reads what the tracker needs out of
    /// them by member name, and passes it on. The names are those of game version 1.0.69;
    /// docs/hooks.md lists them and says which have been seen working. When a
    /// member is missing the log says which, and lists the members the object really has.
    /// </summary>
    public sealed class GameReader
    {
        private readonly Tracker _tracker;
        private readonly ILog _log;
        private readonly Func<string, Type?> _findType;
        private readonly HashSet<string> _said = new();
        private readonly object _gate = new();

        private readonly Dictionary<string, int> _levels = new();      // "weapon:Sword" -> last level passed on
        private readonly Dictionary<long, DateTime> _recent = new();    // enemy -> when its death was last counted
        private int _playerLevel = -1;
        private int _deaths;
        private int _bossDeaths;

        // the list of weapons or tomes last limited, and what was taken out of it
        private sealed class Offers { public long List; public readonly List<object> Removed = new(); public string? Said; }
        private readonly Dictionary<string, Offers> _offers = new();

        /// <summary>
        /// Set by the plugin when the game's own "stage boss died" method is hooked. When it is
        /// not, stage bosses are recognised among the enemy deaths instead.
        /// </summary>
        public bool StageBossEvent { get; set; }

        public GameReader(Tracker tracker, ILog log, Func<string, Type?> findType)
        {
            _tracker = tracker;
            _log = log;
            _findType = findType;
        }

        /// <summary>MapController.StartNewMap(RunConfig): a run begins.</summary>
        public void RunStarted(object? runConfig)
        {
            lock (_gate)
            {
                _levels.Clear();
                _recent.Clear();
                _playerLevel = -1;
                _deaths = 0;
                _bossDeaths = 0;

                if (runConfig == null)
                {
                    Say("A run started but the game gave no run settings to read.");
                    _tracker.RunStarted(null, null, 0);
                    return;
                }

                string? map = Dyn.Name(Dyn.Get(runConfig, "mapData.eMap"));
                if (map == null) Missing(runConfig, "the map (mapData.eMap)");

                // the menu's Tier 1, 2, 3 is mapTierIndex 0, 1, 2
                int? tierIndex = Dyn.Int(Dyn.Get(runConfig, "mapTierIndex"));
                if (tierIndex == null) Missing(runConfig, "the tier (mapTierIndex)");
                int tier = tierIndex == null ? 0 : tierIndex.Value + 1;

                object? challenge = Dyn.Get(runConfig, "challenge");
                string? challengeName = challenge == null ? null : ChallengeNames(challenge).FirstOrDefault(n => n != null);

                // the player does not exist yet on a new run; MyPlayer.StartPlayer names the character shortly
                _tracker.RunStarted(CharacterFromPlayer(), map, tier, challengeName);
            }
        }

        /// <summary>MapController.RestartRun(): the same run settings again.</summary>
        public void RunRestarted() => RunStarted(Dyn.GetStatic(_findType("MapController"), "runConfig"));

        /// <summary>MyPlayer.StartPlayer(ECharacter, Vector3): the player appears, and the game names the character.</summary>
        public void PlayerStarted(object? character) => _tracker.CharacterSeen(Dyn.Name(character));

        /// <summary>TrackStats.OnEnemyDied(Enemy, DamageContainer) or Enemy.EnemyDied(): an enemy died.</summary>
        public void EnemyDied(object? enemy)
        {
            if (enemy == null) return;
            lock (_gate)
            {
                if (!FirstTime(enemy)) return;
                _deaths++;

                string? name = Dyn.Name(Dyn.Get(enemy, "enemyData.enemyName"));
                if (name == null) { Missing(enemy, "the enemy's name (enemyData.enemyName)"); return; }

                // EEnemyFlag: Boss 2, StageBoss 4, SummonerMiniboss 16, FinalBoss 32
                int flags = Dyn.Int(Dyn.Get(enemy, "enemyFlag")) ?? 0;
                bool boss = (flags & (2 | 4 | 16 | 32)) != 0;
                if (boss) _bossDeaths++;
                // a miniboss: any boss that does not end a stage, the ones called up at a boss statue included
                bool miniboss = (flags & (4 | 32)) == 0
                    && ((flags & (2 | 16)) != 0 || name.StartsWith("Miniboss", StringComparison.OrdinalIgnoreCase));
                if (boss) _log.Info($"Boss death: {name}, flags {flags}" + (miniboss ? "." : ", not a miniboss."));
                _tracker.EnemyKilled(name, boss, miniboss);

                if (!StageBossEvent && (flags & (4 | 32)) != 0) _tracker.StageBossDied(TierFinal());
            }
        }

        /// <summary>GameManager.OnStageBossDied(): the boss that ends a stage is dead.</summary>
        public void StageBossDied()
        {
            lock (_gate)
            {
                bool? tierFinal = TierFinal();
                if (tierFinal == null) Say("The game did not say whether this was the tier's last stage (MapController.IsTierFinalStage)."
                    + " Stage bosses are counted against the tier instead.");
                _tracker.StageBossDied(tierFinal);
                _log.Info("Stage boss dead. " + Summary());
            }
        }

        /// <summary>PlayerXp.AddXp(int): experience was gained, so the level may have changed.</summary>
        public void XpChanged(object? playerXp)
        {
            lock (_gate)
            {
                int? level = Dyn.Int(Dyn.Get(playerXp, "level"));
                if (level == null)
                {
                    if (playerXp != null) Missing(playerXp, "the player's level (level)");
                    return;
                }
                if (level.Value == _playerLevel) return;
                _playerLevel = level.Value;
                _tracker.PlayerLevel(level.Value);
            }
        }

        /// <summary>WeaponInventory.AddWeapon(...): a weapon was taken or upgraded.</summary>
        public void WeaponsChanged(object? weaponInventory)
        {
            if (weaponInventory == null) return;
            lock (_gate)
            {
                int seen = 0;
                Array? all = Dyn.EnumOfFirstArgument(weaponInventory.GetType(), "GetWeaponLevel");
                object? owned = Dyn.Get(weaponInventory, "weapons");
                if (all != null && owned != null && Dyn.Method(owned.GetType(), "ContainsKey", 1) != null)
                {
                    // ask about each weapon in turn: the game's dictionary is awkward to walk from outside
                    foreach (object weapon in all)
                    {
                        if (!Dyn.Bool(Dyn.Call(owned, "ContainsKey", weapon))) continue;
                        int? level = Dyn.Int(Dyn.Call(weaponInventory, "GetWeaponLevel", weapon));
                        if (level == null) continue;
                        seen++;
                        if (Changed("weapon:" + weapon, level.Value)) _tracker.WeaponLevel(weapon.ToString(), level.Value);
                    }
                    return;
                }

                foreach ((object? key, object? value) in Dyn.Pairs(owned))
                {
                    string? name = Dyn.Name(key);
                    int? level = Dyn.Int(Dyn.First(value, "level", "Level"));
                    if (name == null || level == null) continue;
                    seen++;
                    if (Changed("weapon:" + name, level.Value)) _tracker.WeaponLevel(name, level.Value);
                }
                if (seen == 0) Missing(weaponInventory, "the weapons (GetWeaponLevel and weapons)");
            }
        }

        /// <summary>TomeInventory.AddTome(...): a tome was taken or upgraded.</summary>
        public void TomesChanged(object? tomeInventory)
        {
            if (tomeInventory == null) return;
            lock (_gate)
            {
                Type type = tomeInventory.GetType();
                Array? all = Dyn.EnumOfFirstArgument(type, "GetTomeLevel");
                if (all != null && Dyn.Method(type, "HasTome", 1) != null)
                {
                    foreach (object tome in all)
                    {
                        if (!Dyn.Bool(Dyn.Call(tomeInventory, "HasTome", tome))) continue;
                        int? level = Dyn.Int(Dyn.Call(tomeInventory, "GetTomeLevel", tome));
                        if (level == null) continue;
                        if (Changed("tome:" + tome, level.Value)) _tracker.TomeLevel(tome.ToString(), level.Value);
                    }
                    return;
                }

                int seen = 0;
                foreach ((object? key, object? value) in Dyn.Pairs(Dyn.Get(tomeInventory, "tomeLevels")))
                {
                    string? name = Dyn.Name(key);
                    int? level = Dyn.Int(value);
                    if (name == null || level == null) continue;
                    seen++;
                    if (Changed("tome:" + name, level.Value)) _tracker.TomeLevel(name, level.Value);
                }
                if (seen == 0) Missing(tomeInventory, "the tomes (HasTome, GetTomeLevel and tomeLevels)");
            }
        }

        /// <summary>LevelupScreen.OnClose(): look at every weapon and tome again.</summary>
        public void InventoryChanged()
        {
            object? inventory = Dyn.Get(Player(), "inventory")
                ?? Dyn.GetStatic(_findType("MapController"), "inventory");
            if (inventory == null) return;      // between runs, or before the player exists
            WeaponsChanged(Dyn.Get(inventory, "weaponInventory"));
            TomesChanged(Dyn.Get(inventory, "tomeInventory"));
        }

        /// <summary>
        /// GraveyardBossRoom.OnEnemyDied(Enemy, DamageContainer): an enemy died while the Graveyard's
        /// boss room exists. When it is the room's own boss, that is Big Bob, and the run is complete.
        /// </summary>
        public void GraveyardEnemyDied(object? room, object? enemy)
        {
            if (room == null || enemy == null) return;
            object? boss = Dyn.Get(room, "bossEnemy");
            if (boss == null || Identity(boss) != Identity(enemy)) return;
            _log.Info($"The Graveyard's boss is dead: the game calls it '{Dyn.Name(Dyn.Get(enemy, "enemyData.enemyName"))}'.");
            _tracker.BossDefeated("Big Bob", endsRun: true);
        }

        /// <summary>GraveyardBossRoom.Activate().</summary>
        public void CryptOpened() => _tracker.CryptOpened();

        /// <summary>ChallengesTracker.CompleteChallenge(): the run's challenge is done.</summary>
        public void ChallengeCompleted()
        {
            object? challenge = Dyn.GetStatic(_findType("ChallengesTracker"), "GetCurrentChallenge()");
            _tracker.ChallengeCompleted(challenge == null ? Array.Empty<string?>() : ChallengeNames(challenge).ToArray());
        }

        /// <summary>
        /// PlayerHealth.PlayerDied(), or WatchHealth seeing the player dead: whichever comes first
        /// reports the death, the other is ignored until the player is alive again.
        /// </summary>
        public void PlayerDied()
        {
            lock (_gate)
            {
                if (_dead) return;
                _dead = true;
                _log.Info("You died. " + Summary());
            }
            _tracker.PlayerDied();
        }

        /// <summary>Another player died (DeathLink): end this player at the next look at their health, if one comes within ten seconds.</summary>
        public void RequestKill()
        {
            lock (_gate) _killBefore = Now().AddSeconds(10);
        }

        /// <summary>
        /// Call a few times a second from the game's own thread. It looks at the player's health:
        /// a death is reported even if the game's PlayerDied was never seen, and a waiting
        /// DeathLink is carried out. Then it looks at the level-up offers on screen.
        /// </summary>
        public void Frame()
        {
            WatchHealth();
            RefreshOffers();
        }

        private void WatchHealth()
        {
            bool died = false;
            lock (_gate)
            {
                // the player's health, by each way the game is known to keep it
                object? health = Dyn.Get(Player(), "inventory.playerHealth")
                    ?? Dyn.GetStatic(_findType("MyPlayer"), "Instance.inventory.playerHealth")
                    ?? Dyn.GetStatic(_findType("MapController"), "inventory.playerHealth");
                if (health == null)                                                   // between runs
                {
                    if (_killBefore != null && Now() > _killBefore)
                    {
                        _killBefore = null;
                        _log.Info("DeathLink: no player was found within ten seconds, so nothing happened.");
                    }
                    _dead = false;
                    _killCheck = null;
                    return;
                }
                bool? dead = Dyn.Get(health, "IsDead()") as bool?;
                if (dead == null) Say($"Could not ask the game whether the player is dead (PlayerHealth.IsDead). It has: {Dyn.Describe(health.GetType())}");

                DateTime now = Now();
                if (_killBefore != null)
                {
                    bool inTime = now <= _killBefore;
                    _killBefore = null;
                    if (inTime && dead != true)
                    {
                        Kill(health);
                        _killCheck = now.AddSeconds(1.5);
                        return;
                    }
                }
                if (_killCheck != null && now >= _killCheck)
                {
                    _killCheck = null;
                    if (dead == false)
                    {
                        // KillPlayer did not do it: empty the health and let the game notice
                        bool emptied = Dyn.Set(health, "hp", 0);
                        Dyn.Set(health, "shield", 0f);
                        Dyn.Set(health, "overheal", 0f);
                        Dyn.Call(health, "CheckDead");
                        _log.Warn("DeathLink: KillPlayer left the player alive, so their health was " + (emptied ? "set to zero." : "left alone: it could not be set."));
                    }
                }

                if (dead == true && !_dead) died = true;
                else if (dead != true) _dead = false;
            }
            if (died) PlayerDied();
        }

        private bool _dead;                 // this death has been reported
        private DateTime? _killBefore;      // a DeathLink waits until then
        private DateTime? _killCheck;       // when to see whether the kill worked

        /// <summary>The clock, replaceable in tests.</summary>
        public Func<DateTime> Now { get; set; } = () => DateTime.UtcNow;

        /// <summary>DeathLink: end the player through the game's own PlayerHealth.KillPlayer().</summary>
        public void Kill(object? playerHealth)
        {
            if (playerHealth == null || Dyn.Method(playerHealth.GetType(), "KillPlayer", 0) == null)
            {
                Say("DeathLink: the game's KillPlayer could not be found, so nothing happened.");
                return;
            }
            Dyn.Call(playerHealth, "KillPlayer");
            _log.Info("DeathLink: the game's KillPlayer was called.");
        }

        // ------------------------------------------------------------------ what may be used

        /// <summary>
        /// CharacterMenu.OnConfirmCharacter(MyButtonCharacter), before the game acts on it. False:
        /// the character is locked and the game must not go on.
        /// </summary>
        public bool MayConfirmCharacter(object? menu, object? button)
        {
            object? character = Dyn.First(button, "characterData.eCharacter", "character.eCharacter", "data.eCharacter", "eCharacter")
                ?? Dyn.OfEnum(button, "ECharacter");
            if (character == null)
            {
                if (button != null) Missing(button, "the character on the button; the menu's selectedCharacter is used instead");
                character = Dyn.Get(menu, "selectedCharacter");
            }
            if (character == null)
            {
                if (menu != null) Missing(menu, "the chosen character (selectedCharacter)");
                return true;
            }
            return _tracker.CharacterRefusal(Dyn.Name(character)) == null;
        }

        /// <summary>MapController.StartNewMap(RunConfig), before the game acts on it. False: the map or tier is locked.</summary>
        public bool MayStartRun(object? runConfig)
        {
            if (runConfig == null) return true;
            string? map = Dyn.Name(Dyn.Get(runConfig, "mapData.eMap"));
            int? tierIndex = Dyn.Int(Dyn.Get(runConfig, "mapTierIndex"));
            if (map == null) return true;
            return _tracker.RunRefusal(map, tierIndex == null ? 0 : tierIndex.Value + 1) == null;
        }

        /// <summary>SelectionGroupToggleSingleButtonTier.OnClick(), before the game acts on it. False: that tier is locked.</summary>
        public bool MayPickTier(object? tierButton) => MayStartRun(Dyn.Get(tierButton, "runConfig"));

        /// <summary>
        /// UnlockUtility.GetAvailableWeapons() / GetAvailableTomes() returned this collection: the
        /// things the game may offer. Take out what is not held. kind: "weapon" or "tome".
        /// </summary>
        public void LimitOffers(string kind, object? collection)
        {
            if (collection == null) return;
            lock (_gate)
            {
                if (!_offers.TryGetValue(kind, out Offers? offers)) _offers[kind] = offers = new Offers();

                // the game may hand out the same list again: put back what was taken out of it, then judge afresh
                long id = Identity(collection);
                if (offers.List == id)
                {
                    foreach (object item in offers.Removed)
                        if (!Dyn.Bool(Dyn.Call(collection, "Contains", item))) Dyn.Call(collection, "Add", item);
                }
                offers.Removed.Clear();
                offers.List = id;
                if (!_tracker.Locking) return;

                string enumName = kind == "weapon" ? "EWeapon" : "ETome";
                List<object> all = Dyn.Items(collection).Where(i => i != null).Select(i => i!).ToList();
                if (all.Count == 0)
                {
                    if ((Dyn.Int(Dyn.First(collection, "Count", "count")) ?? 0) > 0)
                        Missing(collection, $"the {kind}s the game may offer (the list could not be walked)");
                    return;
                }

                var kept = new List<string>();
                var locked = new List<object>();
                foreach (object item in all)
                {
                    object? named = Dyn.First(item, kind == "weapon" ? "eWeapon" : "eTome") ?? Dyn.OfEnum(item, enumName);
                    if (named == null) { Missing(item, $"which {kind} this is ({enumName})"); return; }
                    string name = Dyn.Name(named) ?? "";
                    if (_tracker.OfferAllowed(kind, name)) kept.Add(name);
                    else locked.Add(item);
                }

                if (kept.Count == 0)
                {
                    SayOffers(offers, $"You hold none of the {all.Count} {kind}s the game can offer, so its {kind} offers are left as they are.");
                    return;
                }
                if (locked.Count > 0 && Dyn.Method(collection.GetType(), "Remove", 1) == null)
                {
                    Missing(collection, $"a way to take locked {kind}s out of the offers (Remove)");
                    return;
                }
                foreach (object item in locked)
                {
                    Dyn.Call(collection, "Remove", item);
                    offers.Removed.Add(item);
                }
                kept.Sort(StringComparer.OrdinalIgnoreCase);
                SayOffers(offers, $"Level-up {kind}s limited to the {kept.Count} of {all.Count} you hold: {string.Join(", ", kept)}.");
            }
        }

        /// <summary>
        /// UpgradePicker.ShuffleUpgrades(EEncounter): a level-up's offers are on screen. Locked
        /// weapons and tomes are dimmed. The picker is remembered so RefreshOffers can look again.
        /// </summary>
        public void OffersShown(object? picker)
        {
            lock (_gate)
            {
                _picker = picker;
                DimLocked();
            }
        }

        /// <summary>Look at the offers on screen again: the game may fill its buttons a moment after it says so.</summary>
        public void RefreshOffers()
        {
            lock (_gate)
            {
                if (_picker == null) return;
                try
                {
                    if (Dyn.First(_picker, "window.activeInHierarchy", "gameObject.activeInHierarchy") is bool shown && !shown)
                    {
                        Forget();                       // the level-up is over
                        return;
                    }
                    DimLocked();
                    SkipWhenAsked();
                }
                catch (Exception) { Forget(); }
            }
        }

        /// <summary>
        /// UpgradePicker.SelectUpgrade(IUpgradable, List, UpgradeButton, ERarity), before the game
        /// acts on it. False: that weapon or tome is locked. When every offer on screen is locked
        /// the pick is refused too, and the level-up is skipped for the player at no cost, so
        /// there is always a way out. Only when the other offers cannot be seen is a locked pick
        /// let through.
        /// </summary>
        public bool MayPickOffer(object? picker, object? upgradable)
        {
            lock (_gate)
            {
                if (!_tracker.Locking) return true;
                (string kind, string name)? what = WhatIs(upgradable);
                if (what == null || _tracker.OfferAllowed(what.Value.kind, what.Value.name)) return true;
                if (Dyn.Get(picker, "banishMode") is bool banishing && banishing) return true;      // banishing a locked offer is fine

                List<(object button, bool locked)> buttons = Buttons(picker);
                if (buttons.Count == 0)
                {
                    Say("The level-up's other offers could not be seen, so a locked offer can still be picked."
                        + (picker != null ? $" UpgradePicker has: {Dyn.Describe(picker.GetType())}" : ""));
                    return true;
                }
                if (buttons.All(b => b.locked))
                {
                    _picker = picker;
                    _stuck = true;
                    _skipWanted = true;                 // carried out at the next Frame, outside the game's own click
                    return false;
                }
                return _tracker.OfferRefusal(what.Value.kind, what.Value.name) == null;
            }
        }

        private object? _picker;

        /// <summary>The offers on screen: each button in use, and whether what it offers is locked.</summary>
        private List<(object button, bool locked)> Buttons(object? picker)
        {
            var buttons = new List<(object, bool)>();
            foreach (object? button in Dyn.Items(Dyn.Get(picker, "buttons")))
            {
                if (button == null) continue;
                if (Dyn.Get(button, "gameObject.activeInHierarchy") is bool shown && !shown) continue;
                (string kind, string name)? what = null;
                foreach (string member in Dyn.MembersOfType(button.GetType(), "upgrad", "WeaponData", "TomeData", "UnlockableBase"))
                {
                    what = WhatIs(Dyn.Get(button, member));
                    if (what != null) break;
                }
                buttons.Add((button, what != null && !_tracker.OfferAllowed(what.Value.kind, what.Value.name)));
            }
            return buttons;
        }

        private void DimLocked()
        {
            List<(object button, bool locked)> buttons = Buttons(_picker);
            bool locking = _tracker.Locking;
            foreach ((object button, bool locked) in buttons) Dim(button, locking && locked);

            bool stuck = locking && buttons.Count > 0 && buttons.All(b => b.locked);
            if (stuck == _stuck) return;
            _stuck = stuck;
            if (!stuck) return;
            // the tools' prices are now zero (ToolPrice): have the game look at its buttons again
            Dyn.Call(Dyn.Get(_picker, "levelupScreen"), "RefreshUtilityButtons");
            _tracker.Say("Every offer here is locked. Skip costs nothing, or pick any offer to skip.");
        }

        /// <summary>
        /// Whether the level-up on screen has nothing the player may pick. While it has, skipping,
        /// refreshing and banishing cost nothing (see ToolPrice).
        /// </summary>
        public bool Stuck
        {
            get { lock (_gate) return _stuck; }
        }

        /// <summary>LevelupScreen.GetShopToolPrice(int) returned this price: what it should be instead.</summary>
        public int ToolPrice(int price) => Stuck ? 0 : price;

        /// <summary>
        /// The player picked an offer on a level-up where everything is locked: skip it for them
        /// through the game's own Skip, and if the offers are still there a moment later, close
        /// the level-up.
        /// </summary>
        private void SkipWhenAsked()
        {
            object? screen = Dyn.Get(_picker, "levelupScreen");
            DateTime now = Now();
            if (_skipWanted)
            {
                _skipWanted = false;
                if (screen == null || Dyn.Method(screen.GetType(), "Skip", 0) == null)
                {
                    Say("A level-up with only locked offers could not be skipped: the game's LevelupScreen.Skip was not found."
                        + (_picker != null ? $" UpgradePicker has: {Dyn.Describe(_picker.GetType())}" : ""));
                    return;
                }
                Dyn.Call(screen, "Skip");
                _skipCheck = now.AddSeconds(1);
                _tracker.Say("Every offer was locked, so the level-up was skipped at no cost.");
                return;
            }
            if (_skipCheck != null && now >= _skipCheck)
            {
                _skipCheck = null;
                if (!_stuck) return;                    // other offers are showing, or it closed
                Dyn.Call(screen, "CloseLevelupScreen");
                _log.Warn("The game's Skip left the locked offers on screen, so the level-up was closed (LevelupScreen.CloseLevelupScreen).");
            }
        }

        private void Forget()
        {
            _picker = null;
            _stuck = false;
            _skipWanted = false;
            _skipCheck = null;
        }

        private bool _stuck;                // every offer on screen is locked
        private bool _skipWanted;           // the player picked one anyway: skip at the next Frame
        private DateTime? _skipCheck;       // when to see whether the skip worked

        /// <summary>Fade a button the way a thing you cannot afford is faded, or bring it back.</summary>
        private void Dim(object button, bool dim)
        {
            Type? groupType = _findType("UnityEngine.CanvasGroup");
            object? gameObject = Dyn.Get(button, "gameObject");
            if (groupType == null || gameObject == null)
            {
                if (dim) Say("Locked offers cannot be dimmed: " + (groupType == null ? "the game has no CanvasGroup." : "the offer's button has no gameObject."));
                return;
            }
            object? group = Dyn.Generic(gameObject, "GetComponent", groupType);
            if (group == null && dim) group = Dyn.Generic(gameObject, "AddComponent", groupType);
            if (group == null) return;
            if (!Dyn.Set(group, "alpha", dim ? 0.3f : 1f) && dim) Say("Locked offers cannot be dimmed: CanvasGroup has no alpha.");
        }

        /// <summary>Which weapon or tome an offer is, under the game's name, or null when it is neither.</summary>
        private (string kind, string name)? WhatIs(object? offered)
        {
            if (offered == null) return null;
            foreach ((string kind, string typeName, string member, string enumName) in new[]
                { ("weapon", "WeaponData", "eWeapon", "EWeapon"), ("tome", "TomeData", "eTome", "ETome") })
            {
                object? data = As(offered, typeName);
                if (data == null) continue;
                string? name = Dyn.Name(Dyn.First(data, member) ?? Dyn.OfEnum(data, enumName));
                if (name != null) return (kind, name);
            }
            return null;
        }

        /// <summary>
        /// The thing as the named game type, or null when it is not one. The game hands offers over
        /// as "something upgradable"; its wrapper has to be asked for the real type (TryCast).
        /// </summary>
        private object? As(object thing, string typeName)
        {
            for (Type? t = thing.GetType(); t != null; t = t.BaseType)
                if (t.Name == typeName) return thing;
            Type? wanted = _findType(typeName);
            return wanted == null ? null : Dyn.Generic(thing, "TryCast", wanted);
        }

        private void SayOffers(Offers offers, string message)
        {
            if (offers.Said == message) return;
            offers.Said = message;
            _log.Info(message);
        }

        // ------------------------------------------------------------------ inside

        /// <summary>What the mod has seen this run, in one line: enough to tell which hooks are alive.</summary>
        private string Summary()
        {
            string levels = string.Join(", ", _levels.OrderBy(l => l.Key).Select(l => $"{l.Key.Split(':')[1]} {l.Value}"));
            return $"Seen this run: {_deaths} enemy deaths ({_bossDeaths} bosses), player level "
                + (_playerLevel < 0 ? "unknown" : _playerLevel.ToString()) + ", weapons and tomes: " + (levels.Length > 0 ? levels : "none") + ".";
        }

        /// <summary>A challenge under each name it might match by: the one shown to the player, then the asset's.</summary>
        private static IEnumerable<string?> ChallengeNames(object challenge)
        {
            yield return Dyn.Get(challenge, "GetDisplayName()") as string;
            yield return Dyn.Get(challenge, "name") as string;
        }

        private bool? TierFinal() => Dyn.GetStatic(_findType("MapController"), "IsTierFinalStage()") as bool?;

        private string? CharacterFromPlayer() =>
            Dyn.Name(Dyn.First(Player(), "character", "inventory.characterData.eCharacter"));

        private object? Player() => Dyn.GetStatic(_findType("GameManager"), "Instance.player");

        /// <summary>The game's objects reach the mod through wrappers; two wrappers of one object share its Pointer.</summary>
        private static long Identity(object thing)
        {
            object? pointer = Dyn.Get(thing, "Pointer");
            if (pointer is IntPtr p) return p.ToInt64();
            return RuntimeHelpers.GetHashCode(thing);
        }

        /// <summary>
        /// False when this enemy's death was already counted a moment ago (the game can report one
        /// death twice). The game reuses its enemy objects, so the same object dying again later
        /// is a new enemy and counts.
        /// </summary>
        private bool FirstTime(object enemy)
        {
            long id = Identity(enemy);
            DateTime now = Now();
            if (_recent.TryGetValue(id, out DateTime before) && (now - before).TotalSeconds < 1) return false;
            _recent[id] = now;
            if (_recent.Count > 2048)
                foreach (long old in _recent.Where(r => (now - r.Value).TotalSeconds >= 1).Select(r => r.Key).ToList()) _recent.Remove(old);
            return true;
        }

        private bool Changed(string key, int level)
        {
            if (_levels.TryGetValue(key, out int before) && before == level) return false;
            _levels[key] = level;
            return true;
        }

        private void Missing(object where, string what)
        {
            Type type = where.GetType();
            Say($"Could not read {what} from {type.Name}. It has: {Dyn.Describe(type)}");
        }

        private void Say(string message)
        {
            if (_said.Add(message)) _log.Warn(message);
        }
    }
}
