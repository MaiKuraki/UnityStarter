using System.Collections.Generic;

namespace Build.Pipeline.Editor
{
    [BuildRecoveryRegistration(ParticipantId, 100)]
    public sealed class GlobalBuildStateRecoveryParticipant : IBuildRecoveryParticipant
    {
        public const string ParticipantId = "GlobalBuildState";
        private static readonly string[] StatePaths =
        {
            ".buildpipeline/transactions/global-state"
        };

        public string Id => ParticipantId;
        public int Priority => 100;
        public IReadOnlyList<string> StateDirectoryRelativePaths => StatePaths;

        public void Recover(string projectRoot)
        {
            BuildGlobalStateScope.RecoverPending(projectRoot);
        }
    }

    [BuildRecoveryRegistration(ParticipantId, 100)]
    public sealed class PlayerOutputRecoveryParticipant : IBuildRecoveryParticipant
    {
        public const string ParticipantId = "PlayerOutput";
        private static readonly string[] StatePaths =
        {
            ".buildpipeline/transactions/player"
        };

        public string Id => ParticipantId;
        public int Priority => 100;
        public IReadOnlyList<string> StateDirectoryRelativePaths => StatePaths;

        public void Recover(string projectRoot)
        {
            PlayerOutputTransaction.RecoverPending(projectRoot);
        }
    }

}
