using System;
using System.Collections.Generic;
using System.IO;

namespace Build.Pipeline.Integrations.YooAsset3.Publication
{
    /// <summary>
    /// Restores Player-build publication artifacts that were relocated into the project Temp
    /// directory by a Player build session. Runs on the owner thread from the recovery
    /// coordinator (Editor startup and -pipelineRecoverOnly) and fails closed: any entry whose
    /// filesystem state contradicts the journal (Conflict, MissingBoth) aborts recovery with an
    /// exception instead of guessing, so a normal build cannot start on top of stranded
    /// publication evidence.
    /// </summary>
    internal static class RelocationRecovery
    {
        /// <summary>
        /// Restores every pending relocation journal under the project. Returns the number of
        /// artifacts restored. Journals whose entries are all restored are deleted; a journal
        /// with a Conflict or MissingBoth entry is retained and surfaced as an exception.
        /// </summary>
        internal static int RestorePending(
            string projectRoot,
            IJournalSerializer serializer,
            Action<string> log)
        {
            string normalizedProjectRoot = Path.GetFullPath(projectRoot);
            string[] transactionIds = RelocationJournalStore.EnumeratePendingTransactionIds(
                normalizedProjectRoot);
            if (transactionIds.Length == 0)
            {
                return 0;
            }

            if (transactionIds.Length > 32)
            {
                throw new InvalidOperationException(
                    "YooAsset relocation recovery exceeds the 32-journal safety budget; " +
                    "inspect Temp/BuildPipeline/YooAssetRelocationJournals manually.");
            }

            int restored = 0;
            var blocked = new List<string>();
            var blockedStates = new List<string>();
            foreach (string transactionId in transactionIds)
            {
                RelocationJournalDocument document = RelocationJournalStore.Load(
                    normalizedProjectRoot,
                    transactionId,
                    serializer);
                if (document == null)
                {
                    continue;
                }

                restored += RestoreDocument(
                    normalizedProjectRoot, document, serializer, blocked, blockedStates, log);

                bool clean = true;
                foreach (RelocationEntry entry in document.entries)
                {
                    if (!string.Equals(entry.state, RelocationJournalStore.RestoredState, StringComparison.Ordinal))
                    {
                        clean = false;
                        break;
                    }
                }

                if (clean)
                {
                    RelocationJournalStore.DeleteIfClean(document, normalizedProjectRoot);
                    log?.Invoke($"YooAsset relocation journal for '{transactionId}' fully restored and removed.");
                }
            }

            if (blocked.Count > 0)
            {
                // Temp-wipe discriminator: the artifacts live under Temp, so if EVERY blocked
                // entry reports MissingBoth then the relocated artifacts disappeared together with
                // Temp. Nothing is left to restore, and keeping the journal would block every later
                // build on a loss that no longer exists - report it and retire the session instead.
                // A partial loss (some entries found, some not) is a real inconsistency and stays
                // fail-closed below.
                bool lostWithTemp = true;
                foreach (string state in blockedStates)
                {
                    if (!string.Equals(state, RelocationJournalStore.MissingBothState, StringComparison.Ordinal))
                    {
                        lostWithTemp = false;
                        break;
                    }
                }

                if (lostWithTemp)
                {
                    foreach (string transactionId in transactionIds)
                    {
                        RelocationJournalStore.Delete(normalizedProjectRoot, transactionId);
                    }

                    log?.Invoke(
                        "YooAsset relocation recovery: the relocated Player-build artifacts are gone "
                        + "together with the Temp directory, so there is nothing left to restore. The "
                        + "relocation session has been retired. If a .meta or backup file is missing, "
                        + "restore it from version control before building again.");
                    return restored;
                }

                throw new InvalidOperationException(
                    "YooAsset publication artifact relocation could not be restored automatically and requires " +
                    "manual resolution. Blocked entries:\n" + string.Join("\n", blocked.ToArray()));
            }

            return restored;
        }

        private static int RestoreDocument(
            string projectRoot,
            RelocationJournalDocument document,
            IJournalSerializer serializer,
            List<string> blocked,
            List<string> blockedStates,
            Action<string> log)
        {
            int restored = 0;
            // Reverse order: artifacts were relocated in forward order, so the last move is the
            // first one undone (the marker may sit beside a backup directory that must move first).
            for (int index = document.entries.Length - 1; index >= 0; index--)
            {
                RelocationEntry entry = document.entries[index];
                if (string.Equals(entry.state, RelocationJournalStore.RestoredState, StringComparison.Ordinal))
                {
                    continue;
                }

                string failure = TryRestoreEntry(entry);
                entry.attemptCount++;
                if (failure == null)
                {
                    entry.state = RelocationJournalStore.RestoredState;
                    entry.lastError = string.Empty;
                    restored++;
                    log?.Invoke($"YooAsset relocation restored: '{entry.originalPath}'.");
                }
                else
                {
                    entry.lastError = failure;
                    blocked.Add($"[{entry.state}] original='{entry.originalPath}' relocated='{entry.relocatedPath}': {failure}");
                    blockedStates.Add(entry.state);
                }

                // Persist after every entry: a crash mid-recovery must leave an accurate journal.
                RelocationJournalStore.Persist(document, projectRoot, serializer);
            }

            return restored;
        }

        /// <summary>
        /// One restore attempt with the same fail-closed rules as the build session: never
        /// overwrite a recreated original, never move an entry whose kind contradicts the record,
        /// never treat a vanished artifact as a success.
        /// </summary>
        private static string TryRestoreEntry(RelocationEntry entry)
        {
            bool isDirectory = string.Equals(entry.kind, RelocationJournalStore.KindDirectory, StringComparison.Ordinal);
            bool relocatedIsDirectory = Directory.Exists(entry.relocatedPath);
            bool relocatedIsFile = File.Exists(entry.relocatedPath);
            bool originalExists = isDirectory ? Directory.Exists(entry.originalPath) : File.Exists(entry.originalPath);

            if (isDirectory ? relocatedIsFile : relocatedIsDirectory)
            {
                entry.state = RelocationJournalStore.ConflictState;
                return "relocation type mismatch: expected a " + (isDirectory ? "directory" : "file") +
                       " at the relocated path but found a " + (isDirectory ? "file" : "directory") + ".";
            }

            bool relocatedExists = isDirectory ? relocatedIsDirectory : relocatedIsFile;
            if (!relocatedExists)
            {
                if (originalExists)
                {
                    // The artifact is already back at its original path: a previous restore
                    // succeeded but the journal update was lost. Treat the entry as restored.
                    return null;
                }

                entry.state = RelocationJournalStore.MissingBothState;
                return "neither the relocated artifact nor the original path exists; the artifact is lost and requires manual restoration from the publication backup.";
            }

            if (originalExists)
            {
                entry.state = RelocationJournalStore.ConflictState;
                return "both the original and the relocated paths exist; refusing to overwrite either one.";
            }

            try
            {
                if (isDirectory)
                {
                    Directory.Move(entry.relocatedPath, entry.originalPath);
                }
                else
                {
                    File.Move(entry.relocatedPath, entry.originalPath);
                }

                return null;
            }
            catch (Exception exception)
            {
                return exception.Message;
            }
        }
    }
}
