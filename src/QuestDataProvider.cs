using System;
using System.Collections.Generic;
using System.IO;
using DolocTown;
using DolocTown.Config;
using DolocTown.Config.Mission;
using DolocTown.Config.Plant;
using DolocTown.GameData;
using UnityEngine;

namespace DolocTownQuestTracker
{
    /// <summary>
    /// Reads the two tracked systems out of the live game and flattens them into a snapshot.
    ///
    /// Faction missions pass two gates before they are actually submittable:
    ///   1. present in factionMissionManager.TotalMissions (unlocked)
    ///   2. Proto.UnlockBeforeSettled || QueryFactionJoinState(MainSeries)  -- the game's own
    ///      check, mirrored from FactionMissionData's ctor (it sets unlock=false + lockHint).
    /// Seed research needs only IsSeedNodeAvailableToUnlock(), which already excludes
    /// already-unlocked nodes and folds in the DefaultUnlock fallback.
    /// </summary>
    internal sealed class QuestDataProvider
    {
        // Save layout under Application.persistentDataPath, i.e.
        // %USERPROFILE%\AppData\LocalLow\RedSawGames\DolocTown.
        private const string SaveFolder = "SAVE";
        private const string SaveFileFormat = "doloc-save-{0}.data";

        private static readonly DateTime UnixEpoch =
            new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // DolocAPI.GetItemTitle() allocates an Item on every call (it goes through
        // GenerateItem), so titles must be cached rather than fetched per frame.
        private readonly Dictionary<string, string> itemTitles = new Dictionary<string, string>();

        // The cache has to be dropped when the player switches language, otherwise the old
        // strings stay while the font atlas swaps -- Korean names under a Latin atlas render
        // as tofu boxes. Mission titles were unaffected because Luban retranslates those and
        // we re-read them every build.
        private string cachedL10nId;

        // Resolved once per Build: GetBackpackWithInsideBoxes composes a fresh
        // LinearInventory[] on every call, and a build asks about a hundred item names.
        private LinearInventory[] inventory;

        /// <summary>
        /// Whether owned counts reach past what the player is carrying.
        ///
        /// Off (default) counts the backpack plus any box carried in it -- those items are on
        /// the player, so leaving them out under-reported anything stored in a paper box.
        /// On also counts shared chests and shelves in the current room, which matches what
        /// the game will let you submit but makes the number jump on entering a room with a
        /// stocked chest and drop again on leaving.
        /// </summary>
        internal bool CountNearbyContainers;

        private int Owned(string itemName)
        {
            // DolocAPI.CountItem(name, false) would be backpack slots alone, which misses
            // items inside a carried box.
            if (inventory == null)
            {
                return 0;
            }
            return inventory.CountItem(itemName);
        }

        private string ItemTitle(string itemName)
        {
            if (string.IsNullOrEmpty(itemName))
            {
                return "";
            }
            string title;
            if (itemTitles.TryGetValue(itemName, out title))
            {
                return title;
            }
            title = DolocAPI.GetItemTitle(itemName);
            if (string.IsNullOrEmpty(title))
            {
                title = itemName;
            }
            itemTitles[itemName] = title;
            return title;
        }

        /// <summary>
        /// Whether the id still exists in the game's static tables. Used to drop watchlist
        /// entries that a game patch renamed or removed -- unlike "absent from the snapshot",
        /// this does not depend on which save is loaded.
        /// </summary>
        internal static bool IdExists(QuestKind kind, string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return false;
            }
            try
            {
                if (kind == QuestKind.Seed)
                {
                    return DolocConfig.Tables.TbSeedUnlock.GetOrDefault(id) != null;
                }
                return DolocConfig.Tables.TbFactionMission.GetOrDefault(id) != null;
            }
            catch
            {
                return true;   // tables not ready -- never delete on a failed lookup
            }
        }

        // Cached because Contains() is asked once per checkbox per panel refresh, and the file
        // behind a loaded save does not change while it is loaded. An unknown value is not
        // cached: a new game has no save file until it is first saved, and until then the
        // slot's stored entries -- which belong to whatever save was there before -- would
        // never be recognised as stale.
        private static int fingerprintSlot = -1;
        private static long fingerprintValue;
        private static int reportedMissingSlot = -1;

