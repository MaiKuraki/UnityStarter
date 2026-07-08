using System;
using System.Collections.Generic;
using System.IO;

using CycloneGames.IO.Runtime;

namespace CycloneGames.AssetManagement.Runtime.Trust
{
    public sealed class ContentTrustManifestBuilder
    {
        private readonly List<ContentTrustFileEntry> _entries = new List<ContentTrustFileEntry>(128);

        private string _version;
        private string _minimumClientVersion;
        private string _rollbackVersion;
        private string _contentRoot;
        private string _signature;

        public ContentTrustManifestBuilder WithVersion(string version)
        {
            _version = version;
            return this;
        }

        public ContentTrustManifestBuilder WithMinimumClientVersion(string minimumClientVersion)
        {
            _minimumClientVersion = minimumClientVersion;
            return this;
        }

        public ContentTrustManifestBuilder WithRollbackVersion(string rollbackVersion)
        {
            _rollbackVersion = rollbackVersion;
            return this;
        }

        public ContentTrustManifestBuilder WithContentRoot(string contentRoot)
        {
            _contentRoot = NormalizeOptionalLocation(contentRoot);
            return this;
        }

        public ContentTrustManifestBuilder WithSignature(string signature)
        {
            _signature = signature;
            return this;
        }

        public ContentTrustManifestBuilder AddEntry(ContentTrustFileEntry entry)
        {
            _entries.Add(NormalizeEntry(entry));
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

            string normalizedLocation = NormalizeRequiredLocation(relativeLocation);
            string fullPath = FilePathSecurity.EnsureWithinRoot(rootDirectory, Path.Combine(rootDirectory, normalizedLocation));
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException("Manifest file entry does not exist.", fullPath);
            }

            string expectedHashHex = null;
            if (hashAlgorithm != ContentTrustHashAlgorithm.None)
            {
                expectedHashHex = FileUtility.ComputeFileHashToHexString(fullPath, ToFileUtilityAlgorithm(hashAlgorithm));
                if (string.IsNullOrEmpty(expectedHashHex))
                {
                    throw new IOException($"Failed to compute hash for manifest file entry: {normalizedLocation}");
                }
            }

            var fileInfo = new FileInfo(fullPath);
            return AddEntry(new ContentTrustFileEntry(
                normalizedLocation,
                fileInfo.Length,
                hashAlgorithm,
                expectedHashHex));
        }

        public ContentTrustManifest Build(bool sortEntries = true)
        {
            if (string.IsNullOrWhiteSpace(_version))
            {
                throw new InvalidOperationException("Content trust manifest version cannot be null or empty.");
            }

            var entries = new List<ContentTrustFileEntry>(_entries.Count);
            for (int i = 0; i < _entries.Count; i++)
            {
                entries.Add(NormalizeEntry(_entries[i]));
            }

            if (sortEntries)
            {
                entries.Sort(CompareEntries);
            }

            ValidateUniqueLocations(entries);
            return new ContentTrustManifest(
                _version,
                entries,
                _minimumClientVersion,
                _rollbackVersion,
                _contentRoot,
                _signature);
        }

        public void ClearEntries()
        {
            _entries.Clear();
        }

        private static ContentTrustFileEntry NormalizeEntry(in ContentTrustFileEntry entry)
        {
            return new ContentTrustFileEntry(
                NormalizeRequiredLocation(entry.Location),
                entry.SizeBytes,
                entry.HashAlgorithm,
                entry.ExpectedHashHex);
        }

        private static string NormalizeRequiredLocation(string location)
        {
            string normalized = NormalizeOptionalLocation(location);
            if (string.IsNullOrEmpty(normalized))
            {
                throw new ArgumentException("Manifest entry location cannot be null or empty.", nameof(location));
            }

            if (Path.IsPathRooted(normalized))
            {
                throw new ArgumentException("Manifest entry location must be relative.", nameof(location));
            }

            return normalized;
        }

        private static string NormalizeOptionalLocation(string location)
        {
            if (string.IsNullOrWhiteSpace(location))
            {
                return null;
            }

            return location.Trim().Replace('\\', '/');
        }

        private static void ValidateUniqueLocations(List<ContentTrustFileEntry> entries)
        {
            for (int i = 1; i < entries.Count; i++)
            {
                if (string.Equals(entries[i - 1].Location, entries[i].Location, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Duplicate manifest entry location: {entries[i].Location}");
                }
            }
        }

        private static int CompareEntries(ContentTrustFileEntry x, ContentTrustFileEntry y)
        {
            int location = string.CompareOrdinal(x.Location, y.Location);
            if (location != 0)
            {
                return location;
            }

            int size = x.SizeBytes.CompareTo(y.SizeBytes);
            if (size != 0)
            {
                return size;
            }

            int algorithm = x.HashAlgorithm.CompareTo(y.HashAlgorithm);
            return algorithm != 0 ? algorithm : string.CompareOrdinal(x.ExpectedHashHex, y.ExpectedHashHex);
        }

        private static HashAlgorithmType ToFileUtilityAlgorithm(ContentTrustHashAlgorithm algorithm)
        {
            switch (algorithm)
            {
                case ContentTrustHashAlgorithm.Sha256:
                    return HashAlgorithmType.SHA256;
                case ContentTrustHashAlgorithm.XxHash64:
                    return HashAlgorithmType.XxHash64;
                default:
                    throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "Unsupported manifest file hash algorithm.");
            }
        }
    }
}
