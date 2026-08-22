using System.Collections.Generic;
using System.Text;
using BepInEx.Configuration;

namespace DolocTownQuestTracker
{
    /// <summary>
    /// The set of entries the overlay shows, kept per save.
    ///
    /// BepInEx config is one file for the whole game, so every save's selections share a
    /// single string, grouped by save slot:
    ///
    ///     0/1786903040=faction:vulture_fossil,seed:seed_tomato;2/1787376658=faction:x
    ///     ^ ^          ^                                      ^
    ///     | |          that slot's selections                 next slot
    ///     | which save was in the slot (SaveIdentity)
    ///     slot number
    ///
    /// The slot number alone cannot name a save, because deleting one frees its number for the
    /// next new game -- see SaveIdentity for what the second field is and why.
    ///
    /// Only ids are stored; completion is recomputed every refresh. Resolve filters finished
    /// entries out of what's shown -- freeing a slot toward the cap -- but the file itself is
    /// only rewritten on the next toggle, when Prune drops them for good (see Prune).
    /// </summary>
    internal sealed class Watchlist
    {
        internal const int MaxTracked = 5;

        /// <summary>Shape of the stored string, for the config file's own comment.</summary>
        internal const string StoredFormat = "slot/save=faction:id,seed:id;slot/save=...";

        private const char SlotSeparator = ';';
        private const char HeaderSeparator = '=';
        private const char SaveSeparator = '/';
        private const char KeySeparator = ',';

        private sealed class Slot
        {
            internal long Save;   // 0 = not known, see SaveIdentity
            internal readonly List<string> Keys = new List<string>();
        }

        private readonly ConfigEntry<string> store;
        private readonly Dictionary<int, Slot> slots = new Dictionary<int, Slot>();

        internal Watchlist(ConfigEntry<string> store)
        {
            this.store = store;
            Load();
        }

        /// <summary>
        /// The loaded save's list, or null when no save is open.
        ///
        /// Resolved on every call rather than cached: the plugin starts long before a save
        /// exists, the panel patches fire independently of Core's tick, and the player can
        /// switch saves without anything telling us. Pulling here means the answer is right on
        /// the first call whenever it happens, with no initialisation order to get wrong.
        /// </summary>
        private Slot Active()
        {
            int index;
            long save;
            if (!SaveIdentity.TryCurrent(out index, out save))
            {
                return null;
            }

            Slot slot;
            bool changed = false;
            if (!slots.TryGetValue(index, out slot))
            {
                slot = new Slot();
                slots[index] = slot;
            }
            else if (SaveIdentity.IsDifferentSave(slot.Save, save))
            {
                // A new game took this slot number over from the save these entries belong to.
                // Drop them rather than show quests nobody picked.
                slot.Keys.Clear();
                changed = true;
            }

            if (save != 0L && slot.Save != save)
            {
                slot.Save = save;
                changed = true;
            }
            if (changed)
            {
                Save();
            }
            return slot;
        }

        private void Load()
        {
            slots.Clear();

            string raw = store.Value;
            if (string.IsNullOrEmpty(raw))
            {
                return;
            }

            string[] parts = raw.Split(SlotSeparator);
            for (int i = 0; i < parts.Length; i++)
            {
                // Split with a count of 2 so a stray separator in the tail stays in the tail.
                string[] halves = parts[i].Split(new char[] { HeaderSeparator }, 2);
                if (halves.Length != 2)
                {
                    continue;
                }
                string[] header = halves[0].Split(new char[] { SaveSeparator }, 2);
                if (header.Length != 2)
                {
                    continue;
                }
                int index;
                if (!int.TryParse(header[0].Trim(), out index))
                {
                    continue;
                }

                Slot slot = new Slot();
                long.TryParse(header[1].Trim(), out slot.Save);   // unparsable = 0 = unknown
                slot.Keys.AddRange(ParseKeys(halves[1]));
                slots[index] = slot;
            }
        }

        private static List<string> ParseKeys(string csv)
        {
            List<string> keys = new List<string>();
            string[] parts = csv.Split(KeySeparator);
            for (int i = 0; i < parts.Length; i++)
            {
                QuestKind kind;
                string id;
                if (!WatchKey.TryParse(parts[i].Trim(), out kind, out id))
                {
                    continue;
                }
                string normalized = WatchKey.Format(kind, id);
                if (!keys.Contains(normalized))
                {
                    keys.Add(normalized);
                }
            }
            return keys;
        }

