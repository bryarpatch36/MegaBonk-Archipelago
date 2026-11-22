using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MegabonkAP.Core
{
    /// <summary>
    /// The text of the on-screen tracker, made from what the tracker knows. It only writes text:
    /// the plugin puts it on the screen. Colours are TextMeshPro's rich text tags.
    /// </summary>
    public static class Overlay
    {
        public const int Hidden = 0, Compact = 1, Full = 2;

        private const string Bad = "#ff8a80", Good = "#9be29b", Dim = "#b8c0cc", Head = "#f2b134";

        /// <summary>How long a note stays on screen.</summary>
        public static readonly TimeSpan NoteLife = TimeSpan.FromSeconds(25);

        /// <summary>The next view when the key is pressed: compact, full, hidden, compact...</summary>
        public static int Next(int mode) => mode == Compact ? Full : mode == Full ? Hidden : Compact;

        public static int ParseMode(string? text) => (text ?? "").Trim().ToLowerInvariant() switch
        {
            "hidden" or "off" => Hidden,
            "full" => Full,
            _ => Compact,
        };

        public static string ModeName(int mode) => mode == Hidden ? "Hidden" : mode == Full ? "Full" : "Compact";

        /// <summary>
        /// The overlay's text. slotSet: a slot name is configured. key: the key that changes the
        /// view, shown as a hint, or null when there is none.
        /// </summary>
        public static string Render(Tracker.Snapshot s, CheckList list, int mode, DateTime now, bool slotSet = true, string? key = null)
        {
            if (mode == Hidden) return "";
            var text = new StringBuilder();

            text.Append($"<b><color={Head}>Archipelago</color></b>  ");
            if (!slotSet) text.Append($"<color={Bad}>no slot name set</color>");
            else if (!s.ItemsKnown) text.Append($"<color={Bad}>not connected</color>");
            else
            {
                text.Append($"{s.Done}/{s.Total} checks");
                if (!s.Connected) text.Append($"  <color={Bad}>connection lost</color>");
                if (s.Owed > 0) text.Append($"  <color={Dim}>({s.Owed} waiting to be sent)</color>");
                if (s.DeathLink != null) text.Append($"  <color={Dim}>DeathLink {(s.DeathLink == "on" ? "on" : "off")}</color>");
            }
            text.Append('\n');

            if (s.InRun)
            {
                text.Append(s.Map ?? "Unknown map");
                if (s.Tier > 0 && s.Map != null && list.Maps[s.Map].TierChecks.Count > 0) text.Append($" Tier {s.Tier}");
                if (s.Character != null) text.Append($" as {s.Character}");
                text.Append('\n');
                if (s.RunProblem != null) text.Append($"<color={Bad}>Does not count: {s.RunProblem}</color>\n");
                else
                {
                    List<string> open = OpenHere(s, list);
                    if (open.Count > 0) text.Append($"<color={Dim}>Open here:</color> {string.Join(", ", open)}\n");
                }
            }

            if (s.ItemsKnown)
            {
                if (mode == Full) AppendLists(text, s, list);
                else AppendCounts(text, s, list);
            }

            foreach (Tracker.Note note in s.Notes.Where(n => now - n.At < NoteLife).TakeLast(mode == Full ? 6 : 4))
                text.Append(note.Bad ? $"<color={Bad}>> {note.Text}</color>\n" : $"<color={Good}>> {note.Text}</color>\n");

            if (key != null) text.Append($"<size=75%><color={Dim}>{key}: {(mode == Full ? "hide" : "show everything")}"
                + (s.Locking ? "" : s.ItemsKnown ? "  |  nothing is locked" : "") + "</color></size>");
            return text.ToString().TrimEnd('\n');
        }

        /// <summary>The checks this run can still earn by itself: the tier, the character's run and level.</summary>
        private static List<string> OpenHere(Tracker.Snapshot s, CheckList list)
        {
            var open = new List<string>();
            if (s.Map != null)
            {
                CheckList.Map map = list.Maps[s.Map];
                if (s.Tier >= 1 && s.Tier <= map.TierChecks.Count && !s.DoneChecks.Contains(map.TierChecks[s.Tier - 1]))
                    open.Add($"Tier {s.Tier}");
            }
            if (s.Character != null)
            {
                CheckList.Character character = list.Characters[s.Character];
                if (!s.DoneChecks.Contains(character.RunCheck)) open.Add($"{s.Character}'s run");
                if (!s.DoneChecks.Contains(character.LevelCheck)) open.Add($"Level {list.CharacterLevel}");
            }
            return open;
        }

        private static void AppendCounts(StringBuilder text, Tracker.Snapshot s, CheckList list)
        {
            int characters = list.Characters.Values.Count(c => Held(s, c.Item) > 0);
            int weapons = list.Weapons.Values.Count(w => Held(s, w.Item) > 0);
            int tomes = list.Tomes.Values.Count(t => Held(s, t.Item) > 0);
            text.Append($"<color={Dim}>Held:</color> {characters} characters, {weapons} weapons, {tomes} tomes\n");
            text.Append(Maps(s, list)).Append('\n');
            AppendHunt(text, s, list);
        }

        private static void AppendHunt(StringBuilder text, Tracker.Snapshot s, CheckList list)
        {
            if (s.Minibosses == null) return;
            int? next = list.MinibossHunt.Keys.Cast<int?>().FirstOrDefault(n => n > s.Minibosses);
            text.Append($"<color={Dim}>Minibosses:</color> {s.Minibosses}"
                + (next != null ? $" <color={Dim}>(next check at {next})</color>" : $" <color={Good}>(hunt done)</color>")).Append('\n');
        }

        private static void AppendLists(StringBuilder text, Tracker.Snapshot s, CheckList list)
        {
            text.Append($"<color={Dim}>Characters:</color> ").Append(Names(list.Characters.Values
                .Where(c => Held(s, c.Item) > 0)
                .Select(c => (c.Name, s.DoneChecks.Contains(c.RunCheck) && s.DoneChecks.Contains(c.LevelCheck))))).Append('\n');
            text.Append($"<color={Dim}>Weapons:</color> ").Append(Names(list.Weapons.Values
                .Where(w => Held(s, w.Item) > 0)
                .Select(w => (w.Name, s.DoneChecks.Contains(w.LevelCheck))))).Append('\n');
            text.Append($"<color={Dim}>Tomes:</color> ").Append(Names(list.Tomes.Values
                .Where(t => Held(s, t.Item) > 0)
                .Select(t => (t.Name, s.DoneChecks.Contains(t.LevelCheck))))).Append('\n');
            text.Append(Maps(s, list)).Append('\n');
            AppendHunt(text, s, list);
            text.Append($"<size=75%><color={Good}>green</color><color={Dim}>: its checks are done</color></size>\n");
        }

        private static string Names(IEnumerable<(string name, bool done)> things)
        {
            List<string> parts = things.OrderBy(t => t.name, StringComparer.OrdinalIgnoreCase)
                .Select(t => t.done ? $"<color={Good}>{t.name}</color>" : t.name).ToList();
            return parts.Count == 0 ? "none" : string.Join(", ", parts);
        }

        private static string Maps(Tracker.Snapshot s, CheckList list)
        {
            var parts = new List<string>();
            foreach (CheckList.Map map in list.Maps.Values)
            {
                int copies = Held(s, map.Item);
                if (map.TierChecks.Count > 0)
                    parts.Add(copies > 0 ? $"{map.Name} Tier {Math.Min(copies, map.TierChecks.Count)}" : $"<color={Dim}>{map.Name} locked</color>");
                else
                    parts.Add(copies > 0 ? map.Name : $"<color={Dim}>{map.Name} locked</color>");
            }
            return $"<color={Dim}>Maps:</color> " + string.Join(", ", parts);
        }

        private static int Held(Tracker.Snapshot s, string item) => s.Held.TryGetValue(item, out int copies) ? copies : 0;
    }
}
