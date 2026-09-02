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
    /// The journal lives under <c>Temp/BuildPipeline/YooAssetRelocationJournals/</c> — the same
    /// lifetime domain as the relocated artifacts themselves. If Temp is wiped, both the journal
    /// and the artifacts are gone together, and the outer publication journal still detects the
    /// missing originals through its own ownership validation.
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

        internal const string StateRootRelativePath = "Temp/BuildPipeline/YooAssetRelocationJournals";

        internal static string GetStateRoot(string projectRoot)
        {
            return Path.GetFullPath(Path.Combine(
                projectRoot,
                StateRootRelativePath.Replace('/', Path.DirectorySeparatorChar)));
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
            string temporaryPath = journalPath + ".tmp";
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }

            if (File.Exists(journalPath))
            {
                File.Replace(temporaryPath, journalPath, null);
            }
            else
            {
                File.Move(temporaryPath, journalPath);
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

            TryDeleteEmptyStateDirectory(GetStateRoot(projectRoot));
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
