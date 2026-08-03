using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    [Serializable]
    internal sealed class HybridCLRFileIdentity
    {
        public long size;
        public string sha256;
    }

    [Serializable]
    internal sealed class HybridCLRDirectoryIdentity
    {
        public string kind;
        public string transactionId;
        public int fileCount;
        public long totalSize;
        public long manifestSize;
        public string manifestSha256;
        public string treeSha256;
    }

    internal static class HybridCLROutputOwnership
    {
        [Serializable]
        private sealed class OwnershipManifest
        {
            public int schemaVersion;
            public string owner;
            public string role;
            public string transactionId;
            public OwnershipFileEntry[] files;
        }

        [Serializable]
        private sealed class OwnershipFileEntry
        {
            public string kind;
            public string path;
            public long size;
            public string sha256;
        }

        internal const string ManifestFileName = ".buildpipeline-owner.json";
        internal const string Owner = "Build.Pipeline.Editor.HybridCLR";
        internal const int SchemaVersion = 2;
        internal const int MaximumArtifactCount = 4096;
        internal const int MaximumManagedFileCount = MaximumArtifactCount * 2 + 2;
        internal const int MaximumArtifactFileNameByteCount = 240;
        internal const long MaximumManifestByteCount = 4L * 1024L * 1024L;
        internal const long MaximumManagedFileByteCount = 512L * 1024L * 1024L;
        internal const long MaximumManagedDirectoryByteCount = 4L * 1024L * 1024L * 1024L;
        internal const string EmptyDirectoryKind = "Empty";
        internal const string OwnedDirectoryKind = "Owned";

        private const string ArtifactKind = "Artifact";
        private const string MetaKind = "Meta";
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private static readonly UTF8Encoding Utf8WithoutBom = new UTF8Encoding(false);

        internal static HybridCLRDirectoryIdentity CaptureInitialDirectory(
            string directory,
            string role)
        {
            if (File.Exists(directory))
            {
                throw new InvalidOperationException(
                    $"HybridCLR output directory resolves to a file: '{directory}'.");
            }

            if (!Directory.Exists(directory))
            {
                return null;
            }

            EnsureDirectoryIsNotRedirected(directory);
            using (IEnumerator<string> entries = Directory
                       .EnumerateFileSystemEntries(directory, "*", SearchOption.TopDirectoryOnly)
                       .GetEnumerator())
            {
                if (!entries.MoveNext())
                {
                    return CreateEmptyDirectoryIdentity();
                }
            }

            return CaptureOwnedDirectory(directory, role);
        }

        internal static HybridCLRDirectoryIdentity PrepareStagedDirectory(
            string directory,
            string role,
            string transactionId,
            IEnumerable<string> artifactFileNames,
            string existingDirectory)
        {
            if (!IsTransactionId(transactionId))
            {
                throw new ArgumentException("A valid HybridCLR transaction ID is required.", nameof(transactionId));
            }

            string[] artifacts = NormalizeArtifactFileNames(artifactFileNames);
            ValidateArtifactsForRole(role, artifacts);
            EnsureOwnedEmptyDirectory(directory);

            var files = new List<OwnershipFileEntry>(artifacts.Length * 2 + 1);
            foreach (string artifact in artifacts)
            {
                string artifactPath = Path.Combine(directory, artifact);
                HybridCLRFileIdentity artifactIdentity = CaptureRequiredFileIdentity(
                    artifactPath,
                    $"staged HybridCLR artifact '{role}/{artifact}'");
                files.Add(CreateManifestEntry(ArtifactKind, artifact, artifactIdentity));

                string metaName = artifact + ".meta";
                string stagedMetaPath = Path.Combine(directory, metaName);
                PreserveOrCreateMeta(
                    existingDirectory,
                    metaName,
                    stagedMetaPath,
                    folderAsset: false);
                HybridCLRFileIdentity metaIdentity = CaptureRequiredFileIdentity(
                    stagedMetaPath,
                    $"staged HybridCLR artifact meta '{role}/{metaName}'");
                files.Add(CreateManifestEntry(MetaKind, metaName, metaIdentity));
            }

            string manifestMetaName = ManifestFileName + ".meta";
            string stagedManifestMeta = Path.Combine(directory, manifestMetaName);
            PreserveOrCreateMeta(
                existingDirectory,
                manifestMetaName,
                stagedManifestMeta,
                folderAsset: false);
            files.Add(CreateManifestEntry(
                MetaKind,
                manifestMetaName,
                CaptureRequiredFileIdentity(
                    stagedManifestMeta,
                    $"staged HybridCLR ownership meta '{role}/{manifestMetaName}'")));

            files.Sort((left, right) => StringComparer.Ordinal.Compare(left.path, right.path));
            var manifest = new OwnershipManifest
            {
                schemaVersion = SchemaVersion,
                owner = Owner,
                role = role,
                transactionId = transactionId,
                files = files.ToArray()
            };
            string json = JsonUtility.ToJson(manifest, true) + Environment.NewLine;
            byte[] manifestBytes = Utf8WithoutBom.GetBytes(json);
            if (manifestBytes.Length <= 0 || manifestBytes.Length > MaximumManifestByteCount)
            {
                throw new InvalidOperationException(
                    $"HybridCLR ownership manifest exceeds {MaximumManifestByteCount} bytes.");
            }

            WriteFileDurably(Path.Combine(directory, ManifestFileName), manifestBytes);
            HybridCLRDirectoryIdentity identity = CaptureOwnedDirectory(directory, role);
            if (!string.Equals(identity.transactionId, transactionId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Staged HybridCLR ownership transaction ID changed for role '{role}'.");
            }

            return identity;
        }

        internal static HybridCLRDirectoryIdentity CaptureDirectory(
            string directory,
            string role)
        {
            if (File.Exists(directory))
            {
                throw new InvalidOperationException(
                    $"HybridCLR managed directory became a file: '{directory}'.");
            }

            if (!Directory.Exists(directory))
            {
                return null;
            }

            EnsureDirectoryIsNotRedirected(directory);
            using (IEnumerator<string> entries = Directory
                       .EnumerateFileSystemEntries(directory, "*", SearchOption.TopDirectoryOnly)
                       .GetEnumerator())
            {
                if (!entries.MoveNext())
                {
                    return CreateEmptyDirectoryIdentity();
                }
            }

            return CaptureOwnedDirectory(directory, role);
        }

        internal static void RequireDirectoryIdentity(
            string directory,
            string role,
            HybridCLRDirectoryIdentity expected,
            string description)
        {
            HybridCLRDirectoryIdentity actual = CaptureDirectory(directory, role);
            if (!DirectoryIdentityEquals(actual, expected))
            {
                throw new InvalidOperationException(
                    $"HybridCLR {description} identity changed: '{directory}'.");
            }
        }

        internal static HybridCLRFileIdentity CaptureOptionalFileIdentity(
            string path,
            string description)
        {
            if (Directory.Exists(path))
            {
                throw new InvalidOperationException(
                    $"HybridCLR {description} resolves to a directory: '{path}'.");
            }

            if (!File.Exists(path))
            {
                return null;
            }

            FileAttributes attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    $"HybridCLR {description} cannot be a symbolic link or reparse point: '{path}'.");
            }

            var info = new FileInfo(path);
            if (info.Length < 0 || info.Length > MaximumManagedFileByteCount)
            {
                throw new InvalidOperationException(
                    $"HybridCLR {description} exceeds the {MaximumManagedFileByteCount}-byte safety budget: '{path}'.");
            }

            return new HybridCLRFileIdentity
            {
                size = info.Length,
                sha256 = ComputeFileSha256(path)
            };
        }

        internal static HybridCLRFileIdentity CaptureRequiredFileIdentity(
            string path,
            string description)
        {
            return CaptureOptionalFileIdentity(path, description)
                ?? throw new FileNotFoundException($"HybridCLR {description} is missing.", path);
        }

        internal static void RequireFileIdentity(
            string path,
            HybridCLRFileIdentity expected,
            string description)
        {
            HybridCLRFileIdentity actual = CaptureOptionalFileIdentity(path, description);
            if (!FileIdentityEquals(actual, expected))
            {
                throw new InvalidOperationException(
                    $"HybridCLR {description} identity changed: '{path}'.");
            }
        }

        internal static HybridCLRFileIdentity WriteGeneratedMeta(
            string path,
            bool folderAsset)
        {
            return WriteGeneratedMeta(path, Guid.NewGuid().ToString("N"), folderAsset);
        }

        internal static HybridCLRFileIdentity WriteGeneratedMeta(
            string path,
            string guid,
            bool folderAsset)
        {
            if (File.Exists(path) || Directory.Exists(path))
            {
                throw new InvalidOperationException(
                    $"HybridCLR generated meta path is not empty: '{path}'.");
            }

            string content = CreateMetaContent(guid, folderAsset);
            WriteFileDurably(path, Utf8WithoutBom.GetBytes(content));
            return CaptureRequiredFileIdentity(path, "generated Unity meta file");
        }

        internal static HybridCLRFileIdentity CreateGeneratedMetaIdentity(
            string guid,
            bool folderAsset)
        {
            byte[] bytes = Utf8WithoutBom.GetBytes(CreateMetaContent(guid, folderAsset));
            return new HybridCLRFileIdentity
            {
                size = bytes.Length,
                sha256 = ComputeSha256(bytes)
            };
        }

        internal static string CreateDeterministicMetaGuid(string seed)
        {
            if (string.IsNullOrEmpty(seed))
            {
                throw new ArgumentException("A deterministic Unity meta seed is required.", nameof(seed));
            }

            return ComputeSha256(Encoding.UTF8.GetBytes(seed)).Substring(0, 32);
        }

        internal static void CopyFileAndVerify(
            string source,
            string destination,
            HybridCLRFileIdentity expected,
            string description)
        {
            BuildPathPolicy.EnsureLegacyWindowsPathBudget(
                source,
                $"HybridCLR {description} source");
            BuildPathPolicy.EnsureLegacyWindowsPathBudget(
                destination,
                $"HybridCLR {description} destination");
            RequireFileIdentity(source, expected, description + " source");
            if (File.Exists(destination) || Directory.Exists(destination))
            {
                throw new InvalidOperationException(
                    $"HybridCLR {description} destination is not empty: '{destination}'.");
            }

            string parent = Path.GetDirectoryName(destination);
            if (string.IsNullOrEmpty(parent))
            {
                throw new InvalidOperationException(
                    $"HybridCLR {description} destination has no parent: '{destination}'.");
            }

            BuildPathPolicy.EnsureLegacyWindowsDirectoryPathBudget(
                parent,
                $"HybridCLR {description} destination directory");
            Directory.CreateDirectory(parent);
            using (var input = new FileStream(
                       source,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read,
                       64 * 1024,
                       FileOptions.SequentialScan))
            using (var output = new FileStream(
                       destination,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       64 * 1024,
                       FileOptions.WriteThrough))
            {
                input.CopyTo(output, 64 * 1024);
                output.Flush(true);
            }

            RequireFileIdentity(destination, expected, description + " copy");
        }

        internal static void DeleteFileExact(
            string path,
            HybridCLRFileIdentity expected,
            string description)
        {
            RequireFileIdentity(path, expected, description);
            File.Delete(path);
            if (File.Exists(path) || Directory.Exists(path))
            {
                throw new IOException($"Failed to delete HybridCLR {description}: '{path}'.");
            }
        }

        internal static bool DirectoryIdentityEquals(
            HybridCLRDirectoryIdentity left,
            HybridCLRDirectoryIdentity right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            return left != null
                && right != null
                && string.Equals(left.kind, right.kind, StringComparison.Ordinal)
                && string.Equals(left.transactionId, right.transactionId, StringComparison.Ordinal)
                && left.fileCount == right.fileCount
                && left.totalSize == right.totalSize
                && left.manifestSize == right.manifestSize
                && string.Equals(left.manifestSha256, right.manifestSha256, StringComparison.OrdinalIgnoreCase)
                && string.Equals(left.treeSha256, right.treeSha256, StringComparison.OrdinalIgnoreCase);
        }

        internal static bool FileIdentityEquals(
            HybridCLRFileIdentity left,
            HybridCLRFileIdentity right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            return left != null
                && right != null
                && left.size == right.size
                && string.Equals(left.sha256, right.sha256, StringComparison.OrdinalIgnoreCase);
        }

        internal static void ValidateDirectoryIdentitySchema(
            HybridCLRDirectoryIdentity identity,
            bool allowNull,
            string fieldName)
        {
            if (identity == null)
            {
                if (allowNull)
                {
                    return;
                }

                throw new InvalidOperationException(
                    $"HybridCLR durable journal is missing directory identity '{fieldName}'.");
            }

            if (identity.kind == EmptyDirectoryKind)
            {
                if (!string.IsNullOrEmpty(identity.transactionId)
                    || identity.fileCount != 0
                    || identity.totalSize != 0
                    || identity.manifestSize != 0
                    || !string.IsNullOrEmpty(identity.manifestSha256)
                    || !IsSha256(identity.treeSha256))
                {
                    throw new InvalidOperationException(
                        $"HybridCLR durable journal has an invalid empty identity '{fieldName}'.");
                }

                return;
            }

            if (identity.kind != OwnedDirectoryKind
                || !IsTransactionId(identity.transactionId)
                || identity.fileCount <= 0
                || identity.fileCount > MaximumManagedFileCount + 1
                || identity.totalSize < 0
                || identity.totalSize > MaximumManagedDirectoryByteCount
                || identity.manifestSize <= 0
                || identity.manifestSize > MaximumManifestByteCount
                || !IsSha256(identity.manifestSha256)
                || !IsSha256(identity.treeSha256))
            {
                throw new InvalidOperationException(
                    $"HybridCLR durable journal has an invalid owned identity '{fieldName}'.");
            }
        }

        internal static void ValidateFileIdentitySchema(
            HybridCLRFileIdentity identity,
            bool allowNull,
            string fieldName)
        {
            if (identity == null)
            {
                if (allowNull)
                {
                    return;
                }

                throw new InvalidOperationException(
                    $"HybridCLR durable journal is missing file identity '{fieldName}'.");
            }

            if (identity.size < 0
                || identity.size > MaximumManagedFileByteCount
                || !IsSha256(identity.sha256))
            {
                throw new InvalidOperationException(
                    $"HybridCLR durable journal has an invalid file identity '{fieldName}'.");
            }
        }

        internal static string[] NormalizeArtifactFileNames(IEnumerable<string> artifactFileNames)
        {
            if (artifactFileNames == null)
            {
                throw new ArgumentNullException(nameof(artifactFileNames));
            }

            var artifacts = new List<string>();
            var portableNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string fileName in artifactFileNames)
            {
                if (artifacts.Count >= MaximumArtifactCount)
                {
                    throw new InvalidOperationException(
                        $"HybridCLR output exceeds the {MaximumArtifactCount}-artifact safety budget.");
                }

                ValidateManagedFileName(fileName, allowMeta: false);
                if (!portableNames.Add(fileName))
                {
                    throw new InvalidOperationException(
                        $"HybridCLR output contains a duplicate or casing-aliased artifact name: '{fileName}'.");
                }

                artifacts.Add(fileName);
            }

            artifacts.Sort(StringComparer.Ordinal);
            return artifacts.ToArray();
        }

        internal static void ValidateManagedFileName(string fileName, bool allowMeta)
        {
            if (string.IsNullOrWhiteSpace(fileName)
                || fileName == "."
                || fileName == ".."
                || !string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal)
                || Path.IsPathRooted(fileName)
                || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || fileName.IndexOfAny(new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' }) >= 0
                || fileName.Any(char.IsControl)
                || fileName.EndsWith(".", StringComparison.Ordinal)
                || fileName.EndsWith(" ", StringComparison.Ordinal)
                || Encoding.UTF8.GetByteCount(fileName) > MaximumArtifactFileNameByteCount
                || IsReservedWindowsDeviceName(fileName)
                || (!allowMeta && fileName.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                || fileName.Equals(ManifestFileName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"HybridCLR managed artifact must be a safe flat file name: '{fileName}'.");
            }
        }

        internal static bool IsSha256(string value)
        {
            return !string.IsNullOrEmpty(value)
                && value.Length == 64
                && value.All(character =>
                    (character >= '0' && character <= '9')
                    || (character >= 'a' && character <= 'f')
                    || (character >= 'A' && character <= 'F'));
        }

        internal static bool IsTransactionId(string value)
        {
            return !string.IsNullOrEmpty(value)
                && value.Length == 32
                && Guid.TryParseExact(value, "N", out _);
        }

        private static HybridCLRDirectoryIdentity CaptureOwnedDirectory(
            string directory,
            string expectedRole)
        {
            string manifestPath = Path.Combine(directory, ManifestFileName);
            OwnershipManifest manifest = ReadManifest(manifestPath);
            if (manifest.schemaVersion != SchemaVersion
                || !string.Equals(manifest.owner, Owner, StringComparison.Ordinal)
                || !string.Equals(manifest.role, expectedRole, StringComparison.Ordinal)
                || !IsTransactionId(manifest.transactionId)
                || manifest.files == null
                || manifest.files.Length == 0
                || manifest.files.Length > MaximumManagedFileCount)
            {
                throw new InvalidOperationException(
                    $"HybridCLR ownership manifest is invalid or uses an unsupported schema: '{manifestPath}'.");
            }

            OwnershipFileEntry[] entries = manifest.files
                .OrderBy(entry => entry?.path, StringComparer.Ordinal)
                .ToArray();
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ManifestFileName
            };
            var artifactNames = new List<string>();
            long totalSize = 0;
            foreach (OwnershipFileEntry entry in entries)
            {
                if (entry == null
                    || (entry.kind != ArtifactKind && entry.kind != MetaKind)
                    || !IsSha256(entry.sha256)
                    || entry.size < 0
                    || entry.size > MaximumManagedFileByteCount)
                {
                    throw new InvalidOperationException(
                        $"HybridCLR ownership manifest contains an invalid file entry: '{manifestPath}'.");
                }

                ValidateManagedFileName(entry.path, allowMeta: true);
                if (!allowed.Add(entry.path))
                {
                    throw new InvalidOperationException(
                        $"HybridCLR ownership manifest contains duplicate or casing-aliased paths: '{manifestPath}'.");
                }

                if (entry.kind == ArtifactKind)
                {
                    if (entry.path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            $"HybridCLR ownership manifest marks a meta file as an artifact: '{entry.path}'.");
                    }

                    artifactNames.Add(entry.path);
                }
                else if (!entry.path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"HybridCLR ownership manifest has an invalid meta path: '{entry.path}'.");
                }

                string filePath = Path.Combine(directory, entry.path);
                HybridCLRFileIdentity actual = CaptureRequiredFileIdentity(
                    filePath,
                    $"owned HybridCLR file '{entry.path}'");
                var expected = new HybridCLRFileIdentity
                {
                    size = entry.size,
                    sha256 = entry.sha256
                };
                if (!FileIdentityEquals(actual, expected))
                {
                    throw new InvalidOperationException(
                        $"HybridCLR owned file identity changed: '{filePath}'.");
                }

                totalSize = checked(totalSize + entry.size);
                if (totalSize > MaximumManagedDirectoryByteCount)
                {
                    throw new InvalidOperationException(
                        $"HybridCLR managed directory exceeds {MaximumManagedDirectoryByteCount} bytes: '{directory}'.");
                }
            }

            ValidateArtifactsForRole(expectedRole, artifactNames);
            ValidateMetaRelationships(entries, artifactNames, manifestPath);

            int enumerated = 0;
            foreach (string path in Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.TopDirectoryOnly))
            {
                enumerated++;
                if (enumerated > MaximumManagedFileCount + 1)
                {
                    throw new InvalidOperationException(
                        $"HybridCLR managed directory exceeds its entry budget: '{directory}'.");
                }

                FileAttributes attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0
                    || (attributes & FileAttributes.Directory) != 0)
                {
                    throw new InvalidOperationException(
                        $"HybridCLR managed output must be a flat, non-redirected directory: '{path}'.");
                }

                string fileName = Path.GetFileName(path);
                if (!allowed.Contains(fileName))
                {
                    throw new InvalidOperationException(
                        $"HybridCLR managed output contains an undeclared file: '{path}'.");
                }
            }

            HybridCLRFileIdentity manifestIdentity = CaptureRequiredFileIdentity(
                manifestPath,
                "ownership manifest");
            totalSize = checked(totalSize + manifestIdentity.size);
            if (totalSize > MaximumManagedDirectoryByteCount)
            {
                throw new InvalidOperationException(
                    $"HybridCLR managed directory exceeds {MaximumManagedDirectoryByteCount} bytes: '{directory}'.");
            }

            return new HybridCLRDirectoryIdentity
            {
                kind = OwnedDirectoryKind,
                transactionId = manifest.transactionId,
                fileCount = entries.Length + 1,
                totalSize = totalSize,
                manifestSize = manifestIdentity.size,
                manifestSha256 = manifestIdentity.sha256,
                treeSha256 = ComputeTreeSha256(manifestIdentity, entries)
            };
        }

        private static OwnershipManifest ReadManifest(string manifestPath)
        {
            if (Directory.Exists(manifestPath) || !File.Exists(manifestPath))
            {
                throw new InvalidOperationException(
                    "HybridCLR output directories are Build-exclusive. Refusing to replace a non-empty " +
                    $"directory without '{ManifestFileName}': '{Path.GetDirectoryName(manifestPath)}'.");
            }

            FileAttributes attributes = File.GetAttributes(manifestPath);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    $"HybridCLR ownership manifest cannot be a reparse point: '{manifestPath}'.");
            }

            var info = new FileInfo(manifestPath);
            if (info.Length <= 0 || info.Length > MaximumManifestByteCount)
            {
                throw new InvalidOperationException(
                    $"HybridCLR ownership manifest size is invalid: '{manifestPath}'.");
            }

            try
            {
                byte[] bytes = File.ReadAllBytes(manifestPath);
                if (HasUtf8Bom(bytes))
                {
                    throw new InvalidDataException("Ownership manifest must use UTF-8 without BOM.");
                }

                return JsonUtility.FromJson<OwnershipManifest>(StrictUtf8.GetString(bytes))
                    ?? throw new InvalidDataException("Ownership manifest JSON is empty.");
            }
            catch (Exception exception) when (!(exception is InvalidOperationException))
            {
                throw new InvalidOperationException(
                    $"Failed to read HybridCLR ownership manifest: '{manifestPath}'.",
                    exception);
            }
        }

        private static void ValidateMetaRelationships(
            IEnumerable<OwnershipFileEntry> entries,
            IReadOnlyCollection<string> artifacts,
            string manifestPath)
        {
            var metas = new HashSet<string>(
                entries.Where(entry => entry.kind == MetaKind).Select(entry => entry.path),
                StringComparer.Ordinal);
            foreach (string artifact in artifacts)
            {
                if (!metas.Contains(artifact + ".meta"))
                {
                    throw new InvalidOperationException(
                        $"HybridCLR ownership manifest is missing meta identity for '{artifact}': '{manifestPath}'.");
                }
            }

            if (!metas.Contains(ManifestFileName + ".meta"))
            {
                throw new InvalidOperationException(
                    $"HybridCLR ownership manifest is missing its Unity meta identity: '{manifestPath}'.");
            }

            foreach (string meta in metas)
            {
                string baseName = meta.Substring(0, meta.Length - ".meta".Length);
                if (!baseName.Equals(ManifestFileName, StringComparison.Ordinal)
                    && !artifacts.Contains(baseName, StringComparer.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"HybridCLR ownership manifest contains an orphan meta identity '{meta}': '{manifestPath}'.");
                }
            }
        }

        private static void ValidateArtifactsForRole(
            string role,
            IReadOnlyCollection<string> artifacts)
        {
            string listFileName;
            if (role == HybridCLRBuilder.HotUpdateOutputRole)
            {
                listFileName = "HotUpdate.bytes";
            }
            else if (role == HybridCLRBuilder.AOTOutputRole)
            {
                listFileName = "AOT.bytes";
            }
            else
            {
                throw new InvalidOperationException($"Unsupported HybridCLR output role: '{role}'.");
            }

            if (!artifacts.Contains(listFileName, StringComparer.Ordinal)
                || !artifacts.Any(fileName => fileName.EndsWith(".dll.bytes", StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    $"HybridCLR '{role}' ownership manifest must declare '{listFileName}' and at least one DLL artifact.");
            }

            foreach (string artifact in artifacts)
            {
                if (!artifact.Equals(listFileName, StringComparison.Ordinal)
                    && !artifact.EndsWith(".dll.bytes", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"HybridCLR '{role}' ownership manifest declares an unsupported artifact: '{artifact}'.");
                }
            }
        }

        private static OwnershipFileEntry CreateManifestEntry(
            string kind,
            string path,
            HybridCLRFileIdentity identity)
        {
            return new OwnershipFileEntry
            {
                kind = kind,
                path = path,
                size = identity.size,
                sha256 = identity.sha256
            };
        }

        private static HybridCLRDirectoryIdentity CreateEmptyDirectoryIdentity()
        {
            return new HybridCLRDirectoryIdentity
            {
                kind = EmptyDirectoryKind,
                transactionId = string.Empty,
                fileCount = 0,
                totalSize = 0,
                manifestSize = 0,
                manifestSha256 = string.Empty,
                treeSha256 = ComputeSha256(Array.Empty<byte>())
            };
        }

        private static string ComputeTreeSha256(
            HybridCLRFileIdentity manifestIdentity,
            IEnumerable<OwnershipFileEntry> entries)
        {
            var builder = new StringBuilder(1024);
            AppendCanonical(builder, ManifestFileName);
            AppendCanonical(builder, manifestIdentity.size.ToString(CultureInfo.InvariantCulture));
            AppendCanonical(builder, manifestIdentity.sha256);
            foreach (OwnershipFileEntry entry in entries.OrderBy(value => value.path, StringComparer.Ordinal))
            {
                AppendCanonical(builder, entry.kind);
                AppendCanonical(builder, entry.path);
                AppendCanonical(builder, entry.size.ToString(CultureInfo.InvariantCulture));
                AppendCanonical(builder, entry.sha256);
            }

            return ComputeSha256(Encoding.UTF8.GetBytes(builder.ToString()));
        }

        private static void AppendCanonical(StringBuilder builder, string value)
        {
            builder.Append(value.Length.ToString(CultureInfo.InvariantCulture));
            builder.Append(':');
            builder.Append(value);
        }

        private static string ComputeFileSha256(string path)
        {
            using (FileStream stream = new FileStream(
                       path,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read,
                       64 * 1024,
                       FileOptions.SequentialScan))
            using (SHA256 sha256 = SHA256.Create())
            {
                return ToHex(sha256.ComputeHash(stream));
            }
        }

        private static string ComputeSha256(byte[] bytes)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                return ToHex(sha256.ComputeHash(bytes));
            }
        }

        private static string ToHex(IEnumerable<byte> bytes)
        {
            var builder = new StringBuilder(64);
            foreach (byte value in bytes)
            {
                builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }

        private static void PreserveOrCreateMeta(
            string existingDirectory,
            string metaName,
            string stagedMetaPath,
            bool folderAsset)
        {
            string existingMeta = string.IsNullOrWhiteSpace(existingDirectory)
                ? null
                : Path.Combine(existingDirectory, metaName);
            if (!string.IsNullOrEmpty(existingMeta) && File.Exists(existingMeta))
            {
                HybridCLRFileIdentity identity = CaptureRequiredFileIdentity(
                    existingMeta,
                    $"existing Unity meta '{metaName}'");
                CopyFileAndVerify(existingMeta, stagedMetaPath, identity, $"Unity meta '{metaName}'");
                return;
            }

            WriteGeneratedMeta(stagedMetaPath, folderAsset);
        }

        private static string CreateMetaContent(string guid, bool folderAsset)
        {
            var builder = new StringBuilder(192);
            builder.AppendLine("fileFormatVersion: 2");
            builder.Append("guid: ").AppendLine(guid);
            if (folderAsset)
            {
                builder.AppendLine("folderAsset: yes");
            }

            builder.AppendLine("DefaultImporter:");
            builder.AppendLine("  externalObjects: {}");
            builder.AppendLine("  userData:");
            builder.AppendLine("  assetBundleName:");
            builder.AppendLine("  assetBundleVariant:");
            return builder.ToString();
        }

        internal static void WriteFileDurably(string path, byte[] bytes)
        {
            BuildPathPolicy.EnsureLegacyWindowsPathBudget(
                path,
                "HybridCLR durable artifact");
            string parent = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(parent))
            {
                throw new InvalidOperationException($"HybridCLR file has no parent: '{path}'.");
            }

            BuildPathPolicy.EnsureLegacyWindowsDirectoryPathBudget(
                parent,
                "HybridCLR durable artifact directory");
            Directory.CreateDirectory(parent);
            using (var stream = new FileStream(
                       path,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
        }

        private static void EnsureOwnedEmptyDirectory(string directory)
        {
            if (File.Exists(directory) || !Directory.Exists(directory))
            {
                throw new InvalidOperationException(
                    $"HybridCLR staging directory is unavailable: '{directory}'.");
            }

            EnsureDirectoryIsNotRedirected(directory);
            if (Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.TopDirectoryOnly).Any())
            {
                // Generated artifacts are written before CompleteStaging. Only safe flat files are accepted here.
                int count = 0;
                foreach (string entry in Directory.EnumerateFileSystemEntries(
                             directory,
                             "*",
                             SearchOption.TopDirectoryOnly))
                {
                    count++;
                    if (count > MaximumArtifactCount)
                    {
                        throw new InvalidOperationException(
                            $"HybridCLR staging exceeds the {MaximumArtifactCount}-entry pre-manifest budget: '{directory}'.");
                    }

                    FileAttributes attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.Directory) != 0
                        || (attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new InvalidOperationException(
                            $"HybridCLR staging contains an unsafe entry: '{entry}'.");
                    }

                    ValidateManagedFileName(Path.GetFileName(entry), allowMeta: false);
                }
            }
        }

        private static void EnsureDirectoryIsNotRedirected(string directory)
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    $"HybridCLR directory cannot be a symbolic link or reparse point: '{directory}'.");
            }
        }

        private static bool HasUtf8Bom(byte[] bytes)
        {
            return bytes.Length >= 3
                && bytes[0] == 0xEF
                && bytes[1] == 0xBB
                && bytes[2] == 0xBF;
        }

        private static bool IsReservedWindowsDeviceName(string fileName)
        {
            string stem = fileName.Split('.')[0];
            if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase)
                || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase)
                || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase)
                || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (stem.Length == 4
                && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                    || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)))
            {
                char suffix = stem[3];
                return suffix >= '1' && suffix <= '9';
            }

            return false;
        }
    }
}
