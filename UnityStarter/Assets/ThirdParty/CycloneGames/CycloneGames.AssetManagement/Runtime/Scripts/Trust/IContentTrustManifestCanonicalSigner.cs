namespace CycloneGames.AssetManagement.Runtime.Trust
{
    /// <summary>
    /// Signing boundary for large manifest pipelines that need caller-owned buffers, streams, or platform crypto handles.
    /// </summary>
    public interface IContentTrustManifestCanonicalSigner
    {
        string SignCanonicalManifest(in ContentTrustManifest manifest);
    }
}
