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
    /// log; the others still attach. Nothing here changes what the game does.
    ///
    /// Where each name comes from is in docs/hooks.md. None has been run on the real game yet.
    /// </summary>
    internal static class Hooks
    {
        private sealed record Hook(string Type, string Method, int? Parameters, string After, string Feature);

        private static readonly Hook[] All =
        {
            new("MapController", "StartNewMap", 1, nameof(AfterStartNewMap), "knowing which character, map and tier a run is"),
            new("TrackStats", "OnEnemyDied", 2, nameof(AfterEnemyDiedStatic), "boss, miniboss and tier checks"),
            new("PlayerXp", "AddXp", null, nameof(AfterAddXp), "character level checks"),
            new("WeaponInventory", "AddWeapon", null, nameof(AfterInventory), "weapon level checks"),
            new("TomeInventory", "AddTome", null, nameof(AfterInventory), "tome level checks"),
            new("LevelupScreen", "OnClose", null, nameof(AfterInventory), "weapon and tome levels after each level-up"),
            new("PlayerHealth", "PlayerDied", 0, nameof(AfterPlayerDied), "sending DeathLink"),
            new("GraveyardBossRoom", "Activate", null, nameof(AfterCrypt), "the Crypt Keys check"),
            new("GameManager", "StopDungeon", null, nameof(AfterStopDungeon), "knowing when a run is over"),
        };

        // only used when TrackStats.OnEnemyDied is not there
        private static readonly Hook EnemyFallback =
            new("Enemy", "EnemyDied", 0, nameof(AfterEnemyDiedInstance), "boss, miniboss and tier checks");

        public static int Wanted => All.Length;

        private static GameReader? _reader;
        private static ILog? _log;
        private static readonly HashSet<string> Failed = new();

        public static int Attach(Harmony harmony, GameReader reader, TypeFinder finder, ILog log)
        {
            _reader = reader;
            _log = log;
            int attached = 0;
            foreach (Hook hook in All)
            {
                if (Attach(harmony, finder, log, hook)) { attached++; continue; }
                if (hook.Type == "TrackStats" && Attach(harmony, finder, log, EnemyFallback)) attached++;
            }
            return attached;
        }

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

        private static void AfterEnemyDiedStatic(object[] __args) =>
            Safely(nameof(AfterEnemyDiedStatic), r => r.EnemyDied(__args.Length > 0 ? __args[0] : null));

        private static void AfterEnemyDiedInstance(object __instance) =>
            Safely(nameof(AfterEnemyDiedInstance), r => r.EnemyDied(__instance));

        private static void AfterAddXp(object __instance) =>
            Safely(nameof(AfterAddXp), r => r.XpChanged(__instance));

        private static void AfterInventory() =>
            Safely(nameof(AfterInventory), r => r.InventoryChanged());

        private static void AfterPlayerDied() =>
            Safely(nameof(AfterPlayerDied), r => r.PlayerDied());

        private static void AfterCrypt() =>
            Safely(nameof(AfterCrypt), r => r.CryptOpened());

        private static void AfterStopDungeon() =>
            Safely(nameof(AfterStopDungeon), r => r.RunEnded());

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
