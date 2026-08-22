using System.Collections.Generic;
using BepInEx.Configuration;

namespace DolocTownQuestTracker
{
    /// <summary>
    /// The set of entries the overlay shows.
    ///
    /// Only ids are persisted; completion is recomputed every refresh. BepInEx config is
    /// global while completion is per-save, so deleting a finished id from the file would
    /// wipe the selection for other saves too. Filtering at display time gives the same
    /// visible behaviour ("finished, so it disappeared") and stays correct across saves.
    /// A side effect: finished entries stop counting toward the cap, so their slots free up.
    /// </summary>
    internal sealed class Watchlist
    {
        internal const int MaxTracked = 5;

        private readonly ConfigEntry<string> store;
        private readonly List<string> keys = new List<string>();

        internal Watchlist(ConfigEntry<string> store)
        {
            this.store = store;
            Load();
        }

        private void Load()
        {
            keys.Clear();
            string raw = store.Value;
            if (string.IsNullOrEmpty(raw))
            {
                return;
            }
            string[] parts = raw.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                string key = parts[i].Trim();
                QuestKind kind;
                string id;
                if (!WatchKey.TryParse(key, out kind, out id))
                {
                    continue;
                }
                string normalized = WatchKey.Format(kind, id);
                if (!keys.Contains(normalized))
                {
                    keys.Add(normalized);
                }
            }
        }

        private void Save()
        {
            store.Value = string.Join(",", keys.ToArray());
        }

        internal bool Contains(string key)
        {
            return keys.Contains(key);
        }

        /// <summary>Entries still visible in the given snapshot, in selection order, capped.</summary>
        internal List<QuestEntry> Resolve(QuestSnapshot snapshot)
        {
            List<QuestEntry> result = new List<QuestEntry>();
            for (int i = 0; i < keys.Count && result.Count < MaxTracked; i++)
            {
                QuestEntry e = snapshot.Find(keys[i]);
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
            int n = 0;
            for (int i = 0; i < keys.Count; i++)
            {
                if (snapshot.Find(keys[i]) == null)
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
            if (keys.Contains(key))
            {
                keys.Remove(key);
            }
            else if (!CanAdd(snapshot))
            {
                return false;
            }
            else
            {
                keys.Add(key);
            }
            Prune();
            Save();
            return true;
        }

        /// <summary>
        /// Drops ids that no longer exist in the game's data at all -- a renamed or removed
        /// mission after a patch. Runs on every toggle rather than once at load because the
        /// game's config tables are not populated yet while the plugin is starting up.
        ///
        /// Deliberately NOT pruning ids that are merely absent from the current snapshot:
        /// absent covers both "finished here" and "belongs to another save", and the config is
        /// global. Removing on that basis wiped another save's selection as soon as anything
        /// was toggled. Hiding finished entries is already handled by Resolve, so the file can
        /// safely keep them.
        /// </summary>
        private void Prune()
        {
            for (int i = keys.Count - 1; i >= 0; i--)
            {
                QuestKind kind;
                string id;
                if (!WatchKey.TryParse(keys[i], out kind, out id))
                {
                    keys.RemoveAt(i);
                    continue;
                }
                if (!QuestDataProvider.IdExists(kind, id))
                {
                    keys.RemoveAt(i);
                }
            }
        }

        internal string Raw
        {
            get { return store.Value; }
        }
    }
}
