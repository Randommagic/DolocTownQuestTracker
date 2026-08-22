using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DolocQuestTracker
{
    /// <summary>
    /// Reaches the panel behind a UI state. DolocUiState&lt;TPanel&gt; declares it as
    /// `protected TPanel panel { get; private set; }`, so there is no public seam and the
    /// plugin cannot subclass into the game's state hierarchy -- reflection is the only way in.
    /// AccessTools.Property already searches base types, which is what both hooked states need
    /// (SeedUnLockUiState reaches it through PageUiStateBase).
    /// </summary>
    internal static class UiAccess
    {
        internal static T PanelOf<T>(object uiState) where T : Component
        {
            if (uiState == null)
            {
                return null;
            }
            PropertyInfo p = AccessTools.Property(uiState.GetType(), "panel");
            if (p == null)
            {
                return null;
            }
            return p.GetValue(uiState, null) as T;
        }
    }
}
