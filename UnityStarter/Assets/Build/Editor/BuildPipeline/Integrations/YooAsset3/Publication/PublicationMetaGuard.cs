using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using static Build.Pipeline.Integrations.YooAsset3.Publication.PublicationConstants;
namespace Build.Pipeline.Integrations.YooAsset3.Publication
{
    internal static class PublicationMetaGuard
    {
        internal static void ValidatePreRefreshSiblingMeta(
            string projectRoot,
            PublicationJournalOperation operation,
            IJournalSerializer serializer,
            bool allowMissingOriginalMeta)
        {
            if (!operation.managesSiblingMeta)
            {
                return;
            }

            MetaFileSnapshot actual = CaptureMetaFile(projectRoot, operation.targetMeta);
            if (operation.originalMetaExisted && !actual.Exists && allowMissingOriginalMeta)
            {
                return;
            }


            if (!operation.originalMetaExisted && operation.installedMetaExisted)
            {
                ValidateMetaSnapshot(
                    actual,
                    operation.targetMeta,
                    true,
                    operation.installedMetaLength,
                    operation.installedMetaSha256,
                    "activated bundled publication meta",
                    serializer);
                return;
            }

            ValidateMetaSnapshot(
                actual,
                operation.targetMeta,
                operation.originalMetaExisted,
                operation.originalMetaLength,
                operation.originalMetaSha256,
                "pre-refresh bundled publication meta",
                serializer);
        }


