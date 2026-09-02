using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Build.Pipeline.Editor;

namespace Build.Pipeline.Integrations.YooAsset3.Publication
{
    internal static class PublicationOwnership
    {
        internal const string MarkerFileName = ".yoo-pub.json";
        internal const string Owner = "Build.Pipeline.Editor.Integrations.YooAsset3";
        internal const string PackageOutputKind = "PackageOutput";
        internal const string BundledPackageKind = "BundledPackage";

        private const string MarkerDocumentType = "yooasset-publication-owner";
        private const int MaximumMarkerBytes = 64 * 1024;
        private const int MaximumIdentityEntries = 250000;
        private const int MaximumIdentityDepth = 64;
        private const long MaximumIdentityBytes = 256L * 1024L * 1024L * 1024L;

        public static PublicationSnapshot CaptureExisting(
            string projectRoot,
            string directory,
            string expectedKind,
            string expectedPackageName,
            IJournalSerializer serializer)
        {
            string root = Path.GetFullPath(directory);
            PublicationSafety.ValidateNoPathRedirection(projectRoot, root);
            if (File.Exists(root))
            {
                throw new InvalidOperationException($"Publication directory resolves to a file: '{root}'.");
            }

            if (!Directory.Exists(root))
            {
                return PublicationSnapshot.Missing;
            }

            string markerPath = Path.Combine(root, MarkerFileName);
            if (!File.Exists(markerPath))
            {
                if (Directory.EnumerateFileSystemEntries(root, "*", SearchOption.TopDirectoryOnly).Any())
                {
                    throw new InvalidOperationException(
                        $"Refusing to replace a non-empty directory that is not a Build-owned YooAsset publication: '{root}'.");
                }

                ContentIdentity emptyIdentity = ComputeContentIdentity(root);
                return new PublicationSnapshot(
                    true,
                    false,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    emptyIdentity.Hash,
                    emptyIdentity.EntryCount);
            }

            return ReadAndValidateOwned(
                serializer,
                root,
                expectedKind,
                expectedPackageName,
                null,
                null,
                null,
                null,
                null,
                null);
        }

