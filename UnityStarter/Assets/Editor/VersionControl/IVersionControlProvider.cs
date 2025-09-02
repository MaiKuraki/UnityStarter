namespace CycloneGames.Editor.VersionControl
{
    public interface IVersionControlProvider
    {
        string GetCommitHash();
        string GetCommitCount();
        void UpdateVersionInfoAsset(string assetPath, string commitHash, string commitCount);
        void ClearVersionInfoAsset(string assetPath);
    }
}