namespace CycloneGames.AssetManagement.Runtime
{
    public enum AssetPatchRootDirectorySource : byte
    {
        ExplicitPath = 0,
        PersistentDataPath = 1,
        TemporaryCachePath = 2,
        StreamingAssetsPath = 3
    }
}
