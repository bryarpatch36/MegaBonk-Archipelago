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
    /// log; the others still attach.
    ///
    /// Most hooks only watch. The ones that change the game: a received DeathLink calls the game's
    /// own KillPlayer; the "Before" hooks stop a locked character, map, tier or level-up offer
    /// from being taken; the two offer hooks take locked weapons and tomes out of what the game
    /// may offer; and locked offers on screen are dimmed. When one of those fails it lets the game go on as if the mod were not there.
    ///
    /// The names are those of game version 1.0.69. docs/hooks.md says which were seen working.
    /// </summary>
    internal static class Hooks
    {
        /// <summary>After: runs after the game's method. Before: runs first, and false stops the game's method.</summary>
        private sealed record Hook(string Type, string Method, int? Parameters, string? After, string Feature, string? Before = null);

        private static readonly Hook[] All =
        {
            new("MapController", "StartNewMap", 1, nameof(AfterStartNewMap), "knowing which map and tier a run is, and locking maps", nameof(BeforeStartNewMap)),
            new("MapController", "RestartRun", 0, nameof(AfterRestartRun), "noticing a restarted run"),
            new("MyPlayer", "StartPlayer", 2, nameof(AfterStartPlayer), "knowing which character is played"),
            new("TrackStats", "OnEnemyDied", 2, nameof(AfterEnemyDiedStatic), "miniboss and hidden boss checks"),
            new("GameManager", "OnStageBossDied", 0, nameof(AfterStageBossDied), "tier and run completion"),
            new("PlayerXp", "AddXp", 1, nameof(AfterAddXp), "character level checks"),
            new("WeaponInventory", "AddWeapon", null, nameof(AfterAddWeapon), "weapon level checks"),
            new("TomeInventory", "AddTome", null, nameof(AfterAddTome), "tome level checks"),
            new("LevelupScreen", "OnClose", null, nameof(AfterLevelup), "weapon and tome levels after each level-up"),
            new("PlayerHealth", "PlayerDied", 0, nameof(AfterPlayerDied), "sending DeathLink"),
            new("PlayerHealth", "Tick", 0, nameof(AfterHealthTick), "a turn every frame during a run"),
            new("GraveyardBossRoom", "Activate", 0, nameof(AfterCrypt), "the Crypt Keys check"),
            new("GraveyardBossRoom", "OnEnemyDied", 2, nameof(AfterGraveyardEnemyDied), "the Big Bob check and Graveyard runs"),
            new("ChallengesTracker", "CompleteChallenge", 0, nameof(AfterChallenge), "challenge checks"),
            new("CharacterMenu", "OnConfirmCharacter", 1, null, "locking characters you do not hold", nameof(BeforeConfirmCharacter)),
            new("SelectionGroupToggleSingleButtonTier", "OnClick", 0, null, "locking tiers in the map menu", nameof(BeforeTierClick)),
            new("UnlockUtility", "GetAvailableWeapons", 0, nameof(AfterAvailableWeapons), "offering only weapons you hold"),
            new("UnlockUtility", "GetAvailableTomes", 0, nameof(AfterAvailableTomes), "offering only tomes you hold"),
            new("UpgradePicker", "ShuffleUpgrades", 1, nameof(AfterShuffleUpgrades), "dimming locked level-up offers"),
            new("UpgradePicker", "SelectUpgrade", 4, null, "refusing a locked level-up offer", nameof(BeforeSelectUpgrade)),
        };

        // Any one of these gives the overlay its turn on the game's own thread, in menus and in a run.
        private static readonly Hook[] Frames =
        {
            new("UnityEngine.EventSystems.EventSystem", "Update", 0, nameof(AfterFrame), "the overlay"),
            new("UnityEngine.Canvas", "SendWillRenderCanvases", 0, nameof(AfterFrame), "the overlay"),
            new("WindowManager", "Update", 0, nameof(AfterFrame), "the overlay"),
        };

        // only used when TrackStats.OnEnemyDied is not there
        private static readonly Hook EnemyFallback =
            new("Enemy", "EnemyDied", 0, nameof(AfterEnemyDiedInstance), "miniboss and hidden boss checks");

        public static int Wanted => All.Length;

        private static GameReader? _reader;
        private static ILog? _log;
        private static Screen? _screen;
        private static bool _runRefused;        // StartNewMap was stopped: what runs after it must not count a run
        private static readonly HashSet<string> Failed = new();

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

        /// <summary>Give the overlay its turn every frame. Returns how many of the game's per-frame methods were found.</summary>
        public static int AttachFrames(Harmony harmony, Screen screen, TypeFinder finder, ILog log)
        {
            _screen = screen;
            int attached = 0;
            foreach (Hook hook in Frames)
            {
                if (finder.Find(hook.Type) == null) continue;       // only one is needed: no complaint for the others
                if (Attach(harmony, finder, log, hook)) attached++;
            }
            if (attached == 0)
                log.Warn("Overlay: none of the game's per-frame methods could be hooked, so it only updates during a run.");
            return attached;
        }

        /// <summary>Another player died. The player dies at the next look at their health, if one comes within ten seconds.</summary>
        public static void RequestKill() => _reader?.RequestKill();

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

            const BindingFlags mine = BindingFlags.Static | BindingFlags.NonPublic;
            MethodInfo? after = hook.After == null ? null : typeof(Hooks).GetMethod(hook.After, mine)!;
            MethodInfo? before = hook.Before == null ? null : typeof(Hooks).GetMethod(hook.Before, mine)!;
            int done = 0;
            foreach (MethodInfo target in targets)
            {
                try
                {
                    harmony.Patch(target,
                        prefix: before == null ? null : new HarmonyMethod(before),
                        postfix: after == null ? null : new HarmonyMethod(after));
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

        private static bool BeforeStartNewMap(object[] __args)
        {
            _runRefused = !Allowed(nameof(BeforeStartNewMap), r => r.MayStartRun(__args.Length > 0 ? __args[0] : null));
            return !_runRefused;
        }

        private static void AfterStartNewMap(object[] __args)
        {
            if (_runRefused) { _runRefused = false; return; }       // the game's method did not run: no run began
            Safely(nameof(AfterStartNewMap), r => r.RunStarted(__args.Length > 0 ? __args[0] : null));
        }

        private static bool BeforeConfirmCharacter(object __instance, object[] __args) =>
            Allowed(nameof(BeforeConfirmCharacter), r => r.MayConfirmCharacter(__instance, __args.Length > 0 ? __args[0] : null));

        private static bool BeforeTierClick(object __instance) =>
            Allowed(nameof(BeforeTierClick), r => r.MayPickTier(__instance));

        private static void AfterAvailableWeapons(object __result) =>
            Safely(nameof(AfterAvailableWeapons), r => r.LimitOffers("weapon", __result));

        private static void AfterAvailableTomes(object __result) =>
            Safely(nameof(AfterAvailableTomes), r => r.LimitOffers("tome", __result));

        private static void AfterShuffleUpgrades(object __instance) =>
            Safely(nameof(AfterShuffleUpgrades), r => r.OffersShown(__instance));

        private static bool BeforeSelectUpgrade(object __instance, object[] __args) =>
            Allowed(nameof(BeforeSelectUpgrade), r => r.MayPickOffer(__instance, __args.Length > 0 ? __args[0] : null));

        private static long _nextOffersLook;

        private static void AfterFrame()
        {
            _screen?.Pump();
            long now = Environment.TickCount64;
            if (now < _nextOffersLook) return;
            _nextOffersLook = now + 300;
            Safely(nameof(AfterFrame), r => r.Frame());
        }

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

        private static void AfterHealthTick() => AfterFrame();

        private static void AfterCrypt() =>
            Safely(nameof(AfterCrypt), r => r.CryptOpened());

        private static void AfterGraveyardEnemyDied(object __instance, object[] __args) =>
            Safely(nameof(AfterGraveyardEnemyDied), r => r.GraveyardEnemyDied(__instance, __args.Length > 0 ? __args[0] : null));

        private static void AfterChallenge() =>
            Safely(nameof(AfterChallenge), r => r.ChallengeCompleted());

        /// <summary>Ask whether the game may go on. Anything that goes wrong means yes.</summary>
        private static bool Allowed(string where, Func<GameReader, bool> question)
        {
            bool allowed = true;
            Safely(where, r => allowed = question(r));
            return allowed;
        }

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
