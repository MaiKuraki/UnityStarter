namespace CycloneGames.AssetManagement.Runtime.Trust
{
    public interface IContentTrustManifestSigner
    {
        string Sign(byte[] canonicalPayload);
    }
}
