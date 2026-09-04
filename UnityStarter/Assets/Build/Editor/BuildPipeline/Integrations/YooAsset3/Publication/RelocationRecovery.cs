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
        private const int MaximumJournalCount = 32;

        /// <summary>
        /// Restores every pending relocation journal under the project. Returns the number of
        /// artifacts restored. Journals whose entries are all restored are deleted; a journal
        /// whose entries ALL report MissingBoth is retired (the artifacts were lost together with
        /// Temp); a journal with a Conflict entry or a partial loss is retained and surfaced as an
        /// exception.
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

            if (transactionIds.Length > MaximumJournalCount)
            {
                throw new InvalidOperationException(
                    "YooAsset relocation recovery exceeds the 32-journal safety budget; " +
                    "inspect .buildpipeline/transactions/yooasset3-relocations manually.");
            }

            int restored = 0;
            var blocked = new List<string>();
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

                restored += RestoreDocument(normalizedProjectRoot, document, serializer, log);

                if (AllEntriesInState(document, RelocationJournalStore.RestoredState))
                {
                    RelocationJournalStore.DeleteIfClean(document, normalizedProjectRoot);
                    log?.Invoke($"YooAsset relocation journal for '{transactionId}' fully restored and removed.");
                    continue;
                }

                // Per-journal Temp-wipe retirement: the relocated artifacts live under Temp, so
                // when EVERY entry of THIS transaction reports MissingBoth the artifacts
                // disappeared together with the Temp directory. Nothing is left to restore, and
                // keeping this journal would block every later build on a loss that no longer
                // exists - report it and retire the session. A mixed journal (some entries
                // restored, some missing) or any Conflict entry is a real inconsistency and stays
                // fail-closed below. Retirement is decided per journal so one journal's I/O
                // failure can no longer block another journal's retirement.
                if (document.entries.Length > 0 &&
                    AllEntriesInState(document, RelocationJournalStore.MissingBothState))
                {
                    log?.Invoke(
                        $"YooAsset relocation journal for '{transactionId}': the relocated "
                        + "Player-build artifacts are gone together with the Temp directory, so "
                        + "there is nothing left to restore. The relocation session has been "
                        + "retired. If a .meta or backup file is missing, restore it from version "
                        + "control before building again.");
                    RelocationJournalStore.Delete(normalizedProjectRoot, transactionId);
                    continue;
                }

                CollectBlockedEntries(document, blocked);
            }

            if (blocked.Count > 0)
            {
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

                string failure = TryRestoreEntry(projectRoot, entry);
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
                }

                // Persist after every entry: a crash mid-recovery must leave an accurate journal.
                RelocationJournalStore.Persist(document, projectRoot, serializer);
            }

            return restored;
        }

        private static void CollectBlockedEntries(
            RelocationJournalDocument document,
            List<string> blocked)
        {
            foreach (RelocationEntry entry in document.entries)
            {
                if (string.Equals(entry.state, RelocationJournalStore.RestoredState, StringComparison.Ordinal))
                {
                    continue;
                }

                blocked.Add($"[{entry.state}] original='{entry.originalPath}' relocated='{entry.relocatedPath}': {entry.lastError}");
            }
        }

        private static bool AllEntriesInState(RelocationJournalDocument document, string state)
        {
            foreach (RelocationEntry entry in document.entries)
            {
                if (!string.Equals(entry.state, state, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// One restore attempt with the same fail-closed rules as the build session: never
        /// overwrite a recreated original, never move an entry whose kind contradicts the record,
        /// never treat a vanished artifact as a success, and never move a path that fails the
        /// trust-boundary validation.
        /// </summary>
        private static string TryRestoreEntry(string projectRoot, RelocationEntry entry)
        {
            // Trust boundary: journal paths are untrusted input. A path outside its approved root
            // (Temp relocation root / StreamingAssets) or reached through a reparse point must
            // never be moved - fail closed as a Conflict instead of guessing.
            try
            {
                RelocationPathPolicy.ValidateEntryRoots(projectRoot, entry);
            }
            catch (Exception validationException)
            {
                entry.state = RelocationJournalStore.ConflictState;
                return "relocation path failed the trust-boundary validation: "
                       + validationException.Message;
            }

            bool isDirectory = string.Equals(entry.kind, RelocationJournalStore.KindDirectory, StringComparison.Ordinal);
            bool relocatedIsDirectory = Directory.Exists(entry.relocatedPath);
            bool relocatedIsFile = File.Exists(entry.relocatedPath);
            // Any entry at the original path counts: a kind-mismatched leftover is a conflict, not
            // a free path to move onto.
            bool originalAnyExists = Directory.Exists(entry.originalPath) || File.Exists(entry.originalPath);
            bool originalKindMatches = isDirectory
                ? Directory.Exists(entry.originalPath)
                : File.Exists(entry.originalPath);

            if (isDirectory ? relocatedIsFile : relocatedIsDirectory)
            {
                entry.state = RelocationJournalStore.ConflictState;
                return "relocation type mismatch: expected a " + (isDirectory ? "directory" : "file") +
                       " at the relocated path but found a " + (isDirectory ? "file" : "directory") + ".";
            }

            bool relocatedExists = isDirectory ? relocatedIsDirectory : relocatedIsFile;
            if (!relocatedExists)
            {
                if (originalAnyExists)
                {
                    if (!originalKindMatches)
                    {
                        entry.state = RelocationJournalStore.ConflictState;
                        return "the original path exists but its file-system kind contradicts the journal " +
                               "record; refusing to treat it as the restored artifact.";
                    }

                    // The artifact is already back at its original path: a previous restore
                    // succeeded but the journal update was lost. Treat the entry as restored.
                    return null;
                }

                entry.state = RelocationJournalStore.MissingBothState;
                return "neither the relocated artifact nor the original path exists; the artifact is lost and requires manual restoration from the publication backup.";
            }

            if (originalAnyExists)
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
