using System;

namespace CycloneGames.AssetManagement.Runtime.Trust
{
    public static class ContentTrustManifestSignatureUtility
    {
        public static ContentTrustManifest SignCanonical(in ContentTrustManifest manifest, IContentTrustManifestCanonicalSigner signer)
        {
            if (signer == null)
            {
                throw new ArgumentNullException(nameof(signer));
            }

            string signature = signer.SignCanonicalManifest(in manifest);
            if (string.IsNullOrWhiteSpace(signature))
            {
                throw new InvalidOperationException("Content trust manifest signer returned an empty signature.");
            }

            return WithSignature(in manifest, signature);
        }

        public static ContentTrustManifest Sign(in ContentTrustManifest manifest, IContentTrustManifestSigner signer)
        {
            if (signer == null)
            {
                throw new ArgumentNullException(nameof(signer));
            }

            byte[] canonicalPayload = ContentTrustManifestCodec.ToCanonicalPayloadBytes(in manifest);
            string signature = signer.Sign(canonicalPayload);
            if (string.IsNullOrWhiteSpace(signature))
            {
                throw new InvalidOperationException("Content trust manifest signer returned an empty signature.");
            }

            return WithSignature(in manifest, signature);
        }

        public static ContentTrustManifest WithSignature(in ContentTrustManifest manifest, string signature)
        {
            return manifest.WithSignature(signature);
        }
    }
}
