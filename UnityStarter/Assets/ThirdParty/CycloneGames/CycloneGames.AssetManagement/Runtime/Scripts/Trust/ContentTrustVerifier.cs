using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using CycloneGames.Hash.Core;
using CycloneGames.IO.Runtime;

namespace CycloneGames.AssetManagement.Runtime.Trust
{
    /// <summary>
    /// Provider-neutral content verifier for downloaded bundles, raw files, and provider manifests.
    /// Designed for update/download boundaries rather than gameplay hot paths.
    /// </summary>
    public sealed class ContentTrustVerifier : IContentTrustBufferVerifier
    {
        public static readonly ContentTrustVerifier Shared = new ContentTrustVerifier();

        private const int SHA256_HEX_LENGTH = 64;
        private const int XXHASH64_HEX_LENGTH = 16;

        public ContentTrustVerificationResult VerifyBytes(byte[] bytes, in ContentTrustFileEntry entry)
        {
            if (bytes == null)
            {
                return ContentTrustVerificationResult.Failed(ContentTrustFailure.MissingFile, entry.Location);
            }

            return VerifyBytes(bytes.AsSpan(), in entry);
        }

        public ContentTrustVerificationResult VerifyBytes(ReadOnlySpan<byte> bytes, in ContentTrustFileEntry entry)
        {
            ContentTrustVerificationResult validation = ValidateEntry(in entry);
            if (!validation.Succeeded)
            {
                return validation;
            }

            if (entry.SizeBytes != bytes.Length)
            {
                return ContentTrustVerificationResult.Failed(
                    ContentTrustFailure.SizeMismatch,
                    entry.Location,
                    entry.SizeBytes.ToString(),
                    bytes.Length.ToString());
            }

            return VerifyHash(bytes, in entry);
        }

        public ContentTrustVerificationResult VerifyFile(string rootDirectory, in ContentTrustFileEntry entry)
        {
            ContentTrustVerificationResult validation = ValidateEntry(in entry);
            if (!validation.Succeeded)
            {
                return validation;
            }

            if (!TryResolveEntryPath(rootDirectory, entry.Location, out string filePath, out ContentTrustVerificationResult pathFailure))
            {
                return pathFailure;
            }

            try
            {
                if (!File.Exists(filePath))
                {
                    return ContentTrustVerificationResult.Failed(ContentTrustFailure.MissingFile, entry.Location);
                }

                long actualSize = new FileInfo(filePath).Length;
                if (entry.SizeBytes != actualSize)
                {
                    return ContentTrustVerificationResult.Failed(
                        ContentTrustFailure.SizeMismatch,
                        entry.Location,
                        entry.SizeBytes.ToString(),
                        actualSize.ToString());
                }

                return VerifyFileHash(filePath, in entry);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException || ex is ArgumentException)
            {
                return ContentTrustVerificationResult.Failed(ContentTrustFailure.IoError, entry.Location, message: ex.Message);
            }
        }

        public int VerifyManifestFiles(
            string rootDirectory,
            in ContentTrustManifest manifest,
            List<ContentTrustVerificationResult> failures = null,
            IContentTrustSignatureVerifier signatureVerifier = null)
        {
            failures?.Clear();

            if (manifest.Entries == null)
            {
                AddFailure(failures, ContentTrustVerificationResult.Failed(ContentTrustFailure.InvalidManifest, null, message: "Manifest entries are null."));
                return 1;
            }

            if (signatureVerifier != null && !signatureVerifier.Verify(in manifest, out string signatureError))
            {
                AddFailure(failures, ContentTrustVerificationResult.Failed(ContentTrustFailure.SignatureRejected, null, message: signatureError));
                return 1;
            }

            if (!TryResolveManifestRoot(rootDirectory, manifest.ContentRoot, out string contentRoot, out ContentTrustVerificationResult rootFailure))
            {
                AddFailure(failures, rootFailure);
                return 1;
            }

            int failureCount = 0;
            for (int i = 0; i < manifest.Entries.Count; i++)
            {
                ContentTrustVerificationResult result = VerifyFile(contentRoot, manifest.Entries[i]);
                if (result.Succeeded)
                {
                    continue;
                }

                failureCount++;
                AddFailure(failures, result);
            }

            return failureCount;
        }

