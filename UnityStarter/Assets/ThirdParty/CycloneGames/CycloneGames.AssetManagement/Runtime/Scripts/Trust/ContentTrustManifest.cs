using System.Collections.Generic;
using CycloneGames.Hash.Core;

namespace CycloneGames.AssetManagement.Runtime.Trust
{
    /// <summary>
    /// Provider-neutral content manifest metadata. The manifest can describe Addressables bundles,
    /// YooAsset bundles, raw files, or any future provider payload.
    /// </summary>
    public readonly struct ContentTrustManifest
    {
        public readonly string Version;
        public readonly string MinimumClientVersion;
        public readonly string RollbackVersion;
        public readonly string ContentRoot;
        public readonly IReadOnlyList<ContentTrustFileEntry> Entries;
        public readonly string Signature;

        public ContentTrustManifest(
            string version,
            IReadOnlyList<ContentTrustFileEntry> entries,
            string minimumClientVersion = null,
            string rollbackVersion = null,
            string contentRoot = null,
            string signature = null)
        {
            Version = version;
            Entries = entries;
            MinimumClientVersion = minimumClientVersion;
            RollbackVersion = rollbackVersion;
            ContentRoot = contentRoot;
            Signature = signature;
        }

        /// <summary>
        /// Computes a deterministic non-cryptographic fingerprint for cache invalidation and diagnostics.
        /// This is not a tamper-proof signature.
        /// </summary>
        public ulong ComputeFingerprint()
        {
            ulong hash = StableHash64.ComputeUtf16Ordinal(Version ?? string.Empty);
            hash = CombineString(hash, MinimumClientVersion);
            hash = CombineString(hash, RollbackVersion);
            hash = CombineString(hash, ContentRoot);

            int count = Entries?.Count ?? 0;
            hash = StableHash64.CombineUInt64LittleEndian(hash, (ulong)count);

            for (int i = 0; i < count; i++)
            {
                ContentTrustFileEntry entry = Entries[i];
                hash = CombineString(hash, entry.Location);
                hash = StableHash64.CombineUInt64LittleEndian(hash, unchecked((ulong)entry.SizeBytes));
                hash = StableHash64.CombineUInt64LittleEndian(hash, (ulong)entry.HashAlgorithm);
                hash = CombineString(hash, entry.ExpectedHashHex);
            }

            return StableHash64.EnsureNonZero(hash);
        }

        private static ulong CombineString(ulong hash, string value)
        {
            return StableHash64.CombineUInt64LittleEndian(hash, StableHash64.ComputeUtf16Ordinal(value ?? string.Empty));
        }
    }
}
