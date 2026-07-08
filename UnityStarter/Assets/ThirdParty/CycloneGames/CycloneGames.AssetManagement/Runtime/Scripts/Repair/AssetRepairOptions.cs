namespace CycloneGames.AssetManagement.Runtime
{
    public readonly struct AssetRepairOptions
    {
        private readonly byte _clearUnusedCacheBeforeDownload;
        private readonly byte _recursiveDownloadLocations;
        private readonly byte _verifyAfterRepair;

        public readonly PatchDownloadOptions DownloadOptions;
        public readonly PatchContentTrustOptions TrustOptions;

        public AssetRepairOptions(
            PatchDownloadOptions downloadOptions = default,
            PatchContentTrustOptions trustOptions = default,
            bool clearUnusedCacheBeforeDownload = true,
            bool recursiveDownloadLocations = true,
            bool verifyAfterRepair = true)
        {
            DownloadOptions = downloadOptions;
            TrustOptions = trustOptions;
            _clearUnusedCacheBeforeDownload = ToFlag(clearUnusedCacheBeforeDownload);
            _recursiveDownloadLocations = ToFlag(recursiveDownloadLocations);
            _verifyAfterRepair = ToFlag(verifyAfterRepair);
        }

        public bool ClearUnusedCacheBeforeDownload => _clearUnusedCacheBeforeDownload == 0 || _clearUnusedCacheBeforeDownload == 1;
        public bool RecursiveDownloadLocations => _recursiveDownloadLocations == 0 || _recursiveDownloadLocations == 1;
        public bool VerifyAfterRepair => _verifyAfterRepair == 0 || _verifyAfterRepair == 1;

        public AssetRepairOptions Normalized()
        {
            return new AssetRepairOptions(
                DownloadOptions.Normalized(),
                TrustOptions.Normalized(),
                ClearUnusedCacheBeforeDownload,
                RecursiveDownloadLocations,
                VerifyAfterRepair);
        }

        private static byte ToFlag(bool enabled)
        {
            return enabled ? (byte)1 : (byte)2;
        }
    }
}
