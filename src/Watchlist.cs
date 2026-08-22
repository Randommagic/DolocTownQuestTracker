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
    ///     0/1755367040=faction:vulture_fossil,seed:seed_tomato;2/1755999001=faction:x
    ///     ^ ^          ^                                      ^
    ///     | |          that slot's selections                 next slot
    ///     | which save was in the slot (QuestDataProvider.TryCurrentSave)
    ///     slot number
    ///
    /// The slot number alone is not enough to name a save, because deleting one frees its
    /// number for the next new game. The second field settles that -- see Active().
    ///
    /// Only ids are stored; completion is recomputed every refresh. Finished entries stay in
    /// the file and are filtered out by Resolve, so completing a mission never rewrites the
    /// list. A side effect: finished entries stop counting toward the cap, freeing a slot.
    /// </summary>
    internal sealed class Watchlist
    {
        internal const int MaxTracked = 5;

        private const char SlotSeparator = ';';
        private const char HeaderSeparator = '=';
        private const char SaveSeparator = '/';
        private const char KeySeparator = ',';

        private sealed class Slot
        {
            internal long Save;   // 0 = not known, see QuestDataProvider.TryCurrentSave
            internal readonly List<string> Keys = new List<string>();
        }

        private readonly ConfigEntry<string> store;
        private readonly Dictionary<int, Slot> slots = new Dictionary<int, Slot>();

        /// <summary>
        /// Selections written before the format grew slot numbers. Handed to the first save
        /// opened afterwards: which save they belonged to is recorded nowhere, and the one
        /// being opened is the only sensible guess.
        /// </summary>
        private List<string> legacy;

        internal Watchlist(ConfigEntry<string> store)
        {
            this.store = store;
            Load();
        }

        /// <summary>
        /// The loaded save's list, or null when no save is open.
        ///
        /// Resolved on every call rather than cached: the plugin starts long before a save
        /// exists, and the player can switch saves without anything telling us.
        /// </summary>
        private Slot Active()
        {
            int index;
            long save;
            if (!QuestDataProvider.TryCurrentSave(out index, out save))
            {
                return null;
            }

            Slot slot;
            if (!slots.TryGetValue(index, out slot))
            {
                slot = new Slot();
                slot.Save = save;
                slots[index] = slot;
                if (legacy != null)
                {
                    slot.Keys.AddRange(legacy);
                    legacy = null;
                    Save();
                }
                return slot;
            }

            // Different save, same slot number: the one these entries belong to was deleted
            // and a new game took its place. Drop them rather than show quests nobody picked.
            //
            // Either side being zero means the save could not be identified, and a guess would
            // throw away real selections, so the entries are left alone. That is the same
            // behaviour as having no check at all, which is the right way to degrade.
            if (slot.Save != 0L && save != 0L && slot.Save != save)
            {
                slot.Keys.Clear();
                slot.Save = save;
                Save();
                return slot;
            }

            if (slot.Save == 0L)
            {
                slot.Save = save;   // persisted by the next toggle; not worth a write of its own
            }
            return slot;
        }

        private void Load()
        {
            slots.Clear();
            legacy = null;

            string raw = store.Value;
            if (string.IsNullOrEmpty(raw))
            {
                return;
            }

            // No header separator means the flat, pre-slot format.
            if (raw.IndexOf(HeaderSeparator) < 0)
            {
                List<string> keys = ParseKeys(raw);
                if (keys.Count > 0)
                {
                    legacy = keys;
                }
                return;
            }

            string[] parts = raw.Split(SlotSeparator);
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].Trim();
                int split = part.IndexOf(HeaderSeparator);
                if (split <= 0)
                {
                    continue;
                }

                int index;
                long save;
                if (!ParseHeader(part.Substring(0, split), out index, out save))
                {
                    continue;
                }

                Slot slot = new Slot();
                slot.Save = save;
                slot.Keys.AddRange(ParseKeys(part.Substring(split + 1)));
                slots[index] = slot;
            }
        }

        private static bool ParseHeader(string header, out int index, out long save)
        {
            index = -1;
            save = 0L;
            int slash = header.IndexOf(SaveSeparator);
            if (slash < 0)
            {
                return int.TryParse(header.Trim(), out index);
            }
            if (!int.TryParse(header.Substring(0, slash).Trim(), out index))
            {
                return false;
            }
            long.TryParse(header.Substring(slash + 1).Trim(), out save);
            return true;
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
                  .Append(HeaderSeparator)
                  .Append(string.Join(KeySeparator.ToString(), slot.Keys.ToArray()));
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
            if (slot == null)
            {
                return result;
            }
            for (int i = 0; i < slot.Keys.Count && result.Count < MaxTracked; i++)
            {
                QuestEntry e = snapshot.Find(slot.Keys[i]);
                if (e != null)
                {
                    result.Add(e);
                }
            }
            return result;
        }

        /// <summary>The cap counts only entries live in this snapshot, not every id on file.</summary>
        internal bool CanAdd(QuestSnapshot snapshot)
        {
            Slot slot = Active();
            return slot != null && CanAddTo(slot, snapshot);
        }

        private static bool CanAddTo(Slot slot, QuestSnapshot snapshot)
        {
            int n = 0;
            for (int i = 0; i < slot.Keys.Count; i++)
            {
                if (snapshot.Find(slot.Keys[i]) == null)
                {
                    continue;
                }
                n++;
                if (n >= MaxTracked)
                {
                    return false;
                }
            }
            return true;
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
            else if (!CanAddTo(slot, snapshot))
            {
                return false;
            }
            else
            {
                slot.Keys.Add(key);
            }
            Prune(slot);
            Save();
            return true;
        }

        /// <summary>
        /// Drops ids that no longer exist in the game's data at all -- a renamed or removed
        /// mission after a patch. Runs on every toggle rather than once at load because the
        /// game's config tables are not populated yet while the plugin is starting up.
        ///
        /// Deliberately NOT pruning ids that are merely absent from the current snapshot:
        /// absent covers both "finished" and "not unlocked yet", and hiding finished entries
        /// is already Resolve's job.
        /// </summary>
        private static void Prune(Slot slot)
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
                if (!QuestDataProvider.IdExists(kind, id))
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
