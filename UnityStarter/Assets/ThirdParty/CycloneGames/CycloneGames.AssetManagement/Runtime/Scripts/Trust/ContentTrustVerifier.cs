using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

using Cysharp.Threading.Tasks;

using CycloneGames.IO;

namespace CycloneGames.AssetManagement.Runtime.Trust
{
    /// <summary>
    /// Provider-neutral content verifier for downloaded bundles, raw files, and provider manifests.
    /// Designed for update/download boundaries rather than gameplay hot paths.
    /// </summary>
    public sealed class ContentTrustVerifier : IContentTrustBufferVerifier
    {
        public static readonly ContentTrustVerifier Shared = new ContentTrustVerifier(ContentTrustPolicy.RequireSignature);
        public static readonly ContentTrustVerifier IntegrityOnly = new ContentTrustVerifier(ContentTrustPolicy.IntegrityOnly);

        private const int SHA256_HEX_LENGTH = 64;
        private const int XXHASH64_HEX_LENGTH = 16;

        public ContentTrustPolicy Policy { get; }

        public ContentTrustVerifier(ContentTrustPolicy policy = ContentTrustPolicy.RequireSignature)
        {
            if (policy != ContentTrustPolicy.RequireSignature && policy != ContentTrustPolicy.IntegrityOnly)
            {
                throw new ArgumentOutOfRangeException(nameof(policy), policy, "Unsupported content trust policy.");
            }

            Policy = policy;
        }

        public static ContentTrustVerifier ForPolicy(ContentTrustPolicy policy)
        {
            switch (policy)
            {
                case ContentTrustPolicy.RequireSignature:
                    return Shared;
                case ContentTrustPolicy.IntegrityOnly:
                    return IntegrityOnly;
                default:
                    throw new ArgumentOutOfRangeException(nameof(policy), policy, "Unsupported content trust policy.");
            }
        }

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

        public ContentTrustVerificationResult ValidateManifest(
            in ContentTrustManifest manifest,
            IContentTrustSignatureVerifier signatureVerifier = null)
        {
            if (manifest.Entries == null || string.IsNullOrEmpty(manifest.Version))
            {
                return ContentTrustVerificationResult.Failed(
                    ContentTrustFailure.InvalidManifest,
                    null,
                    message: "Manifest is not initialized.");
            }

            for (int i = 0; i < manifest.Entries.Count; i++)
            {
                ContentTrustFileEntry entry = manifest.Entries[i];
                ContentTrustVerificationResult validation = ValidateEntry(in entry);
                if (!validation.Succeeded)
                {
                    return validation;
                }
            }

            return Policy == ContentTrustPolicy.RequireSignature
                ? VerifyRequiredSignature(in manifest, signatureVerifier)
                : ContentTrustVerificationResult.Passed(null);
        }

        public int VerifyManifestFiles(
            string rootDirectory,
            in ContentTrustManifest manifest,
            List<ContentTrustVerificationResult> failures = null,
            IContentTrustSignatureVerifier signatureVerifier = null)
        {
            failures?.Clear();

            ContentTrustVerificationResult manifestValidation = ValidateManifest(
                in manifest,
                signatureVerifier);
            if (!manifestValidation.Succeeded)
            {
                AddFailure(failures, manifestValidation);
                return 1;
            }

            return VerifyValidatedManifestFiles(rootDirectory, in manifest, failures);
        }

        private int VerifyValidatedManifestFiles(
            string rootDirectory,
            in ContentTrustManifest manifest,
            List<ContentTrustVerificationResult> failures = null)
        {
            failures?.Clear();

            if (!TryResolveManifestRoot(rootDirectory, manifest.ContentRoot, out string contentRoot, out ContentTrustVerificationResult rootFailure))
            {
                AddFailure(failures, rootFailure);
                return 1;
            }

            FilePathSandbox contentSandbox;
            try
            {
                contentSandbox = new FilePathSandbox(contentRoot);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException)
            {
                AddFailure(
                    failures,
                    ContentTrustVerificationResult.Failed(
                        ContentTrustFailure.PathEscapesRoot,
                        manifest.ContentRoot,
                        message: ex.Message));
                return 1;
            }

            int failureCount = 0;
            for (int i = 0; i < manifest.Entries.Count; i++)
            {
                ContentTrustFileEntry entry = manifest.Entries[i];
                ContentTrustVerificationResult result = VerifyValidatedFile(contentSandbox, in entry);
                if (result.Succeeded)
                {
                    continue;
                }

                failureCount++;
                AddFailure(failures, result);
            }

            return failureCount;
        }

