using System;
using System.Collections.Generic;

namespace MegabonkAP.Core
{
    /// <summary>Where the mod writes what it is doing. The plugin sends it to BepInEx's log.</summary>
    public interface ILog
    {
        void Info(string message);
        void Warn(string message);
    }

    /// <summary>The Archipelago server as the tracker sees it. ArchipelagoLink is the real one.</summary>
    public interface IServerLink
    {
        bool Connected { get; }

        /// <summary>Names the seed and slot connected to, or null before the first connection.</summary>
        string? Session { get; }

        /// <summary>Names of every item received so far, one entry per copy.</summary>
        IReadOnlyList<string> ReceivedItems { get; }

        /// <summary>True when the server already counts this check as done.</summary>
        bool IsChecked(string check);

        /// <summary>True when this seed has the check at all (an option may have switched it off).</summary>
        bool Exists(string check);

        /// <summary>Send checks. Returns the ones the server took; the rest stay owed.</summary>
        IReadOnlyCollection<string> Send(IEnumerable<string> checks);

        void SendGoal();
        void SendDeath(string cause);

        /// <summary>An option of the slot as generated ("goal", "death_link", ...), or null.</summary>
        object? SlotValue(string option);

        /// <summary>Raised after connecting and whenever an item arrives. May come from another thread.</summary>
        event Action? Changed;

        /// <summary>Another player died (DeathLink): who, and why. May come from another thread.</summary>
        event Action<string, string>? DeathReceived;
    }
}
