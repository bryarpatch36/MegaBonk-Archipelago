using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MegabonkAP.Core;

namespace MegabonkAP.Plugin
{
    /// <summary>
    /// Attaches to the game's methods by name, after each has run, and hands what it sees to the
    /// GameReader. A method that is not found costs the feature named next to it and a line in the
    /// log; the others still attach. Only one hook ever changes the game: a received DeathLink
    /// calls the game's own KillPlayer.
    ///
    /// The names are those of game version 1.0.69. docs/hooks.md says which were seen working.
    /// </summary>
    internal static class Hooks
    {
        private sealed record Hook(string Type, string Method, int? Parameters, string After, string Feature);

        private static readonly Hook[] All =
        {
            new("MapController", "StartNewMap", 1, nameof(AfterStartNewMap), "knowing which map and tier a run is"),
            new("MapController", "RestartRun", 0, nameof(AfterRestartRun), "noticing a restarted run"),
            new("MyPlayer", "StartPlayer", 2, nameof(AfterStartPlayer), "knowing which character is played"),
            new("TrackStats", "OnEnemyDied", 2, nameof(AfterEnemyDiedStatic), "miniboss and hidden boss checks"),
            new("GameManager", "OnStageBossDied", 0, nameof(AfterStageBossDied), "tier and run completion"),
            new("PlayerXp", "AddXp", 1, nameof(AfterAddXp), "character level checks"),
            new("WeaponInventory", "AddWeapon", null, nameof(AfterAddWeapon), "weapon level checks"),
            new("TomeInventory", "AddTome", null, nameof(AfterAddTome), "tome level checks"),
            new("LevelupScreen", "OnClose", null, nameof(AfterLevelup), "weapon and tome levels after each level-up"),
            new("PlayerHealth", "PlayerDied", 0, nameof(AfterPlayerDied), "sending DeathLink"),
            new("PlayerHealth", "Tick", 0, nameof(AfterHealthTick), "dying when a DeathLink arrives"),
            new("GraveyardBossRoom", "Activate", 0, nameof(AfterCrypt), "the Crypt Keys check"),
            new("GraveyardBossRoom", "OnEnemyDied", 2, nameof(AfterGraveyardEnemyDied), "the Big Bob check and Graveyard runs"),
            new("ChallengesTracker", "CompleteChallenge", 0, nameof(AfterChallenge), "challenge checks"),
        };

        // only used when TrackStats.OnEnemyDied is not there
        private static readonly Hook EnemyFallback =
            new("Enemy", "EnemyDied", 0, nameof(AfterEnemyDiedInstance), "miniboss and hidden boss checks");

        public static int Wanted => All.Length;

        private static GameReader? _reader;
        private static ILog? _log;
        private static readonly HashSet<string> Failed = new();
        private static long _killBefore;        // a DeathLink waits here for the game's own thread: ticks, 0 = none

        public static int Attach(Harmony harmony, GameReader reader, TypeFinder finder, ILog log)
        {
            _reader = reader;
            _log = log;
            int attached = 0;
            foreach (Hook hook in All)
            {
                bool ok = Attach(harmony, finder, log, hook);
                if (!ok && hook.Type == "TrackStats") ok = Attach(harmony, finder, log, EnemyFallback);
                if (ok && hook.Method == "OnStageBossDied") reader.StageBossEvent = true;
                if (ok) attached++;
            }
            return attached;
        }

        /// <summary>Another player died. The player dies at the next health tick, if one comes within ten seconds.</summary>
        public static void RequestKill() =>
            System.Threading.Interlocked.Exchange(ref _killBefore, DateTime.UtcNow.AddSeconds(10).Ticks);

