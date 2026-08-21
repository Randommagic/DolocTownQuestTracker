using DolocTown;
using DolocTown.UI;
using UnityEngine;
using UnityEngine.UI;

namespace DolocQuestTracker
{
    /// <summary>
    /// Builds a tracking switch from the game's own settings toggle prefab
    /// (UI_ELEMENT_SETTING_TOGGLE, carrying ToggleItemUI).
    ///
    /// The prefab is a full settings row -- 860x72 with a 723px label and a 932px divider --
    /// so it is trimmed to just the switch. ToggleItemUI.Init() is what wires
    /// toggle.onValueChanged and the knob animation helper; without it the switch is inert.
    /// </summary>
    internal static class ToggleWidget
    {
        internal static GameObject Create(RectTransform parent, string name,
            Vector2 size, Vector2 anchoredPosition, out ToggleItemUI toggle)
        {
            toggle = null;
            if (parent == null)
            {
                return null;
            }

            GameObject prefab = DolocAPI.GetAsset<GameObject>(DolocGameAssets.UI_ELEMENT_SETTING_TOGGLE);
            if (prefab == null)
            {
                Diag.Error("ToggleWidget: UI_ELEMENT_SETTING_TOGGLE not available.");
                return null;
            }

            GameObject go = Object.Instantiate(prefab, parent);
            go.name = name;

            RectTransform rect = go.transform as RectTransform;
            if (rect != null)
            {
                rect.anchorMin = new Vector2(1f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(1f, 1f);
                rect.sizeDelta = size;
                rect.anchoredPosition = anchoredPosition;
            }

            // Host panels run layout groups that would otherwise claim a row for this.
            LayoutElement ignore = go.GetComponent<LayoutElement>();
            if (ignore == null)
            {
                ignore = go.AddComponent<LayoutElement>();
            }
            ignore.ignoreLayout = true;

            Hide(go, "ui_element_split_line");
            Hide(go, "ui_element_setting_label");

            toggle = go.GetComponent<ToggleItemUI>();
            if (toggle == null)
            {
                Diag.Error("ToggleWidget: ToggleItemUI missing on the prefab.");
                return go;
            }
            toggle.Init();
            toggle.onValueChanged.RemoveAllListeners();
            return go;
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
