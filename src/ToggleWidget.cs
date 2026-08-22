using DolocTown;
using DolocTown.UI;
using UnityEngine;
using UnityEngine.UI;

namespace DolocQuestTracker
{
    /// <summary>
    /// The tracking switch: how one is built, and what a click on one means.
    ///
    /// It is a clone of the game's own settings toggle (UI_ELEMENT_SETTING_TOGGLE, carrying
    /// ToggleItemUI). The prefab is a full settings row -- 860x72 with a 723px label and a
    /// 932px divider -- so it is trimmed to just the switch.
    ///
    /// Commit/ApplyState live here rather than in the two panel adapters because the tracking
    /// rules (the cap, the greyed-out switch, the callback latch) are the same wherever a
    /// switch appears; only finding out which quest a given switch stands for differs between
    /// the single-mission viewer and the seed grid.
    /// </summary>
    internal static class ToggleWidget
    {
        private static bool suppress;

        /// <summary>
        /// True while a switch is being set from code. The panel adapters check it so pushing
        /// state onto a switch does not read back as a click.
        /// </summary>
        internal static bool Suppressed
        {
            get { return suppress; }
        }

        internal static ToggleItemUI Create(RectTransform parent, string name,
            Vector2 size, Vector2 anchoredPosition)
        {
            if (parent == null)
            {
                return null;
            }

            // CreateUiEntity instantiates and calls DolocUiObject.Init(), which is what wires
            // toggle.onValueChanged and the knob animation; without it the switch is inert.
            ToggleItemUI toggle = DolocUtils.CreateUiEntity<ToggleItemUI>(
                DolocGameAssets.UI_ELEMENT_SETTING_TOGGLE, parent);
            if (toggle == null)
            {
                Diag.Error("ToggleWidget: UI_ELEMENT_SETTING_TOGGLE unavailable or missing ToggleItemUI.");
                return null;
            }

            GameObject go = toggle.gameObject;
            go.name = name;

            RectTransform rect = go.transform as RectTransform;
            if (rect != null)
            {
                DolocUtils.SetAnchorAndPivot(rect, UIAlignmentType.RightTop);
                rect.sizeDelta = size;
                rect.anchoredPosition = anchoredPosition;
            }

            // Host panels run layout groups that would otherwise claim a row for this.
            DolocUtils.GetOrCreateComponent<LayoutElement>(go).ignoreLayout = true;

            Hide(go, "ui_element_split_line");
            Hide(go, "ui_element_setting_label");

            toggle.onValueChanged.RemoveAllListeners();
            return toggle;
        }

        /// <summary>Applies a click: flips tracking for one key, or reports the cap.</summary>
        internal static void Commit(string key)
        {
            if (!Core.List.Toggle(key, Core.CurrentSnapshot))
            {
                Diag.Info("Watchlist full (" + Watchlist.MaxTracked + "), ignored: " + key);
            }
            Core.InvalidateRender();
        }

        /// <summary>Pushes watchlist state onto a switch without the write reading back as a click.</summary>
        internal static void ApplyState(ToggleItemUI toggle, string key)
        {
            bool tracked = Core.List.Contains(key);
            suppress = true;
            toggle.InitValue(tracked, false);
            suppress = false;

            // At the cap, an untracked switch is greyed out rather than accepting a click and
            // snapping back, which read as a glitch.
            Selectable selectable = toggle.selectable;
            if (selectable != null)
            {
                selectable.interactable = tracked || Core.List.CanAdd(Core.CurrentSnapshot);
            }
        }

        private static void Hide(GameObject go, string childName)
        {
            Transform child = go.transform.Find(childName);
            if (child != null)
            {
                child.gameObject.SetActive(false);
            }
        }
    }
}
