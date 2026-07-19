using System;
using System.Threading;

using Cysharp.Threading.Tasks;

namespace CycloneGames.AssetManagement.Runtime
{
    public enum AssetStorageCapacityStatus : byte
    {
        Unknown = 0,
        Available = 1,
        Insufficient = 2,
        Failed = 3,
    }

    public readonly struct AssetStoragePreflightRequest
    {
        public readonly long RequiredFreeBytes;

        public AssetStoragePreflightRequest(long requiredFreeBytes)
        {
            if (requiredFreeBytes < 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(requiredFreeBytes));
            }

            RequiredFreeBytes = requiredFreeBytes;
        }
    }

    public readonly struct AssetStoragePreflightResult
    {
        public readonly AssetStorageCapacityStatus Status;
        public readonly long AvailableBytes;
        public readonly string StorageLocation;
        public readonly string Error;

        public AssetStoragePreflightResult(
            AssetStorageCapacityStatus status,
            long availableBytes = -1L,
            string storageLocation = null,
            string error = null)
        {
            if (status < AssetStorageCapacityStatus.Unknown ||
                status > AssetStorageCapacityStatus.Failed)
            {
                throw new ArgumentOutOfRangeException(nameof(status));
            }

            if ((status == AssetStorageCapacityStatus.Available ||
                 status == AssetStorageCapacityStatus.Insufficient) &&
                availableBytes < 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(availableBytes));
            }

            Status = status;
            AvailableBytes = availableBytes;
            StorageLocation = storageLocation;
            Error = error;
        }

        public static AssetStoragePreflightResult Unknown(string error = null)
        {
            return new AssetStoragePreflightResult(AssetStorageCapacityStatus.Unknown, error: error);
        }
    }

    /// <summary>
    /// Optional provider capability for platform-aware cache quota and free-space checks.
    /// Implementations must return Unknown when the platform cannot report a reliable capacity.
    /// </summary>
    public interface IAssetStoragePreflight
    {
        UniTask<AssetStoragePreflightResult> CheckStorageAsync(
            AssetStoragePreflightRequest request,
            CancellationToken cancellationToken = default);
    }

}
