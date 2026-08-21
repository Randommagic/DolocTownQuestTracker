using DolocTown;
using DolocTown.UI;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace DolocQuestTracker
{
    /// <summary>
    /// Adds a tracking toggle to the game's faction mission panel.
    ///
    /// The panel is a single-mission viewer (FactionMissionViewer : DolocSinglePageUI), not a
    /// list, so one toggle reflects whichever mission is paged into view. Selection changes
    /// ride on OnMissionSelect, which the panel already calls; currentMission is private so it
    /// is read with AccessTools.
    ///
    /// The widget is a clone of the game's own settings toggle
    /// (UI_ELEMENT_SETTING_TOGGLE / ToggleItemUI), trimmed down to just the switch -- the
    /// prefab is a full 860x72 settings row with a 723px label and a 932px divider.
    /// </summary>
    internal static class FactionCheckbox
    {
        private const string ObjectName = "DQT_TrackToggle";

        private static FactionMission current;
        private static GameObject widget;
        private static ToggleItemUI toggle;
        private static bool suppressCallback;

        internal static void OnPanelRegistered(FactionMissionUiState state)
        {
            FactionMissionViewer viewer = ViewerOf(state);
            if (viewer == null)
            {
                Diag.Error("Faction toggle: viewer not found.");
                return;
            }
            EnsureWidget(viewer);
            Refresh();
        }

        internal static void OnMissionChanged(FactionMissionUiState state)
        {
            current = AccessTools.Field(typeof(FactionMissionUiState), "currentMission")
                .GetValue(state) as FactionMission;
            Refresh();
        }

        private static FactionMissionViewer ViewerOf(FactionMissionUiState state)
        {
            FactionMissionPanel panel = UiAccess.PanelOf<FactionMissionPanel>(state);
            if (panel == null || panel.missionWidget == null)
            {
                return null;
            }
            return panel.missionWidget.viewer;
        }

        private static void EnsureWidget(FactionMissionViewer viewer)
        {
            // Unity-null covers a destroyed widget, so a rebuilt panel gets a fresh one.
            if (widget != null)
            {
                return;
            }
            RectTransform parent = viewer.transform as RectTransform;
            if (parent == null)
            {
                return;
            }

            widget = ToggleWidget.Create(parent, ObjectName,
                new Vector2(240f, 40f), new Vector2(-12f, -8f), out toggle);
            if (toggle == null)
            {
                return;
            }
            toggle.onValueChanged.AddListener(OnToggled);
        }

        private static void OnToggled(bool value)
        {
            if (suppressCallback || current == null || !current.IsValid)
            {
                return;
            }
            string key = WatchKey.Format(QuestKind.Faction, current.Proto.Id);
            if (!Core.List.Toggle(key, Core.CurrentSnapshot))
            {
                Diag.Info("Watchlist full (" + Watchlist.MaxTracked + "), ignored: " + key);
            }
            Core.InvalidateRender();
            Refresh();
        }

        private static void Refresh()
        {
            if (toggle == null || widget == null)
            {
                return;
            }

            // Nothing to track on a finished mission.
            bool applicable = current != null && current.IsValid && !current.IsComplete;
            if (widget.activeSelf != applicable)
            {
                widget.SetActive(applicable);
            }
            if (!applicable)
            {
                return;
            }

            bool tracked = Core.List.Contains(WatchKey.Format(QuestKind.Faction, current.Proto.Id));
            suppressCallback = true;
            toggle.InitValue(tracked, false);
            suppressCallback = false;

            // At the cap, an untracked mission's switch is greyed out rather than accepting a
            // click and snapping back, which read as a glitch.
            Selectable selectable = toggle.selectable;
            if (selectable != null)
            {
                selectable.interactable = tracked || Core.List.CanAdd(Core.CurrentSnapshot);
            }
        }
    }

    [HarmonyPatch(typeof(FactionMissionUiState), "Register")]
    internal static class FactionMissionUiState_Register_Hook
    {
        private static void Postfix(FactionMissionUiState __instance)
        {
            try
            {
                FactionCheckbox.OnPanelRegistered(__instance);
            }
            catch (System.Exception e)
            {
                Diag.Error("Faction toggle register failed: " + e);
            }
        }
    }

    [HarmonyPatch(typeof(FactionMissionUiState), "OnMissionSelect")]
    internal static class FactionMissionUiState_OnMissionSelect_Hook
    {
        private static void Postfix(FactionMissionUiState __instance)
        {
            try
            {
                FactionCheckbox.OnMissionChanged(__instance);
            }
            catch (System.Exception e)
            {
                Diag.Error("Faction toggle select failed: " + e);
            }
        }
    }
}
