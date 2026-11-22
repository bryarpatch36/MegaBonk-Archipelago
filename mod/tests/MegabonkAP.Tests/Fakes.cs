using System;
using System.Collections.Generic;
using System.Linq;
using MegabonkAP.Core;

namespace MegabonkAP.Tests
{
    /// <summary>Keeps what the mod writes to its log.</summary>
    public sealed class ListLog : ILog
    {
        public readonly List<string> Lines = new();
        public void Info(string message) { lock (Lines) Lines.Add("info: " + message); }
        public void Warn(string message) { lock (Lines) Lines.Add("warn: " + message); }
        public bool Has(string part) { lock (Lines) return Lines.Any(l => l.Contains(part)); }
        public int Count(string part) { lock (Lines) return Lines.Count(l => l.Contains(part)); }
    }

    /// <summary>Stands in for the Archipelago server.</summary>
    public sealed class FakeLink : IServerLink
    {
        public bool Connected { get; set; } = true;
        public string? Session { get; set; } = "seed-a:1";
        public List<string> Items = new();
        public HashSet<string> CheckedAlready = new();
        public HashSet<string> NotInSeed = new();
        public List<string> Sent = new();
        public int Goals;
        public List<string> Deaths = new();
        public Dictionary<string, object> Slot = new() { ["goal"] = 1L, ["death_link"] = 0L };

        public IReadOnlyList<string> ReceivedItems => Items;
        public bool IsChecked(string check) => CheckedAlready.Contains(check) || Sent.Contains(check);
        public bool Exists(string check) => !NotInSeed.Contains(check);
        public IReadOnlyCollection<string> CheckedNames => CheckedAlready.Concat(Sent).Distinct().ToList();
        public int TotalChecks { get; set; } = 110;

        public IReadOnlyCollection<string> Send(IEnumerable<string> checks)
        {
            List<string> taken = checks.ToList();
            Sent.AddRange(taken);
            return taken;
        }

        public void SendGoal() => Goals++;
        public void SendDeath(string cause) => Deaths.Add(cause);
        public object? SlotValue(string option) => Slot.TryGetValue(option, out object? value) ? value : null;

        public event Action? Changed;
        public event Action<string, string>? DeathReceived;

        /// <summary>Give items and tell the tracker, as the server does.</summary>
        public void Give(params string[] items)
        {
            Items.AddRange(items);
            Changed?.Invoke();
        }

        public void Announce() => Changed?.Invoke();
        public void Die(string who, string why) => DeathReceived?.Invoke(who, why);
    }
}

// Stand-ins for the game's own classes, shaped after game version 1.0.69 as the probe recorded it
// (docs/probes/probe-1.0.69.txt): the same type names, member names, statics and enum values.
// The collections are deliberately not .NET collections, like the game's.
namespace MegabonkAP.Tests.FakeGame
{
    public enum EMap { None = 0, Forest = 1, Desert = 2, Hell = 4, Graveyard = 8 }
    public enum ECharacter { Fox = 0, Calcium = 1, SirOofie = 2, Cl4nk = 3 }
    public enum EWeapon { None = -1, FireStaff = 0, Bone = 1, Sword = 2, Test = 8, Sniper = 15 }
    public enum ETome { Damage = 0, Health = 6, Regeneration = 7, Blood = 18, Bounce = 20, Chaos = 24 }

    public enum EEnemy
    {
        Skeleton = 0, MinibossPig = 7, Goblin = 12, MinibossGolem = 15, Ent1 = 19, Ent2 = 20, Golem = 23,
        MinibossGolemSand = 25, Scorpion = 26, MinibossScorpion = 27, Bandit = 37, Bush = 38, GhostKing = 43,
    }

    [Flags]
    public enum EEnemyFlag { None = 0, Elite = 1, Boss = 2, StageBoss = 4, Challenge = 8, SummonerMiniboss = 16, FinalBoss = 32 }

    /// <summary>A list that is not IEnumerable: only GetEnumerator, MoveNext and Current, like the game's.</summary>
    public sealed class GameList<T>
    {
        private readonly List<T> _items = new();
        public void Add(T item) => _items.Add(item);
        public bool Remove(T item) => _items.Remove(item);
        public bool Contains(T item) => _items.Contains(item);
        public int Count => _items.Count;
        public Walker GetEnumerator() => new Walker(_items);

        public sealed class Walker
        {
            private readonly List<T> _items;
            private int _at = -1;
            public Walker(List<T> items) { _items = items; }
            public bool MoveNext() => ++_at < _items.Count;
            public T Current => _items[_at];
        }
    }

    /// <summary>A dictionary that can be asked ContainsKey and walked, but is not a .NET dictionary.</summary>
    public class GameDict<TKey, TValue> where TKey : notnull
    {
        protected readonly List<Entry> Entries = new();
        public void Set(TKey key, TValue value)
        {
            Entries.RemoveAll(e => e.Key.Equals(key));
            Entries.Add(new Entry { Key = key, Value = value });
        }
        public TValue? Find(TKey key) => Entries.Where(e => e.Key.Equals(key)).Select(e => e.Value).FirstOrDefault();
        public bool ContainsKey(TKey key) => Entries.Any(e => e.Key.Equals(key));
        public Walker GetEnumerator() => new Walker(Entries);

