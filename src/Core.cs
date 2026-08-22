using System;
using System.Collections.Generic;
using System.Text;
using BepInEx.Configuration;
using DolocTown;
using DolocTown.Config;
using DolocTown.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace DolocQuestTracker
{
    /// <summary>
    /// All plugin state and per-frame work, in a plain static class.
    ///
    /// Not on the BaseUnityPlugin MonoBehaviour because in this game that component stops
    /// receiving Unity messages right after OnEnable -- Start/Update/LateUpdate never fire and
    /// the instance reads back as Unity-null, i.e. it is destroyed. The Harmony patches keep
    /// firing, so the pump comes from BodyController.Update and nothing here depends on a live
    /// component.
    /// </summary>
    internal static class Core
    {
        // Game palette, matching how MissionRequire_Simple.Handle.ToString() colours progress.
        private const string ColorSatisfied = "#78A655";   // SLIENTCOLOR_GREEN
        private const string ColorShort = "#00FFA5";       // EYECATCHCOLOR_CYAN

        // Settled values, not worth a config entry each.
        private const float OffsetX = -12f;          // matches the game tips' right margin
        private const float RefreshInterval = 3f;    // s between inventory recounts

        private static ConfigEntry<float> cfgOffsetY;
        private static ConfigEntry<Key> cfgDumpKey;

        private static readonly QuestDataProvider provider = new QuestDataProvider();
        private static readonly TipOverlay overlay = new TipOverlay();

        private static QuestSnapshot snapshot = new QuestSnapshot();
        private static Watchlist watchlist;

        private static bool initialized;
        private static bool prevDumpKey;
        private static double nextRefreshDueAt;
        private static bool snapshotDirty = true;
        private static bool renderDirty = true;
        private static string lastRenderSignature = "";
        private static string lastBuildBlocker = "";

        // The tip manager's CanvasGroup never changes for a given manager instance, and
        // GameTipsVisible runs every frame.
        private static MissionTipManager cachedTips;
        private static CanvasGroup cachedTipsGroup;

        internal static long PumpCount;

        internal static void Init(
            ConfigEntry<string> watchlistStore,
            ConfigEntry<float> offsetY,
            bool useMarquee,
            bool countNearbyContainers,
            ConfigEntry<Key> dumpKey)
        {
            cfgOffsetY = offsetY;
            cfgDumpKey = dumpKey;

            provider.CountNearbyContainers = countNearbyContainers;
            watchlist = new Watchlist(watchlistStore);
            overlay.Configure(useMarquee);

            initialized = true;
            Diag.Info("Core initialized. watchlist=\"" + watchlist.Raw + "\"");
        }

        internal static Watchlist List
        {
            get { return watchlist; }
        }

        internal static QuestSnapshot CurrentSnapshot
        {
            get { return snapshot; }
        }

        /// <summary>Forces the next tick to redraw -- used after the watchlist changes.</summary>
        internal static void InvalidateRender()
        {
            lastRenderSignature = "";
            renderDirty = true;
        }

        /// <summary>Pumped from the BodyController.Update postfix.</summary>
        internal static void Tick()
        {
            if (!initialized)
            {
                return;
            }
            PumpCount++;
            try
            {
                TickInner();
            }
            catch (Exception e)
            {
                // One bad frame must not take the game down with it.
                Diag.Error("Tick threw: " + e);
            }
        }

        private static void TickInner()
        {
            // Only a diagnostics key, defaulting to None. No gameplay hotkeys: this ships to
            // the workshop and global keys just risk colliding with other mods.
            if (cfgDumpKey.Value != Key.None)
            {
                bool down = IsPressed(cfgDumpKey.Value);
                if (down && !prevDumpKey)
                {
                    DumpDiagnostics();
                }
                prevDumpKey = down;
            }

            if (!SafeIsNormalState())
            {
                overlay.SetVisible(false);
                return;
            }

            // Follow the game's own mission-tip stack so we appear and disappear with it:
            // dialogue, menus and cutscenes hide it, and no single state flag covers all of
            // those (IsNormalState stays true during dialogue).
            bool tipsVisible = GameTipsVisible();
            overlay.SetVisible(tipsVisible);
            if (tipsVisible)
            {
                UpdateOverlay();
                overlay.Tick(Time.deltaTime);
            }

            // Outside the guard: the panel checkboxes read CurrentSnapshot for the cap, and
            // they are used while the overlay is hidden behind the panel.
            RefreshSnapshot(false);
        }

        internal static void OnGameEvent(GameEventType type)
        {
            switch (type)
            {
                case GameEventType.SUBMIT_ITEM:
                case GameEventType.UNLOCK_SEED:
                case GameEventType.OBTAIN_ITEM:
                case GameEventType.OBTAIN_FUNCTION_ITEM:
                case GameEventType.MAKE_ITEM:
                case GameEventType.USE_ITEM:
                case GameEventType.DROP_ITEM:
                case GameEventType.SELL_ITEM_IN_STORE:
                case GameEventType.HARVEST_CROP:
                case GameEventType.FELL_DUNGEON_RESOURCE:
                case GameEventType.SETTLED_TREATYPORT:
                case GameEventType.FACTION_MISSION_FINISH:
                    snapshotDirty = true;
                    break;
            }
        }

        private static void RefreshSnapshot(bool force)
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (!force && !snapshotDirty && now < nextRefreshDueAt)
            {
                return;
            }
            nextRefreshDueAt = now + RefreshInterval;
            snapshotDirty = false;

            // A language switch changes every visible string, so the cached render signature
            // would otherwise match and the overlay would keep the old text. This is the only
            // place the latch is consumed.
            bool languageChanged = provider.ConsumeLanguageChange();
            snapshot = provider.Build();
            renderDirty = true;
            if (languageChanged)
            {
                InvalidateRender();
            }
        }

        private static void UpdateOverlay()
        {
            string blocker;
            if (!overlay.TryBuild(out blocker))
            {
                if (blocker != lastBuildBlocker)
                {
                    lastBuildBlocker = blocker;
                    Diag.Info("Overlay not built yet: " + blocker);
                }
                return;
            }
            overlay.SetOffset(new Vector2(OffsetX, cfgOffsetY.Value));

            // The tip text can only change when the snapshot is rebuilt or the watchlist is
            // edited, both of which set renderDirty. Reset it after TryBuild so nothing is
            // lost while the overlay is still waiting on the game.
            if (!renderDirty)
            {
                return;
            }
            renderDirty = false;

            List<QuestEntry> tracked = watchlist.Resolve(snapshot);
            List<TipLine> lines = new List<TipLine>();
            for (int i = 0; i < tracked.Count; i++)
            {
                lines.Add(BuildTipLine(tracked[i]));
            }

            // Render recycles every tip, so only redraw when something actually changed --
            // otherwise a refresh that found nothing new would restart the scroll.
            string signature = Signature(lines);
            if (signature != lastRenderSignature)
            {
                lastRenderSignature = signature;
                overlay.Render(lines);
            }
        }

        /// <summary>
        /// Maps one entry onto the tip. Submitted lines are dropped entirely -- there is
        /// nothing left to do about them.
        /// </summary>
        private static TipLine BuildTipLine(QuestEntry e)
        {
            List<string> items = new List<string>();

            for (int i = 0; i < e.Lines.Count; i++)
            {
                ItemLine l = e.Lines[i];
                if (l.IsFinished)
                {
                    continue;
                }
                items.Add(Chunk(l.IsSatisfied ? ColorSatisfied : ColorShort,
                    l.Title + " " + l.Have + "/" + l.Need));
            }

            if (e.RequiredMoney > 0 && !e.MoneyFinished)
            {
                items.Add(Chunk(e.HasMoney ? ColorSatisfied : ColorShort, MoneyLabel(e.RequiredMoney)));
            }

            string accent = (items.Count > 0 && e.IsSubmittable) ? ColorSatisfied : ColorShort;
            return new TipLine(e.Title, items, accent);
        }

        /// <summary>
        /// The game's own money format ("{0} G"), so the label follows the player's language
        /// instead of being hardcoded here.
        /// </summary>
        private static string MoneyLabel(int amount)
        {
            try
            {
                string format = DolocConfig.StaticTexts.UiMoneyTip;
                if (!string.IsNullOrEmpty(format))
                {
                    return DolocUtils.Format(format, amount);
                }
            }
            catch
            {
                // fall through
            }
            return amount.ToString();
        }

        /// <summary>
        /// One coloured, unbreakable item chunk. nobr keeps a line break from landing inside
        /// an item: a fixed items-per-line count cannot work because names differ enough in
        /// width that any count either overflows or wastes space.
        /// </summary>
        private static string Chunk(string colorHex, string body)
        {
            return "<nobr>" + DolocUtils.Colored(body, colorHex) + "</nobr>";
        }

        private static string Signature(List<TipLine> lines)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < lines.Count; i++)
            {
                sb.Append(lines[i].Title).Append('|')
                  .Append(string.Join("|", lines[i].Items.ToArray())).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>
        /// True while the game is showing its own mission tips. DolocAPI.uiSystem.MissionTips
        /// is public, so we follow it instead of guessing at every UI state.
        /// </summary>
        private static bool GameTipsVisible()
        {
            try
            {
                DolocUiSystem ui = DolocAPI.uiSystem;
                if (ui == null)
                {
                    return false;
                }
                MissionTipManager tips = ui.MissionTips;
                if (tips == null || !tips.gameObject.activeInHierarchy)
                {
                    return false;
                }
                // Unity-null on a destroyed manager forces a re-resolve rather than leaving a
                // stale CanvasGroup behind.
                if (cachedTips == null || !ReferenceEquals(tips, cachedTips))
                {
                    cachedTips = tips;
                    cachedTipsGroup = tips.GetComponentInParent<CanvasGroup>();
                }
                return cachedTipsGroup == null || cachedTipsGroup.alpha >= 0.01f;
            }
            catch
            {
                return true;   // never hide because of a probe failure
            }
        }

        private static bool SafeIsNormalState()
        {
            // DolocAPI.IsNormalState dereferences userInput, which is null before the game
            // state machine exists.
            try
            {
                return DolocAPI.IsNormalState;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsPressed(Key key)
        {
            Keyboard kb = Keyboard.current;
            if (kb == null)
            {
                return false;
            }
            ButtonControl ctrl = kb[key];
            return ctrl != null && ctrl.isPressed;
        }

        /// <summary>Support tool behind [Diagnostics] DumpKey. Off unless a key is assigned.</summary>
        internal static void DumpDiagnostics()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine();
                sb.AppendLine("===== Quest Tracker diagnostics =====");
                sb.AppendLine("pumpCount     = " + PumpCount);
                sb.AppendLine("isNormalState = " + SafeIsNormalState());
                sb.AppendLine("gameTips      = " + GameTipsVisible());
                sb.AppendLine("dataReady     = " + QuestDataProvider.IsReady());
                sb.AppendLine("overlayBuilt  = " + overlay.IsBuilt + " liveTips=" + overlay.LiveCount);
                sb.AppendLine("blocker       = " + lastBuildBlocker);
                sb.AppendLine("watchlist     = \"" + watchlist.Raw + "\"");

                if (QuestDataProvider.IsReady())
                {
                    RefreshSnapshot(true);
                    sb.AppendLine(string.Format("money={0} faction={1} seed={2}",
                        DolocAPI.archiveHandle.CurrentMoney,
                        snapshot.Faction.Count, snapshot.Seed.Count));
                    AppendSection(sb, "FACTION", snapshot.Faction);
                    AppendSection(sb, "SEED", snapshot.Seed);
                }

                sb.AppendLine("===== end diagnostics =====");
                Diag.Info(sb.ToString());
            }
            catch (Exception e)
            {
                Diag.Error("DumpDiagnostics threw: " + e);
            }
        }

        private static void AppendSection(StringBuilder sb, string label, List<QuestEntry> list)
        {
            sb.AppendLine(string.Format("--- {0} ({1}) ---", label, list.Count));

            // Most urgent first, on a copy: the snapshot itself has no meaningful order and
            // nothing else depends on one.
            List<QuestEntry> ordered = new List<QuestEntry>(list);
            ordered.Sort(QuestDataProvider.Compare);

            for (int i = 0; i < ordered.Count; i++)
            {
                QuestEntry e = ordered[i];
                string subtitle = e.Kind == QuestKind.Faction
                    ? QuestDataProvider.FactionSubtitle(e.Id)
                    : "";
                sb.AppendLine(string.Format("[{0}] {1}{2}{3}", e.Key, e.Title,
                    subtitle.Length > 0 ? "  (" + subtitle + ")" : "",
                    e.IsSubmittable ? "  << SUBMITTABLE" : ""));
                for (int k = 0; k < e.Lines.Count; k++)
                {
                    ItemLine l = e.Lines[k];
                    string mark = l.IsFinished ? "done" : (l.IsSatisfied ? "ok" : "-" + l.Shortfall);
                    sb.AppendLine(string.Format("      {0,-24} {1,4}/{2,-4} {3}  ({4})",
                        l.Title, l.Have, l.Need, mark, l.ItemName));
                }
                if (e.RequiredMoney > 0)
                {
                    sb.AppendLine(string.Format("      {0,-24} {1}", "[money]",
                        e.MoneyFinished ? "done" : e.RequiredMoney.ToString()));
                }
            }
        }
    }
}
