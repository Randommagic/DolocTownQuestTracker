using BepInEx.Logging;

namespace DolocQuestTracker
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
    }
}
