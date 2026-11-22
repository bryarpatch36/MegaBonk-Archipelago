using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace MegabonkAP.Core
{
    /// <summary>
    /// The world's names, read from checks.json (written by make_data.py): which item permits
    /// which thing, and which check belongs to which event in the game.
    /// </summary>
    public sealed class CheckList
    {
        public sealed record Character(string Name, string Item, string Weapon, string RunCheck, string LevelCheck);
        public sealed record Leveled(string Name, string Item, string LevelCheck);

        public sealed class Map
        {
            public string Name = "";
            public string Item = "";
            public List<string> TierChecks = new();
            public Dictionary<string, string> ChallengeChecks = new();   // challenge name -> check
            public string? KeysCheck;
        }

        public string Game { get; private set; } = "";
        public int CharacterLevel { get; private set; }
        public int WeaponLevel { get; private set; }
        public int TomeLevel { get; private set; }
        public IReadOnlyDictionary<string, Character> Characters => _characters;
        public IReadOnlyDictionary<string, Leveled> Weapons => _weapons;
        public IReadOnlyDictionary<string, Leveled> Tomes => _tomes;
        public IReadOnlyDictionary<string, Map> Maps => _maps;
        public IReadOnlyList<string> Goals => _goals;

        /// <summary>Bosses the mod recognises by what happens ("Big Bob"), each with its check.</summary>
        public IReadOnlyDictionary<string, string> BossChecks => _bossChecks;

        private readonly Dictionary<string, Character> _characters = new();
        private readonly Dictionary<string, Leveled> _weapons = new();
        private readonly Dictionary<string, Leveled> _tomes = new();
        private readonly Dictionary<string, Map> _maps = new();
        private readonly List<string> _goals = new();
        private readonly Dictionary<string, string> _bossChecks = new();
        private readonly Dictionary<string, string> _enemyChecks = new();     // the game's enemy name, squeezed -> check
        private readonly Dictionary<string, HashSet<string>> _ignored = new();
        // kind -> (name as the game writes it, squeezed) -> the world's name
        private readonly Dictionary<string, Dictionary<string, string>> _lookup = new();

        public static CheckList Load()
        {
            using Stream stream = typeof(CheckList).Assembly.GetManifestResourceStream("checks.json")
                ?? throw new InvalidOperationException("checks.json is not embedded in MegabonkAP.Core");
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return Parse(reader.ReadToEnd());
        }

        public static CheckList Parse(string json)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            var list = new CheckList { Game = root.GetProperty("game").GetString()! };

            JsonElement levels = root.GetProperty("levels");
            list.CharacterLevel = levels.GetProperty("character").GetInt32();
            list.WeaponLevel = levels.GetProperty("weapon").GetInt32();
            list.TomeLevel = levels.GetProperty("tome").GetInt32();

            foreach (JsonProperty p in root.GetProperty("characters").EnumerateObject())
                list._characters[p.Name] = new Character(p.Name, Text(p.Value, "item"), Text(p.Value, "weapon"),
                    Text(p.Value, "run"), Text(p.Value, "level"));
            foreach (JsonProperty p in root.GetProperty("weapons").EnumerateObject())
                list._weapons[p.Name] = new Leveled(p.Name, Text(p.Value, "item"), Text(p.Value, "level"));
            foreach (JsonProperty p in root.GetProperty("tomes").EnumerateObject())
                list._tomes[p.Name] = new Leveled(p.Name, Text(p.Value, "item"), Text(p.Value, "level"));

            foreach (JsonProperty p in root.GetProperty("maps").EnumerateObject())
            {
                var map = new Map { Name = p.Name, Item = Text(p.Value, "item") };
                map.TierChecks.AddRange(p.Value.GetProperty("tiers").EnumerateArray().Select(e => e.GetString()!));
                map.ChallengeChecks = Pairs(p.Value.GetProperty("challenges"));
                if (p.Value.TryGetProperty("keys", out JsonElement keys)) map.KeysCheck = keys.GetString();
                list._maps[p.Name] = map;
            }

            list._goals.AddRange(root.GetProperty("goals").EnumerateArray().Select(e => e.GetString()!));
            foreach (KeyValuePair<string, string> boss in Pairs(root.GetProperty("bosses"))) list._bossChecks[boss.Key] = boss.Value;
            foreach (KeyValuePair<string, string> enemy in Pairs(root.GetProperty("enemies")))
                list._enemyChecks[Squeeze(enemy.Key)] = enemy.Value;
            if (root.TryGetProperty("miniboss_hunt", out JsonElement hunt))
                foreach (JsonProperty p in hunt.EnumerateObject()) list._hunt[int.Parse(p.Name)] = p.Value.GetString()!;
            foreach (JsonProperty kind in root.GetProperty("ignored").EnumerateObject())
                list._ignored[kind.Name] = kind.Value.EnumerateArray().Select(e => Squeeze(e.GetString()!)).ToHashSet();

            list.Index("character", list._characters.Keys, root);
            list.Index("weapon", list._weapons.Keys, root);
            list.Index("tome", list._tomes.Keys, root);
            list.Index("map", list._maps.Keys, root);
            return list;
        }

        /// <summary>Every check the mod can send.</summary>
        public IEnumerable<string> AllChecks()
        {
            foreach (Character c in _characters.Values) { yield return c.RunCheck; yield return c.LevelCheck; }
            foreach (Leveled w in _weapons.Values) yield return w.LevelCheck;
            foreach (Leveled t in _tomes.Values) yield return t.LevelCheck;
            foreach (Map m in _maps.Values)
            {
                foreach (string check in m.TierChecks) yield return check;
                foreach (string check in m.ChallengeChecks.Values) yield return check;
                if (m.KeysCheck != null) yield return m.KeysCheck;
            }
            foreach (string check in _enemyChecks.Values) yield return check;
            foreach (string check in _bossChecks.Values) yield return check;
            foreach (string check in _hunt.Values) yield return check;
        }

        /// <summary>Minibosses defeated in total -> the check earned at that count, lowest first.</summary>
        public IReadOnlyDictionary<int, string> MinibossHunt => _hunt;
        private readonly SortedDictionary<int, string> _hunt = new();

        /// <summary>Every item the mod reads permissions from.</summary>
        public IEnumerable<string> AllItems() =>
            _characters.Values.Select(c => c.Item)
                .Concat(_weapons.Values.Select(w => w.Item))
                .Concat(_tomes.Values.Select(t => t.Item))
                .Concat(_maps.Values.Select(m => m.Item));

        /// <summary>
        /// The world's name for something the game names in its own way ("FireStaff", "Sniper",
        /// "SirOofie"), or null when nothing matches. kind is character, weapon, tome or map.
        /// </summary>
        public string? Resolve(string kind, string? gameName)
        {
            if (string.IsNullOrWhiteSpace(gameName)) return null;
            return _lookup[kind].TryGetValue(Squeeze(gameName), out string? name) ? name : null;
        }

        /// <summary>True for a name the game has and the world deliberately does not ("Test", a tome not in the world).</summary>
        public bool IsIgnored(string kind, string? gameName) =>
            !string.IsNullOrWhiteSpace(gameName) && _ignored.TryGetValue(kind, out HashSet<string>? names)
            && names.Contains(Squeeze(gameName));

        /// <summary>The check a killed enemy earns, by the game's exact name for it ("MinibossPig"), or null.</summary>
        public string? EnemyCheck(string? gameName)
        {
            if (string.IsNullOrWhiteSpace(gameName)) return null;
            return _enemyChecks.TryGetValue(Squeeze(gameName), out string? check) ? check : null;
        }

        /// <summary>The challenge check for a challenge the game names in its own way, or null.</summary>
        public string? ResolveChallenge(string map, string? gameName)
        {
            if (string.IsNullOrWhiteSpace(gameName) || !_maps.TryGetValue(map, out Map? where)) return null;
            string squeezed = Squeeze(gameName);
            foreach (KeyValuePair<string, string> challenge in where.ChallengeChecks)
                if (Squeeze(challenge.Key) == squeezed) return challenge.Value;
            return null;
        }

        /// <summary>Lower case, letters and digits only: "Dragon's Breath" and "DragonsBreath" are the same.</summary>
        public static string Squeeze(string text)
        {
            var builder = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (char.IsLetterOrDigit(c)) builder.Append(char.ToLowerInvariant(c));
                else if (c == '+') builder.Append("plus");     // Speedrunner and Speedrunner+ differ
            }
            return builder.ToString();
        }

        private void Index(string kind, IEnumerable<string> names, JsonElement root)
        {
            var table = new Dictionary<string, string>();
            foreach (string name in names) table[Squeeze(name)] = name;
            if (kind == "tome")
                foreach (string name in names) table[Squeeze(name + " Tome")] = name;
            if (root.GetProperty("aliases").TryGetProperty(kind, out JsonElement aliases))
                foreach (JsonProperty alias in aliases.EnumerateObject())
                {
                    table[Squeeze(alias.Name)] = alias.Value.GetString()!;
                    if (kind == "tome") table[Squeeze(alias.Name + " Tome")] = alias.Value.GetString()!;
                }
            _lookup[kind] = table;
        }

        private static string Text(JsonElement element, string name) => element.GetProperty(name).GetString()!;

        private static Dictionary<string, string> Pairs(JsonElement element) =>
            element.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!);
    }
}
