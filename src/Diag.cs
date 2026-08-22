using System;
using DTMAPI.Abstractions;

namespace DolocTownQuestTracker
{
    /// <summary>
    /// Log sink usable from static code and Harmony patches, which have no mod instance to
    /// reach a Monitor through.
    ///
    /// Falls back to a no-op until Entry attaches DTMAPI's monitor. Nothing here runs before
    /// then in practice, but a Harmony patch firing during PatchAll must not throw on a null
    /// sink.
    /// </summary>
    internal static class Diag
    {
        private static IMonitor monitor;

        /// <summary>Called once from ModEntry.Entry, before anything is patched.</summary>
        internal static void Attach(IMonitor value)
        {
            monitor = value;
        }

        internal static void Info(string message)
        {
            IMonitor m = monitor;
            if (m != null)
            {
                m.Log(message, LogLevel.Info);
            }
        }

        internal static void Error(string message)
        {
            IMonitor m = monitor;
            if (m != null)
            {
                m.Log(message, LogLevel.Error);
            }
        }

        /// <summary>
        /// Runs a patch body so a throw is logged with its origin instead of escaping into the
        /// game's call stack. Not DolocAPI.TryExecute: that logs through UnityEngine.Debug with
        /// no label, so a failure here would land without the mod's log source or any hint of
        /// which hook produced it.
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