        internal static void CaptureInstalledSiblingMetas(
            PublicationJournal recovered,
            IJournalSerializer serializer,
            IReadOnlyDictionary<PublicationJournalOperation, MetaFileSnapshot> recoveryCandidates)
        {
            foreach (PublicationJournalOperation operation in recovered.operations)
            {
                if (!operation.managesSiblingMeta)
                {
                    continue;
                }

                MetaFileSnapshot installed = CaptureMetaFile(recovered.projectRoot, operation.targetMeta);
                if (!installed.Exists)
                {
                    throw new InvalidOperationException(
                        $"AssetDatabase refresh did not create or preserve the bundled publication meta: '{operation.targetMeta}'.");
                }

                if (operation.originalMetaExisted &&
                    (installed.Length != operation.originalMetaLength ||
                     !string.Equals(installed.Sha256, operation.originalMetaSha256, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidOperationException(
                        $"AssetDatabase refresh changed the preserved bundled publication meta identity: '{operation.targetMeta}'.");
                }

                if (recoveryCandidates != null &&
                    recoveryCandidates.TryGetValue(operation, out MetaFileSnapshot candidate) &&
                    (installed.Length != candidate.Length ||
                     !string.Equals(installed.Sha256, candidate.Sha256, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidOperationException(
                        $"AssetDatabase refresh changed a bundled publication meta discovered during recovery: " +
                        $"'{operation.targetMeta}'.");
                }

                operation.installedMetaExisted = true;
                operation.installedMetaLength = installed.Length;
                operation.installedMetaSha256 = installed.Sha256;
            }
        }


        internal static void ValidateInstalledSiblingMeta(
            PublicationJournal recovered,
            IJournalSerializer serializer,
            PublicationJournalOperation operation)
        {
            if (!operation.managesSiblingMeta)
            {
                return;
            }

            ValidateMetaFile(
                recovered.projectRoot,
                operation.targetMeta,
                operation.installedMetaExisted,
                operation.installedMetaLength,
                operation.installedMetaSha256,
                "installed bundled publication meta", serializer);
        }


        internal static void RestoreOriginalSiblingMeta(
            PublicationJournal recovered,
            IJournalSerializer serializer,
            PublicationJournalOperation operation)
        {
            if (!operation.managesSiblingMeta)
            {
                return;
            }

            ValidateMetaFile(
                recovered.projectRoot,
                operation.protectedMeta,
                operation.originalMetaExisted,
                operation.originalMetaLength,
                operation.originalMetaSha256,
                "protected bundled publication meta", serializer);
            MetaFileSnapshot targetMeta = CaptureMetaFile(recovered.projectRoot, operation.targetMeta);
            if (!operation.originalMetaExisted && targetMeta.Exists)
            {
                if (!operation.installedMetaExisted)
                {
                    throw new InvalidOperationException(
                        $"Bundled publication meta appeared without a durable installed identity: '{operation.targetMeta}'.");
                }

                ValidateMetaSnapshot(
                    targetMeta,
                    operation.targetMeta,
                    true,
                    operation.installedMetaLength,
                    operation.installedMetaSha256,
                    "activated bundled publication meta before rollback",
                    serializer);
                PublicationSafety.DeleteOwnedFile(
                    recovered.projectRoot,
                    operation.approvedRoot,
                    operation.targetMeta);
            }
            else if (targetMeta.Exists)
            {
                ValidateMetaSnapshot(
                    targetMeta,
                    operation.targetMeta,
                    operation.originalMetaExisted,
                    operation.originalMetaLength,
                    operation.originalMetaSha256,
                    "restored bundled publication meta",
                    serializer);
            }
            else if (operation.originalMetaExisted)
            {
                CopyMetaFileDurably(operation.protectedMeta, operation.targetMeta);
                ValidateMetaFile(
                    recovered.projectRoot,
                    operation.targetMeta,
                    true,
                    operation.originalMetaLength,
                    operation.originalMetaSha256,
                    "restored bundled publication meta",
                    serializer);
            }

            DeleteProtectedSiblingMeta(recovered, serializer, operation);
        }


        internal static void DeleteProtectedSiblingMeta(
            PublicationJournal recovered,
            IJournalSerializer serializer,
            PublicationJournalOperation operation)
        {
            if (!operation.managesSiblingMeta)
            {
                return;
            }

            ValidateMetaFile(
                recovered.projectRoot,
                operation.protectedMeta,
                operation.originalMetaExisted,
                operation.originalMetaLength,
                operation.originalMetaSha256,
                "protected bundled publication meta", serializer);
            if (operation.originalMetaExisted)
            {
                PublicationSafety.DeleteOwnedFile(
                    recovered.projectRoot,
                    operation.approvedRoot,
                    operation.protectedMeta);
            }
        }


        internal static void DeleteProtectedSiblingMetaIfPresent(
            PublicationJournal recovered,
            IJournalSerializer serializer,
            PublicationJournalOperation operation)
        {
            if (!operation.managesSiblingMeta || !File.Exists(operation.protectedMeta))
            {
                if (operation.managesSiblingMeta && Directory.Exists(operation.protectedMeta))
                {
                    throw new InvalidOperationException(
                        $"Protected bundled publication meta became a directory: '{operation.protectedMeta}'.");
                }

                return;
            }

            ValidateMetaFile(
                recovered.projectRoot,
                operation.protectedMeta,
                true,
                operation.originalMetaLength,
                operation.originalMetaSha256,
                "protected bundled publication meta", serializer);
            PublicationSafety.DeleteOwnedFile(
                recovered.projectRoot,
                operation.approvedRoot,
                operation.protectedMeta);
        }


        internal static MetaFileSnapshot CaptureMetaFile(string projectRoot, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new InvalidOperationException("Bundled publication meta path is missing.");
            }

            PublicationSafety.ValidateNoPathRedirection(projectRoot, path);
            if (Directory.Exists(path))
            {
                throw new InvalidOperationException($"Bundled publication meta path became a directory: '{path}'.");
            }

            if (!File.Exists(path))
            {
                return MetaFileSnapshot.Missing;
            }

            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException($"Bundled publication meta is a reparse point: '{path}'.");
            }

            var before = new FileInfo(path);
            long length = before.Length;
            DateTime lastWriteUtc = before.LastWriteTimeUtc;
            if (length < 0 || length > MaximumSiblingMetaBytes)
            {
                throw new InvalidOperationException(
                    $"Bundled publication meta exceeds the {MaximumSiblingMetaBytes}-byte safety limit: '{path}'.");
            }

            byte[] content = new byte[(int)length];
            using (var stream = new FileStream(
                       path,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read,
                       4096,
                       FileOptions.SequentialScan))
            {
                int offset = 0;
                while (offset < content.Length)
                {
                    int read = stream.Read(content, offset, content.Length - offset);
                    if (read <= 0)
                    {
                        throw new EndOfStreamException($"Bundled publication meta ended while reading: '{path}'.");
                    }

                    offset += read;
                }

                if (stream.ReadByte() >= 0)
                {
                    throw new InvalidOperationException($"Bundled publication meta grew while reading: '{path}'.");
                }
            }

            ValidateUnityFolderMeta(content, path);
            string sha256;
            using (SHA256 hash = SHA256.Create())
            {
                sha256 = BitConverter.ToString(hash.ComputeHash(content)).Replace("-", string.Empty);
            }

            var after = new FileInfo(path);
            if (!after.Exists || after.Length != length || after.LastWriteTimeUtc != lastWriteUtc)
            {
                throw new InvalidOperationException($"Bundled publication meta changed while hashing: '{path}'.");
            }

            return new MetaFileSnapshot(true, length, sha256);
        }


        internal static void ValidateUnityFolderMeta(byte[] content, string path)
        {
            string text;
            try
            {
                text = new UTF8Encoding(false, true).GetString(content);
            }
            catch (DecoderFallbackException exception)
            {
                throw new InvalidOperationException(
                    $"Bundled publication meta is not valid UTF-8 text: '{path}'.",
                    exception);
            }

            bool hasFolderAsset = false;
            bool hasGuid = false;
            using (var reader = new StringReader(text))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    string trimmed = line.Trim();
                    if (string.Equals(trimmed, "folderAsset: yes", StringComparison.Ordinal))
                    {
                        hasFolderAsset = true;
                    }
                    else if (trimmed.StartsWith("guid:", StringComparison.Ordinal))
                    {
                        string guid = trimmed.Substring("guid:".Length).Trim();
                        if (hasGuid || !PublicationJournalFormat.IsHexToken(guid, 32))
                        {
                            throw new InvalidOperationException(
                                $"Bundled publication meta contains an invalid or duplicate GUID: '{path}'.");
                        }

                        hasGuid = true;
                    }
                }
            }

            if (!hasFolderAsset || !hasGuid)
            {
                throw new InvalidOperationException(
                    $"Bundled publication meta is not a Unity folder meta file: '{path}'.");
            }
        }


        internal static void ValidateMetaFile(
            string projectRoot,
            string path,
            bool expectedExists,
            long expectedLength,
            string expectedSha256,
            string description,
            IJournalSerializer serializer)
        {
            ValidateMetaSnapshot(
                CaptureMetaFile(projectRoot, path),
                path,
                expectedExists,
                expectedLength,
                expectedSha256,
                description,
                serializer);
        }


        internal static void ValidateMetaSnapshot(
            MetaFileSnapshot actual,
            string path,
            bool expectedExists,
            long expectedLength,
            string expectedSha256,
            string description,
            IJournalSerializer serializer)
        {
            if (actual.Exists != expectedExists ||
                actual.Exists &&
                (actual.Length != expectedLength ||
                 !string.Equals(actual.Sha256, expectedSha256, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"The {description} identity changed: '{path}'.");
            }
        }


        internal static void CopyMetaFileDurably(string source, string destination)
        {
            using (var input = new FileStream(
                       source,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read,
                       4096,
                       FileOptions.SequentialScan))
            using (var output = new FileStream(
                       destination,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            {
                input.CopyTo(output);
                output.Flush(true);
            }
        }


        


    }
}
