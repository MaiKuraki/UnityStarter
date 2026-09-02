using System;
using System.IO;
using Build.Pipeline.Editor;
using UnityEditor;

namespace Build.Pipeline.Integrations.YooAsset3.Publication
{
    /// <summary>
    /// Recovers project-central YooAsset publication transactions without requiring
    /// the YooAsset package, an active provider, or the original build profile. The
    /// durable journal, ownership markers, and rollback/commit logic all live in the
    /// core assembly so uninstalling or upgrading YooAsset cannot strand a pending
    /// publication.
    /// </summary>
    [BuildRecoveryRegistration(ParticipantId, 100)]
    public sealed class PublicationRecoveryCoordinator : IBuildRecoveryParticipant
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
            string providerStateRoot = PublicationPaths.GetProviderStateRoot(
                normalizedProjectRoot);
            using (PublicationBuildLock.Acquire(
                       normalizedProjectRoot,
                       providerStateRoot,
                       providerStateRoot))
            {
                PublicationRecovery.RecoverPending(
                    normalizedProjectRoot,
                    AssetDatabase.Refresh,
                    UnityJournalSerializer.Instance);

                // Player-build artifact relocations (ownership markers, backups, protected
                // metas, stage directories hidden for the Player build) are restored here so a
                // crashed or killed Editor never strands them in Temp.
                int restored = RelocationRecovery.RestorePending(
                    normalizedProjectRoot,
                    UnityJournalSerializer.Instance,
                    message => UnityEngine.Debug.Log(message));
                if (restored > 0)
                {
                    UnityEngine.Debug.Log(
                        $"YooAsset relocation recovery restored {restored} Player-build publication artifact(s).");
                }
            }
        }
    }
}
