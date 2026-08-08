using System.Collections.Generic;

namespace Build.Pipeline.Editor
{
    [BuildRecoveryRegistration(HybridCLRReleaseBaselineTransaction.PublicationId, 101)]
    public sealed class HybridCLRReleaseBaselineRecoveryParticipant :
        IBuildRecoveryParticipant
    {
        private static readonly string[] StatePaths =
        {
            HybridCLRReleaseBaselineTransaction.StateRelativePath
        };

        public string Id => HybridCLRReleaseBaselineTransaction.PublicationId;
        public int Priority => 101;
        public IReadOnlyList<string> StateDirectoryRelativePaths => StatePaths;

        public void Recover(string projectRoot)
        {
            HybridCLRReleaseBaselineTransaction.RecoverPending(projectRoot);
        }
    }
}