        public async UniTask<int> VerifyManifestFilesAsync(
            string rootDirectory,
            ContentTrustManifest manifest,
            List<ContentTrustVerificationResult> failures = null,
            IContentTrustSignatureVerifier signatureVerifier = null,
            CancellationToken cancellationToken = default)
        {
            failures?.Clear();
            cancellationToken.ThrowIfCancellationRequested();

            ContentTrustVerificationResult manifestValidation = ValidateManifest(
                in manifest,
                signatureVerifier);
            if (!manifestValidation.Succeeded)
            {
                AddFailure(failures, manifestValidation);
                return 1;
            }

            return await VerifyValidatedManifestFilesAsync(
                rootDirectory,
                manifest,
                failures,
                cancellationToken);
        }

        private async UniTask<int> VerifyValidatedManifestFilesAsync(
            string rootDirectory,
            ContentTrustManifest manifest,
            List<ContentTrustVerificationResult> failures,
            CancellationToken cancellationToken)
        {

            if (!TryResolveManifestRoot(
                    rootDirectory,
                    manifest.ContentRoot,
                    out string contentRoot,
                    out ContentTrustVerificationResult rootFailure))
            {
                AddFailure(failures, rootFailure);
                return 1;
            }

            FilePathSandbox contentSandbox;
            try
            {
                contentSandbox = new FilePathSandbox(contentRoot);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is IOException ||
                                       ex is UnauthorizedAccessException || ex is NotSupportedException)
            {
                AddFailure(
                    failures,
                    ContentTrustVerificationResult.Failed(
                        ContentTrustFailure.PathEscapesRoot,
                        manifest.ContentRoot,
                        message: ex.Message));
                return 1;
            }

            var hashBuffer = new byte[32];
            int failureCount = 0;
            for (int i = 0; i < manifest.Entries.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ContentTrustFileEntry entry = manifest.Entries[i];
                ContentTrustVerificationResult result = await VerifyValidatedFileAsync(
                    contentSandbox,
                    entry,
                    hashBuffer,
                    cancellationToken);
                if (!result.Succeeded)
                {
                    failureCount++;
                    AddFailure(failures, result);
                }

#if UNITY_WEBGL && !UNITY_EDITOR
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
#endif
            }

            return failureCount;
        }

