namespace CycloneGames.AssetManagement.Runtime.Trust
{
    /// <summary>
    /// Hash algorithms that can appear in content trust wire data.
    /// SHA-256 is the only algorithm accepted by <see cref="ContentTrustManifestBuilder"/> and
    /// <see cref="ContentTrustVerifier"/>. None and XxHash64 remain parseable as legacy wire values so persisted
    /// manifests can still be read, normalized, fingerprinted, and then rejected at verification with a clear reason.
    /// </summary>
    public enum ContentTrustHashAlgorithm : byte
    {
        None = 0,
        Sha256 = 1,
        XxHash64 = 2,
    }
}
