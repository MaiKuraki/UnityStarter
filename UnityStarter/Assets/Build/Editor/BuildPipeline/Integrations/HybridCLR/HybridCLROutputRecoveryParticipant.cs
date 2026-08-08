namespace Build.Pipeline.Editor
{
    [BuildRecoveryRegistration(ParticipantId, 100)]
    public sealed class HybridCLROutputRecoveryParticipant : IBuildRecoveryParticipant
    {
        public const string ParticipantId = "HybridCLROutput";
        private static readonly string[] StatePaths =
        {
            HybridCLROutputTransaction.StateRelativePath
        };

        public string Id => ParticipantId;
        public int Priority => 100;
        public System.Collections.Generic.IReadOnlyList<string> StateDirectoryRelativePaths => StatePaths;

        public void Recover(string projectRoot)
        {
            HybridCLRBuilder.RecoverPendingManagedOutputs(projectRoot);
        }
    }
}
