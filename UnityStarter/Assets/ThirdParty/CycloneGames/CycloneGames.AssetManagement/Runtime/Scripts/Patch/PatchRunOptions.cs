namespace CycloneGames.AssetManagement.Runtime
{
    public readonly struct PatchRunOptions
    {
        public readonly bool AutoDownloadOnFoundNewVersion;
        public readonly bool AppendTimeTicks;
        public readonly PatchDownloadOptions DownloadOptions;
        public readonly PatchContentTrustOptions TrustOptions;

        public PatchRunOptions(
            bool autoDownloadOnFoundNewVersion,
            PatchDownloadOptions downloadOptions = default,
            PatchContentTrustOptions trustOptions = default,
            bool appendTimeTicks = true)
        {
            AutoDownloadOnFoundNewVersion = autoDownloadOnFoundNewVersion;
            DownloadOptions = downloadOptions;
            TrustOptions = trustOptions;
            AppendTimeTicks = appendTimeTicks;
        }

        public static PatchRunOptions Legacy(bool autoDownloadOnFoundNewVersion, PatchDownloadOptions downloadOptions = default)
        {
            return new PatchRunOptions(autoDownloadOnFoundNewVersion, downloadOptions);
        }

        public PatchRunOptions Normalized()
        {
            return new PatchRunOptions(
                AutoDownloadOnFoundNewVersion,
                DownloadOptions.Normalized(),
                TrustOptions.Normalized(),
                AppendTimeTicks);
        }
    }
}