        private static ContentTrustVerificationResult ValidateEntry(in ContentTrustFileEntry entry)
        {
            if (string.IsNullOrEmpty(entry.Location))
            {
                return ContentTrustVerificationResult.Failed(ContentTrustFailure.InvalidEntry, entry.Location, message: "Location is null or empty.");
            }

            if (entry.SizeBytes < 0L)
            {
                return ContentTrustVerificationResult.Failed(ContentTrustFailure.InvalidEntry, entry.Location, message: "SizeBytes cannot be negative.");
            }

            int expectedHashLength = GetExpectedHashHexLength(entry.HashAlgorithm);
            if (expectedHashLength < 0)
            {
                return ContentTrustVerificationResult.Failed(ContentTrustFailure.UnsupportedHashAlgorithm, entry.Location);
            }

            if (entry.HashAlgorithm != ContentTrustHashAlgorithm.None &&
                !IsExpectedHashValid(entry.ExpectedHashHex, expectedHashLength))
            {
                return ContentTrustVerificationResult.Failed(ContentTrustFailure.InvalidExpectedHash, entry.Location, expectedHashLength.ToString(), entry.ExpectedHashHex);
            }

            return ContentTrustVerificationResult.Passed(entry.Location);
        }

        private static bool TryResolveManifestRoot(
            string rootDirectory,
            string manifestContentRoot,
            out string contentRoot,
            out ContentTrustVerificationResult failure)
        {
            contentRoot = null;
            failure = default;

            if (string.IsNullOrEmpty(rootDirectory))
            {
                failure = ContentTrustVerificationResult.Failed(ContentTrustFailure.InvalidManifest, null, message: "Root directory is null or empty.");
                return false;
            }

            try
            {
                string candidateRoot = string.IsNullOrEmpty(manifestContentRoot)
                    ? rootDirectory
                    : Path.Combine(rootDirectory, manifestContentRoot);
                contentRoot = FilePathSecurity.EnsureWithinRoot(rootDirectory, candidateRoot);
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is UnauthorizedAccessException || ex is NotSupportedException)
            {
                failure = ContentTrustVerificationResult.Failed(ContentTrustFailure.PathEscapesRoot, manifestContentRoot, message: ex.Message);
                return false;
            }
        }

        private static bool TryResolveEntryPath(
            string rootDirectory,
            string entryLocation,
            out string filePath,
            out ContentTrustVerificationResult failure)
        {
            filePath = null;
            failure = default;

            if (string.IsNullOrEmpty(rootDirectory))
            {
                failure = ContentTrustVerificationResult.Failed(ContentTrustFailure.InvalidManifest, entryLocation, message: "Root directory is null or empty.");
                return false;
            }

            try
            {
                string candidatePath = Path.Combine(rootDirectory, entryLocation);
                filePath = FilePathSecurity.EnsureWithinRoot(rootDirectory, candidatePath);
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is UnauthorizedAccessException || ex is NotSupportedException)
            {
                failure = ContentTrustVerificationResult.Failed(ContentTrustFailure.PathEscapesRoot, entryLocation, message: ex.Message);
                return false;
            }
        }

        private static ContentTrustVerificationResult VerifyFileHash(string filePath, in ContentTrustFileEntry entry)
        {
            if (entry.HashAlgorithm == ContentTrustHashAlgorithm.None)
            {
                return ContentTrustVerificationResult.Passed(entry.Location);
            }

            int hashSize = GetHashSizeInBytes(entry.HashAlgorithm);
            Span<byte> hashBuffer = stackalloc byte[hashSize];
            if (!FileUtility.ComputeFileHash(filePath, ToFileUtilityAlgorithm(entry.HashAlgorithm), hashBuffer))
            {
                return ContentTrustVerificationResult.Failed(ContentTrustFailure.HashComputationFailed, entry.Location);
            }

            if (!MatchesExpectedHashHex(hashBuffer, entry.ExpectedHashHex))
            {
                string actual = FileUtility.ToHexString(hashBuffer);
                return ContentTrustVerificationResult.Failed(ContentTrustFailure.HashMismatch, entry.Location, entry.ExpectedHashHex, actual);
            }

            return ContentTrustVerificationResult.Passed(entry.Location);
        }

