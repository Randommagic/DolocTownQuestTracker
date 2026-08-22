namespace DolocTownQuestTracker
{
    /// <summary>
    /// The settings file, at &lt;game&gt;\DTMAPI\config\&lt;UniqueID&gt;.json.
    ///
    /// DTMAPI reads and writes this with DataContractJsonSerializer over a plain type, so the
    /// class and every member has to be public with a getter and a setter, and the type needs
    /// a public parameterless constructor. Defaults are set there rather than in initialisers
    /// so a key missing from the file -- an older config, or one a player trimmed -- falls
    /// back to the shipped value instead of null or zero.
    ///
    /// The Description constants are what the player actually reads, on DTMAPI's settings
    /// page. They live here, next to the value each one describes, so there is one wording to
    /// keep right; ModEntry hands them to the config menu unchanged.
    ///
    /// Only settings a player has a real reason to change live here. Everything else is a
    /// settled value and a constant next to the code that uses it -- the offset X and refresh
    /// interval in Core, the widths, scroll speed, pause and wrapped line count in TipOverlay.
    /// Nobody edits a config file to tune a scroll speed.
    /// </summary>
    public sealed class ModConfig
    {
        internal const string OffsetYDescription =
            "How far below the top-right corner the overlay sits, in pixels. " +
            "The default leaves room for the game's own mission tips; lower it if they " +
            "overlap, raise it to close the gap.";

        internal const string MarqueeDescription =
            "Scroll item lists that do not fit instead of wrapping them. " +
            "Turn this off to keep the overlay completely still.";

        internal const string CountNearbyContainersDescription =
            "Also count chests and shelves in the room you are standing in. " +
            "Your backpack, and any box carried inside it, are always counted. " +
            "On matches what the game will let you submit right now, but the numbers jump " +
            "when you enter a room with a stocked chest and drop again when you leave.";

        internal const string DumpKeyDescription =
            "Leave this as None. The mod uses no hotkeys. " +
            "Set a key only if you are reporting a problem and someone asks for a log: " +
            "pressing it then writes the mod's state to the DTMAPI log. " +
            "Any UnityEngine.InputSystem.Key name works, F10 for instance.";

        public ModConfig()
        {
            Watchlist = "";
            OffsetY = -300f;
            Marquee = true;
            CountNearbyContainers = false;
            DumpKey = "None";
        }

        /// <summary>
        /// Tracked entries, kept separately for each save -- see Watchlist for the format.
        /// Written by the toggles in the game's faction and seed panels, so it is deliberately
        /// absent from the settings page.
        /// </summary>
        public string Watchlist { get; set; }

        /// <summary>Overlay position. See OffsetYDescription.</summary>
        public float OffsetY { get; set; }

        /// <summary>Scroll or wrap overflowing item lists. See MarqueeDescription.</summary>
        public bool Marquee { get; set; }

        /// <summary>Widen the stock count past the backpack. See CountNearbyContainersDescription.</summary>
        public bool CountNearbyContainers { get; set; }

        /// <summary>Diagnostics key, None by default -- the mod has no hotkeys. See DumpKeyDescription.</summary>
        public string DumpKey { get; set; }
    }
}
