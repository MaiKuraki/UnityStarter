namespace Build.VersionControl.Editor
{
    public enum VersionControlWorkspaceComponentStatus
    {
        Unknown = 0,
        Clean = 1,
        Dirty = 2,
        NotApplicable = 3
    }

    public sealed class VersionControlWorkspaceComponentEvidence
    {
        public VersionControlWorkspaceComponentEvidence(
            VersionControlWorkspaceComponentStatus status,
            int? changeCount = null)
        {
            if (status != VersionControlWorkspaceComponentStatus.Unknown
                && status != VersionControlWorkspaceComponentStatus.Clean
                && status != VersionControlWorkspaceComponentStatus.Dirty
                && status != VersionControlWorkspaceComponentStatus.NotApplicable)
            {
                throw new System.ArgumentOutOfRangeException(nameof(status), status, null);
            }

            if (changeCount.HasValue && changeCount.Value < 0)
            {
                throw new System.ArgumentOutOfRangeException(
                    nameof(changeCount),
                    changeCount,
                    "Workspace change count cannot be negative.");
            }

            if ((status == VersionControlWorkspaceComponentStatus.Clean
                 || status == VersionControlWorkspaceComponentStatus.NotApplicable)
                && changeCount.GetValueOrDefault() != 0)
            {
                throw new System.ArgumentException(
                    "Clean and not-applicable workspace components cannot report changes.",
                    nameof(changeCount));
            }

            Status = status;
            ChangeCount = changeCount;
        }

        public VersionControlWorkspaceComponentStatus Status { get; }
        public int? ChangeCount { get; }
    }

    public sealed class VersionControlWorkspaceEvidence
    {
        public const string NoFailure = "None";
        public const string MetadataUnavailable = "MetadataUnavailable";
        public const string ExecutableUnavailable = "ExecutableUnavailable";
        public const string CommandTimedOut = "CommandTimedOut";
        public const string OutputLimitExceeded = "OutputLimitExceeded";
        public const string CommandFailed = "CommandFailed";
        public const string MalformedOutput = "MalformedOutput";
        public const string IncoherentSnapshot = "IncoherentSnapshot";

        public VersionControlWorkspaceEvidence(
            VersionControlWorkspaceComponentEvidence trackedChanges,
            VersionControlWorkspaceComponentEvidence untrackedChanges,
            VersionControlWorkspaceComponentEvidence submodules,
            VersionControlWorkspaceComponentEvidence gitLfs,
            string failureCode = NoFailure)
        {
            TrackedChanges = trackedChanges
                ?? throw new System.ArgumentNullException(nameof(trackedChanges));
            UntrackedChanges = untrackedChanges
                ?? throw new System.ArgumentNullException(nameof(untrackedChanges));
            Submodules = submodules
                ?? throw new System.ArgumentNullException(nameof(submodules));
            GitLfs = gitLfs
                ?? throw new System.ArgumentNullException(nameof(gitLfs));
            FailureCode = ValidateFailureCode(failureCode);
            OverallStatus = ResolveOverallStatus(
                TrackedChanges,
                UntrackedChanges,
                Submodules,
                GitLfs);
        }

        public VersionControlWorkspaceComponentStatus OverallStatus { get; }
        public VersionControlWorkspaceComponentEvidence TrackedChanges { get; }
        public VersionControlWorkspaceComponentEvidence UntrackedChanges { get; }
        public VersionControlWorkspaceComponentEvidence Submodules { get; }
        public VersionControlWorkspaceComponentEvidence GitLfs { get; }
        public string FailureCode { get; }
        public bool IsVerifiedClean =>
            string.Equals(FailureCode, NoFailure, System.StringComparison.Ordinal)
            && OverallStatus == VersionControlWorkspaceComponentStatus.Clean
            && TrackedChanges.Status == VersionControlWorkspaceComponentStatus.Clean
            && UntrackedChanges.Status == VersionControlWorkspaceComponentStatus.Clean;

        public static VersionControlWorkspaceEvidence Unknown(string failureCode)
        {
            var unknown = new VersionControlWorkspaceComponentEvidence(
                VersionControlWorkspaceComponentStatus.Unknown);
            return new VersionControlWorkspaceEvidence(
                unknown,
                unknown,
                unknown,
                unknown,
                failureCode);
        }

        private static VersionControlWorkspaceComponentStatus ResolveOverallStatus(
            params VersionControlWorkspaceComponentEvidence[] components)
        {
            bool hasUnknown = false;
            for (int index = 0; index < components.Length; index++)
            {
                switch (components[index].Status)
                {
                    case VersionControlWorkspaceComponentStatus.Dirty:
                        return VersionControlWorkspaceComponentStatus.Dirty;
                    case VersionControlWorkspaceComponentStatus.Unknown:
                        hasUnknown = true;
                        break;
                }
            }

            return hasUnknown
                ? VersionControlWorkspaceComponentStatus.Unknown
                : VersionControlWorkspaceComponentStatus.Clean;
        }

        private static string ValidateFailureCode(string value)
        {
            string code = value ?? string.Empty;
            if (code.Length == 0 || code.Length > 64)
            {
                throw new System.ArgumentException(
                    "Workspace failure code must contain 1-64 ASCII identifier characters.",
                    nameof(value));
            }

            for (int index = 0; index < code.Length; index++)
            {
                char character = code[index];
                if (!((character >= 'A' && character <= 'Z')
                      || (character >= 'a' && character <= 'z')
                      || (character >= '0' && character <= '9')))
                {
                    throw new System.ArgumentException(
                        "Workspace failure code must contain only ASCII letters and digits.",
                        nameof(value));
                }
            }

            return code;
        }
    }

    public sealed class VersionControlMetadata
    {
        public VersionControlMetadata(
            string providerId,
            string commitHash,
            string commitCount,
            string branchName,
            string commitDate)
            : this(
                providerId,
                commitHash,
                commitCount,
                branchName,
                commitDate,
                VersionControlWorkspaceEvidence.Unknown(
                    VersionControlWorkspaceEvidence.MetadataUnavailable))
        {
        }

        public VersionControlMetadata(
            string providerId,
            string commitHash,
            string commitCount,
            string branchName,
            string commitDate,
            VersionControlWorkspaceEvidence workspace)
        {
            ProviderId = providerId ?? string.Empty;
            CommitHash = commitHash ?? string.Empty;
            CommitCount = commitCount ?? string.Empty;
            BranchName = branchName ?? string.Empty;
            CommitDate = commitDate ?? string.Empty;
            Workspace = workspace
                ?? VersionControlWorkspaceEvidence.Unknown(
                    VersionControlWorkspaceEvidence.MetadataUnavailable);
        }

        public string ProviderId { get; }
        public string CommitHash { get; }
        public string CommitCount { get; }
        public string BranchName { get; }
        public string CommitDate { get; }
        public VersionControlWorkspaceEvidence Workspace { get; }
    }

    public interface IVersionControlProvider
    {
        VersionControlMetadata Capture();
    }

    /// <summary>
    /// Extensible detector/factory contract discovered through Unity TypeCache.
    /// </summary>
    public interface IVersionControlProviderDetector
    {
        string ProviderId { get; }
        int Priority { get; }
        bool IsAvailable(string projectRoot);
        IVersionControlProvider Create(string projectRoot);
    }
}
