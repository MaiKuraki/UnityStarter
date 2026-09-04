using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Build.Pipeline.Editor;
using System.Security.Cryptography;
using System.Text;
using static Build.Pipeline.Integrations.YooAsset3.Publication.PublicationConstants;

namespace Build.Pipeline.Integrations.YooAsset3.Publication
{
    /// <summary>
    /// Durable journal for Player-build artifact relocations. When a Player build hides
    /// publication artifacts (ownership markers, backups, protected metas, stage directories)
    /// by moving them under the project's Temp directory, every move is recorded here BEFORE
    /// it happens, so a crashed or killed process can be recovered by a fresh Editor session.
    ///
    /// Entry lifecycle: <c>Planned</c> (durable before the move) → <c>Moved</c> (durable after
    /// the move is verified) → <c>Restored</c> (durable after the artifact is back). <c>Conflict</c>
    /// (both paths exist, or the file/directory kind contradicts the record) and <c>MissingBoth</c>
    /// (neither path exists) are terminal fail-closed states: recovery refuses to guess and the
    /// entry stays in the journal until a human resolves it.
    ///
    /// The journal lives under <c>.buildpipeline/transactions/yooasset3-relocations/</c> so the
    /// workspace recovery service can see it. Artifacts themselves still relocate into Temp; when
    /// Temp is wiped every entry reports <c>MissingBoth</c> and recovery treats the session as
    /// ended with Temp rather than blocking the next build (see RelocationRecovery).
    /// </summary>
    internal static class RelocationJournalStore
    {
        internal const string DocumentType = "yooasset-relocation-journal";
        internal const string PlannedState = "Planned";
        internal const string MovedState = "Moved";
        internal const string RestoredState = "Restored";
        internal const string ConflictState = "Conflict";
        internal const string MissingBothState = "MissingBoth";
        internal const string KindFile = "file";
        internal const string KindDirectory = "directory";

        // The journal lives under the build transaction root, NOT beside the relocated artifacts in
        // Temp. Two reasons:
        //   1. BuildWorkspaceService only resolves recovery claims below '.buildpipeline/transactions'
        //      (see ResolveStateClaim), so a Temp-based journal is invisible to Inspect — the
        //      workspace would be reported Clean and the relocation would never be restored.
        //   2. Journal and artifacts are decoupled: entries record absolute artifact paths, so the
        //      journal surviving a Temp wipe is what lets recovery report the loss instead of
        //      silently leaving moved metas/backups stranded.
        internal const string StateRootRelativePath = ".buildpipeline/transactions/yooasset3-relocations";

        // Shared single source of truth for where relocated artifacts live in Temp. The adapter
        // builds relocation destinations with this helper and recovery validates journal entries
        // against it, so the two sides can never drift apart.
        internal const string RelocationRootRelativePath = "Temp/BuildPipeline/YooAssetPublicationMarkers";

