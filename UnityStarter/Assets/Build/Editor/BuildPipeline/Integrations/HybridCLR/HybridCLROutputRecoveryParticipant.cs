namespace Build.Pipeline.Editor
{
    [BuildRecoveryRegistration(ParticipantId, 100)]
    public sealed class HybridCLROutputRecoveryParticipant : IBuildRecoveryParticipant
    {
        public const string ParticipantId = "HybridCLROutput";

        public string Id => ParticipantId;
        public int Priority => 100;

        public void Recover(string projectRoot)
        {
            HybridCLRBuilder.RecoverPendingManagedOutputs(projectRoot);
        }
    }
}
