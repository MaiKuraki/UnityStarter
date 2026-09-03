using System;
using System.IO;
using Build.Pipeline.Editor;
using UnityEditor;

namespace Build.Pipeline.Integrations.YooAsset3.Publication
{
    /// <summary>
    /// Recovery participant for Player-build artifact relocations.
    ///
    /// Why this exists as its own participant instead of a step inside
    /// <see cref="PublicationRecoveryCoordinator"/>: the relocation journal used to live in Temp,
    /// and <c>BuildWorkspaceService.ResolveStateClaim</c> only accepts claims that are a single
    /// directory directly below <c>.buildpipeline/transactions</c>. A Temp-based journal is
    /// therefore invisible to <c>Inspect</c>: after a crash that left the publication journal
    /// cleaned but the relocations unrestored, the workspace was reported <c>Clean</c>,
    /// <c>RecoverUnderLease</c> returned early, and the moved metas and backups were never put
    /// back — the next build let Unity regenerate the metas, breaking GUIDs.
    ///
    /// The journal now lives under the transaction root (see
    /// <see cref="RelocationJournalStore.StateRootRelativePath"/>), so a leftover journal file makes
    /// <c>Inspect</c> report <c>RecoveryRequired</c> and this participant runs.
    /// </summary>
    [BuildRecoveryRegistration(ParticipantId, 90)]
    public sealed class RelocationRecoveryCoordinator : IBuildRecoveryParticipant
    {
        public const string ParticipantId = "YooAsset3Relocation";

        // Must match RelocationJournalStore.StateRootRelativePath: this is the claim the workspace
        // service scans, and the journal directory itself.
        private static readonly string[] StatePaths =
        {
            ".buildpipeline/transactions/yooasset3-relocations"
        };

        public string Id => ParticipantId;

        // Runs before the publication participant (100): restoring metas and backups first gives the
        // publication rollback its originals back, instead of rolling back against missing paths.
        public int Priority => 90;

        public System.Collections.Generic.IReadOnlyList<string> StateDirectoryRelativePaths => StatePaths;

        public void Recover(string projectRoot)
        {
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                throw new ArgumentException("A Unity project root is required.", nameof(projectRoot));
            }

            string normalizedProjectRoot = Path.GetFullPath(projectRoot);

            // Relocation restoration touches StreamingAssets and the Temp staging root only, so it
            // does not need the publication lock; the recovery lease already serializes it.
            int restored = RelocationRecovery.RestorePending(
                normalizedProjectRoot,
                UnityJournalSerializer.Instance,
                message => UnityEngine.Debug.Log(message));

            if (restored > 0)
            {
                UnityEngine.Debug.Log(
                    $"[BuildPipeline] Relocation recovery restored {restored} Player-build "
                    + "publication artifact(s).");
            }
        }
    }
}
