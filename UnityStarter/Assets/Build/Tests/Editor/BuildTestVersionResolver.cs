using Build.Pipeline.Editor;
using Build.VersionControl.Editor;

namespace Build.Pipeline.Tests.Editor
{
    internal static class BuildTestVersionResolver
    {
        internal static BuildVersionContext ResolveClean(BuildRequest request)
        {
            var clean = new VersionControlWorkspaceComponentEvidence(
                VersionControlWorkspaceComponentStatus.Clean,
                0);
            var notApplicable = new VersionControlWorkspaceComponentEvidence(
                VersionControlWorkspaceComponentStatus.NotApplicable,
                0);
            var workspace = new VersionControlWorkspaceEvidence(
                clean,
                clean,
                notApplicable,
                notApplicable);
            return new BuildVersionContext(
                request.ApplicationVersion,
                request.ApplicationVersion + ".1",
                1,
                "0123456789ab",
                "1",
                "tests",
                "2026-08-11T00:00:00Z",
                "Test",
                sourceWorkspace: workspace);
        }
    }
}
