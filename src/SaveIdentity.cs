using System;
using System.IO;
using DolocTown;
using DolocTown.GameData;
using UnityEngine;

namespace DolocTownQuestTracker
{
    /// <summary>
    /// Tells one save apart from another.
    ///
    /// Slot numbers are reused -- delete a save and the next new game takes its number -- and
    /// nothing inside a save identifies it: ArchiveDataHandle and the *ArchiveData types carry
    /// no uuid, creation time or world seed, only mutable values. So the identity comes from
    /// the save file's creation time, which the game preserves: every rotated backup of a save
    /// still carries the timestamp of the file first written when the world was created, while
    /// their write times differ. Deleting a save renames it to .bak, so a new game in the same
    /// slot writes a new file and gets a new value.
    ///
    /// Zero means "could not tell" -- no file yet, or it could not be read. Never compare zero
    /// as a mismatch; use IsDifferentSave, which is where that rule lives.
    /// </summary>
    internal static class SaveIdentity
    {
        // Mirrors LocalSave.GetDataFullPath: persistentDataPath + "SAVE", then the file name
        // from the game's own format string. The directory half has to be rebuilt here because
        // LocalSave's members are not public, but the file name -- the part a patch is most
        // likely to change -- is read from the game rather than guessed.
        private const string SaveFolder = "SAVE";
        private const string FallbackFileFormat = "doloc-save-{0}.data";

        // While the identity is unknown the file is re-checked, because a new game has no save
        // file until it is first saved. Bounded so the retry rate does not follow how many
        // checkboxes a panel happens to draw: Contains and CanAdd both ask, once per slot of
        // the seed grid.
        private const double ProbeInterval = 1.0;

        private static int knownSlot = -1;
        private static long knownValue;
        private static double nextProbeAt;
        private static bool reportedUnavailable;

        private static string saveDir;
        private static int pathSlot = -1;
        private static string path;

        /// <summary>
        /// The loaded save's slot number and identity. False before a save is open, which is
        /// most of the mod's startup.
        /// </summary>
        internal static bool TryCurrent(out int slot, out long save)
        {
            slot = -1;
            save = 0L;
            try
            {
                ArchiveDataHandle handle = DolocAPI.archiveHandle;
                if (handle == null)
                {
                    return false;
                }
                slot = handle.archiveIndex;
            }
            catch
            {
                return false;
            }

            if (slot != knownSlot)
            {
                knownSlot = slot;
                knownValue = 0L;
                nextProbeAt = 0.0;
                reportedUnavailable = false;
            }
            if (knownValue == 0L)
            {
                double now = Time.realtimeSinceStartupAsDouble;
                if (now >= nextProbeAt)
                {
                    nextProbeAt = now + ProbeInterval;
                    knownValue = Read(slot);
                }
            }
            save = knownValue;
            return true;
        }

        /// <summary>
        /// True only when both sides are known and they differ. An unknown value leaves the
        /// caller's stored state alone -- the same behaviour as having no check at all, which
        /// is the right way to degrade: a guess would throw away real selections.
        /// </summary>
        internal static bool IsDifferentSave(long stored, long current)
        {
            return stored != 0L && current != 0L && stored != current;
        }

        private static long Read(int slot)
        {
            try
            {
                if (slot != pathSlot)
                {
                    if (saveDir == null)
                    {
                        saveDir = Path.Combine(Application.persistentDataPath, SaveFolder);
                    }
                    pathSlot = slot;
                    path = Path.Combine(saveDir, FileName(slot));
                }
                if (!File.Exists(path))
                {
                    Report("No save file for slot " + slot + " yet: " + path);
                    return 0L;
                }
                reportedUnavailable = false;
                return (long)(File.GetCreationTimeUtc(path) - UnixEpoch).TotalSeconds;
            }
            catch (Exception e)
            {
                Report("Save identity unavailable for slot " + slot + ": " + e.GetType().Name);
                return 0L;
            }
        }

        private static string FileName(int slot)
        {
            string format = null;
            try
            {
                GameManager manager = DolocAPI.gameManager;
                if (manager != null)
                {
                    format = manager.archiveFileNameFormat;
                }
            }
            catch
            {
                // fall through to the fallback
            }
            if (string.IsNullOrEmpty(format))
            {
                format = FallbackFileFormat;   // before the game boots, or the field moved
            }
            return DolocUtils.Format(format, slot);
        }

        /// <summary>Once per slot: the probe repeats until the file turns up.</summary>
        private static void Report(string message)
        {
            if (reportedUnavailable)
            {
                return;
            }
            reportedUnavailable = true;
            Diag.Info(message);
        }

        private static readonly DateTime UnixEpoch =
            new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    }
}
