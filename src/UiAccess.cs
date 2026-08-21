using System;
using System.Reflection;
using UnityEngine;

namespace DolocQuestTracker
{
    /// <summary>
    /// Reaches the panel behind a UI state. DolocUiState&lt;TPanel&gt; keeps it as
    /// `protected TPanel panel { get; private set; }`, so it is read by walking the type
    /// chain rather than named directly -- the two states we hook sit at different depths
    /// (DolocUiState vs PageUiStateBase).
    /// </summary>
    internal static class UiAccess
    {
        internal static T PanelOf<T>(object uiState) where T : Component
        {
            if (uiState == null)
            {
                return null;
            }
            Type t = uiState.GetType();
            while (t != null)
            {
                PropertyInfo p = t.GetProperty("panel",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                    | BindingFlags.DeclaredOnly);
                if (p != null)
                {
                    return p.GetValue(uiState, null) as T;
                }
                FieldInfo f = t.GetField("panel",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                    | BindingFlags.DeclaredOnly);
                if (f != null)
                {
                    return f.GetValue(uiState) as T;
                }
                t = t.BaseType;
            }
            return null;
        }
    }
}