        internal static string GetStateRoot(string projectRoot)
        {
            return Path.GetFullPath(Path.Combine(
                projectRoot,
                StateRootRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        internal static string GetRelocationRoot(string projectRoot)
        {
            return Path.GetFullPath(Path.Combine(
                projectRoot,
                RelocationRootRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        internal static string GetJournalPath(string projectRoot, string transactionId)
        {
            return Path.Combine(GetStateRoot(projectRoot), transactionId + ".json");
        }

        /// <summary>Creates an empty journal document for a relocation session.</summary>
        internal static RelocationJournalDocument Create(string transactionId)
        {
            return new RelocationJournalDocument
            {
                documentType = DocumentType,
                version = 1,
                transactionId = transactionId,
                entries = new RelocationEntry[0],
            };
        }

        /// <summary>
        /// Loads the journal for a transaction, or returns null when none exists. The document is
        /// validated (document type, entry shapes, states, checksum) before it is trusted.
        /// </summary>
        internal static RelocationJournalDocument Load(
            string projectRoot,
            string transactionId,
            IJournalSerializer serializer)
        {
            string journalPath = GetJournalPath(projectRoot, transactionId);
            if (!File.Exists(journalPath))
            {
                return null;
            }

            var info = new FileInfo(journalPath);
            if (info.Length <= 0 || info.Length > MaximumJournalBytes)
            {
                throw new InvalidOperationException(
                    $"YooAsset relocation journal size is invalid: '{journalPath}', {info.Length} bytes.");
            }

            string json = File.ReadAllText(journalPath, Encoding.UTF8);
            RelocationJournalDocument document;
            try
            {
                BuildJsonDocumentContract.Validate<RelocationJournalDocument>(
                    json,
                    DocumentType,
                    "YooAsset relocation journal");
                document = serializer.FromJson<RelocationJournalDocument>(json);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"YooAsset relocation journal is not valid JSON: '{journalPath}'.", exception);
            }

            ValidateDocument(document, journalPath);
            return document;
        }

        /// <summary>Transaction ids that have a relocation journal on disk, ordered by name.</summary>
        /// <remarks>
        /// The "*.json" pattern never matches leftover temporary candidate files
        /// ("&lt;transactionId&gt;.json.tmp-&lt;guid&gt;"): such names do not end in ".json", and the
        /// Windows FindFirstFile quirk that widens a pattern to "*.ext*" only applies to
        /// exactly-three-character extensions (like "*.xls"), never to "*.json". A unit test
        /// pins this contract.
        /// </remarks>
        internal static string[] EnumeratePendingTransactionIds(string projectRoot)
        {
            string stateRoot = GetStateRoot(projectRoot);
            if (!Directory.Exists(stateRoot))
            {
                return new string[0];
            }

            return Directory.GetFiles(stateRoot, "*.json", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileNameWithoutExtension)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// Persists the document atomically: write-then-flush a temporary file, replace the active
        /// journal, then read it back and compare the checksum so a torn write can never be
        /// mistaken for a durable state change.
        /// </summary>
        /// <remarks>
        /// The temporary name is unique per call and opened with <c>FileMode.CreateNew</c> +
        /// <c>FileShare.None</c>, so concurrent writers can never truncate each other's candidate
        /// file; a promotion race (two writers moving onto the same target) throws and fails
        /// closed instead of retrying. This mirrors PublicationJournalStore.WriteJournal without
        /// its sequence/promotion flow, which the relocation journal does not have.
        /// </remarks>
        internal static void Persist(
            RelocationJournalDocument document,
            string projectRoot,
            IJournalSerializer serializer)
        {
            // The checksum is computed first: the structural validation below must see the value
            // that will actually be persisted, not a stale one from the previous write.
            document.checksum = ComputeChecksum(document);
            ValidateDocument(document, null);
            string journalPath = GetJournalPath(projectRoot, document.transactionId);
            // ".tmp-" plus a 32-character GUID suffix must still fit the Win32 path budget after
            // promotion, so reserve that margin on the journal path itself.
            BuildPathPolicy.EnsureWin32MaxPathBudget(
                journalPath,
                "YooAsset relocation journal",
                ".tmp-".Length + 32);
            PublicationSafety.ValidateNoPathRedirection(projectRoot, journalPath);

            string json = serializer.ToJson(document);
            byte[] bytes = new UTF8Encoding(false).GetBytes(json);
            if (bytes.Length <= 0 || bytes.Length > MaximumJournalBytes)
            {
                throw new InvalidOperationException(
                    $"YooAsset relocation journal exceeds {MaximumJournalBytes} bytes: '{journalPath}'.");
            }

            string stateRoot = GetStateRoot(projectRoot);
            Directory.CreateDirectory(stateRoot);
            string temporaryPath = journalPath + ".tmp-" + Guid.NewGuid().ToString("N");
            BuildPathPolicy.EnsureWin32MaxPathBudget(
                temporaryPath,
                "YooAsset relocation temporary journal");
            PublicationSafety.ValidateNoPathRedirection(projectRoot, temporaryPath);
            bool candidateIsDurable = false;
            try
            {
                using (var stream = new FileStream(
                           temporaryPath,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None,
                           4096,
                           FileOptions.WriteThrough))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }

                candidateIsDurable = true;

                if (File.Exists(journalPath))
                {
                    File.Replace(temporaryPath, journalPath, null);
                }
                else
                {
                    // If another writer promotes first between the Exists check and the Move,
                    // the Move fails closed with an IOException. Retrying is deliberately not
                    // attempted: the caller re-runs Persist after re-reading the document.
                    File.Move(temporaryPath, journalPath);
                }
            }
            catch (Exception exception)
            {
                // Delete only OUR temporary file, and only when it was never fully written
                // durably. A cleanup failure must not be swallowed silently: the orphaned
                // candidate is kept as diagnostic evidence (later removed by
                // DeleteIfClean/Delete stale-temporary cleanup) and both failures propagate.
                if (!candidateIsDurable && File.Exists(temporaryPath))
                {
                    try
                    {
                        File.Delete(temporaryPath);
                    }
                    catch (Exception cleanupException)
                    {
                        throw new AggregateException(
                            "YooAsset relocation journal write failed and its temporary file could " +
                            $"not be removed; the file was kept for diagnosis: '{temporaryPath}'.",
                            exception,
                            cleanupException);
                    }
                }

                throw;
            }

            RelocationJournalDocument persisted = Load(projectRoot, document.transactionId, serializer);
            if (persisted == null ||
                !string.Equals(persisted.checksum, document.checksum, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"YooAsset relocation journal write could not be verified: '{journalPath}'.");
            }
        }

        /// <summary>Deletes a journal whose entries are all restored. Refuses otherwise.</summary>
        internal static void DeleteIfClean(
            RelocationJournalDocument document,
            string projectRoot)
        {
            foreach (RelocationEntry entry in document.entries)
            {
                if (!string.Equals(entry.state, RestoredState, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Refusing to delete a YooAsset relocation journal that still has unrestored entries: " +
                        $"'{entry.originalPath}'.");
                }
            }

            string journalPath = GetJournalPath(projectRoot, document.transactionId);
            PublicationSafety.ValidateNoPathRedirection(projectRoot, journalPath);
            if (File.Exists(journalPath))
            {
                File.Delete(journalPath);
            }

            CleanupStaleTemporaryFiles(projectRoot, document.transactionId);
            TryDeleteEmptyStateDirectory(GetStateRoot(projectRoot));
        }

        /// <summary>
        /// Deletes a journal outright. Only for sessions that provably ended with the Temp
        /// directory (every entry reported MissingBoth): the artifacts are gone with Temp, so the
        /// journal is a tombstone that would otherwise block every later build. Recovery logs the
        /// loss before calling this.
        /// </summary>
        internal static void Delete(string projectRoot, string transactionId)
        {
            string journalPath = GetJournalPath(projectRoot, transactionId);
            PublicationSafety.ValidateNoPathRedirection(projectRoot, journalPath);
            if (File.Exists(journalPath))
            {
                File.Delete(journalPath);
            }

            CleanupStaleTemporaryFiles(projectRoot, transactionId);
            TryDeleteEmptyStateDirectory(GetStateRoot(projectRoot));
        }

        /// <summary>
        /// Removes leftover temporary candidate files ("&lt;transactionId&gt;.json.tmp-*") that a
        /// failed Persist could not clean up itself. Only files whose name starts with this
        /// transaction's journal name are touched, so a concurrent transaction's candidates are
        /// never deleted. A file that cannot be removed is surfaced as an IOException instead of
        /// being silently kept: a journal-retirement decision must not leave undiagnosable residue
        /// behind. The empty-state-directory deletion stays best-effort (see
        /// <see cref="TryDeleteEmptyStateDirectory"/>) because a locked directory is harmless and
        /// the next pass retries it.
        /// </summary>
        private static void CleanupStaleTemporaryFiles(string projectRoot, string transactionId)
        {
            string stateRoot = GetStateRoot(projectRoot);
            if (!Directory.Exists(stateRoot))
            {
                return;
            }

            string prefix = transactionId + ".json.tmp-";
            foreach (string candidate in Directory.GetFiles(
                         stateRoot,
                         "*.json.tmp-*",
                         SearchOption.TopDirectoryOnly))
            {
                if (!Path.GetFileName(candidate).StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                try
                {
                    File.Delete(candidate);
                }
                catch (Exception exception)
                {
                    throw new IOException(
                        $"A stale YooAsset relocation journal temporary file could not be removed: '{candidate}'.",
                        exception);
                }
            }
        }

        internal static string ComputeChecksum(RelocationJournalDocument document)
        {
            var builder = new StringBuilder();
            builder.Append(DocumentType).Append('|')
                .Append(document.version).Append('|')
                .Append(document.transactionId).Append('|');
            foreach (RelocationEntry entry in document.entries ?? new RelocationEntry[0])
            {
                builder.Append(entry.order).Append('|')
                    .Append(entry.originalPath).Append('|')
                    .Append(entry.relocatedPath).Append('|')
                    .Append(entry.kind).Append('|')
                    .Append(entry.state).Append('|')
                    .Append(entry.attemptCount).Append('|')
                    .Append(entry.lastError ?? string.Empty).Append(';');
            }

            using (SHA256 sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString())))
                    .Replace("-", string.Empty);
            }
        }

        internal static void AppendEntry(
            RelocationJournalDocument document,
            string originalPath,
            string relocatedPath,
            string kind)
        {
            var existing = new List<RelocationEntry>(document.entries ?? new RelocationEntry[0]);
            existing.Add(new RelocationEntry
            {
                transactionId = document.transactionId,
                originalPath = originalPath,
                relocatedPath = relocatedPath,
                kind = kind,
                state = PlannedState,
                order = existing.Count,
                attemptCount = 0,
                lastError = string.Empty,
            });
            document.entries = existing.ToArray();
        }

        internal static RelocationEntry FindByRelocatedPath(
            RelocationJournalDocument document,
            string relocatedPath)
        {
            return document.entries
                .FirstOrDefault(entry => string.Equals(entry.relocatedPath, relocatedPath, StringComparison.Ordinal));
        }

        private static void ValidateDocument(
            RelocationJournalDocument document,
            string journalPath)
        {
            string location = journalPath ?? "<new>";
            if (document == null ||
                !string.Equals(document.documentType, DocumentType, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(document.transactionId) ||
                document.entries == null)
            {
                throw new InvalidOperationException(
                    $"YooAsset relocation journal has an unsupported or incomplete format: '{location}'.");
            }

            foreach (RelocationEntry entry in document.entries)
            {
                if (entry == null ||
                    !string.Equals(entry.transactionId, document.transactionId, StringComparison.Ordinal) ||
                    string.IsNullOrWhiteSpace(entry.originalPath) ||
                    string.IsNullOrWhiteSpace(entry.relocatedPath) ||
                    (!string.Equals(entry.kind, KindFile, StringComparison.Ordinal) &&
                     !string.Equals(entry.kind, KindDirectory, StringComparison.Ordinal)) ||
                    (!string.Equals(entry.state, PlannedState, StringComparison.Ordinal) &&
                     !string.Equals(entry.state, MovedState, StringComparison.Ordinal) &&
                     !string.Equals(entry.state, RestoredState, StringComparison.Ordinal) &&
                     !string.Equals(entry.state, ConflictState, StringComparison.Ordinal) &&
                     !string.Equals(entry.state, MissingBothState, StringComparison.Ordinal)))
                {
                    throw new InvalidOperationException(
                        $"YooAsset relocation journal contains an invalid entry: '{location}'.");
                }
            }

            string expected = ComputeChecksum(document);
            if (!string.Equals(document.checksum, expected, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"YooAsset relocation journal checksum mismatch: '{location}'.");
            }
        }

        private static void TryDeleteEmptyStateDirectory(string stateRoot)
        {
            try
            {
                if (Directory.Exists(stateRoot) && !Directory.EnumerateFileSystemEntries(stateRoot).Any())
                {
                    Directory.Delete(stateRoot);
                }
            }
            catch (IOException)
            {
                // A non-empty or locked directory is harmless: the next recovery pass retries.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    [Serializable]
    internal sealed class RelocationEntry
    {
        public string transactionId;
        public string originalPath;
        public string relocatedPath;
        public string kind;
        public string state;
        public int order;
        public int attemptCount;
        public string lastError;
    }

    [Serializable]
    internal sealed class RelocationJournalDocument
    {
        public string documentType;
        public int version;
        public string transactionId;
        public RelocationEntry[] entries;
        public string checksum;
    }
}
