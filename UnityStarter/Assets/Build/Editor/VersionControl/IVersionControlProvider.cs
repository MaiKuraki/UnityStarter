namespace Build.VersionControl.Editor
{
    public sealed class VersionControlMetadata
    {
        public VersionControlMetadata(
            string providerId,
            string commitHash,
            string commitCount,
            string branchName,
            string commitDate)
        {
            ProviderId = providerId ?? string.Empty;
            CommitHash = commitHash ?? string.Empty;
            CommitCount = commitCount ?? string.Empty;
            BranchName = branchName ?? string.Empty;
            CommitDate = commitDate ?? string.Empty;
        }

        public string ProviderId { get; }
        public string CommitHash { get; }
        public string CommitCount { get; }
        public string BranchName { get; }
        public string CommitDate { get; }
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
