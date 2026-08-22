using System;
using BepInEx.Logging;

namespace DolocTownQuestTracker
{
    /// <summary>
    /// Log source usable from static code and Harmony patches, which have no plugin instance
    /// to reach a Logger through.
    /// </summary>
    internal static class Diag
    {
        private static readonly ManualLogSource log =
            Logger.CreateLogSource(PluginInfo.Name);

        internal static void Info(string message)
        {
            log.LogInfo(message);
        }

        internal static void Error(string message)
        {
            log.LogError(message);
        }

        /// <summary>
        /// Runs a patch body so a throw is logged with its origin instead of escaping into the
        /// game's call stack. Not DolocAPI.TryExecute: that logs through UnityEngine.Debug with
        /// no label, so a failure here would land without the plugin's log source or any hint
        /// of which hook produced it.
        /// </summary>
        internal static void Guard(string label, Action body)
        {
            try
            {
                body();
            }
            catch (Exception e)
            {
                Error(label + " failed: " + e);
            }
        }
    }
}