        public static PublicationSnapshot Seal(
            string projectRoot,
            string directory,
            string kind,
            string packageName,
            string packageVersion,
            string cryptographyAdapterId,
            string runtimeDecryptContractId,
            string transactionId,
            IJournalSerializer serializer)
        {
            string root = Path.GetFullPath(directory);
            PublicationSafety.ValidateNoPathRedirection(projectRoot, root);
            if (!Directory.Exists(root) || File.Exists(root))
            {
                throw new DirectoryNotFoundException($"Publication stage does not exist: '{root}'.");
            }

            ValidateMarkerIdentity(
                kind,
                packageName,
                packageVersion,
                cryptographyAdapterId,
                runtimeDecryptContractId,
                transactionId,
                serializer);
            string markerPath = BuildPathPolicy.EnsureWin32MaxPathBudget(
                Path.Combine(root, MarkerFileName),
                "YooAsset publication ownership marker");
            if (Directory.Exists(markerPath))
            {
                throw new InvalidOperationException($"Publication marker path resolves to a directory: '{markerPath}'.");
            }

            if (File.Exists(markerPath) && (File.GetAttributes(markerPath) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException($"Refusing to replace a reparse-point publication marker: '{markerPath}'.");
            }

            ContentIdentity identity = ComputeContentIdentity(root);
            var marker = new PublicationMarker
            {
                documentType = MarkerDocumentType,
                owner = Owner,
                kind = kind,
                packageName = packageName,
                packageVersion = packageVersion,
                cryptographyAdapterId = cryptographyAdapterId,
                runtimeDecryptContractId = runtimeDecryptContractId,
                transactionId = transactionId,
                contentIdentity = identity.Hash,
                entryCount = identity.EntryCount
            };
            marker.checksum = ComputeMarkerChecksum(marker);
            byte[] bytes = new UTF8Encoding(false).GetBytes(serializer.ToJson(marker));
            if (bytes.Length <= 0 || bytes.Length > MaximumMarkerBytes)
            {
                throw new InvalidOperationException($"Publication marker exceeds {MaximumMarkerBytes} bytes: '{markerPath}'.");
            }

            using (var stream = new FileStream(
                       markerPath,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }

            if ((File.GetAttributes(markerPath) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException($"Publication marker became a reparse point: '{markerPath}'.");
            }

            return ReadAndValidateOwned(
                serializer,
                root,
                kind,
                packageName,
                packageVersion,
                cryptographyAdapterId,
                runtimeDecryptContractId,
                transactionId,
                identity.Hash,
                identity.EntryCount);
        }

        public static PublicationSnapshot ValidateOwned(
            string projectRoot,
            string directory,
            string expectedKind,
            string expectedPackageName,
            string expectedPackageVersion,
            string expectedCryptographyAdapterId,
            string expectedRuntimeDecryptContractId,
            string expectedTransactionId,
            string expectedContentIdentity,
            int expectedEntryCount,
            IJournalSerializer serializer)
        {
            string root = Path.GetFullPath(directory);
            PublicationSafety.ValidateNoPathRedirection(projectRoot, root);
            if (!Directory.Exists(root) || File.Exists(root))
            {
                throw new DirectoryNotFoundException($"Owned publication directory does not exist: '{root}'.");
            }

            return ReadAndValidateOwned(
                serializer,
                root,
                expectedKind,
                expectedPackageName,
                expectedPackageVersion,
                expectedCryptographyAdapterId,
                expectedRuntimeDecryptContractId,
                expectedTransactionId,
                expectedContentIdentity,
                expectedEntryCount);
        }

        public static PublicationSnapshot ValidateEmptyUnowned(string projectRoot, string directory)
        {
            string root = Path.GetFullPath(directory);
            PublicationSafety.ValidateNoPathRedirection(projectRoot, root);
            if (!Directory.Exists(root) || File.Exists(root))
            {
                throw new DirectoryNotFoundException($"Original empty publication directory does not exist: '{root}'.");
            }

            if (Directory.EnumerateFileSystemEntries(root, "*", SearchOption.TopDirectoryOnly).Any())
            {
                throw new InvalidOperationException(
                    $"An originally empty publication directory was modified outside the active transaction: '{root}'.");
            }

            ContentIdentity identity = ComputeContentIdentity(root);
            return new PublicationSnapshot(
                true,
                false,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                identity.Hash,
                identity.EntryCount);
        }

        public static bool IsMarkerArtifact(string path)
        {
            return string.Equals(Path.GetFileName(path), MarkerFileName, StringComparison.Ordinal) ||
                   string.Equals(Path.GetFileName(path), MarkerFileName + ".meta", StringComparison.Ordinal);
        }

        private static PublicationSnapshot ReadAndValidateOwned(
            IJournalSerializer serializer,
            string root,
            string expectedKind,
            string expectedPackageName,
            string expectedPackageVersion,
            string expectedCryptographyAdapterId,
            string expectedRuntimeDecryptContractId,
            string expectedTransactionId,
            string expectedContentIdentity,
            int? expectedEntryCount)
        {
            string markerPath = Path.Combine(root, MarkerFileName);
            if (!File.Exists(markerPath) || Directory.Exists(markerPath))
            {
                throw new InvalidOperationException($"Build-owned publication marker is missing: '{markerPath}'.");
            }

            FileAttributes markerAttributes = File.GetAttributes(markerPath);
            if ((markerAttributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException($"Build-owned publication marker is a reparse point: '{markerPath}'.");
            }

            var info = new FileInfo(markerPath);
            if (info.Length <= 0 || info.Length > MaximumMarkerBytes)
            {
                throw new InvalidOperationException($"Build-owned publication marker size is invalid: '{markerPath}'.");
            }

            PublicationMarker marker;
            try
            {
                string json = File.ReadAllText(markerPath, Encoding.UTF8);
                BuildJsonDocumentContract.Validate<PublicationMarker>(
                    json,
                    MarkerDocumentType,
                    "YooAsset publication ownership marker");
                marker = serializer.FromJson<PublicationMarker>(json);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException($"Build-owned publication marker is not valid JSON: '{markerPath}'.", exception);
            }

            if (marker == null ||
                !string.Equals(marker.documentType, MarkerDocumentType, StringComparison.Ordinal) ||
                !string.Equals(marker.owner, Owner, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Build-owned publication marker has an unsupported owner or format: '{markerPath}'.");
            }

            ValidateMarkerIdentity(
                marker.kind,
                marker.packageName,
                marker.packageVersion,
                marker.cryptographyAdapterId,
                marker.runtimeDecryptContractId,
                marker.transactionId,
                serializer);
            if (!string.Equals(marker.checksum, ComputeMarkerChecksum(marker), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Build-owned publication marker checksum is invalid: '{markerPath}'.");
            }

            if (!string.Equals(marker.kind, expectedKind, StringComparison.Ordinal) ||
                !string.Equals(marker.packageName, expectedPackageName, StringComparison.Ordinal) ||
                expectedPackageVersion != null && !string.Equals(marker.packageVersion, expectedPackageVersion, StringComparison.Ordinal) ||
                expectedCryptographyAdapterId != null && !string.Equals(marker.cryptographyAdapterId, expectedCryptographyAdapterId, StringComparison.Ordinal) ||
                expectedRuntimeDecryptContractId != null && !string.Equals(marker.runtimeDecryptContractId, expectedRuntimeDecryptContractId, StringComparison.Ordinal) ||
                expectedTransactionId != null && !string.Equals(marker.transactionId, expectedTransactionId, StringComparison.Ordinal) ||
                expectedContentIdentity != null && !string.Equals(marker.contentIdentity, expectedContentIdentity, StringComparison.OrdinalIgnoreCase) ||
                expectedEntryCount.HasValue && marker.entryCount != expectedEntryCount.Value)
            {
                throw new InvalidOperationException($"Build-owned publication marker identity does not match the expected publication: '{markerPath}'.");
            }

            ContentIdentity actual = ComputeContentIdentity(root);
            if (!string.Equals(actual.Hash, marker.contentIdentity, StringComparison.OrdinalIgnoreCase) ||
                actual.EntryCount != marker.entryCount)
            {
                throw new InvalidOperationException(
                    $"Build-owned publication content changed outside the owning transaction: '{root}'.");
            }

            return new PublicationSnapshot(
                true,
                true,
                marker.kind,
                marker.packageName,
                marker.packageVersion,
                marker.cryptographyAdapterId,
                marker.runtimeDecryptContractId,
                marker.transactionId,
                marker.contentIdentity,
                marker.entryCount);
        }

        private static ContentIdentity ComputeContentIdentity(string root)
        {
            List<IdentityEntry> entries = EnumerateIdentityEntries(root);
            long totalBytes = 0;
            using (SHA256 aggregate = SHA256.Create())
            {
                foreach (IdentityEntry entry in entries)
                {
                    if (entry.IsDirectory)
                    {
                        AppendHashRecord(aggregate, "D", entry.RelativePath, string.Empty, 0);
                        continue;
                    }

                    var before = new FileInfo(entry.FullPath);
                    long length = before.Length;
                    long lastWriteTicks = before.LastWriteTimeUtc.Ticks;
                    totalBytes = checked(totalBytes + length);
                    if (totalBytes > MaximumIdentityBytes)
                    {
                        throw new InvalidOperationException(
                            $"Publication identity exceeds the byte budget of {MaximumIdentityBytes}: '{root}'.");
                    }

                    string fileHash;
                    using (SHA256 fileSha = SHA256.Create())
                    using (var stream = new FileStream(entry.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        fileHash = ToHex(fileSha.ComputeHash(stream));
                    }

                    before.Refresh();
                    if (!before.Exists || before.Length != length || before.LastWriteTimeUtc.Ticks != lastWriteTicks)
                    {
                        throw new InvalidOperationException(
                            $"Publication file changed while its content identity was being computed: '{entry.FullPath}'.");
                    }

                    AppendHashRecord(aggregate, "F", entry.RelativePath, fileHash, length);
                }

                List<IdentityEntry> verificationEntries = EnumerateIdentityEntries(root);
                if (entries.Count != verificationEntries.Count)
                {
                    throw new InvalidOperationException($"Publication entries changed while its content identity was being computed: '{root}'.");
                }

                for (int index = 0; index < entries.Count; index++)
                {
                    if (entries[index].IsDirectory != verificationEntries[index].IsDirectory ||
                        !string.Equals(entries[index].RelativePath, verificationEntries[index].RelativePath, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException($"Publication entries changed while its content identity was being computed: '{root}'.");
                    }
                }

                aggregate.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                return new ContentIdentity(ToHex(aggregate.Hash), entries.Count);
            }
        }

        private static List<IdentityEntry> EnumerateIdentityEntries(string root)
        {
            string normalizedRoot = Path.GetFullPath(root);
            string rootPrefix = normalizedRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                                Path.DirectorySeparatorChar;
            var result = new List<IdentityEntry>();
            var pending = new Stack<DirectoryEntry>();
            pending.Push(new DirectoryEntry(normalizedRoot, 0));
            while (pending.Count > 0)
            {
                DirectoryEntry current = pending.Pop();
                if (current.Depth > MaximumIdentityDepth)
                {
                    throw new InvalidOperationException(
                        $"Publication identity exceeds the maximum directory depth of {MaximumIdentityDepth}: '{root}'.");
                }

                foreach (string path in Directory.EnumerateFileSystemEntries(current.Path, "*", SearchOption.TopDirectoryOnly))
                {
                    string fullPath = Path.GetFullPath(path);
                    if (!fullPath.StartsWith(
                            rootPrefix,
                            Path.DirectorySeparatorChar == '\\'
                                ? StringComparison.OrdinalIgnoreCase
                                : StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException($"Publication identity enumeration escaped its root: '{fullPath}'.");
                    }

                    FileAttributes attributes = File.GetAttributes(fullPath);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new InvalidOperationException($"Publication identity refuses a reparse-point entry: '{fullPath}'.");
                    }

                    bool isDirectory = (attributes & FileAttributes.Directory) != 0;
                    string relativePath = fullPath.Substring(rootPrefix.Length)
                        .Replace(Path.DirectorySeparatorChar, '/')
                        .Replace(Path.AltDirectorySeparatorChar, '/');
                    if (!isDirectory &&
                        (string.Equals(relativePath, MarkerFileName, StringComparison.Ordinal) ||
                         relativePath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    result.Add(new IdentityEntry(fullPath, relativePath, isDirectory));
                    if (result.Count > MaximumIdentityEntries)
                    {
                        throw new InvalidOperationException(
                            $"Publication identity exceeds the entry limit of {MaximumIdentityEntries}: '{root}'.");
                    }

                    if (isDirectory)
                    {
                        pending.Push(new DirectoryEntry(fullPath, current.Depth + 1));
                    }
                }
            }

            result.Sort((left, right) =>
            {
                int pathComparison = string.Compare(left.RelativePath, right.RelativePath, StringComparison.Ordinal);
                return pathComparison != 0 ? pathComparison : left.IsDirectory.CompareTo(right.IsDirectory);
            });
            return result;
        }

        private static void AppendHashRecord(
            HashAlgorithm hash,
            string kind,
            string relativePath,
            string contentHash,
            long length)
        {
            string record = string.Concat(
                kind,
                ":",
                relativePath.Length.ToString(CultureInfo.InvariantCulture),
                ":",
                relativePath,
                ":",
                length.ToString(CultureInfo.InvariantCulture),
                ":",
                contentHash,
                ";");
            byte[] bytes = Encoding.UTF8.GetBytes(record);
            hash.TransformBlock(bytes, 0, bytes.Length, bytes, 0);
        }

        private static void ValidateMarkerIdentity(
            string kind,
            string packageName,
            string packageVersion,
            string cryptographyAdapterId,
            string runtimeDecryptContractId,
            string transactionId,
            IJournalSerializer serializer)
        {
            if ((!string.Equals(kind, PackageOutputKind, StringComparison.Ordinal) &&
                 !string.Equals(kind, BundledPackageKind, StringComparison.Ordinal)) ||
                string.IsNullOrWhiteSpace(packageName) ||
                string.IsNullOrWhiteSpace(packageVersion) ||
                !IsTransactionId(transactionId))
            {
                throw new InvalidOperationException("Publication marker identity is incomplete or unsupported.");
            }

            BuildIdentityPolicy.ValidateBuildIdentifier(
                cryptographyAdapterId,
                "YooAsset cryptography adapter id");
            BuildIdentityPolicy.ValidateBuildIdentifier(
                runtimeDecryptContractId,
                "YooAsset runtime decrypt contract id");
        }

        private static string ComputeMarkerChecksum(PublicationMarker marker)
        {
            var builder = new StringBuilder();
            AppendChecksumValue(builder, marker.documentType);
            AppendChecksumValue(builder, marker.owner);
            AppendChecksumValue(builder, marker.kind);
            AppendChecksumValue(builder, marker.packageName);
            AppendChecksumValue(builder, marker.packageVersion);
            AppendChecksumValue(builder, marker.cryptographyAdapterId);
            AppendChecksumValue(builder, marker.runtimeDecryptContractId);
            AppendChecksumValue(builder, marker.transactionId);
            AppendChecksumValue(builder, marker.contentIdentity);
            AppendChecksumValue(builder, marker.entryCount.ToString(CultureInfo.InvariantCulture));
            using (SHA256 sha = SHA256.Create())
            {
                return ToHex(sha.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString())));
            }
        }

        private static void AppendChecksumValue(StringBuilder builder, string value)
        {
            string normalized = value ?? string.Empty;
            builder.Append(normalized.Length.ToString(CultureInfo.InvariantCulture));
            builder.Append(':');
            builder.Append(normalized);
            builder.Append(';');
        }

        private static bool IsTransactionId(string value)
        {
            return value != null && value.Length == 32 && value.All(character =>
                character >= '0' && character <= '9' || character >= 'a' && character <= 'f');
        }

        private static string ToHex(byte[] value)
        {
            return BitConverter.ToString(value).Replace("-", string.Empty);
        }

        [Serializable]
        private sealed class PublicationMarker
        {
            public string documentType;
            public string owner;
            public string kind;
            public string packageName;
            public string packageVersion;
            public string cryptographyAdapterId;
            public string runtimeDecryptContractId;
            public string transactionId;
            public string contentIdentity;
            public int entryCount;
            public string checksum;
        }

        internal readonly struct PublicationSnapshot
        {
            public static readonly PublicationSnapshot Missing = new PublicationSnapshot(
                false,
                false,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                0);

            public PublicationSnapshot(
                bool exists,
                bool owned,
                string kind,
                string packageName,
                string packageVersion,
                string cryptographyAdapterId,
                string runtimeDecryptContractId,
                string transactionId,
                string contentIdentity,
                int entryCount)
            {
                Exists = exists;
                Owned = owned;
                Kind = kind;
                PackageName = packageName;
                PackageVersion = packageVersion;
                CryptographyAdapterId = cryptographyAdapterId;
                RuntimeDecryptContractId = runtimeDecryptContractId;
                TransactionId = transactionId;
                ContentIdentity = contentIdentity;
                EntryCount = entryCount;
            }

            public bool Exists { get; }
            public bool Owned { get; }
            public string Kind { get; }
            public string PackageName { get; }
            public string PackageVersion { get; }
            public string CryptographyAdapterId { get; }
            public string RuntimeDecryptContractId { get; }
            public string TransactionId { get; }
            public string ContentIdentity { get; }
            public int EntryCount { get; }
        }

        private readonly struct ContentIdentity
        {
            public ContentIdentity(string hash, int entryCount)
            {
                Hash = hash;
                EntryCount = entryCount;
            }

            public string Hash { get; }
            public int EntryCount { get; }
        }

        private readonly struct DirectoryEntry
        {
            public DirectoryEntry(string path, int depth)
            {
                Path = path;
                Depth = depth;
            }

            public string Path { get; }
            public int Depth { get; }
        }

        private readonly struct IdentityEntry
        {
            public IdentityEntry(string fullPath, string relativePath, bool isDirectory)
            {
                FullPath = fullPath;
                RelativePath = relativePath;
                IsDirectory = isDirectory;
            }

            public string FullPath { get; }
            public string RelativePath { get; }
            public bool IsDirectory { get; }
        }
    }
}
