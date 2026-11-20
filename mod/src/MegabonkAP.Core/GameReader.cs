using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace MegabonkAP.Core
{
    /// <summary>
    /// Takes the game's own objects from the plugin's hooks, reads what the tracker needs out of
    /// them by member name, and passes it on. Every member name used here is listed in
    /// docs/hooks.md with where it comes from; when one is missing the log says which, and lists
    /// the members the object really has.
    /// </summary>
    public sealed class GameReader
    {
        private readonly Tracker _tracker;
        private readonly ILog _log;
        private readonly Func<string, Type?> _findType;
        private readonly HashSet<string> _said = new();
        private readonly object _gate = new();

        private readonly Dictionary<string, int> _levels = new();      // "weapon:Sword" -> last level passed on
        private readonly Queue<long> _recentOrder = new();
        private readonly HashSet<long> _recent = new();                 // enemies whose death was already counted
        private int _playerLevel = -1;
        private bool _characterKnown;

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
                _recentOrder.Clear();
                _playerLevel = -1;

                if (runConfig == null)
                {
                    Say("A run started but the game gave no run settings to read.");
                    _tracker.RunStarted(null, null, 0);
                    return;
                }

                string? map = Dyn.Name(Dyn.First(runConfig, "mapData.eMap", "mapData.name"));
                if (map == null) Missing(runConfig, "the map (mapData.eMap)");

                int tier = Tier(runConfig);
                string? character = Dyn.Name(Dyn.First(runConfig,
                    "characterData.eCharacter", "character.eCharacter", "eCharacter", "characterData.name"))
                    ?? CharacterFromPlayer();
                _characterKnown = character != null;

                string? challenge = Dyn.Name(Dyn.First(runConfig,
                    "challengeData.name", "challengeData", "challenge.name", "challenge"));

                _tracker.RunStarted(character, map, tier, challenge);
            }
        }

        /// <summary>TrackStats.OnEnemyDied(Enemy, DamageContainer) or Enemy.EnemyDied(): an enemy died.</summary>
        public void EnemyDied(object? enemy)
        {
            if (enemy == null) return;
            lock (_gate)
            {
                if (!FirstTime(enemy)) return;

                string? name = Dyn.Name(Dyn.First(enemy, "enemyData.enemyName", "enemyData.name"));
                if (name == null) { Missing(enemy, "the enemy's name (enemyData.enemyName)"); return; }

                bool stageBoss;
                if (Dyn.Has(enemy.GetType(), "IsStageBoss()"))
                    stageBoss = Dyn.Bool(Dyn.Get(enemy, "IsStageBoss()")) || Dyn.Bool(Dyn.Get(enemy, "IsFinalBoss()"));
                else
                {
                    int flags = Dyn.Int(Dyn.Get(enemy, "enemyFlag")) ?? 0;      // EEnemyFlag: StageBoss 4, FinalBoss 32
                    stageBoss = (flags & (4 | 32)) != 0;
                }
                _tracker.EnemyKilled(name, stageBoss);
            }
        }

        /// <summary>PlayerXp.AddXp(int): experience was gained, so the level may have changed.</summary>
        public void XpChanged(object? playerXp)
        {
            lock (_gate)
            {
                int? level = Dyn.Int(Dyn.First(playerXp, "level", "currentLevel", "Level", "GetLevel()"))
                    ?? Dyn.Int(Dyn.First(Player(), "inventory.playerXp.level", "playerXp.level", "inventory.level"));
                if (level == null)
                {
                    if (playerXp != null) Missing(playerXp, "the player's level (level)");
                    return;
                }
                if (level.Value == _playerLevel) return;
                _playerLevel = level.Value;
                if (!_characterKnown)
                {
                    string? character = CharacterFromPlayer();
                    if (character != null) { _characterKnown = true; _tracker.CharacterSeen(character); }
                }
                _tracker.PlayerLevel(level.Value);
            }
        }

        /// <summary>
        /// WeaponInventory.AddWeapon, TomeInventory.AddTome, LevelupScreen.OnClose: a weapon or tome
        /// was taken or upgraded. Reads every weapon's and tome's level and passes on what changed.
        /// </summary>
        public void InventoryChanged()
        {
            lock (_gate)
            {
                object? inventory = Dyn.First(Player(), "inventory");
                if (inventory == null) { Say("The player's inventory could not be read (GameManager.Instance.player.inventory)."); return; }

                object? weapons = Dyn.Get(inventory, "weaponInventory.weapons");
                if (weapons == null) Missing(inventory, "the weapons (weaponInventory.weapons)");
                foreach ((object? key, object? value) in Dyn.Pairs(weapons))
                {
                    string? name = Dyn.Name(key);
                    int? level = Dyn.Int(value) ?? Dyn.Int(Dyn.First(value, "level", "Level"));
                    if (name == null || level == null) continue;
                    if (Changed("weapon:" + name, level.Value)) _tracker.WeaponLevel(name, level.Value);
                }

                object? tomes = Dyn.First(inventory, "tomeInventory.tomeLevels", "tomeInventory.tomes");
                if (tomes == null) Missing(inventory, "the tomes (tomeInventory.tomeLevels)");
                foreach ((object? key, object? value) in Dyn.Pairs(tomes))
                {
                    string? name = Dyn.Name(key);
                    int? level = Dyn.Int(value) ?? Dyn.Int(Dyn.First(value, "level", "Level"));
                    if (name == null || level == null) continue;
                    if (Changed("tome:" + name, level.Value)) _tracker.TomeLevel(name, level.Value);
                }
            }
        }

        /// <summary>PlayerHealth.PlayerDied().</summary>
        public void PlayerDied() => _tracker.PlayerDied();

        /// <summary>GraveyardBossRoom.Activate().</summary>
        public void CryptOpened() => _tracker.CryptOpened();

        /// <summary>GameManager.StopDungeon().</summary>
        public void RunEnded() => _tracker.RunEnded();

        // ------------------------------------------------------------------ inside

        /// <summary>
        /// The tier is the place of the chosen stage in the map's list of stages: what the menu
        /// calls Tier 1, 2, 3 the game keeps as mapData.stages[0], [1], [2]. 0 when it cannot be read.
        /// </summary>
        private int Tier(object runConfig)
        {
            object? stage = Dyn.Get(runConfig, "stageData");
            object? stages = Dyn.Get(runConfig, "mapData.stages");
            if (stage == null || stages == null)
            {
                Missing(runConfig, "the tier (stageData and mapData.stages)");
                return 0;
            }
            long wanted = Identity(stage);
            string? wantedName = Dyn.Name(stage);
            int index = 0, byName = 0;
            foreach (object? candidate in Dyn.Items(stages))
            {
                index++;
                if (candidate == null) continue;
                if (ReferenceEquals(candidate, stage) || Identity(candidate) == wanted) return index;
                if (byName == 0 && wantedName != null && Dyn.Name(candidate) == wantedName) byName = index;
            }
            if (byName == 0) Say("The chosen stage is not in the map's list of stages, so the tier is unknown.");
            return byName;
        }

        private string? CharacterFromPlayer() => Dyn.Name(Dyn.First(Player(),
            "characterData.eCharacter", "character.eCharacter", "eCharacter", "characterData.name"));

        private object? Player() => Dyn.GetStatic(_findType("GameManager"), "Instance.player");

        /// <summary>The game's objects reach the mod through wrappers; two wrappers of one object share its Pointer.</summary>
        private static long Identity(object thing)
        {
            object? pointer = Dyn.Get(thing, "Pointer");
            if (pointer is IntPtr p) return p.ToInt64();
            return RuntimeHelpers.GetHashCode(thing);
        }

        private bool FirstTime(object enemy)
        {
            long id = Identity(enemy);
            if (!_recent.Add(id)) return false;
            _recentOrder.Enqueue(id);
            if (_recentOrder.Count > 512) _recent.Remove(_recentOrder.Dequeue());
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
