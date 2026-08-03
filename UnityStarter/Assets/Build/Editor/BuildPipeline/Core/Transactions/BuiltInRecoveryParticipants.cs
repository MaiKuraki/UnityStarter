using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Build.Pipeline.Editor
{
    [BuildRecoveryRegistration(ParticipantId, 100)]
    public sealed class GlobalBuildStateRecoveryParticipant : IBuildRecoveryParticipant
    {
        public const string ParticipantId = "GlobalBuildState";

        public string Id => ParticipantId;
        public int Priority => 100;

        public void Recover(string projectRoot)
        {
            BuildGlobalStateScope.RecoverPending(projectRoot);
        }
    }

    [BuildRecoveryRegistration(ParticipantId, 100)]
    public sealed class PlayerOutputRecoveryParticipant : IBuildRecoveryParticipant
    {
        public const string ParticipantId = "PlayerOutput";

        public string Id => ParticipantId;
        public int Priority => 100;

        public void Recover(string projectRoot)
        {
            PlayerOutputTransaction.RecoverPending(projectRoot);
        }
    }

    internal static class OptionalRecoveryStateGuard
    {
        private const string YooAssetParticipantId = "YooAsset3";
        private const string YooAssetStateRelativePath =
            ".buildpipeline/transactions/yooasset3";

        public static void EnsureNoUnavailableRecoveryState(
            string projectRoot,
            IReadOnlyList<IBuildRecoveryParticipant> participants)
        {
            if (participants.Any(participant => string.Equals(
                    participant.Id,
                    YooAssetParticipantId,
                    StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            string normalizedProjectRoot = Path.GetFullPath(projectRoot);
            string stateRoot = Path.GetFullPath(Path.Combine(
                normalizedProjectRoot,
                YooAssetStateRelativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!Directory.Exists(stateRoot))
            {
                if (File.Exists(stateRoot))
                {
                    throw new InvalidOperationException(
                        $"YooAsset recovery state path is not a directory: '{stateRoot}'.");
                }

                return;
            }

            if ((File.GetAttributes(stateRoot) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    $"YooAsset recovery state cannot be a reparse point: '{stateRoot}'.");
            }

            string pendingEntry = Directory.EnumerateFileSystemEntries(stateRoot).FirstOrDefault();
            if (pendingEntry != null)
            {
                throw new InvalidOperationException(
                    "A pending YooAsset 3 publication transaction exists, but the compatible "
                    + "YooAsset 3 integration assembly is unavailable. Reinstall the supported "
                    + "YooAsset package, run recovery, and only then remove the package. "
                    + $"Recovery evidence was preserved under '{stateRoot}'.");
            }
        }
    }
}
