using System;
using System.IO;

namespace Build.Pipeline.Editor
{
    /// <summary>
    /// Recovers project-central Addressables transactions without requiring the
    /// Addressables package, an active provider, or the original build profile.
    /// </summary>
    [BuildRecoveryRegistration(ParticipantId, 100)]
    public sealed class AddressablesRecoveryCoordinator : IBuildRecoveryParticipant
    {
        public const string ParticipantId = "Addressables";
        private static readonly string[] StatePaths =
        {
            ".buildpipeline/transactions/addressables-settings",
            ".buildpipeline/transactions/addressables"
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
            using (AddressablesBuildLock.Acquire(normalizedProjectRoot))
            {
                AddressablesSettingsTransaction.RecoverPending(normalizedProjectRoot);
                AddressablesPublicationTransaction.RecoverPending(normalizedProjectRoot);
            }
        }
    }
}
