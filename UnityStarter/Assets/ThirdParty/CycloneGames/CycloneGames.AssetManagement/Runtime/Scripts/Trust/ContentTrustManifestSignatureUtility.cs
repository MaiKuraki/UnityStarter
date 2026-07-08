using System;

namespace CycloneGames.AssetManagement.Runtime.Trust
{
    public static class ContentTrustManifestSignatureUtility
    {
        public static ContentTrustManifest Sign(in ContentTrustManifest manifest, IContentTrustManifestSigner signer)
        {
            if (signer == null)
            {
                throw new ArgumentNullException(nameof(signer));
            }

            byte[] canonicalPayload = ContentTrustManifestCodec.ToCanonicalPayloadBytes(in manifest);
            string signature = signer.Sign(canonicalPayload);
            if (string.IsNullOrEmpty(signature))
            {
                throw new InvalidOperationException("Content trust manifest signer returned an empty signature.");
            }

            return WithSignature(in manifest, signature);
        }

        public static ContentTrustManifest WithSignature(in ContentTrustManifest manifest, string signature)
        {
            return new ContentTrustManifest(
                manifest.Version,
                manifest.Entries,
                manifest.MinimumClientVersion,
                manifest.RollbackVersion,
                manifest.ContentRoot,
                signature);
        }
    }
}
