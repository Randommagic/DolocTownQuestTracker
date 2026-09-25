using System;
using System.Collections.Generic;
using System.IO;
using DolocTown;
using DolocTown.Config;
using DTMAPI.Abstractions;
using UnityEngine;

namespace DolocTownQuestTracker
{
    /// <summary>
    /// Whether the player still has this mod switched on in the game's own Mods page.
    ///
    /// DTMAPI does re-scan when the official Mod UI is opened, but it cannot unload a CodeMod
    /// that is already in the process -- it says so itself: "disable, unsubscribe and update
    /// take effect after restarting Doloc Town". So a mod turned off mid-session keeps running.
    /// For a mod that draws on screen that reads as a bug, so we watch the game's live state
    /// ourselves and go quiet without waiting for a restart.
    ///
    /// This is a soft disable, not an unload: the Harmony patches stay applied and keep
    /// calling in, they just do nothing. Only a restart truly removes us, which is DTMAPI's
    /// contract and not something a mod can talk its way out of.
    ///
    /// Everything here fails towards "enabled". A mod that hides itself because it could not
    /// read a manager is worse than one that lingers a session too long.
    /// </summary>
    internal static class ModEnablement
    {
        /// <summary>
        /// The state is only ever changed by a player clicking a menu, so once a second is
        /// plenty. GetAllEnabledModInfos allocates an array per call, which is reason enough
        /// not to ask every frame.
        /// </summary>
        private const double PollInterval = 1.0;

        private static string officialId;
        private static bool enabled = true;
        private static double nextPollAt;
        private static bool loggedFailure;

        /// <summary>
        /// Works out the id the game files us under. Called from Entry: DTMAPI publishes the
        /// discovery snapshot before it loads any mod, so our own row is already there.
        /// </summary>
        internal static void Bind(IDtmHelper helper)
        {
            try
            {
                IReadOnlyList<IWorkshopModInfo> mods = helper.Workshop.GetDtmApiMods();
                for (int i = 0; i < mods.Count; i++)
                {
                    IWorkshopModInfo mod = mods[i];
                    if (mod == null ||
                        !string.Equals(mod.UniqueID, ModIdentity.UniqueID, StringComparison.Ordinal))
                    {
                        continue;
                    }
                    officialId = OfficialIdOf(mod);
                    break;
                }
            }
            catch (Exception e)
            {
                Diag.Error("Could not read the DTMAPI mod list: " + e);
            }

            Diag.Info("Official mod id = " +
                (officialId != null ? officialId : "<unknown, staying always-on>"));
        }

        /// <summary>
        /// Mirrors how DTMAPI's own scanner keys the enablement file: a workshop item by its
        /// id, anything else by its folder name.
        /// </summary>
        private static string OfficialIdOf(IWorkshopModInfo mod)
        {
            if (string.Equals(mod.Source, "Workshop", StringComparison.OrdinalIgnoreCase) &&
                mod.WorkshopId.HasValue)
            {
                return "Workshop." + mod.WorkshopId.Value.ToString();
            }

            string root = mod.RootPath;
            if (string.IsNullOrEmpty(root))
            {
                return null;
            }
            return "Local." + Path.GetFileName(root.TrimEnd('\\', '/'));
        }

        internal static bool IsEnabled()
        {
            // No id means no way to ask. Stay on rather than vanish for a reason the player
            // cannot see.
            if (officialId == null)
            {
                return true;
            }

            double now = Time.realtimeSinceStartupAsDouble;
            if (now < nextPollAt)
            {
                return enabled;
            }
            nextPollAt = now + PollInterval;

            try
            {
                ModManager manager = DolocAPI.modManager;
                if (manager == null)
                {
                    return enabled;   // too early; keep the last answer
                }

                // GetAllEnabledModInfos filters on CheckModEnabled, which reads the live
                // ModInfo.enabled flag the Mods page writes. Public, unlike CheckModEnabled
                // itself, so no reflection is needed.
                ModInfo[] active = manager.GetAllEnabledModInfos();
                if (active == null)
                {
                    return enabled;
                }

                bool found = false;
                for (int i = 0; i < active.Length; i++)
                {
                    ModInfo info = active[i];
                    if (info != null && string.Equals(info.id, officialId, StringComparison.Ordinal))
                    {
                        found = true;
                        break;
                    }
                }

                if (found != enabled)
                {
                    enabled = found;
                    Diag.Info(enabled
                        ? "Switched on in the Mods page; resuming."
                        : "Switched off in the Mods page; going quiet. The overlay and the " +
                          "panel toggles stop, but the patches stay applied until you restart " +
                          "the game.");
                }
            }
            catch (Exception e)
            {
                // Once only: this runs on a timer and a broken manager would otherwise fill
                // the log.
                if (!loggedFailure)
                {
                    loggedFailure = true;
                    Diag.Error("Reading the game's mod enablement failed, staying on: " + e);
                }
            }
            return enabled;
        }
    }
}