        /// <summary>
        /// Which save is loaded: its slot number, and a value identifying that particular save.
        /// False before a save is open, which is most of the plugin's startup.
        ///
        /// Slot numbers are reused -- delete a save and the next new game takes its number --
        /// and nothing inside the save says which save it is: ArchiveDataHandle has no uuid,
        /// creation time or world seed. So the identity comes from the save file's creation
        /// time, which the game preserves: every rotated backup of a save still carries the
        /// timestamp of the file first written when the world was created, while their write
        /// times differ. A new game writes a new file and so gets a new value.
        ///
        /// Zero means "could not tell" -- the file was not found where expected, or the plugin
        /// could not read it. Callers must treat zero as unknown rather than as a mismatch.
        /// </summary>
        internal static bool TryCurrentSave(out int slot, out long fingerprint)
        {
            slot = -1;
            fingerprint = 0L;
            try
            {
                ArchiveDataHandle handle = DolocAPI.archiveHandle;
                if (handle == null)
                {
                    return false;
                }
                slot = handle.archiveIndex;
            }
            catch
            {
                return false;
            }

            if (slot != fingerprintSlot)
            {
                fingerprintSlot = slot;
                fingerprintValue = 0L;
            }
            if (fingerprintValue == 0L)
            {
                fingerprintValue = SaveFileFingerprint(slot);
            }
            fingerprint = fingerprintValue;
            return true;
        }

        private static long SaveFileFingerprint(int slot)
        {
            try
            {
                string path = Path.Combine(
                    Path.Combine(Application.persistentDataPath, SaveFolder),
                    string.Format(SaveFileFormat, slot));
                if (!File.Exists(path))
                {
                    // A brand new game has no file until its first save. Unknown, not a
                    // mismatch -- see the contract on TryCurrentSave. Reported once per slot,
                    // because this is re-checked until the file turns up.
                    if (reportedMissingSlot != slot)
                    {
                        reportedMissingSlot = slot;
                        Diag.Info("No save file for slot " + slot + " yet: " + path);
                    }
                    return 0L;
                }
                reportedMissingSlot = -1;
                DateTime created = File.GetCreationTimeUtc(path);
                return (long)(created - UnixEpoch).TotalSeconds;
            }
            catch (Exception e)
            {
                Diag.Info("Save fingerprint unavailable: " + e.GetType().Name);
                return 0L;
            }
        }

        /// <summary>True once the save data needed for a snapshot is reachable.</summary>
        internal static bool IsReady()
        {
            ArchiveDataHandle handle = DolocAPI.archiveHandle;
            if (handle == null)
            {
                return false;
            }
            if (handle.farmData == null || handle.cityData == null)
            {
                return false;
            }
            return handle.cityData.factionMissionManager != null;
        }

        /// <summary>
        /// True when the language changed since the last call, and drops the title cache when
        /// it did. One-shot: the caller that observes the change owns the response to it.
        /// </summary>
        internal bool ConsumeLanguageChange()
        {
            string current;
            try
            {
                current = DolocAPI.CurrentL10nId;
            }
            catch
            {
                return false;
            }
            if (current == cachedL10nId)
            {
                return false;
            }
            cachedL10nId = current;
            itemTitles.Clear();
            return true;
        }

        internal QuestSnapshot Build()
        {
            QuestSnapshot snap = new QuestSnapshot();
            if (!IsReady())
            {
                return snap;
            }

            ArchiveDataHandle handle = DolocAPI.archiveHandle;
            inventory = DolocAPI.GetBackpackWithInsideBoxes(CountNearbyContainers);
            try
            {
                BuildFaction(snap, handle);
                BuildSeed(snap, handle);
            }
            finally
            {
                inventory = null;   // nothing here outlives the build
            }
            return snap;
        }

        private void BuildFaction(QuestSnapshot snap, ArchiveDataHandle handle)
        {
            FactionMission[] all = handle.cityData.factionMissionManager.TotalMissions;
            if (all == null)
            {
                return;
            }

            for (int i = 0; i < all.Length; i++)
            {
                FactionMission m = all[i];
                if (m == null || !m.IsValid)
                {
                    continue;
                }
                if (m.IsComplete)
                {
                    continue;
                }

                FactionMissionInfo proto = m.Proto;
                // Second gate -- mirrors FactionMissionData's unlock/lockHint logic.
                if (!proto.UnlockBeforeSettled && !DolocAPI.QueryFactionJoinState(m.MainSeries))
                {
                    continue;
                }

                QuestEntry e = new QuestEntry();
                e.Kind = QuestKind.Faction;
                e.Id = proto.Id;
                e.Title = proto.Title;
                e.RequiredMoney = proto.RequiredMoney;
                e.MoneyFinished = m.MoneyFinishState;
                e.HasMoney = DolocAPI.CanAffordMoney(proto.RequiredMoney);

                FactionItem[] items = m.FactionItems;
                if (items != null)
                {
                    for (int k = 0; k < items.Length; k++)
                    {
                        FactionItem fi = items[k];
                        if (fi == null)
                        {
                            continue;
                        }
                        e.Lines.Add(MakeLine(fi.ItemName, fi.Count, fi.FinishState));
                    }
                }
                snap.Faction.Add(e);
            }
        }

