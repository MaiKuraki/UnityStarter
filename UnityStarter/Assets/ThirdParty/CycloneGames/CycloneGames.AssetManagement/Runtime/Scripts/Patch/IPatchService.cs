using Cysharp.Threading.Tasks;
using System;
using System.Threading;
using R3;

namespace CycloneGames.AssetManagement.Runtime
{
    public enum PatchEvent
    {
        PatchStatesChanged,
        FoundNewVersion,
        DownloadProgress,
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

    public interface IPatchService : IDisposable
    {
        string PackageName { get; }
        Observable<(PatchEvent, object)> PatchEvents { get; }

        UniTask RunAsync(bool autoDownloadOnFoundNewVersion, CancellationToken cancellationToken = default);
        void Download();
        void Cancel();
    }
}