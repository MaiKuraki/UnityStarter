namespace Build.VersionControl.Editor
{
    public interface IVersionControlProvider
    {
        string GetCommitHash();
        string GetCommitCount();
        string GetBranchName();
        string GetCommitDate();
        void UpdateVersionInfoAsset(string assetPath, string commitHash, string commitCount, string commitBranch, string commitDate);
        void ClearVersionInfoAsset(string assetPath);
    }
}
