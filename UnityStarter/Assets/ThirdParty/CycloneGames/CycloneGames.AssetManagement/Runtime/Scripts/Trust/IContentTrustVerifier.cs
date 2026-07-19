using System.Collections.Generic;
using System.Threading;

using Cysharp.Threading.Tasks;

namespace CycloneGames.AssetManagement.Runtime.Trust
{
    public interface IContentTrustVerifier
    {
        /// <summary>
        /// Gets the immutable policy enforced by this verifier instance.
        /// </summary>
        ContentTrustPolicy Policy { get; }

        ContentTrustVerificationResult VerifyBytes(byte[] bytes, in ContentTrustFileEntry entry);
        ContentTrustVerificationResult VerifyFile(string rootDirectory, in ContentTrustFileEntry entry);

        /// <summary>
        /// Validates manifest structure, hash policy, and the required signature boundary without reading payload files.
        /// </summary>
        ContentTrustVerificationResult ValidateManifest(
            in ContentTrustManifest manifest,
            IContentTrustSignatureVerifier signatureVerifier = null);

        /// <summary>
        /// Verifies all manifest entries under the supplied root directory.
        /// Returns the number of failures and optionally fills <paramref name="failures"/>.
        /// </summary>
        int VerifyManifestFiles(
            string rootDirectory,
            in ContentTrustManifest manifest,
            List<ContentTrustVerificationResult> failures = null,
            IContentTrustSignatureVerifier signatureVerifier = null);

        UniTask<int> VerifyManifestFilesAsync(
            string rootDirectory,
            ContentTrustManifest manifest,
            List<ContentTrustVerificationResult> failures = null,
            IContentTrustSignatureVerifier signatureVerifier = null,
            CancellationToken cancellationToken = default);
    }
}