        private void Save()
        {
            // Numeric slot order, so the file does not reshuffle on every write.
            List<int> indices = new List<int>(slots.Keys);
            indices.Sort();

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < indices.Count; i++)
            {
                Slot slot = slots[indices[i]];
                if (slot.Keys.Count == 0)
                {
                    continue;
                }
                if (sb.Length > 0)
                {
                    sb.Append(SlotSeparator);
                }
                sb.Append(indices[i]).Append(SaveSeparator).Append(slot.Save)
                  .Append(HeaderSeparator);
                for (int k = 0; k < slot.Keys.Count; k++)
                {
                    if (k > 0)
                    {
                        sb.Append(KeySeparator);
                    }
                    sb.Append(slot.Keys[k]);
                }
            }
            store.Value = sb.ToString();
        }

        internal bool Contains(string key)
        {
            Slot slot = Active();
            return slot != null && slot.Keys.Contains(key);
        }

        /// <summary>Entries still visible in the given snapshot, in selection order, capped.</summary>
        internal List<QuestEntry> Resolve(QuestSnapshot snapshot)
        {
            List<QuestEntry> result = new List<QuestEntry>();
            Slot slot = Active();
            if (slot != null)
            {
                CollectLive(slot, snapshot, result);
            }
            return result;
        }

        /// <summary>The cap counts only entries live in this snapshot, not every id on file.</summary>
        internal bool CanAdd(QuestSnapshot snapshot)
        {
            Slot slot = Active();
            return slot != null && CollectLive(slot, snapshot, null) < MaxTracked;
        }

        /// <summary>
        /// Stored keys that are live in this snapshot, up to the cap. Collects into the list
        /// when one is given; passing null just counts, which keeps the cap check -- asked once
        /// per checkbox -- from allocating.
        /// </summary>
        private static int CollectLive(Slot slot, QuestSnapshot snapshot, List<QuestEntry> into)
        {
            int n = 0;
            for (int i = 0; i < slot.Keys.Count && n < MaxTracked; i++)
            {
                QuestEntry e = snapshot.Find(slot.Keys[i]);
                if (e == null)
                {
                    continue;
                }
                n++;
                if (into != null)
                {
                    into.Add(e);
                }
            }
            return n;
        }

        /// <summary>Returns true when the state changed.</summary>
        internal bool Toggle(string key, QuestSnapshot snapshot)
        {
            Slot slot = Active();
            if (slot == null)
            {
                return false;
            }
            if (slot.Keys.Contains(key))
            {
                slot.Keys.Remove(key);
            }
            else if (CollectLive(slot, snapshot, null) >= MaxTracked)
            {
                return false;
            }
            else
            {
                slot.Keys.Add(key);
            }
            Prune(slot, snapshot);
            Save();
            return true;
        }

        /// <summary>
        /// Drops ids that no longer exist in the game's data at all -- a renamed or removed
        /// mission after a patch -- and ids the snapshot just saw finished. Runs on every
        /// toggle rather than once at load because the game's config tables (and the archive
        /// handle the snapshot needs) are not populated yet while the plugin is starting up.
        ///
        /// Deliberately NOT pruning everything merely absent from the snapshot: absent also
        /// covers "not unlocked yet", which must stay watched. snapshot.Finished is the
        /// narrower, positive signal -- completed, not just currently out of view -- that
        /// Resolve's own filtering can't give us, since it only sees what's absent.
        /// </summary>
        private static void Prune(Slot slot, QuestSnapshot snapshot)
        {
            for (int i = slot.Keys.Count - 1; i >= 0; i--)
            {
                QuestKind kind;
                string id;
                if (!WatchKey.TryParse(slot.Keys[i], out kind, out id))
                {
                    slot.Keys.RemoveAt(i);
                    continue;
                }
                if (!QuestDataProvider.IdExists(kind, id) || snapshot.IsFinished(slot.Keys[i]))
                {
                    slot.Keys.RemoveAt(i);
                }
            }
        }

        internal string Raw
        {
            get { return store.Value; }
        }
    }
}
