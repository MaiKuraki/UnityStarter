using System;
using System.Threading;

using Cysharp.Threading.Tasks;
using R3;

namespace CycloneGames.AssetManagement.Runtime
{
    public enum PatchEvent
    {
        PatchStatesChanged,
        FoundNewVersion,
        DownloadProgress,
        ContentTrustVerified,
        ContentRepairCompleted,
        RollbackCompleted,
        PatchDone,
        PatchFailed
    }

    public struct FoundNewVersionEventArgs
    {
        public string PackageVersion;
        public long TotalDownloadSizeBytes;
    }

    public struct DownloadProgressEventArgs
    {
        public int TotalDownloadCount;
        public int CurrentDownloadCount;
        public long TotalDownloadSizeBytes;
        public long CurrentDownloadSizeBytes;
    }

    /// <summary>
    /// Tunable parameters for the hot-update download phase. Defaults are filled in for any
    /// non-positive field via <see cref="Normalized"/>, so <c>default</c> is always safe to pass.
    /// </summary>
    public struct PatchDownloadOptions
    {
        public int MaxConcurrentDownloads;
        public int FailedRetryCount;
        public int RequestTimeoutSeconds;

        public static PatchDownloadOptions Default => new PatchDownloadOptions
        {
            MaxConcurrentDownloads = 10,
            FailedRetryCount = 3,
            RequestTimeoutSeconds = 60
        };

        public PatchDownloadOptions Normalized()
        {
            return new PatchDownloadOptions
            {
                MaxConcurrentDownloads = MaxConcurrentDownloads <= 0 ? 10 : MaxConcurrentDownloads,
                FailedRetryCount = FailedRetryCount < 0 ? 3 : FailedRetryCount,
                RequestTimeoutSeconds = RequestTimeoutSeconds <= 0 ? 60 : RequestTimeoutSeconds
            };
        }
    }

    public interface IPatchService : IDisposable
    {
        string PackageName { get; }
        Observable<(PatchEvent, object)> PatchEvents { get; }

        UniTask RunAsync(bool autoDownloadOnFoundNewVersion, PatchDownloadOptions downloadOptions = default, CancellationToken cancellationToken = default);
        void Download();
        void Cancel();
    }
}
