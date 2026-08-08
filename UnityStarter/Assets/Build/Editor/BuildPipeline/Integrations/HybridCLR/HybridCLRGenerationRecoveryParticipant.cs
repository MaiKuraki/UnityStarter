using System.Collections.Generic;

namespace Build.Pipeline.Editor
{
    [BuildRecoveryRegistration(ParticipantId, 200)]
    public sealed class HybridCLRGenerationRecoveryParticipant : IBuildRecoveryParticipant
    {
        public const string ParticipantId = "HybridCLRGeneration";
        private static readonly string[] StatePaths =
        {
            HybridCLRGenerationTransaction.StateRelativePath
        };

        public string Id => ParticipantId;
        public int Priority => 200;
        public IReadOnlyList<string> StateDirectoryRelativePaths => StatePaths;

        public void Recover(string projectRoot)
        {
            HybridCLRBuilder.RecoverPendingGenerationInputs(projectRoot);
        }
    }
}
