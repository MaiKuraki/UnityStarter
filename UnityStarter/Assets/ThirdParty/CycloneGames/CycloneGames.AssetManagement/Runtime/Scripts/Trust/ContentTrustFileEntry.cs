namespace CycloneGames.AssetManagement.Runtime.Trust
{
    /// <summary>
    /// Provider-neutral content manifest entry for one downloadable or cacheable file.
    /// Location is expected to be relative to the manifest root and is validated against path traversal.
    /// </summary>
    public readonly struct ContentTrustFileEntry
    {
        public readonly string Location;
        public readonly long SizeBytes;
        public readonly ContentTrustHashAlgorithm HashAlgorithm;
        public readonly string ExpectedHashHex;

        public ContentTrustFileEntry(string location, long sizeBytes, ContentTrustHashAlgorithm hashAlgorithm, string expectedHashHex)
        {
            Location = location;
            SizeBytes = sizeBytes;
            HashAlgorithm = hashAlgorithm;
            ExpectedHashHex = expectedHashHex;
        }

        public bool HasHash => HashAlgorithm != ContentTrustHashAlgorithm.None;
    }
}
