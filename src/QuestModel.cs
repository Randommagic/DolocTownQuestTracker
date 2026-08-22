using System.Collections.Generic;

namespace DolocQuestTracker
{
    internal enum QuestKind
    {
        Faction,
        Seed
    }

    /// <summary>One required-item row of a quest. Have/Need are recomputed on every refresh.</summary>
    internal sealed class ItemLine
    {
        public string ItemName;   // internal id, e.g. "copper_ore"
        public string Title;      // localized display name
        public int Need;
        public int Have;
        public bool IsFinished;   // faction only: this line was already submitted

        public bool IsSatisfied
        {
            get { return IsFinished || Have >= Need; }
        }

        public int Shortfall
        {
            get
            {
                if (IsSatisfied)
                {
                    return 0;
                }
                return Need - Have;
            }
        }
    }

    /// <summary>A faction mission or a seed research node, flattened for display.</summary>
    internal sealed class QuestEntry
    {
        public QuestKind Kind;
        public string Id;                 // "vulture_mineral" / "seed_tomato"
        public string Title;
        public List<ItemLine> Lines = new List<ItemLine>();
        public int RequiredMoney;         // faction only, 0 when unused
        public bool MoneyFinished;
        public bool HasMoney;             // filled by the provider (can the player pay now)

        private string keyCache;

        /// <summary>
        /// Stable key used in the watchlist config: "faction:id" / "seed:id". Cached because
        /// Find() reads it for every entry it walks, and Kind/Id never change after the
        /// provider fills them in.
        /// </summary>
        public string Key
        {
            get
            {
                if (keyCache == null)
                {
                    keyCache = WatchKey.Format(Kind, Id);
                }
                return keyCache;
            }
        }

        public int OpenLineCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Lines.Count; i++)
                {
                    if (!Lines[i].IsFinished)
                    {
                        n++;
                    }
                }
                return n;
            }
        }

        /// <summary>True when every open line is covered by inventory (and money, if any).</summary>
        public bool IsSubmittable
        {
            get
            {
                for (int i = 0; i < Lines.Count; i++)
                {
                    if (!Lines[i].IsSatisfied)
                    {
                        return false;
                    }
                }
                return MoneyFinished || RequiredMoney <= 0 || HasMoney;
            }
        }

        /// <summary>Total missing item count across open lines — used for ordering.</summary>
        public int TotalShortfall
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Lines.Count; i++)
                {
                    n += Lines[i].Shortfall;
                }
                return n;
            }
        }
    }

    internal sealed class QuestSnapshot
    {
        public List<QuestEntry> Faction = new List<QuestEntry>();
        public List<QuestEntry> Seed = new List<QuestEntry>();

        public QuestEntry Find(string key)
        {
            for (int i = 0; i < Faction.Count; i++)
            {
                if (Faction[i].Key == key)
                {
                    return Faction[i];
                }
            }
            for (int i = 0; i < Seed.Count; i++)
            {
                if (Seed[i].Key == key)
                {
                    return Seed[i];
                }
            }
            return null;
        }
    }

    /// <summary>
    /// Watchlist keys are stored in the BepInEx config as "faction:id" / "seed:id".
    /// The prefix matters: lookup paths differ (TbFactionMission vs TbSeedUnlock) and
    /// it removes any chance of id collision between the two systems.
    /// </summary>
    internal static class WatchKey
    {
        private const string FactionPrefix = "faction:";
        private const string SeedPrefix = "seed:";

        public static string Format(QuestKind kind, string id)
        {
            if (kind == QuestKind.Seed)
            {
                return SeedPrefix + id;
            }
            return FactionPrefix + id;
        }

        public static bool TryParse(string key, out QuestKind kind, out string id)
        {
            kind = QuestKind.Faction;
            id = null;
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }
            string trimmed = key.Trim();
            if (trimmed.StartsWith(FactionPrefix))
            {
                kind = QuestKind.Faction;
                id = trimmed.Substring(FactionPrefix.Length);
                return id.Length > 0;
            }
            if (trimmed.StartsWith(SeedPrefix))
            {
                kind = QuestKind.Seed;
                id = trimmed.Substring(SeedPrefix.Length);
                return id.Length > 0;
            }
            return false;
        }
    }
}
