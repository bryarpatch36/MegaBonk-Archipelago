using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MegabonkAP.Core;

// Plays a Megabonk slot on a real Archipelago server through the mod's own connection and check
// logic. There is no game: the events a run would produce are given by hand, in the game's names.
//
//     MegabonkAP.E2E <server:port> <slot>
//
// Expects a fresh seed generated from Bonker.yaml (goal: tier 2 bosses). Run by tests/e2e_mod_core.py.

internal static class Program
{
    private sealed class ConsoleLog : ILog
    {
        public readonly List<string> Lines = new();
        public void Info(string message) { lock (Lines) Lines.Add(message); Console.WriteLine("      " + message); }
        public void Warn(string message) { lock (Lines) Lines.Add(message); Console.WriteLine("      ! " + message); }
    }

    private static int _failed;

    private static void Expect(bool condition, string what)
    {
        Console.WriteLine((condition ? "ok    " : "FAIL  ") + what);
        if (!condition) _failed++;
    }

    private static bool Wait(Func<bool> condition, int seconds = 15)
    {
        for (int i = 0; i < seconds * 10; i++)
        {
            if (condition()) return true;
            Thread.Sleep(100);
        }
        return condition();
    }

    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("usage: MegabonkAP.E2E <server:port> <slot>");
            return 2;
        }

        var log = new ConsoleLog();
        CheckList list = CheckList.Load();
        using var link = new ArchipelagoLink(list.Game, log) { Server = args[0], Slot = args[1] };
        var tracker = new Tracker(list, link, log);

        Expect(link.EnsureConnected(), $"connected to {args[0]} as {args[1]}");
        if (!link.Connected) return 1;

        // every check and item the mod knows must be one this server knows
        List<string> unknownChecks = list.AllChecks().Where(c => !link.Exists(c)).ToList();
        Expect(unknownChecks.All(c => c.Contains("Challenge - ")),
            "every check of the mod exists in the seed (challenges are off in this seed): "
            + string.Join(", ", unknownChecks.Where(c => !c.Contains("Challenge - "))));

        List<string> start = link.ReceivedItems.ToList();
        Expect(start.Count == 4, $"4 starting items received ({string.Join(", ", start)})");
        string? characterItem = start.FirstOrDefault(i => i.StartsWith("Character - "));
        Expect(characterItem != null && start.Contains("Progressive Forest Tier"), "they hold a character and Forest Tier 1");
        if (characterItem == null) return 1;
        string character = characterItem.Substring("Character - ".Length);
        CheckList.Character who = list.Characters[character];

        // 0. locks go by what the server says is held: the starting character, Forest Tier 1, two tomes
        string other = list.Characters.Keys.First(c => c != character);
        string heldTome = list.Tomes.Values.First(t => start.Contains(t.Item)).Name;
        string lockedTome = list.Tomes.Values.First(t => !start.Contains(t.Item)).Name;
        Expect(tracker.Locking && tracker.CharacterRefusal(character) == null && tracker.CharacterRefusal(other) != null,
            $"{character} may be played, {other} is locked");
        Expect(tracker.RunRefusal("Forest", 1) == null && tracker.RunRefusal("Forest", 2) != null && tracker.RunRefusal("Desert", 1) != null,
            "Forest Tier 1 may be started; Forest Tier 2 and Desert are locked");
        Expect(tracker.OfferAllowed("tome", heldTome) && !tracker.OfferAllowed("tome", lockedTome)
            && tracker.OfferAllowed("weapon", who.Weapon) && !tracker.OfferAllowed("weapon", list.Characters[other].Weapon),
            $"offers: {heldTome} Tome and {who.Weapon} yes, {lockedTome} Tome and {list.Characters[other].Weapon} no");
        string before0 = Overlay.Render(tracker.Look(), list, Overlay.Compact, DateTime.UtcNow);
        Expect(before0.Contains($"0/{link.TotalChecks} checks") && link.TotalChecks > 50 && before0.Contains("Forest Tier 1"),
            $"the overlay shows 0 of {link.TotalChecks} checks and Forest Tier 1");

        // 1. strict rules, a run the starting items permit
        tracker.RunStarted(character, "Forest", 1);
        tracker.PlayerLevel(30);
        tracker.StageBossDied(true);
        string tier1 = list.Maps["Forest"].TierChecks[0];
        Expect(Wait(() => link.IsChecked(tier1) && link.IsChecked(who.RunCheck) && link.IsChecked(who.LevelCheck)),
            $"the server took: {tier1}; {who.RunCheck}; {who.LevelCheck}");

        tracker.Flush();
        string after1 = Overlay.Render(tracker.Look(), list, Overlay.Full, DateTime.UtcNow);
        Expect(after1.Contains($"3/{link.TotalChecks} checks") && link.CheckedNames.Contains(tier1)
            && after1.Contains($"<color=#9be29b>{character}</color>"),
            $"the overlay shows 3 checks done and {character} in green");
        Console.WriteLine("      --- the overlay, full view ---");
        foreach (string line in System.Text.RegularExpressions.Regex.Replace(after1, "<[^>]+>", "").Split('\n'))
            Console.WriteLine("      | " + line);

        // 2. strict rules, a run they do not permit (Desert without a Desert tier)
        tracker.RunStarted(character, "Desert", 1);
        tracker.StageBossDied(true);
        Thread.Sleep(1500);
        Expect(!link.IsChecked(list.Maps["Desert"].TierChecks[0]), "a Desert run without the Desert tier item sent nothing");
        Expect(log.Lines.Any(l => l.Contains("This run does not count")), "and the log said why");

        // 3. items arrive from the checks above; the tracker must see them without reconnecting
        int before = start.Count;
        Expect(Wait(() => link.ReceivedItems.Count > before), $"items arrived for those checks ({link.ReceivedItems.Count - before} more)");

        // 4. the goal of this seed is Tier 2 Bosses: Tier 2 on Forest and on Desert. With the rules
        //    off (the items are still spread over the seed), finish both and expect the goal.
        tracker.Strict = false;
        foreach (string map in new[] { "Forest", "Desert" })
        {
            tracker.RunStarted(character, map, 2);
            tracker.StageBossDied(false);
            tracker.StageBossDied(true);
            }
        Expect(Wait(() => link.IsChecked(list.Maps["Forest"].TierChecks[1]) && link.IsChecked(list.Maps["Desert"].TierChecks[1])),
            "the server took Tier 2 on Forest and on Desert");
        Expect(log.Lines.Any(l => l.Contains("Goal reached: Tier 2 Bosses")), "the goal was sent");

        // 5. a second connection, as after restarting the game, sees what was done
        using var again = new ArchipelagoLink(list.Game, log) { Server = args[0], Slot = args[1] };
        Expect(again.EnsureConnected() && again.IsChecked(tier1) && again.Session == link.Session,
            "a new connection sees the same seed and the checks already done");

        // 6. DeathLink (the seed has death_link on): a death on one connection reaches the other, both ways
        var tracker2 = new Tracker(list, again, log);
        again.EnsureConnected();
        string? killed1 = null, killed2 = null;
        tracker.KillRequested += (who, why) => killed1 = who;
        tracker2.KillRequested += (who, why) => killed2 = who;
        Thread.Sleep(1000);
        tracker.RunStarted(character, "Forest", 1);
        tracker.PlayerDied();
        Expect(Wait(() => killed2 != null, 8), $"a death sent by the first connection kills the second ({killed2})");
        Thread.Sleep(11000);            // a received death is not sent back for ten seconds
        tracker2.RunStarted(character, "Forest", 1);
        tracker2.PlayerDied();
        Expect(Wait(() => killed1 != null, 8), $"and a death sent by the second kills the first ({killed1})");

        Thread.Sleep(500);
        Console.WriteLine(_failed == 0 ? "all steps passed" : $"{_failed} step(s) failed");
        return _failed == 0 ? 0 : 1;
    }
}
