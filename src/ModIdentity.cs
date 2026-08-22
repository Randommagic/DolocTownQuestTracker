namespace DolocTownQuestTracker
{
    /// <summary>
    /// Who this mod says it is. Not named ModInfo: the game already has
    /// DolocTown.Config.ModInfo and Core imports that namespace.
    /// </summary>
    internal static class ModIdentity
    {
        /// <summary>
        /// The DTMAPI manifest UniqueID. Also the Harmony patch owner id and, through
        /// DTMAPI's config service, the name of the settings file
        /// (&lt;game&gt;\DTMAPI\config\&lt;UniqueID&gt;.json).
        ///
        /// Must match UniqueID in workshop\Content\DTMAPI\manifest.json and uniqueId in
        /// dtmapi-package.json -- package.ps1 fails the build if they drift. Changing it
        /// orphans existing users' settings, so it is fixed from the first release onwards.
        /// </summary>
        internal const string UniqueID = "Randommagic.DolocTownQuestTracker";

        internal const string Name = "Quest Tracker";
        internal const string Version = "1.0.0";
    }
}
