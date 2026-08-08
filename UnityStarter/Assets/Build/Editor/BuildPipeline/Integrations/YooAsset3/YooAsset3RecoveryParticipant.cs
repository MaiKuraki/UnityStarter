using System.IO;
using UnityEditor;

namespace Build.Pipeline.Editor.Integrations.YooAsset3
{
    [BuildRecoveryRegistration(ParticipantId, 100)]
    public sealed class YooAsset3RecoveryParticipant : IBuildRecoveryParticipant
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
            string normalizedProjectRoot = Path.GetFullPath(projectRoot);
            string stateRoot = YooAsset3PublicationTransaction.GetProviderStateRoot(
                normalizedProjectRoot);
            using (YooAsset3BuildLock.Acquire(
                       normalizedProjectRoot,
                       stateRoot,
                       stateRoot))
            {
                YooAsset3PublicationTransaction.RecoverPending(
                    normalizedProjectRoot,
                    AssetDatabase.Refresh);
            }
        }
    }
}
