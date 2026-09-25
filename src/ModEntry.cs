using System;
using DTMAPI.Abstractions;
using HarmonyLib;

namespace DolocTownQuestTracker
{
    /// <summary>
    /// DTMAPI entry point, named by EntryType in Content\DTMAPI\manifest.json.
    ///
    /// Thin shell: attaches the log sink, loads the config, applies the Harmony patches and
    /// hands everything to the static Core. Holds no per-frame logic -- see Core for why a
    /// component cannot be trusted to keep receiving Unity messages in this game.
    /// </summary>
    public sealed class ModEntry : DtmMod
    {
        public override void Entry(IDtmHelper helper)
        {
            // Before anything else: the Harmony patches below log through Diag, and so does
            // Core.Init.
            Diag.Attach(helper.Monitor);
            ModEnablement.Bind(helper);

            ModConfig config = helper.ReadConfig<ModConfig>();
            Core.Init(config, delegate { Write(helper, config); });

            try
            {
                new Harmony(ModIdentity.UniqueID).PatchAll(typeof(ModEntry).Assembly);
            }
            catch (Exception e)
            {
                Diag.Error("Harmony PatchAll failed: " + e);
            }

            RegisterConfigMenu(helper, config);

            Diag.Info(string.Format("{0} {1} loaded.", ModIdentity.Name, ModIdentity.Version));
        }

        /// <summary>
        /// A failed write must not take a toggle down with it: the selection is already applied
        /// in memory and losing it on disk is the lesser fault.
        /// </summary>
        private static void Write(IDtmHelper helper, ModConfig config)
        {
            try
            {
                helper.WriteConfig<ModConfig>(config);
            }
            catch (Exception e)
            {
                Diag.Error("Writing the config failed: " + e);
            }
        }

        /// <summary>
        /// Adds the settings to DTMAPI's own config page, so nothing here needs a text editor.
        /// The menu is optional -- GetApi returns null when ModConfigMenu is not present -- and
        /// the config file stays authoritative either way.
        ///
        /// Wiring only: the descriptions belong to ModConfig, next to the values they describe,
        /// and every write goes through Core so this never has to know which settings need more
        /// than storing.
        ///
        /// Watchlist is deliberately absent: it is written by the toggles in the game's faction
        /// and seed panels and is not a hand-editable value.
        /// </summary>
        private static void RegisterConfigMenu(IDtmHelper helper, ModConfig config)
        {
            IDtmConfigMenuApi menu;
            try
            {
                menu = helper.ModRegistry.GetApi<IDtmConfigMenuApi>("DTMAPI.ModConfigMenu");
            }
            catch (Exception e)
            {
                Diag.Error("Reaching the config menu API failed: " + e);
                return;
            }
            if (menu == null)
            {
                Diag.Info("DTMAPI.ModConfigMenu is not loaded; settings are file-only.");
                return;
            }

            IManifest self = helper.ModManifest;
            ModConfig defaults = new ModConfig();

            menu.Register(self,
                delegate { Core.ResetToDefaults(defaults); },
                delegate { Write(helper, config); },
                false);

            menu.SetDisplayName(self, delegate { return ModIdentity.Name; });

            menu.AddNumberOption(self,
                delegate { return "Vertical offset"; },
                delegate { return ModConfig.OffsetYDescription; },
                delegate { return (double)Core.Config.OffsetY; },
                delegate(double value) { Core.SetOffsetY((float)value); },
                -900d, 0d, 10d);

            menu.AddBoolOption(self,
                delegate { return "Scroll long lists"; },
                delegate { return ModConfig.MarqueeDescription; },
                delegate { return Core.Config.Marquee; },
                delegate(bool value) { Core.SetMarquee(value); });

            menu.AddBoolOption(self,
                delegate { return "Count nearby chests"; },
                delegate { return ModConfig.CountNearbyContainersDescription; },
                delegate { return Core.Config.CountNearbyContainers; },
                delegate(bool value) { Core.SetCountNearbyContainers(value); });

            menu.AddTextOption(self,
                delegate { return "Diagnostics key"; },
                delegate { return ModConfig.DumpKeyDescription; },
                delegate { return Core.Config.DumpKey; },
                delegate(string value) { Core.SetDumpKey(value); });
        }
    }
}