        private static bool Attach(Harmony harmony, TypeFinder finder, ILog log, Hook hook)
        {
            Type? type = finder.Find(hook.Type);
            if (type == null)
            {
                log.Warn($"Hook missing: the game has no type {hook.Type}. Not working: {hook.Feature}.");
                return false;
            }

            const BindingFlags any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public
                | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            List<MethodInfo> targets = type.GetMethods(any)
                .Where(m => m.Name == hook.Method && !m.IsAbstract && !m.IsGenericMethodDefinition
                    && (hook.Parameters == null || m.GetParameters().Length == hook.Parameters))
                .ToList();
            if (targets.Count == 0)
            {
                log.Warn($"Hook missing: {hook.Type} has no method {hook.Method}"
                    + (hook.Parameters != null ? $" with {hook.Parameters} parameter(s)" : "")
                    + $". Not working: {hook.Feature}. It has: {Dyn.Describe(type)}");
                return false;
            }

            MethodInfo after = typeof(Hooks).GetMethod(hook.After, BindingFlags.Static | BindingFlags.NonPublic)!;
            int done = 0;
            foreach (MethodInfo target in targets)
            {
                try
                {
                    harmony.Patch(target, postfix: new HarmonyMethod(after));
                    done++;
                }
                catch (Exception problem)
                {
                    log.Warn($"Hook failed: {hook.Type}.{hook.Method}: {problem.GetType().Name}: {problem.Message}");
                }
            }
            if (done > 0) log.Info($"Hooked {hook.Type}.{hook.Method}" + (done > 1 ? $" ({done} forms)" : ""));
            else log.Warn($"Not working: {hook.Feature}.");
            return done > 0;
        }

        // ---- what runs after the game's methods. An error here must never reach the game. ----

        private static void AfterStartNewMap(object[] __args) =>
            Safely(nameof(AfterStartNewMap), r => r.RunStarted(__args.Length > 0 ? __args[0] : null));

        private static void AfterRestartRun() =>
            Safely(nameof(AfterRestartRun), r => r.RunRestarted());

        private static void AfterStartPlayer(object[] __args) =>
            Safely(nameof(AfterStartPlayer), r => r.PlayerStarted(__args.Length > 0 ? __args[0] : null));

        private static void AfterEnemyDiedStatic(object[] __args) =>
            Safely(nameof(AfterEnemyDiedStatic), r => r.EnemyDied(__args.Length > 0 ? __args[0] : null));

        private static void AfterEnemyDiedInstance(object __instance) =>
            Safely(nameof(AfterEnemyDiedInstance), r => r.EnemyDied(__instance));

        private static void AfterStageBossDied() =>
            Safely(nameof(AfterStageBossDied), r => r.StageBossDied());

        private static void AfterAddXp(object __instance) =>
            Safely(nameof(AfterAddXp), r => r.XpChanged(__instance));

        private static void AfterAddWeapon(object __instance) =>
            Safely(nameof(AfterAddWeapon), r => r.WeaponsChanged(__instance));

        private static void AfterAddTome(object __instance) =>
            Safely(nameof(AfterAddTome), r => r.TomesChanged(__instance));

        private static void AfterLevelup() =>
            Safely(nameof(AfterLevelup), r => r.InventoryChanged());

        private static void AfterPlayerDied() =>
            Safely(nameof(AfterPlayerDied), r => r.PlayerDied());

        private static void AfterHealthTick(object __instance)
        {
            if (_killBefore == 0) return;                   // every frame: nothing else may happen here
            long before = System.Threading.Interlocked.Exchange(ref _killBefore, 0);
            if (before == 0 || DateTime.UtcNow.Ticks > before) return;
            Safely(nameof(AfterHealthTick), r => r.Kill(__instance));
        }

        private static void AfterCrypt() =>
            Safely(nameof(AfterCrypt), r => r.CryptOpened());

        private static void AfterGraveyardEnemyDied(object __instance, object[] __args) =>
            Safely(nameof(AfterGraveyardEnemyDied), r => r.GraveyardEnemyDied(__instance, __args.Length > 0 ? __args[0] : null));

        private static void AfterChallenge() =>
            Safely(nameof(AfterChallenge), r => r.ChallengeCompleted());

        private static void Safely(string where, Action<GameReader> action)
        {
            GameReader? reader = _reader;
            if (reader == null) return;
            try
            {
                action(reader);
            }
            catch (Exception problem)
            {
                if (Failed.Add(where + problem.GetType().Name))
                    _log?.Warn($"{where} failed and is skipped: {problem.GetType().Name}: {problem.Message}");
            }
        }
    }
}
