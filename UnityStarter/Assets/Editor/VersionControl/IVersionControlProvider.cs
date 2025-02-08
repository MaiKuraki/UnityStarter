namespace CycloneGames.Editor.VersionControl
{
    public interface IVersionControlProvider
    {
        string GetCommitHash();
        void SaveVersionToJson(string commitHash);
        void RemoveVersionJson();
    }
}