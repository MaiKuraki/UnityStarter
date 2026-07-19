using System;
using System.Collections.Generic;

using CycloneGames.Hash.Core;

namespace CycloneGames.AssetManagement.Runtime.Trust
{
    /// <summary>
    /// Provider-neutral content manifest metadata. The manifest can describe Addressables bundles,
    /// YooAsset bundles, raw files, or any future provider payload. Construction validates,
    /// defensively copies, and canonically sorts entries.
    /// </summary>
    public readonly struct ContentTrustManifest
    {
        private static readonly IReadOnlyList<ContentTrustFileEntry> EmptyEntries =
            Array.AsReadOnly(Array.Empty<ContentTrustFileEntry>());

        public readonly string Version;
        public readonly string ContentRoot;
        public readonly IReadOnlyList<ContentTrustFileEntry> Entries;
        public readonly string Signature;

        public ContentTrustManifest(
            string version,
            IReadOnlyList<ContentTrustFileEntry> entries,
            string contentRoot = null,
            string signature = null)
        {
            Version = ContentTrustManifestValidation.NormalizeRequiredVersion(version);
            ContentRoot = ContentTrustManifestValidation.NormalizeOptionalContentRoot(contentRoot);
            Signature = ContentTrustManifestValidation.NormalizeOptionalSignature(signature);

            ContentTrustFileEntry[] entryCopy = ContentTrustManifestValidation.CopyValidateAndSortEntries(entries);
            Entries = entryCopy.Length == 0 ? EmptyEntries : Array.AsReadOnly(entryCopy);
        }

        private ContentTrustManifest(
            string version,
            string contentRoot,
            string signature,
            IReadOnlyList<ContentTrustFileEntry> canonicalEntries)
        {
            Version = version;
            ContentRoot = contentRoot;
            Signature = signature;
            Entries = canonicalEntries;
        }

        /// <summary>
        /// Computes a deterministic non-cryptographic fingerprint for cache invalidation and diagnostics.
        /// This is not a tamper-proof signature.
        /// </summary>
        public ulong ComputeFingerprint()
        {
            ContentTrustManifestValidation.ThrowIfUninitialized(in this);

            ulong hash = StableHash64.ComputeUtf16Ordinal(Version ?? string.Empty);
            hash = CombineString(hash, ContentRoot);

            int count = Entries.Count;
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

        internal ContentTrustManifest WithSignature(string signature)
        {
            ContentTrustManifestValidation.ThrowIfUninitialized(in this);
            return new ContentTrustManifest(
                Version,
                ContentRoot,
                ContentTrustManifestValidation.NormalizeOptionalSignature(signature),
                Entries);
        }

        private static ulong CombineString(ulong hash, string value)
        {
            return StableHash64.CombineUInt64LittleEndian(hash, StableHash64.ComputeUtf16Ordinal(value ?? string.Empty));
        }
    }
}
