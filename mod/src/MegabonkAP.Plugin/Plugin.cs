using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using MegabonkAP.Core;

namespace MegabonkAP.Plugin
{
    /// <summary>
    /// Megabonk Archipelago: connects to the Manual world's slot and sends its checks as you play.
    /// Settings are in BepInEx/config/bbush.megabonk.archipelago.cfg; what it does is written to
    /// BepInEx/LogOutput.log with the prefix [MegabonkAP].
    /// </summary>
    [BepInPlugin(Id, Name, Version)]
    public sealed class Plugin : BasePlugin
    {
        public const string Id = "bbush.megabonk.archipelago";
        public const string Name = "MegabonkAP";
        public const string Version = "0.2.0";

        private ArchipelagoLink? _link;
        private Tracker? _tracker;
        private Timer? _timer;
        private int _busy;

        public override void Load()
        {
            var log = new BepLog(Log);

            ConfigEntry<string> server = Config.Bind("Archipelago", "Server", "archipelago.gg:38281",
                "Address and port of the Archipelago server, as shown on the room page.");
            ConfigEntry<string> slot = Config.Bind("Archipelago", "Slot", "",
                "Your slot name: the name in your YAML. Nothing is sent while this is empty.");
            ConfigEntry<string> password = Config.Bind("Archipelago", "Password", "",
                "The room's password, if it has one.");
            ConfigEntry<bool> strict = Config.Bind("Rules", "Strict", true,
                "On: a check only counts when you hold the items the world's rules ask for (the character, the map tier, "
                + "the weapon or tome). Off: everything you do counts.");
            ConfigEntry<bool> deathLink = Config.Bind("Rules", "DeathLink", true,
                "On: DeathLink follows the death_link option of your YAML. Off: never, whatever the YAML says.");
            ConfigEntry<bool> probe = Config.Bind("Debug", "Probe", true,
                "On: at every start, write what the game's code looks like to BepInEx/MegabonkAP-probe.txt. "
                + "It reads names only and changes nothing. Send that file along when something does not work.");

            CheckList list = CheckList.Load();
            _link = new ArchipelagoLink(list.Game, log)
            {
                Server = server.Value,
                Slot = slot.Value,
                Password = password.Value,
            };
            _tracker = new Tracker(list, _link, log, Path.Combine(Paths.ConfigPath, "MegabonkAP.state.json"))
            {
                Strict = strict.Value,
                DeathLinkAllowed = deathLink.Value,
            };
            _tracker.KillRequested += (who, why) =>
            {
                log.Info($"DeathLink: {who} died, so you do too.");
                Hooks.RequestKill();
            };

            Assembly? game = LoadGame(log);
            var finder = new TypeFinder();
            var reader = new GameReader(_tracker, log, finder.Find);

            int hooked = Hooks.Attach(new Harmony(Id), reader, finder, log);
            log.Info($"{Name} {Version} loaded: {hooked} of {Hooks.Wanted} hooks attached, "
                + (strict.Value ? "strict rules" : "everything counts") + ".");

            if (probe.Value && game != null) WriteProbe(game, log);

            if (string.IsNullOrWhiteSpace(slot.Value))
                log.Warn($"No slot name is set. Put yours in {Path.Combine(Paths.ConfigPath, Id + ".cfg")} and start the game again.");

            // connecting can take seconds, so it never happens on the game's own thread
            _timer = new Timer(_ => Tick(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10));
        }

        public override bool Unload()
        {
            _timer?.Dispose();
            _link?.Dispose();
            return true;
        }

        private void Tick()
        {
            if (Interlocked.Exchange(ref _busy, 1) == 1) return;        // the last try is still running
            try
            {
                if (_link != null && _link.EnsureConnected()) _tracker?.Flush();
            }
            catch (Exception problem)
            {
                Log.LogWarning($"Archipelago: {problem.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _busy, 0);
            }
        }

        /// <summary>
        /// The plugin names no game type, so nothing has loaded the game's code yet. BepInEx keeps
        /// its generated copy as BepInEx/interop/Assembly-CSharp.dll.
        /// </summary>
        private static Assembly? LoadGame(ILog log)
        {
            Assembly? loaded = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
            if (loaded != null) return loaded;
            try { return Assembly.Load("Assembly-CSharp"); }
            catch (Exception) { /* try the file */ }
            string file = Path.Combine(Paths.BepInExRootPath, "interop", "Assembly-CSharp.dll");
            try { return Assembly.LoadFrom(file); }
            catch (Exception problem)
            {
                log.Warn($"The game's code could not be loaded from {file}: {problem.Message}. No hook can attach.");
                return null;
            }
        }

        private static void WriteProbe(Assembly game, ILog log)
        {
            string file = Path.Combine(Paths.BepInExRootPath, "MegabonkAP-probe.txt");
            try
            {
                File.WriteAllText(file, Probe.Report(Probe.TypesOf(game)));
                log.Info($"Probe written to {file}");
            }
            catch (Exception problem)
            {
                log.Warn($"The probe could not be written to {file}: {problem.Message}");
            }
        }

        private sealed class BepLog : ILog
        {
            private readonly ManualLogSource _log;
            public BepLog(ManualLogSource log) { _log = log; }
            public void Info(string message) => _log.LogInfo(message);
            public void Warn(string message) => _log.LogWarning(message);
        }
    }
}
