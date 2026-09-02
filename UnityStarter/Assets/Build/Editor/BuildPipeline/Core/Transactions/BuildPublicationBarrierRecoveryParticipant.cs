using System;
using System.Collections.Generic;
using System.IO;

namespace Build.Pipeline.Editor
{
    public sealed class BuildPublicationBarrierRecoveryParticipant :
        IBuildRecoveryParticipant,
        IBuildRecoveryCoordinator
    {
        private static readonly string[] StatePaths =
        {
            BuildPublicationBarrier.StateRelativePath
        };

        public string Id => BuildPublicationBarrier.ParticipantId;
        public int Priority => 100;
        public IReadOnlyList<string> StateDirectoryRelativePaths => StatePaths;

        public void Recover(string projectRoot)
        {
            BuildPublicationBarrier.Recover(projectRoot);
        }
    }
}