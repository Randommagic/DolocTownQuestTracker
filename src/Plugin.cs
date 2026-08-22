using System;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine.InputSystem;

namespace DolocQuestTracker
{
    /// <summary>
    /// Thin shell: binds config, applies patches, hands everything to the static Core.
    /// Holds no per-frame logic -- see Core for why the MonoBehaviour cannot be trusted to
    /// keep receiving Unity messages in this game.
    ///
    /// Only settings a player has a real reason to change are exposed. Everything else is a
    /// settled value and now a constant next to the code that uses it -- the offset X and
    /// refresh interval in Core, the widths, scroll speed, pause and wrapped line count in
    /// TipOverlay. Nobody edits a .cfg to tune a scroll speed.
    /// </summary>
    [BepInPlugin(PluginInfo.Guid, PluginInfo.Name, PluginInfo.Version)]
    [BepInProcess(PluginInfo.ProcessName)]
    public class Plugin : BaseUnityPlugin
    {
        private void Awake()
        {
            ConfigEntry<string> cfgWatchlist = Config.Bind<string>("Watchlist", "Selected", "",
                "Tracked entries, comma separated, each prefixed with faction: or seed:. " +
                "This is written by the toggles in the game's faction and seed panels -- " +
                "there is no need to edit it by hand.");

            ConfigEntry<float> cfgOffsetY = Config.Bind<float>("Overlay", "OffsetY", -300f,
                "How far below the top-right corner the overlay sits, in pixels. " +
                "The default leaves room for the game's own mission tips; lower it if they " +
                "overlap, raise it to close the gap.");

            ConfigEntry<bool> cfgMarquee = Config.Bind<bool>("Overlay", "Marquee", true,
                "Scroll item lists that do not fit instead of wrapping them. " +
                "Turn this off to keep the overlay completely still.");

            ConfigEntry<bool> cfgCountBoxes = Config.Bind<bool>("Overlay", "CountNearbyContainers", false,
                "Also count chests and shelves in the room you are standing in. " +
                "The backpack and any box carried inside it are always counted. " +
                "On matches what the game will let you submit right now, but the numbers " +
                "jump when you enter a room with a stocked chest and drop again when you leave.");

            ConfigEntry<Key> cfgDumpKey = Config.Bind<Key>("Diagnostics", "DumpKey", Key.None,
                "Key that dumps state to BepInEx\\LogOutput.log. Disabled by default; " +
                "set it only when reporting a problem. Accepts UnityEngine.InputSystem.Key " +
                "names such as F10.");

            Config.Save();

            // Marquee and CountNearbyContainers are read once; the other two are live.
            Core.Init(cfgWatchlist, cfgOffsetY, cfgMarquee.Value, cfgCountBoxes.Value, cfgDumpKey);

            try
            {
                new Harmony(PluginInfo.Guid).PatchAll(typeof(Plugin).Assembly);
            }
            catch (Exception e)
            {
                Diag.Error("Harmony PatchAll failed: " + e);
            }

            Logger.LogInfo(string.Format("{0} {1} loaded.", PluginInfo.Name, PluginInfo.Version));
        }
    }
}