        private static async UniTask<ContentTrustVerificationResult> VerifyValidatedFileAsync(
            FilePathSandbox sandbox,
            ContentTrustFileEntry entry,
            byte[] hashBuffer,
            CancellationToken cancellationToken)
        {
            string filePath;
            try
            {
                filePath = sandbox.Resolve(entry.Location);
                if (!File.Exists(filePath))
                {
                    return ContentTrustVerificationResult.Failed(
                        ContentTrustFailure.MissingFile,
                        entry.Location);
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

                if (entry.HashAlgorithm == ContentTrustHashAlgorithm.None)
                {
                    return ContentTrustVerificationResult.Passed(entry.Location);
                }

                int hashSize = GetHashSizeInBytes(entry.HashAlgorithm);
                await FileHasher.WriteHashAsync(
                    filePath,
                    ToFileHashAlgorithm(entry.HashAlgorithm),
                    new Memory<byte>(hashBuffer, 0, hashSize),
                    cancellationToken: cancellationToken);
                if (!MatchesExpectedHashHex(
                        new ReadOnlySpan<byte>(hashBuffer, 0, hashSize),
                        entry.ExpectedHashHex))
                {
                    return ContentTrustVerificationResult.Failed(
                        ContentTrustFailure.HashMismatch,
                        entry.Location,
                        entry.ExpectedHashHex,
                        ContentHasher.ToHex(new ReadOnlySpan<byte>(hashBuffer, 0, hashSize)));
                }

                return ContentTrustVerificationResult.Passed(entry.Location);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException ||
                                       ex is NotSupportedException || ex is ArgumentException ||
                                       ex is System.Security.Cryptography.CryptographicException)
            {
                return ContentTrustVerificationResult.Failed(
                    ContentTrustFailure.IoError,
                    entry.Location,
                    message: ex.Message);
            }
        }

        private static ContentTrustVerificationResult VerifyValidatedFile(
            FilePathSandbox sandbox,
            in ContentTrustFileEntry entry)
        {
            string filePath;
            try
            {
                filePath = sandbox.Resolve(entry.Location);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException || ex is ArgumentException)
            {
                return ContentTrustVerificationResult.Failed(
                    ContentTrustFailure.PathEscapesRoot,
                    entry.Location,
                    message: ex.Message);
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
                return ContentTrustVerificationResult.Failed(
                    ContentTrustFailure.IoError,
                    entry.Location,
                    message: ex.Message);
            }
        }

        private ContentTrustVerificationResult ValidateEntry(in ContentTrustFileEntry entry)
        {
            if (string.IsNullOrEmpty(entry.Location))
            {
                return ContentTrustVerificationResult.Failed(ContentTrustFailure.InvalidEntry, entry.Location, message: "Location is null or empty.");
            }

            if (entry.SizeBytes < 0L)
            {
                return ContentTrustVerificationResult.Failed(ContentTrustFailure.InvalidEntry, entry.Location, message: "SizeBytes cannot be negative.");
            }

            if (entry.HashAlgorithm != ContentTrustHashAlgorithm.Sha256)
            {
                return ContentTrustVerificationResult.Failed(
                    ContentTrustFailure.HashAlgorithmRejected,
                    entry.Location,
                    ContentTrustHashAlgorithm.Sha256.ToString(),
                    entry.HashAlgorithm.ToString(),
                    "The selected content trust policy requires cryptographic SHA-256 hashes.");
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

        private static ContentTrustVerificationResult VerifyRequiredSignature(
            in ContentTrustManifest manifest,
            IContentTrustSignatureVerifier signatureVerifier)
        {
            if (signatureVerifier == null)
            {
                return ContentTrustVerificationResult.Failed(
                    ContentTrustFailure.SignatureRequired,
                    null,
                    message: "The RequireSignature content trust policy requires a signature verifier.");
            }

            if (string.IsNullOrWhiteSpace(manifest.Signature))
            {
                return ContentTrustVerificationResult.Failed(
                    ContentTrustFailure.SignatureRequired,
                    null,
                    message: "The RequireSignature content trust policy requires a manifest signature.");
            }

            try
            {
                if (!signatureVerifier.Verify(in manifest, out string signatureError))
                {
                    return ContentTrustVerificationResult.Failed(
                        ContentTrustFailure.SignatureRejected,
                        null,
                        message: string.IsNullOrEmpty(signatureError)
                            ? "The content trust manifest signature was rejected."
                            : signatureError);
                }

                return ContentTrustVerificationResult.Passed(null);
            }
            catch (Exception ex) when (AssetRuntimeGuard.IsRecoverableException(ex))
            {
                return ContentTrustVerificationResult.Failed(
                    ContentTrustFailure.SignatureRejected,
                    null,
                    message: $"The content trust manifest signature verifier failed: {ex.Message}");
            }
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
                contentRoot = new FilePathSandbox(rootDirectory).Resolve(
                    manifestContentRoot ?? string.Empty);
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException)
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
                filePath = new FilePathSandbox(rootDirectory).Resolve(entryLocation);
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException)
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
            FileHasher.WriteHash(filePath, ToFileHashAlgorithm(entry.HashAlgorithm), hashBuffer);

            if (!MatchesExpectedHashHex(hashBuffer, entry.ExpectedHashHex))
            {
                string actual = ContentHasher.ToHex(hashBuffer);
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
            ContentHasher.WriteHash(bytes, ToFileHashAlgorithm(entry.HashAlgorithm), hashBuffer);

            if (!MatchesExpectedHashHex(hashBuffer, entry.ExpectedHashHex))
            {
                string actual = ContentHasher.ToHex(hashBuffer);
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

        private static FileHashAlgorithm ToFileHashAlgorithm(ContentTrustHashAlgorithm algorithm)
        {
            return algorithm == ContentTrustHashAlgorithm.XxHash64
                ? FileHashAlgorithm.XxHash64
                : FileHashAlgorithm.Sha256;
        }

        private static void AddFailure(List<ContentTrustVerificationResult> failures, ContentTrustVerificationResult failure)
        {
            failures?.Add(failure);
        }
    }
}
