using System;
using System.Collections.Generic;
using System.IO;

using CycloneGames.IO;

namespace CycloneGames.AssetManagement.Runtime.Trust
{
    public sealed class ContentTrustManifestBuilder
    {
        private readonly List<ContentTrustFileEntry> _entries = new List<ContentTrustFileEntry>(128);

        private string _version;
        private string _contentRoot;
        private string _signature;
        private long _totalContentSizeBytes;

        public ContentTrustManifestBuilder WithVersion(string version)
        {
            _version = version;
            return this;
        }

        public ContentTrustManifestBuilder WithContentRoot(string contentRoot)
        {
            _contentRoot = ContentTrustManifestValidation.NormalizeOptionalContentRoot(contentRoot);
            return this;
        }

        public ContentTrustManifestBuilder WithSignature(string signature)
        {
            _signature = signature;
            return this;
        }

        public ContentTrustManifestBuilder AddEntry(ContentTrustFileEntry entry)
        {
            if (_entries.Count >= ContentTrustManifestValidation.MAX_ENTRY_COUNT)
            {
                throw new InvalidOperationException(
                    $"Content trust manifest entry count cannot exceed {ContentTrustManifestValidation.MAX_ENTRY_COUNT}.");
            }

            ContentTrustFileEntry normalizedEntry = ContentTrustManifestValidation.NormalizeEntry(in entry);
            if (normalizedEntry.SizeBytes >
                ContentTrustManifestValidation.MAX_TOTAL_CONTENT_SIZE_BYTES - _totalContentSizeBytes)
            {
                throw new InvalidOperationException(
                    $"Content trust manifest total content size cannot exceed {ContentTrustManifestValidation.MAX_TOTAL_CONTENT_SIZE_BYTES} bytes.");
            }

            _entries.Add(normalizedEntry);
            _totalContentSizeBytes += normalizedEntry.SizeBytes;
            return this;
        }

        public ContentTrustManifestBuilder AddFile(
            string rootDirectory,
            string relativeLocation,
            ContentTrustHashAlgorithm hashAlgorithm = ContentTrustHashAlgorithm.Sha256)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory))
            {
                throw new ArgumentException("Manifest root directory cannot be null or empty.", nameof(rootDirectory));
            }

            string normalizedLocation = ContentTrustManifestValidation.NormalizeRequiredPath(
                relativeLocation,
                nameof(relativeLocation));
            string fullPath = new FilePathSandbox(rootDirectory).Resolve(normalizedLocation);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException("Manifest file entry does not exist.", fullPath);
            }

            string expectedHashHex = null;
            if (hashAlgorithm != ContentTrustHashAlgorithm.None)
            {
                expectedHashHex = FileHasher.ComputeHex(fullPath, ToFileHashAlgorithm(hashAlgorithm));
            }

            var fileInfo = new FileInfo(fullPath);
            return AddEntry(new ContentTrustFileEntry(
                normalizedLocation,
                fileInfo.Length,
                hashAlgorithm,
                expectedHashHex));
        }

        public ContentTrustManifest Build()
        {
            return new ContentTrustManifest(
                _version,
                _entries,
                _contentRoot,
                _signature);
        }

        public void ClearEntries()
        {
            _entries.Clear();
            _totalContentSizeBytes = 0L;
        }

        private static FileHashAlgorithm ToFileHashAlgorithm(ContentTrustHashAlgorithm algorithm)
        {
            switch (algorithm)
            {
                case ContentTrustHashAlgorithm.Sha256:
                    return FileHashAlgorithm.Sha256;
                case ContentTrustHashAlgorithm.XxHash64:
                    return FileHashAlgorithm.XxHash64;
                default:
                    throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "Unsupported manifest file hash algorithm.");
            }
        }
    }
}
