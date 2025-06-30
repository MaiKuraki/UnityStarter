namespace CycloneGames.Editor.VersionControl
{
    public interface IVersionControlProvider
    {
        string GetCommitHash();
        void UpdateVersionInfoAsset(string assetPath, string commitHash);
        void ClearVersionInfoAsset(string assetPath);
    }
}