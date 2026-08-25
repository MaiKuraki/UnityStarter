using System;
using System.IO;
using UnityEditor;

namespace Build.Pipeline.Editor.Integrations.YooAsset3Core
{
    /// <summary>
    /// Recovers project-central YooAsset publication transactions without requiring
    /// the YooAsset package, an active provider, or the original build profile. The
    /// durable journal, ownership markers, and rollback/commit logic all live in the
    /// core assembly so uninstalling or upgrading YooAsset cannot strand a pending
    /// publication.
    /// </summary>
    [BuildRecoveryRegistration(ParticipantId, 100)]
    public sealed class YooAsset3RecoveryCoordinator : IBuildRecoveryParticipant
    {
        public const string ParticipantId = "YooAsset3";
        private static readonly string[] StatePaths =
        {
            ".buildpipeline/transactions/yooasset3"
        };

        public string Id => ParticipantId;
        public int Priority => 100;
        public System.Collections.Generic.IReadOnlyList<string> StateDirectoryRelativePaths => StatePaths;

        public void Recover(string projectRoot)
        {
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                throw new ArgumentException("A Unity project root is required.", nameof(projectRoot));
            }

            string normalizedProjectRoot = Path.GetFullPath(projectRoot);
            string providerStateRoot = YooAsset3PublicationPaths.GetProviderStateRoot(
                normalizedProjectRoot);
            using (YooAsset3BuildLock.Acquire(
                       normalizedProjectRoot,
                       providerStateRoot,
                       providerStateRoot))
            {
                YooAsset3PublicationRecovery.RecoverPending(
                    normalizedProjectRoot,
                    AssetDatabase.Refresh);
            }
        }
    }
}
