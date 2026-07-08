namespace CycloneGames.AssetManagement.Runtime.Trust
{
    /// <summary>
    /// Hash algorithms supported by content trust entries.
    /// SHA-256 is the default security-boundary choice; XxHash64 is for fast non-cryptographic consistency checks.
    /// </summary>
    public enum ContentTrustHashAlgorithm : byte
    {
        None = 0,
        Sha256 = 1,
        XxHash64 = 2,
    }
}
