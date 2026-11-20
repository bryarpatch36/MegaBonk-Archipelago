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

// Stand-ins for the game's own classes. They carry the member names the mod reads (listed in
// docs/hooks.md), and the collections are deliberately not .NET collections, like the game's.
namespace MegabonkAP.Tests.FakeGame
{
    public enum EMap { None, Forest, Desert, Graveyard, Hell }
    public enum ECharacter { Fox, SirOofie, Calcium }
    public enum EWeapon { FireStaff, Sword, Sniper, Bone }
    public enum ETome { Damage, Hp, Chaos }
    public enum EEnemy { Goblin, MinibossPig, MinibossGolem, ForestBoss1, GraveyardBob }

    /// <summary>A list that is not IEnumerable: only GetEnumerator, MoveNext and Current, like the game's.</summary>
    public sealed class GameList<T>
    {
        private readonly List<T> _items = new();
        public void Add(T item) => _items.Add(item);
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

    public sealed class GameDict<TKey, TValue> where TKey : notnull
    {
        private readonly List<Entry> _entries = new();
        public void Set(TKey key, TValue value)
        {
            _entries.RemoveAll(e => e.Key.Equals(key));
            _entries.Add(new Entry { Key = key, Value = value });
        }
        public Walker GetEnumerator() => new Walker(_entries);

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
    public sealed class RunConfig { public MapData? mapData; public StageData? stageData; }
    public sealed class WeaponBase { public int level { get; set; } }
    public sealed class WeaponInventory { public GameDict<EWeapon, WeaponBase> weapons = new(); }
    public sealed class TomeInventory { public GameDict<ETome, int> tomeLevels = new(); }
    public sealed class PlayerXp { public int level; }

    public sealed class PlayerInventory
    {
        public WeaponInventory weaponInventory = new();
        public TomeInventory tomeInventory = new();
        public PlayerXp playerXp = new();
    }

    public sealed class MyPlayer
    {
        public PlayerInventory inventory = new();
        public CharacterData characterData = new();
    }

    public sealed class GameManager
    {
        public static GameManager? Instance;
        public MyPlayer? player;
    }

    public sealed class EnemyData { public EEnemy enemyName; }

    public sealed class Enemy
    {
        public EnemyData enemyData = new();
        public int enemyFlag;
        public IntPtr Pointer;
        public bool IsStageBoss() => (enemyFlag & 4) != 0;
        public bool IsFinalBoss() => (enemyFlag & 32) != 0;
    }

    /// <summary>An enemy from a game version that renamed everything the mod reads.</summary>
    public sealed class RenamedEnemy { public string kind = "Goblin"; public int hp; }
}
