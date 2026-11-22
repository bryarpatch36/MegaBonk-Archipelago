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
        public const string Version = "0.3.3";

        private ArchipelagoLink? _link;
        private Tracker? _tracker;
        private Timer? _timer;
        private Timer? _overlayTimer;
        private int _busy;
        private int _mode;

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
            ConfigEntry<bool> locking = Config.Bind("Rules", "Lock", true,
                "On, together with Strict: what you do not hold cannot be used. A locked character or map tier cannot be started, "
                + "and locked weapons and tomes are not offered at a level-up. Off: you can use anything, it just does not count.");
            ConfigEntry<string> mode = Config.Bind("Overlay", "Mode", "Compact",
                "The on-screen tracker: Compact, Full (lists everything you hold) or Hidden. The key below steps through them.");
            ConfigEntry<string> key = Config.Bind("Overlay", "Key", "F8",
                "The key that changes the overlay's view, as a Unity key name (F8, Tab, BackQuote, ...). None: no key.");
            ConfigEntry<string> pad = Config.Bind("Overlay", "ControllerButtons", "JoystickButton8+JoystickButton9",
                "Controller buttons that change the overlay's view when held together, as Unity key names joined by +. "
                + "The default is clicking both sticks at once (L3+R3 on an Xbox-style pad, as on the ROG Ally). None: no buttons.");
            ConfigEntry<string> corner = Config.Bind("Overlay", "Position", "MiddleLeft",
                "Where the overlay sits: TopLeft, MiddleLeft, BottomLeft, TopRight, MiddleRight or BottomRight.");
            ConfigEntry<int> fontSize = Config.Bind("Overlay", "FontSize", 20,
                "Text size, as on a 1080p screen. The overlay grows with the screen.");
            ConfigEntry<int> width = Config.Bind("Overlay", "Width", 520,
                "Width of the overlay, as on a 1080p screen.");
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
                Lock = locking.Value,
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

            var harmony = new Harmony(Id);
            int hooked = Hooks.Attach(harmony, reader, finder, log);
            log.Info($"{Name} {Version} loaded: {hooked} of {Hooks.Wanted} hooks attached, "
                + (!strict.Value ? "everything counts" : locking.Value ? "strict rules, locks on" : "strict rules, nothing locked") + ".");

            _mode = Overlay.ParseMode(mode.Value);
            Screen.LoadUnity(log, Path.Combine(Paths.BepInExRootPath, "interop"));
            var screen = new Screen(log, finder, corner.Value, fontSize.Value, width.Value, key.Value + ", " + pad.Value, () =>
            {
                _mode = Overlay.Next(_mode);
                mode.Value = Overlay.ModeName(_mode);       // remembered for the next start
                RenderOverlay();
            });
            _screen = screen;
            _list = list;
            _slotSet = !string.IsNullOrWhiteSpace(slot.Value);
            bool none(string s) => s.Trim().Length == 0 || s.Trim().Equals("none", StringComparison.OrdinalIgnoreCase);
            string padName = pad.Value.Replace(" ", "").Equals("JoystickButton8+JoystickButton9", StringComparison.OrdinalIgnoreCase)
                ? "both sticks" : pad.Value.Trim();
            _keyName = string.Join(" or ", new[] { none(key.Value) ? null : key.Value.Trim(), none(pad.Value) ? null : padName }.Where(n => n != null));
            Hooks.AttachFrames(harmony, screen, finder, log);
            // the text is made away from the game's thread; the game's thread only puts it on screen
            _overlayTimer = new Timer(_ => RenderOverlay(), null, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(400));

            if (probe.Value && game != null) WriteProbe(game, log);

            if (string.IsNullOrWhiteSpace(slot.Value))
                log.Warn($"No slot name is set. Put yours in {Path.Combine(Paths.ConfigPath, Id + ".cfg")} and start the game again.");

            // connecting can take seconds, so it never happens on the game's own thread
            _timer = new Timer(_ => Tick(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10));
        }

        public override bool Unload()
        {
            _timer?.Dispose();
            _overlayTimer?.Dispose();
            _link?.Dispose();
            return true;
        }

        private void Tick()
        {
            if (Interlocked.Exchange(ref _busy, 1) == 1) return;        // the last try is still running
            try
            {
                if (_link == null) return;
                bool connected = _link.EnsureConnected();
                _tracker?.ConnectionSeen(connected);
                if (connected) _tracker?.Flush();
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

        private Screen? _screen;
        private CheckList? _list;
        private bool _slotSet;
        private string _keyName = "";

        private void RenderOverlay()
        {
            Screen? screen = _screen;
            if (screen == null || _tracker == null || _list == null) return;
            try
            {
                // the hint names the key only once it is known to work; hidden stays hidden
                string? hint = screen.KeyWorks && _keyName.Length > 0 ? _keyName : null;
                screen.Text = Overlay.Render(_tracker.Look(), _list, _mode, DateTime.UtcNow, _slotSet, hint);
            }
            catch (Exception problem)
            {
                Log.LogWarning($"Overlay: the text could not be made: {problem.Message}");
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
