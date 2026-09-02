using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    internal enum BuildPublicationDecision
    {
        None = 0,
        Rollback = 1,
        Commit = 2
    }

    /// <summary>
    /// Stores the single durable decision shared by every staged output in one
    /// run. Child transactions retain their own snapshots and consult this
    /// decision during explicit recovery.
    /// </summary>
    internal sealed class BuildPublicationBarrier
    {
        internal const string StateRelativePath = ".buildpipeline/transactions/publication-barrier";
        internal const string ParticipantId = "PublicationBarrier";

        private const string DocumentType = "build-publication-decision";
        private const string PreparedPhase = "Prepared";
        private const string CommittedPhase = "Committed";
        private const string JournalFileName = "active.json";
        private const int MaximumJournalBytes = 2 * 1024 * 1024;
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        private readonly string stateRoot;
        private readonly string journalPath;
        private Journal journal;
        private bool finalized;

        private BuildPublicationBarrier(string stateRoot, Journal journal)
        {
            this.stateRoot = stateRoot;
            journalPath = Path.Combine(stateRoot, JournalFileName);
            this.journal = journal;
        }

        public static BuildPublicationBarrier Begin(
            string projectRoot,
            string runId,
            IReadOnlyList<IBuildDeferredPublication> publications)
        {
            string project = NormalizeProjectRoot(projectRoot);
            if (string.IsNullOrWhiteSpace(runId))
            {
                throw new ArgumentException("Publication barrier run id is required.", nameof(runId));
            }

            if (publications == null
                || publications.Count == 0
                || publications.Count > BuildPipelineBudgets.MaximumDeferredPublicationCount)
            {
                throw new ArgumentException(
                    $"Publication barrier requires between 1 and {BuildPipelineBudgets.MaximumDeferredPublicationCount} publications.",
                    nameof(publications));
            }

            string stateRoot = GetStateRoot(project);
            EnsureSafeStatePath(project, stateRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(stateRoot));
            Directory.CreateDirectory(stateRoot);
            EnsureSafeStatePath(project, stateRoot);

            string journalPath = Path.Combine(stateRoot, JournalFileName);
            if (HasAnyJournalCandidate(journalPath))
            {
                throw new InvalidOperationException(
                    "A pending terminal publication barrier requires explicit workspace recovery.");
            }

            var entries = new PublicationEntry[publications.Count];
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var paths = new List<string>(publications.Count);
            for (int index = 0; index < publications.Count; index++)
            {
                IBuildDeferredPublication publication = publications[index]
                    ?? throw new InvalidOperationException(
                        $"Deferred publication at index {index} is null.");
                ValidatePublicationId(publication.Id);
                string relativeStatePath = ValidatePublicationStatePath(
                    publication.RecoveryStateRelativePath);
                if (!ids.Add(publication.Id))
                {
                    throw new InvalidOperationException(
                        $"Deferred publication id '{publication.Id}' is duplicated.");
                }

                if (paths.Any(existing => StatePathsOverlap(existing, relativeStatePath)))
                {
                    throw new InvalidOperationException(
                        $"Deferred publication state path '{relativeStatePath}' overlaps another publication claim.");
                }

                paths.Add(relativeStatePath);

                entries[index] = new PublicationEntry
                {
                    id = publication.Id,
                    stateRelativePath = relativeStatePath
                };
            }

            var journal = new Journal
            {
                documentType = DocumentType,
                runId = runId,
                phase = PreparedPhase,
                sequence = 1,
                projectRoot = project,
                publications = entries,
                checksum = string.Empty
            };
            WriteJournal(journalPath, journal, createNew: true);
            return new BuildPublicationBarrier(stateRoot, journal);
        }

        public void CommitDecision()
        {
            ThrowIfFinalized();
            if (!string.Equals(journal.phase, PreparedPhase, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Terminal publication barrier is not awaiting its commit decision.");
            }

            Journal next = CloneJournal(journal);
            next.phase = CommittedPhase;
            next.sequence++;
            WriteJournal(journalPath, next, createNew: false);

            Journal durable = ReadBestJournal(journalPath, journal.projectRoot);
            journal = durable;
            if (!string.Equals(durable.phase, CommittedPhase, StringComparison.Ordinal)
                || durable.sequence != next.sequence)
            {
                throw new InvalidOperationException(
                    "Terminal publication commit did not become the authoritative durable decision.");
            }
        }

        /// <summary>
        /// Reads the durable journal candidates and synchronizes the in-memory
        /// state. Callers must use this result after every commit attempt,
        /// including attempts that threw during an atomic replacement window.
        /// </summary>
        internal BuildPublicationDecision ReadDurableDecision()
        {
            ThrowIfFinalized();
            if (!HasAnyJournalCandidate(journalPath))
            {
                return BuildPublicationDecision.None;
            }

            journal = ReadBestJournal(journalPath, journal.projectRoot);
            return string.Equals(journal.phase, CommittedPhase, StringComparison.Ordinal)
                ? BuildPublicationDecision.Commit
                : BuildPublicationDecision.Rollback;
        }

        public void Complete()
        {
            ThrowIfFinalized();
            if (!string.Equals(journal.phase, CommittedPhase, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Only a committed terminal publication barrier can be completed.");
            }

            EnsureChildrenFinalized(journal);
            DeleteJournalCandidates(journalPath);
            TryDeleteEmptyStateRoot(stateRoot);
            finalized = true;
        }

        public void AbortAfterRollback()
        {
            ThrowIfFinalized();
            if (!string.Equals(journal.phase, PreparedPhase, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "A committed terminal publication decision cannot be aborted.");
            }

            EnsureChildrenFinalized(journal);
            DeleteJournalCandidates(journalPath);
            TryDeleteEmptyStateRoot(stateRoot);
            finalized = true;
        }

        public static BuildPublicationDecision GetDecision(
            string projectRoot,
            string publicationId,
            string expectedStateRelativePath)
        {
            ValidatePublicationId(publicationId);
            string expectedPath = ValidatePublicationStatePath(expectedStateRelativePath);
            string project = NormalizeProjectRoot(projectRoot);
            string journalPath = Path.Combine(GetStateRoot(project), JournalFileName);
            if (!HasAnyJournalCandidate(journalPath))
            {
                return BuildPublicationDecision.None;
            }

            Journal value = ReadBestJournal(journalPath, project);
            PublicationEntry match = null;
            foreach (PublicationEntry entry in value.publications)
            {
                if (string.Equals(entry.id, publicationId, StringComparison.Ordinal))
                {
                    if (match != null)
                    {
                        throw new InvalidOperationException(
                            $"Publication barrier contains duplicate id '{publicationId}'.");
                    }

                    match = entry;
                }
            }

            if (match == null)
            {
                return BuildPublicationDecision.None;
            }

            if (!string.Equals(
                    match.stateRelativePath,
                    expectedPath,
                    PathComparison))
            {
                throw new InvalidOperationException(
                    $"Publication barrier state claim for '{publicationId}' does not match its recovery participant.");
            }

            return string.Equals(value.phase, CommittedPhase, StringComparison.Ordinal)
                ? BuildPublicationDecision.Commit
                : BuildPublicationDecision.Rollback;
        }

        internal static void Recover(string projectRoot)
        {
            string project = NormalizeProjectRoot(projectRoot);
            string stateRoot = GetStateRoot(project);
            string journalPath = Path.Combine(stateRoot, JournalFileName);
            if (!HasAnyJournalCandidate(journalPath))
            {
                TryDeleteEmptyStateRoot(stateRoot);
                return;
            }

            Journal value = ReadBestJournal(journalPath, project);
            EnsureChildrenFinalized(value);

            DeleteJournalCandidates(journalPath);
            TryDeleteEmptyStateRoot(stateRoot);
        }

        internal static string GetStateRoot(string projectRoot)
        {
            return Path.Combine(
                NormalizeProjectRoot(projectRoot),
                StateRelativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static void EnsureOnlyInertLocksRemain(string stateRoot, string publicationId)
        {
            if (!Directory.Exists(stateRoot))
            {
                return;
            }

            RejectReparsePoint(stateRoot, "publication recovery state");
            foreach (string directory in Directory.EnumerateDirectories(
                         stateRoot,
                         "*",
                         SearchOption.TopDirectoryOnly))
            {
                throw new InvalidOperationException(
                    $"Publication '{publicationId}' still has recovery directory '{directory}'.");
            }

            foreach (string file in Directory.EnumerateFiles(
                         stateRoot,
                         "*",
                         SearchOption.TopDirectoryOnly))
            {
                RejectReparsePoint(file, "publication recovery file");
                if (!BuildRecoveryEvidencePolicy.IsInertLockFile(Path.GetFileName(file)))
                {
                    throw new InvalidOperationException(
                        $"Publication '{publicationId}' still has recovery evidence '{file}'.");
                }
            }
        }

        private static void EnsureChildrenFinalized(Journal value)
        {
            foreach (PublicationEntry publication in value.publications)
            {
                string childStateRoot = Path.GetFullPath(Path.Combine(
                    value.projectRoot,
                    publication.stateRelativePath.Replace('/', Path.DirectorySeparatorChar)));
                EnsureSafeStatePath(value.projectRoot, childStateRoot);
                EnsureOnlyInertLocksRemain(childStateRoot, publication.id);
            }
        }

        private static Journal ReadBestJournal(string journalPath, string expectedProjectRoot)
        {
            var candidates = new List<JournalCandidate>(3);
            AddCandidate(candidates, journalPath, expectedProjectRoot);
            AddCandidate(candidates, journalPath + ".tmp", expectedProjectRoot);
            AddCandidate(candidates, journalPath + ".bak", expectedProjectRoot);
            if (candidates.Count == 0)
            {
                throw new InvalidOperationException(
                    "Publication barrier has evidence, but no valid journal candidate.");
            }

            long maximumSequence = candidates.Max(candidate => candidate.Value.sequence);
            JournalCandidate[] latest = candidates
                .Where(candidate => candidate.Value.sequence == maximumSequence)
                .ToArray();
            string canonical = latest[0].CanonicalJson;
            for (int index = 1; index < latest.Length; index++)
            {
                if (!string.Equals(canonical, latest[index].CanonicalJson, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Publication barrier has conflicting journal candidates at the latest sequence.");
                }
            }

            return latest[0].Value;
        }

        private static void AddCandidate(
            ICollection<JournalCandidate> candidates,
            string path,
            string expectedProjectRoot)
        {
            if (!File.Exists(path))
            {
                return;
            }

            RejectReparsePoint(path, "publication barrier journal");
            var info = new FileInfo(path);
            if (info.Length <= 0 || info.Length > MaximumJournalBytes)
            {
                throw new InvalidOperationException(
                    $"Publication barrier journal candidate has an invalid size: '{path}'.");
            }

            string json = StrictUtf8.GetString(File.ReadAllBytes(path));
            BuildJsonDocumentContract.Validate<Journal>(
                json,
                DocumentType,
                "Publication barrier journal");
            Journal value = JsonUtility.FromJson<Journal>(json);
            ValidateJournal(value, expectedProjectRoot);
            candidates.Add(new JournalCandidate(value, JsonUtility.ToJson(value, false)));
        }

        private static void ValidateJournal(Journal value, string expectedProjectRoot)
        {
            if (value == null
                || !string.Equals(value.documentType, DocumentType, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(value.runId)
                || value.sequence <= 0
                || (value.phase != PreparedPhase && value.phase != CommittedPhase)
                || value.publications == null
                || value.publications.Length == 0
                || value.publications.Length > BuildPipelineBudgets.MaximumDeferredPublicationCount
                || !PathsEqual(value.projectRoot, expectedProjectRoot))
            {
                throw new InvalidOperationException(
                    "Publication barrier journal has an unsupported or incomplete format.");
            }

            string checksum = value.checksum;
            value.checksum = string.Empty;
            string expectedChecksum = ComputeHash(JsonUtility.ToJson(value, false));
            value.checksum = checksum;
            if (string.IsNullOrWhiteSpace(checksum)
                || !string.Equals(checksum, expectedChecksum, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Publication barrier journal checksum is invalid.");
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var paths = new List<string>(value.publications.Length);
            foreach (PublicationEntry publication in value.publications)
            {
                if (publication == null)
                {
                    throw new InvalidOperationException(
                        "Publication barrier journal contains a null publication entry.");
                }

                ValidatePublicationId(publication.id);
                string path = ValidatePublicationStatePath(publication.stateRelativePath);
                if (!ids.Add(publication.id)
                    || paths.Any(existing => StatePathsOverlap(existing, path)))
                {
                    throw new InvalidOperationException(
                        "Publication barrier journal contains duplicate publication claims.");
                }

                paths.Add(path);
            }
        }

        private static bool StatePathsOverlap(string first, string second)
        {
            return string.Equals(first, second, PathComparison)
                || first.StartsWith(second + "/", PathComparison)
                || second.StartsWith(first + "/", PathComparison);
        }

        private static void WriteJournal(string path, Journal value, bool createNew)
        {
            value.checksum = string.Empty;
            value.checksum = ComputeHash(JsonUtility.ToJson(value, false));
            byte[] bytes = StrictUtf8.GetBytes(JsonUtility.ToJson(value, true));
            if (bytes.Length <= 0 || bytes.Length > MaximumJournalBytes)
            {
                throw new InvalidOperationException(
                    "Publication barrier journal exceeds its size budget.");
            }

            string temporary = path + ".tmp";
            string backup = path + ".bak";
            if (File.Exists(temporary) || File.Exists(backup))
            {
                throw new InvalidOperationException(
                    "Publication barrier journal has unresolved atomic-write evidence.");
            }

            using (var stream = new FileStream(
                       temporary,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }

            if (createNew)
            {
                if (File.Exists(path))
                {
                    throw new IOException(
                        "Publication barrier journal appeared while it was being created.");
                }

                File.Move(temporary, path);
                return;
            }

            if (!File.Exists(path))
            {
                throw new IOException(
                    "Publication barrier active journal disappeared before replacement.");
            }

            File.Move(path, backup);
            try
            {
                File.Move(temporary, path);
                File.Delete(backup);
            }
            catch (Exception writeException)
            {
                try
                {
                    Journal durable = ReadBestJournal(path, value.projectRoot);
                    if (durable.sequence == value.sequence
                        && string.Equals(durable.phase, value.phase, StringComparison.Ordinal)
                        && string.Equals(durable.checksum, value.checksum, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }
                }
                catch (Exception verificationException)
                {
                    throw new AggregateException(
                        "Publication barrier update failed and its durable decision could not be verified.",
                        writeException,
                        verificationException);
                }

                throw;
            }
        }

        private static void DeleteJournalCandidates(string journalPath)
        {
            DeleteRegularFileIfPresent(journalPath + ".tmp");
            DeleteRegularFileIfPresent(journalPath + ".bak");
            DeleteRegularFileIfPresent(journalPath);
        }

        private static void DeleteRegularFileIfPresent(string path)
        {
            if (!File.Exists(path))
            {
                return;
            }

            RejectReparsePoint(path, "publication barrier journal");
            File.Delete(path);
        }

        private static void TryDeleteEmptyStateRoot(string stateRoot)
        {
            if (!Directory.Exists(stateRoot))
            {
                return;
            }

            RejectReparsePoint(stateRoot, "publication barrier state root");
            if (!Directory.EnumerateFileSystemEntries(stateRoot).Any())
            {
                Directory.Delete(stateRoot, false);
            }
        }

        private static bool HasAnyJournalCandidate(string journalPath)
        {
            return File.Exists(journalPath)
                || File.Exists(journalPath + ".tmp")
                || File.Exists(journalPath + ".bak");
        }

        private static string ValidatePublicationStatePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
            {
                throw new ArgumentException(
                    "Deferred publication recovery state path is required and must be project-relative.",
                    nameof(path));
            }

            string normalized = path.Replace('\\', '/');
            const string prefix = ".buildpipeline/transactions/";
            if (!normalized.StartsWith(prefix, StringComparison.Ordinal)
                || normalized.Length <= prefix.Length)
            {
                throw new InvalidOperationException(
                    "Deferred publication recovery state must be below .buildpipeline/transactions.");
            }

            BuildPathPolicy.ValidatePortableProjectRelativePath(
                normalized.Substring(prefix.Length),
                "Deferred publication recovery state directory");
            return normalized;
        }

        private static void ValidatePublicationId(string id)
        {
            BuildIdentityPolicy.ValidatePlainText(
                id,
                "Deferred publication id",
                BuildStepRegistrationAttribute.MaximumIdCharacters);
        }

        private static string NormalizeProjectRoot(string projectRoot)
        {
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                throw new ArgumentException("Project root is required.", nameof(projectRoot));
            }

            return Path.GetFullPath(projectRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static void EnsureSafeStatePath(string projectRoot, string statePath)
        {
            string project = NormalizeProjectRoot(projectRoot);
            string full = Path.GetFullPath(statePath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!BuildPathPolicy.IsStrictDescendant(project, full))
            {
                throw new InvalidOperationException(
                    $"Publication barrier state escaped the project root: '{full}'.");
            }

            string relative = full.Substring(project.Length + 1);
            string current = project;
            foreach (string segment in relative.Split(
                         new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                         StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment);
                if (Directory.Exists(current) || File.Exists(current))
                {
                    RejectReparsePoint(current, "publication barrier path");
                }
            }
        }

        private static void RejectReparsePoint(string path, string label)
        {
            FileAttributes attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    $"{label} cannot be a reparse point: '{path}'.");
            }
        }

        private static string ComputeHash(string value)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                return BitConverter.ToString(
                        sha256.ComputeHash(StrictUtf8.GetBytes(value)))
                    .Replace("-", string.Empty)
                    .ToLowerInvariant();
            }
        }

        private static bool PathsEqual(string first, string second)
        {
            return string.Equals(
                NormalizeProjectRoot(first),
                NormalizeProjectRoot(second),
                PathComparison);
        }

        private static Journal CloneJournal(Journal source)
        {
            var publications = new PublicationEntry[source.publications.Length];
            for (int index = 0; index < source.publications.Length; index++)
            {
                PublicationEntry entry = source.publications[index];
                publications[index] = new PublicationEntry
                {
                    id = entry.id,
                    stateRelativePath = entry.stateRelativePath
                };
            }

            return new Journal
            {
                documentType = source.documentType,
                runId = source.runId,
                phase = source.phase,
                sequence = source.sequence,
                projectRoot = source.projectRoot,
                publications = publications,
                checksum = source.checksum
            };
        }

        private static StringComparison PathComparison =>
            Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        private void ThrowIfFinalized()
        {
            if (finalized)
            {
                throw new InvalidOperationException(
                    "Terminal publication barrier has already been finalized.");
            }
        }

        [Serializable]
        private sealed class Journal
        {
            public string documentType;
            public string runId;
            public string phase;
            public long sequence;
            public string projectRoot;
            public PublicationEntry[] publications;
            public string checksum;
        }

        [Serializable]
        private sealed class PublicationEntry
        {
            public string id;
            public string stateRelativePath;
        }

        private sealed class JournalCandidate
        {
            public JournalCandidate(Journal value, string canonicalJson)
            {
                Value = value;
                CanonicalJson = canonicalJson;
            }

            public Journal Value { get; }
            public string CanonicalJson { get; }
        }
    }

}
