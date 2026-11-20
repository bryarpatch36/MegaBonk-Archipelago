using System;
using System.Collections.Generic;
using System.Linq;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Helpers;

namespace MegabonkAP.Core
{
    /// <summary>
    /// The real Archipelago server, through Archipelago.MultiClient.Net. It connects as the
    /// Manual world's slot, the same way the Manual Client does, so both can be open at once.
    /// </summary>
    public sealed class ArchipelagoLink : IServerLink, IDisposable
    {
        private readonly string _game;
        private readonly ILog _log;
        private readonly object _gate = new();

        private ArchipelagoSession? _session;
        private DeathLinkService? _deathLink;
        private Dictionary<string, object> _slotData = new();
        private string _slot = "";
        private string? _lastProblem;
        private DateTime _quietUntil = DateTime.MinValue;   // on connecting the server repeats every item

        public string Server { get; set; } = "";
        public string Slot { get; set; } = "";
        public string? Password { get; set; }

        public event Action? Changed;
        public event Action<string, string>? DeathReceived;

        public ArchipelagoLink(string game, ILog log)
        {
            _game = game;
            _log = log;
        }

        public bool Connected
        {
            get { lock (_gate) return _session != null && _session.Socket.Connected; }
        }

        public string? Session { get; private set; }

        public IReadOnlyList<string> ReceivedItems
        {
            get
            {
                lock (_gate)
                {
                    if (_session == null) return Array.Empty<string>();
                    return _session.Items.AllItemsReceived.Select(i => i.ItemName ?? i.ItemDisplayName ?? "").ToList();
                }
            }
        }

        /// <summary>Connect if not connected. Returns true when connected afterwards. Blocks while it tries.</summary>
        public bool EnsureConnected()
        {
            lock (_gate)
            {
                if (_session != null && _session.Socket.Connected) return true;
                if (string.IsNullOrWhiteSpace(Server) || string.IsNullOrWhiteSpace(Slot))
                {
                    Problem("No server or slot name is set, so nothing is sent to Archipelago.");
                    return false;
                }

                Drop();
                try
                {
                    (string host, int port) = SplitAddress(Server);
                    ArchipelagoSession session = ArchipelagoSessionFactory.CreateSession(host, port);
                    _quietUntil = DateTime.UtcNow.AddSeconds(30);
                    session.Items.ItemReceived += OnItem;
                    LoginResult result = session.TryConnectAndLogin(_game, Slot, ItemsHandlingFlags.AllItems,
                        version: null, tags: null, uuid: null,
                        password: string.IsNullOrEmpty(Password) ? null : Password, requestSlotData: true);

                    if (result is not LoginSuccessful ok)
                    {
                        string why = result is LoginFailure failure ? string.Join("; ", failure.Errors) : "no answer";
                        Problem($"Could not connect to {Server} as {Slot}: {why}");
                        return false;
                    }

                    _session = session;
                    _slot = Slot;
                    _slotData = ok.SlotData ?? new Dictionary<string, object>();
                    Session = $"{session.RoomState.Seed}:{ok.Slot}";
                    _lastProblem = null;

                    _deathLink = session.CreateDeathLinkService();
                    _deathLink.OnDeathLinkReceived += death =>
                        DeathReceived?.Invoke(death.Source ?? "Someone", death.Cause ?? "no reason given");
                    if (Truthy(SlotValue("death_link"))) _deathLink.EnableDeathLink();

                    _quietUntil = DateTime.UtcNow.AddSeconds(3);
                    _log.Info($"Connected to {Server} as {Slot}: {session.Locations.AllLocationsChecked.Count} of "
                        + $"{session.Locations.AllLocations.Count} checks done.");
                }
                catch (Exception problem)
                {
                    Problem($"Could not connect to {Server} as {Slot}: {problem.Message}");
                    Drop();
                    return false;
                }
            }
            Changed?.Invoke();
            return true;
        }

        public bool IsChecked(string check)
        {
            lock (_gate)
            {
                if (_session == null) return false;
                long id = _session.Locations.GetLocationIdFromName(_game, check);
                return id >= 0 && _session.Locations.AllLocationsChecked.Contains(id);
            }
        }

        public bool Exists(string check)
        {
            lock (_gate)
            {
                if (_session == null) return false;
                long id = _session.Locations.GetLocationIdFromName(_game, check);
                return id >= 0 && _session.Locations.AllLocations.Contains(id);
            }
        }

        public IReadOnlyCollection<string> Send(IEnumerable<string> checks)
        {
            lock (_gate)
            {
                var taken = new List<string>();
                if (_session == null || !_session.Socket.Connected) return taken;
                var ids = new List<long>();
                foreach (string check in checks)
                {
                    long id = _session.Locations.GetLocationIdFromName(_game, check);
                    if (id < 0) continue;
                    ids.Add(id);
                    taken.Add(check);
                }
                if (ids.Count == 0) return taken;
                try
                {
                    _session.Locations.CompleteLocationChecks(ids.ToArray());
                    return taken;
                }
                catch (Exception problem)
                {
                    Problem($"Could not send checks: {problem.Message}");
                    return new List<string>();
                }
            }
        }

        public void SendGoal()
        {
            lock (_gate)
            {
                try { _session?.SetGoalAchieved(); }
                catch (Exception problem) { Problem($"Could not send the goal: {problem.Message}"); }
            }
        }

        public void SendDeath(string cause)
        {
            lock (_gate)
            {
                try { _deathLink?.SendDeathLink(new DeathLink(_slot, $"{_slot} {cause}")); }
                catch (Exception problem) { Problem($"Could not send the death: {problem.Message}"); }
            }
        }

        public object? SlotValue(string option)
        {
            lock (_gate) return _slotData.TryGetValue(option, out object? value) ? value : null;
        }

        public void Dispose()
        {
            lock (_gate) Drop();
        }

        /// <summary>"host", "host:port", "ws://host:port" and "wss://host:port" all work; the port defaults to 38281.</summary>
        public static (string host, int port) SplitAddress(string address)
        {
            string text = address.Trim();
            string scheme = "";
            int schemeEnd = text.IndexOf("://", StringComparison.Ordinal);
            if (schemeEnd >= 0)
            {
                scheme = text.Substring(0, schemeEnd + 3);
                text = text.Substring(schemeEnd + 3);
            }
            text = text.TrimEnd('/');
            int colon = text.LastIndexOf(':');
            if (colon > 0 && int.TryParse(text.Substring(colon + 1), out int port))
                return (scheme + text.Substring(0, colon), port);
            return (scheme + text, 38281);
        }

        private void OnItem(IReceivedItemsHelper helper)
        {
            while (helper.Any())
            {
                var item = helper.DequeueItem();
                if (DateTime.UtcNow >= _quietUntil) _log.Info($"Received: {item.ItemName ?? item.ItemDisplayName}");
            }
            Changed?.Invoke();
        }

        private void Drop()
        {
            ArchipelagoSession? old = _session;
            _session = null;
            _deathLink = null;
            if (old == null) return;
            try { old.Socket.DisconnectAsync().Wait(2000); }
            catch (Exception) { /* it is going away anyway */ }
        }

        /// <summary>Say a problem once, not on every retry.</summary>
        private void Problem(string message)
        {
            if (message == _lastProblem) return;
            _lastProblem = message;
            _log.Warn(message);
        }

        private static bool Truthy(object? value) => value switch
        {
            bool b => b,
            int i => i != 0,
            long l => l != 0,
            _ => false,
        };
    }
}
