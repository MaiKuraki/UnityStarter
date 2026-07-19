namespace CycloneGames.AssetManagement.Runtime.Trust
{
    /// <summary>
    /// Signature boundary used by the RequireSignature content trust policy.
    /// Implementations should verify a canonical signed manifest representation owned by the product.
    /// </summary>
    public interface IContentTrustSignatureVerifier
    {
        bool Verify(in ContentTrustManifest manifest, out string error);
    }
}
