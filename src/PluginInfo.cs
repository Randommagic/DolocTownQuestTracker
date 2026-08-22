namespace DolocTownQuestTracker
{
    internal static class PluginInfo
    {
        /// <summary>
        /// Also the name of the config file (BepInEx\config\&lt;Guid&gt;.cfg) and the Harmony
        /// patch owner id. Changing it orphans existing users' settings, so it is fixed from
        /// the first release onwards.
        /// </summary>
        internal const string Guid = "randommagic.doloctownquesttracker";

        internal const string Name = "Quest Tracker";
        internal const string Version = "1.0.0";

        /// <summary>
        /// Executable name without extension, compared against BepInEx's Paths.ProcessName.
        /// Keeps the plugin from trying to load -- and failing on missing game types -- if the
        /// DLL ends up in another game's BepInEx folder.
        /// </summary>
        internal const string ProcessName = "DolocTown";
    }
}
