using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace MegabonkAP.Core
{
    /// <summary>Finds a game type by its short name, preferring the game's own assembly.</summary>
    public sealed class TypeFinder
    {
        private readonly Func<IEnumerable<Assembly>> _assemblies;
        private readonly Dictionary<string, Type?> _found = new();
        private readonly object _gate = new();

        public TypeFinder(Func<IEnumerable<Assembly>>? assemblies = null)
        {
            _assemblies = assemblies ?? (() => AppDomain.CurrentDomain.GetAssemblies());
        }

        public Type? Find(string name)
        {
            lock (_gate)
            {
                if (_found.TryGetValue(name, out Type? known) && known != null) return known;
                Type? best = null;
                foreach (Assembly assembly in _assemblies())
                {
                    bool game = assembly.GetName().Name == "Assembly-CSharp";
                    foreach (Type type in Probe.TypesOf(assembly))
                    {
                        if (type.Name != name && type.FullName != name) continue;
                        if (game) { _found[name] = type; return type; }
                        best ??= type;
                    }
                }
                _found[name] = best;
                return best;
            }
        }
    }

    /// <summary>
    /// Writes down what the game's code really looks like: the members of the types the mod
    /// hooks, the values of the enums it reads, and every type whose name suggests something the
    /// mod still needs (a win screen, challenges, the Toggler). Reading code only: nothing in the
    /// game is called or changed.
    /// </summary>
    public static class Probe
    {
        public const int Format = 2;

        /// <summary>The types the mod hooks or reads, and the enums whose value names it matches against.</summary>
        public static readonly string[] Wanted =
        {
            "GameManager", "MapController", "RunConfig", "MapData", "StageData", "MyPlayer", "PlayerInventory",
            "WeaponInventory", "TomeInventory", "ItemInventory", "PlayerXp", "PlayerHealth", "Enemy", "EnemyData",
            "TrackStats", "LevelupScreen", "UpgradePicker", "CharacterMenu", "CharacterData", "GraveyardBossRoom",
            "FinalFightController", "ChallengeSummoner", "SaveManager", "DataManager",
            "ChallengesTracker", "ChallengeData", "MyAchievement", "DeathScreen", "WeaponBase", "WeaponData",
            "TomeData", "UnlockableBase", "InteractableCoffin", "InteractableCrypt", "GraveyardBossMetalGate",
            "InteractableGhostBossLeave", "InteractableCharacterFight", "UnlocksUi", "UnlockContainer",
            "UpgradeButton", "IUpgradable", "MyButton", "MyButtonNormal", "MyButtonCharacter", "UnlockUtility", "UpgradeInventoryUI",
            "EMap", "ECharacter", "EWeapon", "ETome", "EEnemy", "EEnemyFlag", "EItem", "EDungeonType",
        };

        /// <summary>Words that mark types worth a look for what is still unknown.</summary>
        public const string Words = "victory|gameover|runend|endscreen|challenge|tier|toggl|unlock|crypt|coffin|ghost|portal|swarm|banish|upgradepick|encounter";

        public static IEnumerable<Type> TypesOf(Assembly assembly)
        {
            try { return assembly.GetTypes(); }
            catch (ReflectionTypeLoadException partial) { return partial.Types.Where(t => t != null)!; }
            catch (Exception) { return Array.Empty<Type>(); }
        }

        public static string Report(IEnumerable<Type> types, IEnumerable<string>? wanted = null, string? words = null)
        {
            List<Type> all = types.ToList();
            var text = new StringBuilder();
            text.AppendLine($"MegabonkAP probe, format {Format}: {all.Count} types read");
            text.AppendLine();

            text.AppendLine("== Types the mod uses ==");
            foreach (string name in wanted ?? Wanted)
            {
                List<Type> matches = all.Where(t => t.Name == name).ToList();
                if (matches.Count == 0) { text.AppendLine($"MISSING  {name}"); text.AppendLine(); continue; }
                foreach (Type type in matches) Describe(type, text, full: true);
            }

            var pattern = new Regex(words ?? Words, RegexOptions.IgnoreCase);
            text.AppendLine("== Types whose name suggests something still needed ==");
            foreach (Type type in all.Where(t => pattern.IsMatch(t.Name) && !t.Name.Contains('<')).OrderBy(t => t.FullName))
                Describe(type, text, full: false);
            return text.ToString();
        }

        private const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public
            | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        private static void Describe(Type type, StringBuilder text, bool full)
        {
            text.AppendLine($"{(type.IsEnum ? "enum" : "type")} {type.FullName}"
                + (type.BaseType != null && !type.IsEnum ? $" : {type.BaseType.Name}" : ""));
            try
            {
                if (type.IsEnum)
                {
                    foreach (object value in Enum.GetValues(type))
                        text.AppendLine($"    {value} = {Convert.ToInt64(value)}");
                    text.AppendLine();
                    return;
                }

                IEnumerable<string> properties = type.GetProperties(Declared)
                    .Where(p => !Noise(p.Name))
                    .Select(p => $"{((p.GetMethod ?? p.SetMethod)?.IsStatic == true ? "static " : "")}{Short(p.PropertyType)} {p.Name}");
                IEnumerable<string> fields = type.GetFields(Declared)
                    .Where(f => !Noise(f.Name))
                    .Select(f => $"{(f.IsStatic ? "static " : "")}{Short(f.FieldType)} {f.Name}");
                IEnumerable<string> methods = type.GetMethods(Declared)
                    .Where(m => !m.IsSpecialName && !Noise(m.Name))
                    .Select(m => $"{(m.IsStatic ? "static " : "")}{Short(m.ReturnType)} {m.Name}("
                        + string.Join(", ", m.GetParameters().Select(p => $"{Short(p.ParameterType)} {p.Name}")) + ")");

                List<string> lines = properties.Concat(fields).OrderBy(l => l.Split(' ').Last()).ToList();
                List<string> calls = methods.OrderBy(l => l).Distinct().ToList();
                int limit = full ? 400 : 40;
                foreach (string line in lines.Take(limit)) text.AppendLine("    " + line);
                foreach (string line in calls.Take(limit)) text.AppendLine("    " + line);
                if (lines.Count > limit || calls.Count > limit) text.AppendLine("    ...");
            }
            catch (Exception problem)
            {
                text.AppendLine($"    (could not be read: {problem.GetType().Name}: {problem.Message})");
            }
            text.AppendLine();
        }

        private static bool Noise(string name) =>
            name.Contains('<') || name.StartsWith("NativeFieldInfoPtr", StringComparison.Ordinal)
            || name.StartsWith("NativeMethodInfoPtr", StringComparison.Ordinal)
            || name.StartsWith("NativeClassPtr", StringComparison.Ordinal);

        private static string Short(Type type)
        {
            if (!type.IsGenericType) return type.Name;
            string name = type.Name;
            int tick = name.IndexOf('`');
            if (tick > 0) name = name.Substring(0, tick);
            return name + "<" + string.Join(", ", type.GetGenericArguments().Select(Short)) + ">";
        }
    }
}
