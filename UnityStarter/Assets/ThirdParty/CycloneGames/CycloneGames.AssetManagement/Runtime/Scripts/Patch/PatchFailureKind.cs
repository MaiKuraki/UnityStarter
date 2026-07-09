namespace CycloneGames.AssetManagement.Runtime
{
    public enum PatchFailureKind : byte
    {
        None = 0,
        ProviderDownloadFailed = 1,
        Cancelled = 2,
        PackageVersionRequestFailed = 3,
        ManifestUpdateFailed = 4,
        DownloaderCreationFailed = 5
    }
}
