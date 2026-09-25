using System.Collections.Generic;
using System.Reflection;
using DolocTown;
using DolocTown.Config.Plant;
using DolocTown.GameData;   // ArchiveOperationFarm extension methods
using DolocTown.UI;
using HarmonyLib;
using UnityEngine;

namespace DolocTownQuestTracker
{
    /// <summary>
    /// Puts a tracking toggle on each box in the seed research panel.
    ///
    /// Unlike the faction viewer this is a grid (SeedUnLockPanel :
    /// DolocPagedGridUI&lt;SeedUINode, SeedUnLockData&gt;), so every visible slot gets its own
    /// toggle at the slot's top-right. Slots are recycled across pages, so the slot -> seed
    /// mapping is rebuilt from DataGetter, which the panel calls on every page change.
    ///
    /// Already-unlocked seeds are skipped: the game marks those with its own badge and there
    /// is nothing left to track.
    ///
    /// The switch itself, and what a click on it does, belong to ToggleWidget.
    /// </summary>
    internal static class SeedCheckbox
    {
        private const string ObjectName = "DTQT_SeedTrackToggle";

        private static readonly List<ToggleItemUI> toggles = new List<ToggleItemUI>();
        private static readonly List<string> slotIds = new List<string>();
        private static FieldInfo seedListField;
        private static SeedUINode[] slots;

        internal static void OnPanelRegistered(SeedUnLockUiState state)
        {
            SeedUnLockPanel panel = UiAccess.PanelOf<SeedUnLockPanel>(state);
            if (panel == null || panel.slots == null)
            {
                Diag.Error("Seed toggle: panel or slots not found.");
                return;
            }
            slots = panel.slots;
            EnsureToggles();
        }

        /// <summary>Called after the panel asks for a page of data.</summary>
        internal static void OnPageFilled(SeedUnLockUiState state, int start, int count)
        {
            slotIds.Clear();
            if (seedListField == null)
            {
                seedListField = AccessTools.Field(typeof(SeedUnLockUiState), "seedList");
            }
            List<SeedUnlockInfo> list = seedListField != null
                ? seedListField.GetValue(state) as List<SeedUnlockInfo>
                : null;

            for (int i = 0; i < count; i++)
            {
                int index = start + i;
                bool valid = list != null && index >= 0 && index < list.Count && list[index] != null;
                slotIds.Add(valid ? list[index].Id : null);
            }
            Refresh();
        }

        private static void EnsureToggles()
        {
            if (slots == null)
            {
                return;
            }
            // Count alone is not enough: the panel is rebuilt on scene changes and on
            // returning to the title, which destroys our toggles while the list still holds
            // their now Unity-null references.
            if (toggles.Count > 0)
            {
                bool stale = false;
                for (int i = 0; i < toggles.Count; i++)
                {
                    if (toggles[i] == null)
                    {
                        stale = true;
                        break;
                    }
                }
                if (!stale)
                {
                    return;
                }
                toggles.Clear();
            }

            for (int i = 0; i < slots.Length; i++)
            {
                SeedUINode slot = slots[i];
                RectTransform parent = slot != null ? slot.transform as RectTransform : null;
                if (parent == null)
                {
                    toggles.Add(null);
                    continue;
                }

                // Smaller than the faction one: a grid slot has far less room, and the switch
                // sits in the corner the game leaves free for its unlocked badge.
                ToggleItemUI toggle = ToggleWidget.Create(parent, ObjectName,
                    new Vector2(110f, 32f), new Vector2(-8f, -8f));
                if (toggle != null)
                {
                    int captured = i;
                    toggle.onValueChanged.AddListener(delegate(bool v) { OnToggled(captured); });
                }
                toggles.Add(toggle);
            }
        }

        private static void OnToggled(int slotIndex)
        {
            if (ToggleWidget.Suppressed)
            {
                return;
            }
            string id = SeedIdAt(slotIndex);
            if (id == null)
            {
                return;
            }
            ToggleWidget.Commit(WatchKey.Format(QuestKind.Seed, id));
            Refresh();
        }

        private static string SeedIdAt(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= slotIds.Count)
            {
                return null;
            }
            return slotIds[slotIndex];
        }

        private static void Refresh()
        {
            if (slots == null)
            {
                return;
            }
            for (int i = 0; i < toggles.Count; i++)
            {
                ToggleItemUI toggle = toggles[i];
                if (toggle == null)
                {
                    continue;
                }
                string id = SeedIdAt(i);
                bool slotActive = i < slots.Length && slots[i] != null
                    && slots[i].gameObject.activeInHierarchy;
                bool applicable = slotActive && id != null && !Unlocked(id);

                toggle.SetVisible(applicable);
                if (!applicable)
                {
                    continue;
                }
                ToggleWidget.ApplyState(toggle, WatchKey.Format(QuestKind.Seed, id));
            }
        }

        private static bool Unlocked(string id)
        {
            try
            {
                return DolocAPI.archiveHandle.IsSeedNodeUnlocked(id);
            }
            catch
            {
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(SeedUnLockUiState), "Register")]
    internal static class SeedUnLockUiState_Register_Hook
    {
        private static void Postfix(SeedUnLockUiState __instance)
        {
            if (!Core.IsActive)
            {
                return;
            }
            Diag.Guard("Seed toggle register",
                delegate { SeedCheckbox.OnPanelRegistered(__instance); });
        }
    }

    [HarmonyPatch(typeof(SeedUnLockUiState), "DataGetter")]
    internal static class SeedUnLockUiState_DataGetter_Hook
    {
        private static void Postfix(SeedUnLockUiState __instance, int start, SeedUnLockData[] __result)
        {
            if (!Core.IsActive)
            {
                return;
            }
            int count = __result != null ? __result.Length : 0;
            Diag.Guard("Seed toggle page refresh",
                delegate { SeedCheckbox.OnPageFilled(__instance, start, count); });
        }
    }
}
