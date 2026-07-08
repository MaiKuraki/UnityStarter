namespace CycloneGames.AssetManagement.Runtime.Trust
{
    /// <summary>
    /// Optional signature boundary for product-specific trust policies.
    /// Implementations should verify a canonical signed manifest representation owned by the project.
    /// </summary>
    public interface IContentTrustSignatureVerifier
    {
        bool Verify(in ContentTrustManifest manifest, out string error);
    }
}
