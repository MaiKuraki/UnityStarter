namespace CycloneGames.AssetManagement.Runtime
{
    public interface IAssetPatchJournalStore
    {
        void Write(in AssetPatchJournalRecord record);
        bool TryRead(out AssetPatchJournalRecord record, out string error);
        void Clear();
    }
}