        private static ContentTrustVerificationResult VerifyHash(ReadOnlySpan<byte> bytes, in ContentTrustFileEntry entry)
        {
            if (entry.HashAlgorithm == ContentTrustHashAlgorithm.None)
            {
                return ContentTrustVerificationResult.Passed(entry.Location);
            }

            int hashSize = GetHashSizeInBytes(entry.HashAlgorithm);
            Span<byte> hashBuffer = stackalloc byte[hashSize];
            if (entry.HashAlgorithm == ContentTrustHashAlgorithm.XxHash64)
            {
                XxHash64 hasher = XxHash64.Create();
                hasher.Append(bytes);
                if (!hasher.TryWriteHash(hashBuffer))
                {
                    return ContentTrustVerificationResult.Failed(ContentTrustFailure.HashComputationFailed, entry.Location);
                }
            }
            else
            {
                using (var incrementalHasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                {
                    incrementalHasher.AppendData(bytes);
                    if (!incrementalHasher.TryGetHashAndReset(hashBuffer, out int bytesWritten) || bytesWritten != hashSize)
                    {
                        return ContentTrustVerificationResult.Failed(ContentTrustFailure.HashComputationFailed, entry.Location);
                    }
                }
            }

            if (!MatchesExpectedHashHex(hashBuffer, entry.ExpectedHashHex))
            {
                string actual = FileUtility.ToHexString(hashBuffer);
                return ContentTrustVerificationResult.Failed(ContentTrustFailure.HashMismatch, entry.Location, entry.ExpectedHashHex, actual);
            }

            return ContentTrustVerificationResult.Passed(entry.Location);
        }

        private static bool MatchesExpectedHashHex(ReadOnlySpan<byte> hashBytes, string expectedHashHex)
        {
            if (string.IsNullOrEmpty(expectedHashHex) || expectedHashHex.Length != hashBytes.Length * 2)
            {
                return false;
            }

            for (int i = 0; i < hashBytes.Length; i++)
            {
                byte value = hashBytes[i];
                int high = FromHex(expectedHashHex[i * 2]);
                int low = FromHex(expectedHashHex[(i * 2) + 1]);
                if (high < 0 || low < 0 || value != (byte)((high << 4) | low))
                {
                    return false;
                }
            }

            return true;
        }

        private static int FromHex(char value)
        {
            if (value >= '0' && value <= '9')
            {
                return value - '0';
            }

            if (value >= 'a' && value <= 'f')
            {
                return value - 'a' + 10;
            }

            if (value >= 'A' && value <= 'F')
            {
                return value - 'A' + 10;
            }

            return -1;
        }

        private static bool IsExpectedHashValid(string hex, int expectedLength)
        {
            if (string.IsNullOrEmpty(hex) || hex.Length != expectedLength)
            {
                return false;
            }

            for (int i = 0; i < hex.Length; i++)
            {
                char c = hex[i];
                bool isHex = (c >= '0' && c <= '9') ||
                             (c >= 'a' && c <= 'f') ||
                             (c >= 'A' && c <= 'F');
                if (!isHex)
                {
                    return false;
                }
            }

            return true;
        }

        private static int GetExpectedHashHexLength(ContentTrustHashAlgorithm algorithm)
        {
            switch (algorithm)
            {
                case ContentTrustHashAlgorithm.None:
                    return 0;
                case ContentTrustHashAlgorithm.Sha256:
                    return SHA256_HEX_LENGTH;
                case ContentTrustHashAlgorithm.XxHash64:
                    return XXHASH64_HEX_LENGTH;
                default:
                    return -1;
            }
        }

        private static int GetHashSizeInBytes(ContentTrustHashAlgorithm algorithm)
        {
            return GetExpectedHashHexLength(algorithm) / 2;
        }

        private static HashAlgorithmType ToFileUtilityAlgorithm(ContentTrustHashAlgorithm algorithm)
        {
            return algorithm == ContentTrustHashAlgorithm.XxHash64
                ? HashAlgorithmType.XxHash64
                : HashAlgorithmType.SHA256;
        }

        private static void AddFailure(List<ContentTrustVerificationResult> failures, ContentTrustVerificationResult failure)
        {
            failures?.Add(failure);
        }
    }
}
