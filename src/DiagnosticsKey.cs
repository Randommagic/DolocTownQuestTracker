using System;
using UnityEngine.InputSystem;

namespace DolocTownQuestTracker
{
    /// <summary>
    /// The diagnostics key, resolved from the name stored in the config.
    ///
    /// DTMAPI stores settings as JSON, so the key arrives as text and has to be turned into a
    /// Key. That is a config concern rather than a per-frame one, so it lives here instead of
    /// in Core: the tick just asks for the current key and gets an answer.
    ///
    /// The last answer is cached against the text it came from, so re-pointing the key from
    /// the settings page takes effect immediately while the common case -- unchanged text,
    /// once per frame -- costs a string comparison.
    /// </summary>
    internal sealed class DiagnosticsKey
    {
        private string resolvedFrom;
        private Key resolved;

        /// <summary>
        /// Key.None for empty text, and for text that names no key -- reported once rather
        /// than re-failing every frame, since the tick asks on every pass.
        /// </summary>
        internal Key Resolve(string name)
        {
            if (name == resolvedFrom)
            {
                return resolved;
            }
            resolvedFrom = name;

            Key parsed;
            if (string.IsNullOrEmpty(name))
            {
                resolved = Key.None;
            }
            else if (Enum.TryParse<Key>(name, true, out parsed))
            {
                resolved = parsed;
            }
            else
            {
                resolved = Key.None;
                Diag.Error("DumpKey \"" + name +
                    "\" is not a UnityEngine.InputSystem.Key name; treating it as None.");
            }
            return resolved;
        }
    }
}
