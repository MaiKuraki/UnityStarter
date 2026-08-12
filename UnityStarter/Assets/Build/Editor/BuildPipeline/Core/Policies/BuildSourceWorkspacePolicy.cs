using System;
using Build.VersionControl.Editor;
using UnityEditor.Build;

namespace Build.Pipeline.Editor
{
    internal enum BuildSourceWorkspaceClassification
    {
        VerifiedClean,
        KnownDirty,
        Unverified
    }

    internal enum BuildInteractiveReleaseRoute
    {
        Blocked,
        QualifiedRelease,
        LocalReleasePreview
    }

    internal readonly struct BuildSourceWorkspaceDecision
    {
        internal BuildSourceWorkspaceDecision(
            bool allowed,
            bool requiresVerifiedClean,
            BuildSourceWorkspaceClassification classification,
            string reasonCode,
            string summary)
        {
            Allowed = allowed;
            RequiresVerifiedClean = requiresVerifiedClean;
            Classification = classification;
            ReasonCode = reasonCode ?? string.Empty;
            Summary = summary ?? string.Empty;
        }

        internal bool Allowed { get; }
        internal bool RequiresVerifiedClean { get; }
        internal BuildSourceWorkspaceClassification Classification { get; }
        internal string ReasonCode { get; }
        internal string Summary { get; }
    }

    internal static class BuildSourceWorkspacePolicy
    {
        internal const string VerifiedCleanReason = "VerifiedClean";
        internal const string LocalDirtyDevelopmentAllowedReason =
            "LocalDirtyDevelopmentAllowed";
        internal const string LocalPreviewAllowedReason =
            "LocalPreviewAllowed";
        internal const string VerifiedCleanRequiredReason = "VerifiedCleanRequired";

        internal static bool RequiresVerifiedClean(
            bool batchMode,
            bool debugBuild,
            BuildSourceCleanlinessPolicy policy)
        {
            return RequiresVerifiedClean(
                batchMode,
                debugBuild ? BuildPurpose.Development : BuildPurpose.Release,
                policy);
        }

        internal static bool RequiresVerifiedClean(
            bool batchMode,
            BuildPurpose purpose,
            BuildSourceCleanlinessPolicy policy)
        {
            ValidatePolicy(policy);
            if (purpose == BuildPurpose.LocalReleasePreview)
            {
                if (batchMode)
                {
                    throw new ArgumentException(
                        "Local Release Preview is available only in an interactive Editor.",
                        nameof(batchMode));
                }

                return false;
            }

            return batchMode
                || purpose == BuildPurpose.Release
                || policy == BuildSourceCleanlinessPolicy.RequireClean;
        }

        internal static BuildSourceWorkspaceDecision Evaluate(
            bool batchMode,
            bool debugBuild,
            BuildSourceCleanlinessPolicy policy,
            VersionControlWorkspaceEvidence workspace)
        {
            return Evaluate(
                batchMode,
                debugBuild ? BuildPurpose.Development : BuildPurpose.Release,
                policy,
                workspace);
        }

        internal static BuildSourceWorkspaceDecision Evaluate(
            bool batchMode,
            BuildPurpose purpose,
            BuildSourceCleanlinessPolicy policy,
            VersionControlWorkspaceEvidence workspace)
        {
            bool requiresVerifiedClean = RequiresVerifiedClean(
                batchMode,
                purpose,
                policy);
            BuildSourceWorkspaceClassification classification = Classify(workspace);
            string summary = FormatSummary(workspace);
            if (workspace != null && workspace.IsVerifiedClean)
            {
                return new BuildSourceWorkspaceDecision(
                    allowed: true,
                    requiresVerifiedClean,
                    classification,
                    VerifiedCleanReason,
                    summary);
            }

            if (!requiresVerifiedClean)
            {
                return new BuildSourceWorkspaceDecision(
                    allowed: true,
                    requiresVerifiedClean: false,
                    classification,
                    purpose == BuildPurpose.LocalReleasePreview
                        ? LocalPreviewAllowedReason
                        : LocalDirtyDevelopmentAllowedReason,
                    summary);
            }

            return new BuildSourceWorkspaceDecision(
                allowed: false,
                requiresVerifiedClean: true,
                classification,
                VerifiedCleanRequiredReason,
                summary);
        }

        internal static BuildInteractiveReleaseRoute ResolveInteractiveReleaseRoute(
            BuildSourceCleanlinessPolicy policy,
            bool qualifiedReleaseAllowed,
            bool localPreviewAvailable)
        {
            ValidatePolicy(policy);
            if (qualifiedReleaseAllowed)
            {
                return BuildInteractiveReleaseRoute.QualifiedRelease;
            }

            return policy == BuildSourceCleanlinessPolicy.AllowDirtyLocalRelease
                   && localPreviewAvailable
                ? BuildInteractiveReleaseRoute.LocalReleasePreview
                : BuildInteractiveReleaseRoute.Blocked;
        }

        internal static void EnsureAllowed(
            BuildRequest request,
            BuildVersionContext version)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            VersionControlWorkspaceEvidence workspace = version?.SourceWorkspace;
            BuildSourceWorkspaceDecision decision = Evaluate(
                request.BatchMode,
                request.Purpose,
                request.SourceCleanlinessPolicy,
                workspace);
            if (decision.Allowed)
            {
                return;
            }

            throw new BuildFailedException(
                "This build requires a verified clean source workspace. " +
                decision.Summary + ". No file paths or file contents are included in this diagnostic.");
        }

        private static void ValidatePolicy(BuildSourceCleanlinessPolicy policy)
        {
            if (policy != BuildSourceCleanlinessPolicy.RequireClean
                && policy != BuildSourceCleanlinessPolicy.AllowDirtyDevelopment
                && policy != BuildSourceCleanlinessPolicy.AllowDirtyLocalRelease)
            {
                throw new ArgumentOutOfRangeException(nameof(policy), policy, null);
            }
        }

        private static BuildSourceWorkspaceClassification Classify(
            VersionControlWorkspaceEvidence workspace)
        {
            if (workspace != null && workspace.IsVerifiedClean)
            {
                return BuildSourceWorkspaceClassification.VerifiedClean;
            }

            return workspace != null
                   && string.Equals(
                       workspace.FailureCode,
                       VersionControlWorkspaceEvidence.NoFailure,
                       StringComparison.Ordinal)
                   && workspace.OverallStatus ==
                   VersionControlWorkspaceComponentStatus.Dirty
                ? BuildSourceWorkspaceClassification.KnownDirty
                : BuildSourceWorkspaceClassification.Unverified;
        }

        private static string FormatSummary(VersionControlWorkspaceEvidence workspace)
        {
            return workspace == null
                ? "overall=Unknown; failure=MetadataUnavailable"
                : "overall=" + workspace.OverallStatus
                  + "; tracked=" + Format(workspace.TrackedChanges)
                  + "; untracked=" + Format(workspace.UntrackedChanges)
                  + "; submodules=" + Format(workspace.Submodules)
                  + "; gitLfs=" + Format(workspace.GitLfs)
                  + "; failure=" + workspace.FailureCode;
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
