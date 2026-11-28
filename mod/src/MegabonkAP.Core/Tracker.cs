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

        // what the overlay shows, and what locking goes by
        private bool _itemsKnown;                                // the server has said what is held, this start
        private bool _connected;
        private HashSet<string> _done = new();                   // checks the server counts as done
        private int _total;
        private int _minibosses;                                 // defeated in this seed, over all runs
        private int _minibossesThisStart;
        private bool? _huntOn;                                   // whether this seed has the miniboss hunt; null before connecting
        private string? _deathLink;                              // "on", or why it is off; null before connecting
        private string? _picked;                                 // the character last confirmed in the menu
        private readonly List<Note> _notes = new();

        // traps: items that do something in the game when they arrive, each exactly once
        private IReadOnlyList<string> _received = Array.Empty<string>();    // every item received, in the server's order
        private int _itemsActedOn;                               // the traps among the first so many of them are done
        private bool _trapsReady;                                // the server has said how far an earlier start got
        private DateTime _nextTrap = DateTime.MinValue;

        /// <summary>A trap that is due: its place among the received items (from 1), its item and what it does.</summary>
        public sealed record Trap(int Position, string Item, string Effect);

        /// <summary>How long a run must have been going before a trap arrives.</summary>
        public static readonly TimeSpan TrapDelay = TimeSpan.FromSeconds(10);

        /// <summary>The pause between two traps.</summary>
        public static readonly TimeSpan TrapGap = TimeSpan.FromSeconds(15);

        /// <summary>A line for the player: an item arrived, a check went out, something was refused.</summary>
        public sealed record Note(DateTime At, string Text, bool Bad);

        /// <summary>
        /// On: a check only counts when the run and the thing are permitted by held items, as the
        /// world's rules say. Off: everything that happens counts.
        /// </summary>
        public bool Strict { get; set; } = true;

        /// <summary>
        /// On, together with Strict: what is not held cannot be used. A locked character or map tier
        /// cannot be started, and locked weapons and tomes are not offered. Nothing is locked before
        /// the server has said what is held.
        /// </summary>
        public bool Lock { get; set; } = true;

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

        /// <summary>
        /// A run began. Any argument may be null or 0 when it could not be read; the character
        /// usually follows a moment later through CharacterSeen.
        /// </summary>
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
                DateTime first = _now() + TrapDelay;
                if (first > _nextTrap) _nextTrap = first;
                _log.Info($"Run started: {_map ?? "unknown map"}" + (_tier > 0 ? $" Tier {_tier}" : "")
                    + (_character != null ? $" as {_character}" : "") + (_challenge != null ? $", challenge {_challenge}" : ""));
            }
        }

        /// <summary>The character being played. The game says so when the player appears, after the run began.</summary>
        public void CharacterSeen(string? character)
        {
            lock (_gate)
            {
                string? name = Known("character", character);
                if (name == null || name == _character) return;
                _character = name;
                _picked = name;
                _refusalSaid = false;
                _log.Info($"Playing as {_character}.");
                if (_inRun) RunCounts();        // say at once when this run will not count
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

        /// <summary>
        /// An enemy died, under the game's own name for it. boss: the game marks it as some kind of
        /// boss, which only matters for the log: a boss the list does not know is written down once.
        /// </summary>
        public void EnemyKilled(string? name, bool boss, bool miniboss = false)
        {
            lock (_gate)
            {
                if (!_inRun) return;
                // a boss with a check of its own that is not a miniboss (the Suspicious Bush, the Bandit) is not one for the hunt
                if (miniboss && _list.EnemyCheck(name) != null && !(name ?? "").StartsWith("Miniboss", StringComparison.OrdinalIgnoreCase))
                    miniboss = false;
                if (miniboss && _huntOn != false && _list.MinibossHunt.Count > 0 && RunCounts())
                {
                    _minibosses++;
                    _minibossesThisStart++;
                    int? next = _list.MinibossHunt.Keys.Cast<int?>().FirstOrDefault(n => n > _minibosses);
                    _log.Info($"Miniboss defeated ({name}): {_minibosses} in this seed"
                        + (next != null ? $", next check at {next}." : "."));
                    if (_huntOn == true && !_list.MinibossHunt.ContainsKey(_minibosses))
                        Tell(next != null ? $"Miniboss {_minibosses} defeated: next check at {next}." : $"Miniboss {_minibosses} defeated.");
                    foreach (KeyValuePair<int, string> step in _list.MinibossHunt)
                        if (step.Key <= _minibosses) Earn(step.Value);
                    SaveState();
                }
                string? check = _list.EnemyCheck(name);
                if (check != null)
                {
                    if (RunCounts()) Earn(check);
                }
                else if (boss && _said.Add("boss:" + _map + name))
                {
                    _log.Info($"Boss killed on {_map ?? "an unknown map"}: the game calls it '{name}'.");
                }
            }
        }

        /// <summary>
        /// The boss that ends a stage died. tierFinal: whether the game says this was the last stage
        /// of the chosen tier; null when that could not be read, and then stage bosses are counted
        /// against the tier instead.
        /// </summary>
        public void StageBossDied(bool? tierFinal)
        {
            lock (_gate)
            {
                if (!_inRun) return;
                _stageBosses++;
                if (tierFinal == true) { RunWon(); return; }
                if (tierFinal == false) { _log.Info($"Stage boss {_stageBosses} down; the tier is not finished yet."); return; }
                if (_tier > 0 && _stageBosses >= _tier) RunWon();
                else if (_tier <= 0) SayOnce("A stage boss died, but the tier of this run could not be read, so no tier check is sent.");
            }
        }

        /// <summary>A boss the mod recognises by what happened ("Big Bob"). endsRun: beating it completes the run.</summary>
        public void BossDefeated(string boss, bool endsRun)
        {
            lock (_gate)
            {
                if (!_inRun) return;
                if (_list.BossChecks.TryGetValue(boss, out string? check) && RunCounts()) Earn(check);
                if (endsRun) RunWon();
            }
        }

        /// <summary>The game says the run's challenge is complete. name: the game's name for it, or null to use the one the run began with.</summary>
        public void ChallengeCompleted(params string?[] names)
        {
            lock (_gate)
            {
                if (!_inRun || _map == null) return;
                var candidates = new List<string?>(names) { _challenge };
                foreach (string? candidate in candidates)
                {
                    string? check = _list.ResolveChallenge(_map, candidate);
                    if (check == null) continue;
                    if (RunCounts()) Earn(check);
                    return;
                }
                string called = string.Join("' / '", candidates.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct());
                SayOnce($"Challenge finished on {_map}: the game calls it '{called}', which matches no check.");
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

        /// <summary>Try again to send what is still owed. Call it every few seconds.</summary>
        public void Flush()
        {
            lock (_gate)
            {
                if (!_link.Connected) { _connected = false; return; }
                _connected = true;
                AdoptSession();
                ReadProgress();
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
                            Tell($"Check sent: {check}");
                        }
                    }
                    SaveState();
                }
                if (_trapsReady && _link.ItemsActedOn is int kept && kept < _itemsActedOn) _link.SetItemsActedOn(_itemsActedOn);
                CheckGoal();
            }
        }

        // ------------------------------------------------------------------ traps

        /// <summary>
        /// The trap to carry out now, or null: the oldest trap item not yet dealt with, once a run
        /// has been going for a moment and the last trap is a while ago. Traps that arrive outside
        /// a run wait for the next one.
        /// </summary>
        public Trap? NextTrap()
        {
            lock (_gate)
            {
                if (!_inRun || !_trapsReady || _now() < _nextTrap) return null;
                for (int i = Math.Max(_itemsActedOn, 0); i < _received.Count; i++)
                    if (_list.Traps.TryGetValue(_received[i], out string? effect)) return new Trap(i + 1, _received[i], effect);
                return null;
            }
        }

        /// <summary>
        /// The trap was dealt with and will not come again. problem: null when it was carried out,
        /// otherwise why the game could not be made to do it.
        /// </summary>
        public void TrapDone(Trap trap, string? problem = null)
        {
            lock (_gate)
            {
                if (trap.Position <= _itemsActedOn) return;
                _itemsActedOn = trap.Position;
                _nextTrap = _now() + TrapGap;
                if (problem == null)
                {
                    _log.Info($"Trap sprung: {trap.Item}.");
                    Tell($"Trap sprung: {TrapName(trap.Item)}!", bad: true);
                }
                else
                {
                    _log.Warn($"{trap.Item} did nothing: {problem}");
                    Tell($"{trap.Item} did nothing: the game could not be made to do it (see the log).", bad: true);
                }
                SaveState();
                if (_link.Connected) _link.SetItemsActedOn(_itemsActedOn);
            }
        }

        private static string TrapName(string item) =>
            item.StartsWith("Trap - ", StringComparison.Ordinal) ? item.Substring("Trap - ".Length) : item;

        private int TrapsWaiting()
        {
            int waiting = 0;
            for (int i = Math.Max(_itemsActedOn, 0); i < _received.Count; i++)
                if (_list.Traps.ContainsKey(_received[i])) waiting++;
            return waiting;
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

        // ------------------------------------------------------------------ what may be used

        /// <summary>Whether anything is locked at all right now.</summary>
        public bool Locking
        {
            get { lock (_gate) return Strict && Lock && _itemsKnown; }
        }

        /// <summary>
        /// The player confirmed a character in the menu, under the game's name for it. Null: go
        /// ahead. Otherwise the reason it is refused, already written to the log and the overlay.
        /// </summary>
        public string? CharacterRefusal(string? gameName)
        {
            lock (_gate)
            {
                string? name = _list.Resolve("character", gameName);
                if (name == null) return null;                      // not a character of the world: not ours to refuse
                if (Locking && !Holds(_list.Characters[name].Item))
                    return Refuse($"{name} is locked: you do not hold {_list.Characters[name].Item}.");
                _picked = name;
                return null;
            }
        }

        /// <summary>A run is about to start on this map and tier. Null: go ahead. Otherwise the reason it is refused.</summary>
        public string? RunRefusal(string? gameMap, int tier)
        {
            lock (_gate)
            {
                string? name = _list.Resolve("map", gameMap);
                if (name == null || !Locking) return null;
                CheckList.Map map = _list.Maps[name];
                if (map.TierChecks.Count == 0)
                    return Holds(map.Item) ? null : Refuse($"{name} is locked: you do not hold {map.Item}.");
                int needed = Math.Max(tier, 1);
                if (Copies(map.Item) >= needed) return null;
                return Refuse($"{name} Tier {needed} is locked: it needs {needed} {map.Item}, you hold {Copies(map.Item)}.");
            }
        }

        /// <summary>Whether the game may offer this weapon or tome ("weapon" / "tome", the game's name) at a level-up.</summary>
        public bool OfferAllowed(string kind, string? gameName)
        {
            lock (_gate)
            {
                if (!Locking) return true;
                string? name = _list.Resolve(kind, gameName);
                if (name == null) return true;                      // not in the world: left alone
                if (kind == "tome") return Holds(_list.Tomes[name].Item);
                if (Holds(_list.Weapons[name].Item)) return true;
                string? character = _character ?? _picked;          // a character's own weapon comes with the character
                return character != null && _list.Characters[character].Weapon == name
                    && Holds(_list.Characters[character].Item);
            }
        }

        /// <summary>
        /// The player tried to pick a weapon or tome at a level-up. Null: go ahead. Otherwise the
        /// reason it is refused, already written to the log and the overlay.
        /// </summary>
        public string? OfferRefusal(string kind, string? gameName)
        {
            lock (_gate)
            {
                if (OfferAllowed(kind, gameName)) return null;
                string name = _list.Resolve(kind, gameName)!;
                return kind == "tome"
                    ? Refuse($"{name} Tome is locked: you do not hold {_list.Tomes[name].Item}.")
                    : Refuse($"{name} is locked: you do not hold {_list.Weapons[name].Item}.");
            }
        }

        /// <summary>Put a line on the overlay and in the log.</summary>
        public void Say(string text, bool bad = false)
        {
            lock (_gate)
            {
                if (bad) _log.Warn(text); else _log.Info(text);
                Tell(text, bad);
            }
        }

        /// <summary>The plugin learned whether the server can be reached. Call it after every try.</summary>
        public void ConnectionSeen(bool connected)
        {
            lock (_gate) _connected = connected;
        }

        /// <summary>Everything the overlay shows, as of now.</summary>
        public Snapshot Look()
        {
            lock (_gate)
            {
                return new Snapshot
                {
                    Connected = _connected,
                    ItemsKnown = _itemsKnown,
                    Locking = Locking,
                    Done = _done.Count,
                    Total = _total,
                    DoneChecks = new HashSet<string>(_done.Concat(_sent)),
                    Owed = _owed.Count,
                    Held = new Dictionary<string, int>(_held),
                    InRun = _inRun,
                    Character = _character,
                    Map = _map,
                    Tier = _tier,
                    RunProblem = _inRun ? RunProblem() : null,
                    DeathLink = _deathLink,
                    Minibosses = _huntOn == true ? _minibosses : (int?)null,
                    TrapsWaiting = _trapsReady ? TrapsWaiting() : 0,
                    Notes = _notes.ToList(),
                };
            }
        }

        public sealed class Snapshot
        {
            public bool Connected, ItemsKnown, Locking, InRun;
            public int Done, Total, Owed, Tier;
            public int TrapsWaiting;        // trap items received and not yet carried out
            public int? Minibosses;         // defeated in this seed; null when the seed has no miniboss hunt
            public string? Character, Map, RunProblem, DeathLink;
            public IReadOnlyCollection<string> DoneChecks = Array.Empty<string>();
            public IReadOnlyDictionary<string, int> Held = new Dictionary<string, int>();
            public IReadOnlyList<Note> Notes = Array.Empty<Note>();
        }

        private string Refuse(string reason)
        {
            _log.Warn(reason);
            Tell(reason, bad: true);
            return reason;
        }

        /// <summary>Keep a line for the overlay. The same line again within a few seconds is one line.</summary>
        private void Tell(string text, bool bad = false)
        {
            DateTime now = _now();
            if (_notes.Count > 0 && _notes[^1].Text == text && (now - _notes[^1].At).TotalSeconds < 5)
            {
                _notes[^1] = new Note(now, text, bad);
                return;
            }
            _notes.Add(new Note(now, text, bad));
            if (_notes.Count > 12) _notes.RemoveAt(0);
        }

        private void ReadProgress()
        {
            if (!_link.Connected) return;
            string deathLink = !DeathLinkAllowed ? "off in the mod's settings"
                : AsInt(_link.SlotValue("death_link")) == 1 ? "on" : "off: this seed was generated with death_link off";
            if (deathLink != _deathLink)
            {
                _deathLink = deathLink;
                _log.Info($"DeathLink is {deathLink}.");
            }
            if (_link.ItemsActedOn is int kept && _link.Session == _session)
            {
                // another start, or another computer, may have carried out more of them
                if (kept > _itemsActedOn) { _itemsActedOn = kept; SaveState(); }
                _trapsReady = true;
            }
            _done = new HashSet<string>(_link.CheckedNames);
            _total = _link.TotalChecks;
            if (_list.MinibossHunt.Count > 0)
            {
                _huntOn = _link.Exists(_list.MinibossHunt.Values.First());
                // the server remembers the hunt even when this computer's own count was lost
                foreach (KeyValuePair<int, string> step in _list.MinibossHunt)
                    if (_done.Contains(step.Value) && step.Key > _minibosses) _minibosses = step.Key;
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
            string? reason = RunProblem();
            if (reason == null) return true;
            if (!_refusalSaid)
            {
                _refusalSaid = true;
                _log.Warn($"This run does not count: {reason}.");
                Tell($"This run does not count: {reason}.", bad: true);
            }
            return false;
        }

        /// <summary>Why the run being played does not count, or null when it does.</summary>
        private string? RunProblem()
        {
            if (!Strict) return null;
            string? reason = null;
            if (_character == null) reason = "the game has not said which character this is";
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
            return reason;
        }

        private void Earn(string check)
        {
            if (_sent.Contains(check) || !_owed.Add(check)) return;
            _earnedThisStart.Add(check);
            _log.Info($"Check earned: {check}");
            if (!_link.Connected) Tell($"Check earned, kept until the server is back: {check}");
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
            Tell($"Goal reached: {_list.Goals[goal.Value]}!");
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
            lock (_gate) Tell($"DeathLink: {who} died ({why}).", bad: true);
            KillRequested?.Invoke(who, why);
        }

        private void OnServerChanged()
        {
            lock (_gate)
            {
                if (_link.Connected) AdoptSession();        // what was kept from another seed must not be applied to this one
                IReadOnlyList<string> received = _link.ReceivedItems;
                var held = new Dictionary<string, int>();
                foreach (string item in received)
                    held[item] = held.TryGetValue(item, out int copies) ? copies + 1 : 1;
                if (_itemsKnown)
                {
                    foreach (KeyValuePair<string, int> now in held.OrderBy(h => h.Key))
                        if (now.Value > (_held.TryGetValue(now.Key, out int before) ? before : 0)) Tell($"Received: {now.Key}");
                }
                else if (_link.Connected)
                {
                    _itemsKnown = true;
                    Tell($"Connected: you hold {held.Values.Sum()} items.");
                }
                _held = held;
                if (_link.Connected) _received = received;
                _connected = _link.Connected;
                _refusalSaid = false;       // a new item may have made the run count
                ReadProgress();
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
                _minibosses = _minibossesThisStart;
                _itemsActedOn = 0;
                _received = Array.Empty<string>();
            }
            _trapsReady = false;                            // until this seed's own record has been read
            _session = session;
            _goalSent = false;
            SaveState();
        }

        private string? Known(string kind, string? gameName)
        {
            if (string.IsNullOrWhiteSpace(gameName)) return null;
            string? name = _list.Resolve(kind, gameName);
            if (name == null && !_list.IsIgnored(kind, gameName)) SayOnce($"The game named a {kind} '{gameName}' that matches nothing in the list.");
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
                if (root.TryGetProperty("minibosses", out JsonElement m)) _minibosses = m.GetInt32();
                if (root.TryGetProperty("items_acted_on", out JsonElement a)) _itemsActedOn = a.GetInt32();
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
                    ["minibosses"] = _minibosses,
                    ["items_acted_on"] = _itemsActedOn,
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