        public sealed class Entry { public TKey Key = default!; public TValue Value = default!; }
        public sealed class Walker
        {
            private readonly List<Entry> _entries;
            private int _at = -1;
            public Walker(List<Entry> entries) { _entries = entries; }
            public bool MoveNext() => ++_at < _entries.Count;
            public Entry Current => _entries[_at];
        }
    }

    public sealed class StageData { public string name = ""; }
    public sealed class MapData { public EMap eMap; public GameList<StageData> stages = new(); }
    public sealed class CharacterData { public ECharacter eCharacter; }
    public sealed class WeaponData { public EWeapon eWeapon; }
    public sealed class TomeData { public ETome eTome; }
    public sealed class RenamedTomeData { public string title = ""; public ETome which; }      // a game update renamed the member
    public sealed class MyButtonCharacter { public CharacterData characterData = new(); }
    public sealed class PlainButton { public string label = ""; }
    public sealed class CharacterMenu { public ECharacter selectedCharacter; }
    public sealed class SelectionGroupToggleSingleButtonTier { public RunConfig? runConfig; }
    public sealed class ItemData { public string name = ""; }

    /// <summary>How the game hands an offer over: as "something upgradable" that must be asked for its real type.</summary>
    public sealed class IUpgradable
    {
        private readonly object _real;
        public IUpgradable(object real) { _real = real; }
        public T? TryCast<T>() where T : class => _real as T;
    }

    public sealed class UpgradeButton
    {
        public string label = "";
        public IUpgradable? upgradable;
        public UnityEngine.GameObject gameObject = new("UpgradeButton");
    }

    public sealed class LevelupScreen
    {
        public UpgradePicker? picker;
        public bool SkipWorks = true;
        public int Skips, Closes, ButtonRefreshes;
        public void Skip() { Skips++; if (SkipWorks) picker!.window.SetActive(false); }
        public void CloseLevelupScreen() { Closes++; picker!.window.SetActive(false); }
        public void RefreshUtilityButtons() => ButtonRefreshes++;
    }

    public sealed class UpgradePicker
    {
        public GameList<UpgradeButton> buttons = new();
        public bool banishMode;
        public UnityEngine.GameObject window = new("LevelupWindow");
        public LevelupScreen levelupScreen = new();
        public UpgradePicker() { levelupScreen.picker = this; }
    }

    public sealed class ChallengeData
    {
        public string name = "";            // the asset's name
        public string shown = "";           // what the menu shows
        public string GetDisplayName() => shown;
    }

    public sealed class RunConfig
    {
        public ChallengeData? challenge;
        public MapData? mapData;
        public int mapTierIndex;
        public StageData? stageData;
    }

    public sealed class WeaponBase { public int level { get; set; } }

    public sealed class WeaponInventory
    {
        public GameDict<EWeapon, WeaponBase> weapons = new();
        public int GetWeaponLevel(EWeapon eWeapon) => weapons.Find(eWeapon)!.level;     // fails for a weapon not held, like the game's
    }

    public sealed class TomeInventory
    {
        public GameDict<ETome, int> tomeLevels = new();
        public bool HasTome(ETome eTome) => tomeLevels.ContainsKey(eTome);
        public int GetTomeLevel(ETome tome) => tomeLevels.Find(tome);
    }

    public sealed class PlayerXp { public int level; }

    public sealed class PlayerHealth
    {
        public int Killed;
        public int hp = 100;
        public float shield = 5f;
        public bool KillWorks = true;
        public int Checked;
        public void KillPlayer() { Killed++; if (KillWorks) hp = 0; }
        public bool IsDead() => hp <= 0;
        public void CheckDead() => Checked++;
    }

    public sealed class PlayerInventory
    {
        public CharacterData characterData = new();
        public WeaponInventory weaponInventory = new();
        public TomeInventory tomeInventory = new();
        public PlayerXp playerXp = new();
        public PlayerHealth playerHealth = new();
    }

    public sealed class MyPlayer
    {
        public ECharacter character;
        public PlayerInventory inventory = new();
    }

    public sealed class GameManager
    {
        public static GameManager? Instance;
        public MyPlayer? player;
    }

    public static class MapController
    {
        public static RunConfig? runConfig;
        public static PlayerInventory? inventory;
        public static bool TierFinal;
        public static bool IsTierFinalStage() => TierFinal;
    }

    public static class ChallengesTracker
    {
        public static ChallengeData? Current;
        public static ChallengeData? GetCurrentChallenge() => Current;
    }

    public sealed class EnemyData { public EEnemy enemyName; }

    public sealed class Enemy
    {
        public EnemyData enemyData = new();
        public EEnemyFlag enemyFlag;
        public IntPtr Pointer;
    }

    public sealed class GraveyardBossRoom { public Enemy? bossEnemy; }

    /// <summary>An enemy from a game version that renamed everything the mod reads.</summary>
    public sealed class RenamedEnemy { public string kind = "Goblin"; public int hp; }

    /// <summary>Inventories from a game version without GetWeaponLevel, HasTome and GetTomeLevel.</summary>
    public sealed class OlderWeaponInventory { public GameList<GameDict<EWeapon, WeaponBase>.Entry> weapons = new(); }
    public sealed class OlderTomeInventory { public GameList<GameDict<ETome, int>.Entry> tomeLevels = new(); }
}