        private void BuildSeed(QuestSnapshot snap, ArchiveDataHandle handle)
        {
            TbSeedUnlock table = DolocConfig.Tables.TbSeedUnlock;
            if (table == null)
            {
                return;
            }
            List<SeedUnlockInfo> list = table.DataList;
            if (list == null)
            {
                return;
            }

            for (int i = 0; i < list.Count; i++)
            {
                SeedUnlockInfo info = list[i];
                if (info == null)
                {
                    continue;
                }
                // Single predicate: false when already unlocked, true when open for research.
                if (!handle.IsSeedNodeAvailableToUnlock(info.Id))
                {
                    continue;
                }

                QuestEntry e = new QuestEntry();
                e.Kind = QuestKind.Seed;
                e.Id = info.Id;
                e.Title = ItemTitle(info.Id);   // node id == seed item id
                e.RequiredMoney = 0;
                e.MoneyFinished = true;
                e.HasMoney = true;

                CountItem[] costs = info.Costs;
                if (costs != null)
                {
                    for (int k = 0; k < costs.Length; k++)
                    {
                        // Seed research keeps no per-line submitted state, so nothing here is
                        // ever "already handed in".
                        e.Lines.Add(MakeLine(costs[k].itemName, costs[k].itemCount, false));
                    }
                }
                snap.Seed.Add(e);
            }
        }

        private ItemLine MakeLine(string itemName, int need, bool finished)
        {
            ItemLine line = new ItemLine();
            line.ItemName = itemName;
            line.Title = ItemTitle(itemName);
            line.Need = need;
            line.IsFinished = finished;
            // Already-submitted lines keep Have at Need so ordering stays sane.
            line.Have = finished ? need : Owned(itemName);
            return line;
        }

        /// <summary>
        /// "&lt;faction&gt; · &lt;category&gt;" for the diagnostics dump. Resolved on demand rather
        /// than stored on the entry: it is two table lookups per mission and nothing on screen
        /// shows it.
        /// </summary>
        internal static string FactionSubtitle(string missionId)
        {
            FactionMissionInfo proto = DolocConfig.Tables.TbFactionMission.GetOrDefault(missionId);
            if (proto == null)
            {
                return "";
            }
            string main = "";
            string sub = "";
            FactionTypeInfo mainInfo = DolocConfig.Tables.TbFactionType.GetOrDefault(proto.MainSeries);
            if (mainInfo != null)
            {
                main = mainInfo.Title;
            }
            FactionMissionTypeInfo subInfo =
                DolocConfig.Tables.TbFactionMissionType.GetOrDefault(proto.SubSeries);
            if (subInfo != null)
            {
                sub = subInfo.Title;
            }
            if (main.Length > 0 && sub.Length > 0)
            {
                return main + " · " + sub;
            }
            return main + sub;
        }

        /// <summary>
        /// Submittable first, then fewest missing items, then title. Ordering is a display
        /// concern, so the snapshot is left in table order and this is applied by whoever
        /// presents it -- today only the diagnostics dump.
        /// </summary>
        internal static int Compare(QuestEntry a, QuestEntry b)
        {
            if (a.IsSubmittable != b.IsSubmittable)
            {
                return a.IsSubmittable ? -1 : 1;
            }
            int byShortfall = a.TotalShortfall.CompareTo(b.TotalShortfall);
            if (byShortfall != 0)
            {
                return byShortfall;
            }
            int byOpen = a.OpenLineCount.CompareTo(b.OpenLineCount);
            if (byOpen != 0)
            {
                return byOpen;
            }
            return string.Compare(a.Title, b.Title, StringComparison.Ordinal);
        }
    }
}
