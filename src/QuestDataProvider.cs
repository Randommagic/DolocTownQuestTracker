using System.Collections.Generic;
using DolocTown;
using DolocTown.Config;
using DolocTown.Config.Mission;
using DolocTown.Config.Plant;
using DolocTown.GameData;

namespace DolocQuestTracker
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
        // DolocAPI.GetItemTitle() allocates an Item on every call (it goes through
        // GenerateItem), so titles must be cached rather than fetched per frame.
        private readonly Dictionary<string, string> itemTitles = new Dictionary<string, string>();

        // The cache has to be dropped when the player switches language, otherwise the old
        // strings stay while the font atlas swaps -- Korean names under a Latin atlas render
        // as tofu boxes. Mission titles were unaffected because Luban retranslates those and
        // we re-read them every build.
        private string cachedL10nId;

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
            // Both branches include carried boxes; only the room containers differ.
            // DolocAPI.CountItem(name, false) would be backpack slots alone, which misses them.
            return DolocAPI.GetBackpackWithInsideBoxes(CountNearbyContainers).CountItem(itemName);
        }

        internal void ClearCaches()
        {
            itemTitles.Clear();
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

        /// <summary>True when the language changed since the last build.</summary>
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
            ConsumeLanguageChange();

            ArchiveDataHandle handle = DolocAPI.archiveHandle;
            int money = handle.CurrentMoney;

            BuildFaction(snap, handle, money);
            BuildSeed(snap, handle);

            snap.Faction.Sort(Compare);
            snap.Seed.Sort(Compare);
            return snap;
        }

        private void BuildFaction(QuestSnapshot snap, ArchiveDataHandle handle, int money)
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
                e.Subtitle = FactionSubtitle(proto);
                e.RequiredMoney = proto.RequiredMoney;
                e.MoneyFinished = m.MoneyFinishState;
                e.HasMoney = money >= proto.RequiredMoney;

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
                        ItemLine line = new ItemLine();
                        line.ItemName = fi.ItemName;
                        line.Title = ItemTitle(fi.ItemName);
                        line.Need = fi.Count;
                        line.IsFinished = fi.FinishState;
                        // Already-submitted lines keep Have at Need so ordering stays sane.
                        line.Have = fi.FinishState ? fi.Count : Owned(fi.ItemName);
                        e.Lines.Add(line);
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
                e.Subtitle = "";
                e.RequiredMoney = 0;
                e.MoneyFinished = true;
                e.HasMoney = true;

                CountItem[] costs = info.Costs;
                if (costs != null)
                {
                    for (int k = 0; k < costs.Length; k++)
                    {
                        ItemLine line = new ItemLine();
                        line.ItemName = costs[k].itemName;
                        line.Title = ItemTitle(costs[k].itemName);
                        line.Need = costs[k].itemCount;
                        line.IsFinished = false;   // seed research has no per-line state
                        line.Have = Owned(costs[k].itemName);
                        e.Lines.Add(line);
                    }
                }
                snap.Seed.Add(e);
            }
        }

        private static string FactionSubtitle(FactionMissionInfo proto)
        {
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

        /// <summary>Submittable first, then fewest missing items, then title.</summary>
        private static int Compare(QuestEntry a, QuestEntry b)
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
            return string.Compare(a.Title, b.Title, System.StringComparison.Ordinal);
        }
    }
}
