using System.Collections.Generic;

namespace CycloneGames.AssetManagement.Runtime.Trust
{
    public interface IContentTrustVerifier
    {
        ContentTrustVerificationResult VerifyBytes(byte[] bytes, in ContentTrustFileEntry entry);
        ContentTrustVerificationResult VerifyFile(string rootDirectory, in ContentTrustFileEntry entry);

        /// <summary>
        /// Verifies all manifest entries under the supplied root directory.
        /// Returns the number of failures and optionally fills <paramref name="failures"/>.
        /// </summary>
        int VerifyManifestFiles(
            string rootDirectory,
            in ContentTrustManifest manifest,
            List<ContentTrustVerificationResult> failures = null,
            IContentTrustSignatureVerifier signatureVerifier = null);
    }
}
