using System.Reflection;
using DolocTown;
using DolocTown.UI;
using HarmonyLib;
using UnityEngine;

namespace DolocQuestTracker
{
    /// <summary>
    /// Puts a tracking toggle on the game's faction mission panel.
    ///
    /// The panel is a single-mission viewer (FactionMissionViewer : DolocSinglePageUI), not a
    /// list, so one toggle reflects whichever mission is paged into view. Selection changes
    /// ride on OnMissionSelect, which the panel already calls; currentMission is private so it
    /// is read with AccessTools.
    ///
    /// The switch itself, and what a click on it does, belong to ToggleWidget.
    /// </summary>
    internal static class FactionCheckbox
    {
        private const string ObjectName = "DQT_TrackToggle";

        private static FieldInfo currentMissionField;
        private static FactionMission current;
        private static ToggleItemUI toggle;

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
            if (currentMissionField == null)
            {
                currentMissionField = AccessTools.Field(typeof(FactionMissionUiState), "currentMission");
            }
            current = currentMissionField != null
                ? currentMissionField.GetValue(state) as FactionMission
                : null;
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
            // Unity-null covers a destroyed toggle, so a rebuilt panel gets a fresh one.
            if (toggle != null)
            {
                return;
            }
            RectTransform parent = viewer.transform as RectTransform;
            if (parent == null)
            {
                return;
            }

            toggle = ToggleWidget.Create(parent, ObjectName,
                new Vector2(240f, 40f), new Vector2(-12f, -8f));
            if (toggle != null)
            {
                toggle.onValueChanged.AddListener(OnToggled);
            }
        }

        private static void OnToggled(bool value)
        {
            if (ToggleWidget.Suppressed || current == null || !current.IsValid)
            {
                return;
            }
            ToggleWidget.Commit(WatchKey.Format(QuestKind.Faction, current.Proto.Id));
            Refresh();
        }

        private static void Refresh()
        {
            if (toggle == null)
            {
                return;
            }

            // Nothing to track on a finished mission.
            bool applicable = current != null && current.IsValid && !current.IsComplete;
            toggle.SetVisible(applicable);
            if (!applicable)
            {
                return;
            }
            ToggleWidget.ApplyState(toggle, WatchKey.Format(QuestKind.Faction, current.Proto.Id));
        }
    }

    [HarmonyPatch(typeof(FactionMissionUiState), "Register")]
    internal static class FactionMissionUiState_Register_Hook
    {
        private static void Postfix(FactionMissionUiState __instance)
        {
            Diag.Guard("Faction toggle register",
                delegate { FactionCheckbox.OnPanelRegistered(__instance); });
        }
    }

    [HarmonyPatch(typeof(FactionMissionUiState), "OnMissionSelect")]
    internal static class FactionMissionUiState_OnMissionSelect_Hook
    {
        private static void Postfix(FactionMissionUiState __instance)
        {
            Diag.Guard("Faction toggle select",
                delegate { FactionCheckbox.OnMissionChanged(__instance); });
        }
    }
}
