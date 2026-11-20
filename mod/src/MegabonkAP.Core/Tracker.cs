using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MegabonkAP.Core
{
    /// <summary>
    /// Turns what happens in the game into checks. It knows nothing about the game's code: the
    /// plugin tells it "a run started", "this enemy died", "this weapon is now level 7", in the
    /// game's own names, and the tracker decides which check that earns and whether it counts.
    /// Safe to call from any thread.
    /// </summary>
    public sealed class Tracker
    {
        private readonly CheckList _list;
        private readonly IServerLink _link;
        private readonly ILog _log;
        private readonly string? _stateFile;
        private readonly Func<DateTime> _now;
        private readonly object _gate = new();

        // checks earned but not yet taken by the server, and checks the server has taken
        private readonly HashSet<string> _owed = new();
        private readonly HashSet<string> _earnedThisStart = new();
        private readonly HashSet<string> _sent = new();
        private string? _session;
        private bool _goalSent;

        private readonly HashSet<string> _said = new();          // things already written to the log once
        private Dictionary<string, int> _held = new();           // item -> copies, as last heard from the server

        // the run being played
        private bool _inRun;
        private string? _character;
        private string? _map;
        private int _tier;
        private string? _challenge;
        private int _stageBosses;
        private bool _runCounted;
        private bool _refusalSaid;
        private DateTime _deathReceived = DateTime.MinValue;

        /// <summary>
        /// On: a check only counts when the run and the thing are permitted by held items, as the
        /// world's rules say. Off: everything that happens counts.
        /// </summary>
        public bool Strict { get; set; } = true;

        /// <summary>Off: deaths are neither sent nor acted on, whatever the slot says.</summary>
        public bool DeathLinkAllowed { get; set; } = true;

        /// <summary>Another player died and this player should too: who, and why.</summary>
        public event Action<string, string>? KillRequested;

        public Tracker(CheckList list, IServerLink link, ILog log, string? stateFile = null, Func<DateTime>? now = null)
        {
            _list = list;
            _link = link;
            _log = log;
            _stateFile = stateFile;
            _now = now ?? (() => DateTime.UtcNow);
            LoadState();
            _link.Changed += OnServerChanged;
            _link.DeathReceived += OnDeathReceived;
        }

        // ------------------------------------------------------------------ what the plugin reports

        /// <summary>A run began. Any argument may be null or 0 when the plugin could not read it.</summary>
        public void RunStarted(string? character, string? map, int tier, string? challenge = null)
        {
            lock (_gate)
            {
                _inRun = true;
                _character = Known("character", character);
                _map = Known("map", map);
                _tier = tier;
                _challenge = string.IsNullOrWhiteSpace(challenge) ? null : challenge;
                _stageBosses = 0;
                _runCounted = false;
                _refusalSaid = false;
                _log.Info($"Run started: {_character ?? "unknown character"} on {_map ?? "unknown map"}"
                    + (_tier > 0 ? $" Tier {_tier}" : "") + (_challenge != null ? $", challenge {_challenge}" : ""));
                RunCounts();
            }
        }

        /// <summary>The character being played, for when it could not be read at the start of the run.</summary>
        public void CharacterSeen(string? character)
        {
            lock (_gate)
            {
                if (_character != null) return;
                _character = Known("character", character);
                if (_character != null) _log.Info($"Playing as {_character}.");
            }
        }

        public void PlayerLevel(int level)
        {
            lock (_gate)
            {
                if (!_inRun || _character == null || level < _list.CharacterLevel) return;
                if (RunCounts()) Earn(_list.Characters[_character].LevelCheck);
            }
        }

        public void WeaponLevel(string? weapon, int level)
        {
            lock (_gate)
            {
                if (!_inRun || level < _list.WeaponLevel) return;
                string? name = Known("weapon", weapon);
                if (name == null || !RunCounts()) return;
                if (Strict && !HoldsWeapon(name))
                {
                    SayOnce($"{name} reached Level {level}, but it does not count: you do not hold {_list.Weapons[name].Item}.");
                    return;
                }
                Earn(_list.Weapons[name].LevelCheck);
            }
        }

        public void TomeLevel(string? tome, int level)
        {
            lock (_gate)
            {
                if (!_inRun || level < _list.TomeLevel) return;
                string? name = Known("tome", tome);
                if (name == null || !RunCounts()) return;
                if (Strict && !Holds(_list.Tomes[name].Item))
                {
                    SayOnce($"{name} Tome reached Level {level}, but it does not count: you do not hold {_list.Tomes[name].Item}.");
                    return;
                }
                Earn(_list.Tomes[name].LevelCheck);
            }
        }

        /// <summary>An enemy died. stageBoss: the game marks it as the boss that ends a stage (or the final boss).</summary>
        public void EnemyKilled(string? name, bool stageBoss)
        {
            lock (_gate)
            {
                if (!_inRun || _map == null) return;
                CheckList.Map map = _list.Maps[_map];

                string? boss = _list.ResolveBoss(_map, name);
                if (boss != null && map.BossChecks.TryGetValue(boss, out string? bossCheck) && RunCounts())
                {
                    Earn(bossCheck);
                    if (_map == "Graveyard" && boss == "Big Bob") RunWon();     // Big Bob ends the Graveyard
                }
                else if (stageBoss && boss == null)
                {
                    if (_said.Add("boss:" + _map + name)) _log.Info($"Stage boss killed on {_map}: the game calls it '{name}'.");
                }

                if (stageBoss && map.TierChecks.Count > 0)
                {
                    _stageBosses++;
                    if (_tier > 0 && _stageBosses >= _tier) RunWon();
                    else if (_tier <= 0) SayOnce("A stage boss died, but the tier of this run could not be read, so no tier check is sent.");
                }
            }
        }

        /// <summary>The Graveyard's boss room opened, which takes all four Crypt Keys.</summary>
        public void CryptOpened()
        {
            lock (_gate)
            {
                if (!_inRun || _map != "Graveyard") return;
                string? check = _list.Maps["Graveyard"].KeysCheck;
                if (check != null && RunCounts()) Earn(check);
            }
        }

        /// <summary>The run was completed: the last boss of the chosen tier is dead.</summary>
        public void RunWon()
        {
            lock (_gate)
            {
                if (!_inRun || _runCounted || !RunCounts()) return;
                _runCounted = true;
                if (_map != null)
                {
                    CheckList.Map map = _list.Maps[_map];
                    if (_tier >= 1 && _tier <= map.TierChecks.Count) Earn(map.TierChecks[_tier - 1]);
                    if (_challenge != null)
                    {
                        string? challenge = _list.ResolveChallenge(_map, _challenge);
                        if (challenge != null) Earn(challenge);
                        else SayOnce($"Challenge finished on {_map}: the game calls it '{_challenge}', which matches no check.");
                    }
                }
                if (_character != null) Earn(_list.Characters[_character].RunCheck);
                CheckGoal();
            }
        }

        public void PlayerDied()
        {
            lock (_gate)
            {
                if (!_inRun) return;
                if (!DeathLinkOn()) return;
                if ((_now() - _deathReceived).TotalSeconds < 10) return;     // that death came from someone else
                _log.Info("You died. Telling the others.");
                _link.SendDeath("got bonked");
            }
        }

        /// <summary>The run is over, however it ended.</summary>
        public void RunEnded()
        {
            lock (_gate) { _inRun = false; }
        }

        /// <summary>Try again to send what is still owed. Call it every few seconds.</summary>
        public void Flush()
        {
            lock (_gate)
            {
                if (!_link.Connected) return;
                AdoptSession();
                if (_owed.Count > 0)
                {
                    foreach (string gone in _owed.Where(c => !_link.Exists(c)).ToList())
                    {
                        _owed.Remove(gone);                 // this seed does not have that check
                    }
                    foreach (string done in _owed.Where(c => _link.IsChecked(c)).ToList())
                    {
                        _owed.Remove(done);
                        _sent.Add(done);
                    }
                    if (_owed.Count > 0)
                    {
                        IReadOnlyCollection<string> taken = _link.Send(_owed.ToList());
                        foreach (string check in taken)
                        {
                            _owed.Remove(check);
                            _sent.Add(check);
                            _log.Info($"Check sent: {check}");
                        }
                    }
                    SaveState();
                }
                CheckGoal();
            }
        }

        // ------------------------------------------------------------------ what the player holds

        public bool Holds(string item)
        {
            lock (_gate) return _held.TryGetValue(item, out int copies) && copies > 0;
        }

        public int Copies(string item)
        {
            lock (_gate) return _held.TryGetValue(item, out int copies) ? copies : 0;
        }

        /// <summary>A weapon is usable through its own item, or because the character in play starts with it.</summary>
        public bool HoldsWeapon(string weapon)
        {
            lock (_gate)
            {
                if (Holds(_list.Weapons[weapon].Item)) return true;
                return _character != null && _list.Characters[_character].Weapon == weapon
                    && Holds(_list.Characters[_character].Item);
            }
        }

        /// <summary>Checks earned and not yet taken by the server.</summary>
        public IReadOnlyCollection<string> Owed
        {
            get { lock (_gate) return _owed.ToList(); }
        }

        // ------------------------------------------------------------------ inside

        /// <summary>Whether this run counts at all: the character and the map tier must be held.</summary>
        private bool RunCounts()
        {
            if (!Strict) return true;
            string? reason = null;
            if (_character == null) reason = "the character could not be read";
            else if (!Holds(_list.Characters[_character].Item)) reason = $"you do not hold {_list.Characters[_character].Item}";
            else if (_map == null) reason = "the map could not be read";
            else
            {
                CheckList.Map map = _list.Maps[_map];
                int needed = map.TierChecks.Count > 0 ? Math.Max(_tier, 1) : 1;
                if (Copies(map.Item) < needed)
                    reason = map.TierChecks.Count > 0
                        ? $"{_map} Tier {needed} needs {needed} {map.Item}, you hold {Copies(map.Item)}"
                        : $"you do not hold {map.Item}";
            }
            if (reason == null) return true;
            if (!_refusalSaid)
            {
                _refusalSaid = true;
                _log.Warn($"This run does not count: {reason}.");
            }
            return false;
        }

        private void Earn(string check)
        {
            if (_sent.Contains(check) || !_owed.Add(check)) return;
            _earnedThisStart.Add(check);
            _log.Info($"Check earned: {check}");
            SaveState();
            Flush();
        }

        private void CheckGoal()
        {
            if (_goalSent || !_link.Connected) return;
            int? goal = AsInt(_link.SlotValue("goal"));
            if (goal == null || goal < 0 || goal >= _list.Goals.Count) return;
            int tier = goal.Value + 1;      // the goals are "Tier 1 Bosses", "Tier 2 Bosses", "Tier 3 Bosses"
            foreach (string mapName in new[] { "Forest", "Desert" })
            {
                CheckList.Map map = _list.Maps[mapName];
                if (tier > map.TierChecks.Count) return;
                string check = map.TierChecks[tier - 1];
                if (!_sent.Contains(check) && !_link.IsChecked(check)) return;
            }
            _goalSent = true;
            _log.Info($"Goal reached: {_list.Goals[goal.Value]}.");
            _link.SendGoal();
        }

        private bool DeathLinkOn() => DeathLinkAllowed && _link.Connected && AsInt(_link.SlotValue("death_link")) == 1;

        private void OnDeathReceived(string who, string why)
        {
            lock (_gate)
            {
                if (!DeathLinkOn()) return;
                _deathReceived = _now();
            }
            _log.Info($"{who} died ({why}).");
            KillRequested?.Invoke(who, why);
        }

        private void OnServerChanged()
        {
            lock (_gate)
            {
                var held = new Dictionary<string, int>();
                foreach (string item in _link.ReceivedItems)
                    held[item] = held.TryGetValue(item, out int copies) ? copies + 1 : 1;
                _held = held;
                _refusalSaid = false;       // a new item may have made the run count
            }
            Flush();
        }

        /// <summary>Owed checks belong to one seed and slot: drop the ones kept from another.</summary>
        private void AdoptSession()
        {
            string? session = _link.Session;
            if (session == null || session == _session) return;
            if (_session != null)
            {
                _owed.IntersectWith(_earnedThisStart);      // what was kept belongs to another seed
                _sent.Clear();
            }
            _session = session;
            _goalSent = false;
            SaveState();
        }

        private string? Known(string kind, string? gameName)
        {
            if (string.IsNullOrWhiteSpace(gameName)) return null;
            string? name = _list.Resolve(kind, gameName);
            if (name == null) SayOnce($"The game named a {kind} '{gameName}' that matches nothing in the list.");
            return name;
        }

        private void SayOnce(string message)
        {
            if (_said.Add(message)) _log.Warn(message);
        }

        private static int? AsInt(object? value) => value switch
        {
            null => null,
            bool b => b ? 1 : 0,
            int i => i,
            long l => (int)l,
            double d => (int)d,
            string s when int.TryParse(s, out int parsed) => parsed,
            _ => null,
        };

        private void LoadState()
        {
            if (_stateFile == null || !File.Exists(_stateFile)) return;
            try
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(_stateFile));
                JsonElement root = document.RootElement;
                _session = root.TryGetProperty("session", out JsonElement s) ? s.GetString() : null;
                foreach (JsonElement e in root.GetProperty("owed").EnumerateArray()) _owed.Add(e.GetString()!);
                foreach (JsonElement e in root.GetProperty("sent").EnumerateArray()) _sent.Add(e.GetString()!);
            }
            catch (Exception problem)
            {
                _log.Warn($"Could not read {_stateFile}: {problem.Message}. Starting with nothing owed.");
                _owed.Clear();
                _sent.Clear();
            }
        }

        private void SaveState()
        {
            if (_stateFile == null) return;
            try
            {
                string? folder = Path.GetDirectoryName(_stateFile);
                if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
                string json = JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["session"] = _session,
                    ["owed"] = _owed.OrderBy(c => c).ToList(),
                    ["sent"] = _sent.OrderBy(c => c).ToList(),
                }, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_stateFile, json);
            }
            catch (Exception problem)
            {
                SayOnce($"Could not write {_stateFile}: {problem.Message}");
            }
        }
    }
}
