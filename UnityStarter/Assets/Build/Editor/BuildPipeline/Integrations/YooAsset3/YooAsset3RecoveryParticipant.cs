using System.IO;
using UnityEditor;

namespace Build.Pipeline.Editor.Integrations.YooAsset3
{
    [BuildRecoveryRegistration(ParticipantId, 100)]
    public sealed class YooAsset3RecoveryParticipant : IBuildRecoveryParticipant
    {
        public const string ParticipantId = "YooAsset3";

        public string Id => ParticipantId;
        public int Priority => 100;

        public void Recover(string projectRoot)
        {
            string normalizedProjectRoot = Path.GetFullPath(projectRoot);
            string stateRoot = YooAsset3PublicationTransaction.GetStateRoot(
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
