using System;
using Build.VersionControl.Editor;
using UnityEditor.Build;

namespace Build.Pipeline.Editor
{
    internal static class BuildSourceWorkspacePolicy
    {
        internal static void EnsureAllowed(
            BuildRequest request,
            BuildVersionContext version)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (!request.RequireCleanSource)
            {
                return;
            }

            VersionControlWorkspaceEvidence workspace = version?.SourceWorkspace;
            if (workspace != null && workspace.IsVerifiedClean)
            {
                return;
            }

            string summary = workspace == null
                ? "overall=Unknown; failure=MetadataUnavailable"
                : "overall=" + workspace.OverallStatus
                  + "; tracked=" + Format(workspace.TrackedChanges)
                  + "; untracked=" + Format(workspace.UntrackedChanges)
                  + "; submodules=" + Format(workspace.Submodules)
                  + "; gitLfs=" + Format(workspace.GitLfs)
                  + "; failure=" + workspace.FailureCode;
            throw new BuildFailedException(
                "This build requires a verified clean source workspace. " +
                summary + ". No file paths or file contents are included in this diagnostic.");
        }

        private static string Format(VersionControlWorkspaceComponentEvidence component)
        {
            if (component == null)
            {
                return "Unknown";
            }

            return component.ChangeCount.HasValue
                ? component.Status + "(" + component.ChangeCount.Value + ")"
                : component.Status.ToString();
        }
    }
}
