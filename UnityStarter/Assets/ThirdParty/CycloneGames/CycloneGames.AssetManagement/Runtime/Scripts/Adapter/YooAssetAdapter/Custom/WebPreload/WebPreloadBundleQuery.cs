#if CYCLONEGAMES_HAS_YOOASSET
using System.Collections.Generic;

namespace CycloneGames.AssetManagement.Runtime
{
    /// <summary>
    /// Snapshot of the bundle a Web preload strategy is asked about. The instance is reused across queries by
    /// its owning strategy, so product callbacks must read it synchronously and must not retain the reference.
    /// </summary>
    public sealed class WebPreloadBundleQuery
    {
        private string _bundleName = string.Empty;
        private string _fileHash = string.Empty;
        private long _fileSize;
        private bool _isEncrypted;
        private IReadOnlyList<string> _candidateUrls;

        /// <summary>Manifest bundle name of the queried bundle.</summary>
        public string BundleName => _bundleName;

        /// <summary>Manifest file hash of the queried bundle. Doubles as the bundle GUID inside YooAsset.</summary>
        public string FileHash => _fileHash;

        /// <summary>Encoded size of the queried bundle in bytes.</summary>
        public long FileSize => _fileSize;

        /// <summary>True when the queried bundle was built with bundle encryption enabled.</summary>
        public bool IsEncrypted => _isEncrypted;

        /// <summary>
        /// Candidate download URLs resolved for the queried bundle, in provider preference order.
        /// The list is owned by the provider and must not be retained or mutated.
        /// </summary>
        public IReadOnlyList<string> CandidateUrls => _candidateUrls;

        internal void Reset(
            string bundleName,
            string fileHash,
            long fileSize,
            bool isEncrypted,
            IReadOnlyList<string> candidateUrls)
        {
            _bundleName = bundleName ?? string.Empty;
            _fileHash = fileHash ?? string.Empty;
            _fileSize = fileSize;
            _isEncrypted = isEncrypted;
            _candidateUrls = candidateUrls;
        }
    }
}
#endif
