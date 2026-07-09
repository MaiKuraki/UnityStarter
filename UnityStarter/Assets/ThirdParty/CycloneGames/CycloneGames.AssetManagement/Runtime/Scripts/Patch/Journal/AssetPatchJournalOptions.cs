namespace CycloneGames.AssetManagement.Runtime
{
    public readonly struct AssetPatchJournalOptions
    {
        public readonly IAssetPatchJournalStore Store;
        public readonly AssetPatchJournalWriteFailurePolicy WriteFailurePolicy;

        public AssetPatchJournalOptions(
            IAssetPatchJournalStore store,
            AssetPatchJournalWriteFailurePolicy writeFailurePolicy = AssetPatchJournalWriteFailurePolicy.ContinueWithoutJournal)
        {
            Store = store;
            WriteFailurePolicy = writeFailurePolicy;
        }

        public bool Enabled => Store != null;

        public static AssetPatchJournalOptions Disabled => default;
    }
}
