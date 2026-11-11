using System;
using System.Text;
using System.Threading;
using CycloneGames.InputSystem.Runtime;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CycloneGames.InputSystem.Sample
{
    /// <summary>
    /// Serves an authored configuration straight from a <see cref="TextAsset"/>, so a scene carries its own
    /// configuration instead of copying files into StreamingAssets before running.
    /// </summary>
    public sealed class TextAssetInputConfigurationSource : IInputConfigurationSource
    {
        private readonly TextAsset _asset;
        private readonly int _maximumBytes;

        public TextAssetInputConfigurationSource(
            TextAsset asset,
            int maximumBytes = FileInputConfigurationStore.DefaultMaximumBytes)
        {
            _asset = asset != null ? asset : throw new ArgumentNullException(nameof(asset));
            if (maximumBytes <= 0 || maximumBytes > FileInputConfigurationStore.MaximumSupportedBytes)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumBytes));
            }

            _maximumBytes = maximumBytes;
        }

        public UniTask<InputConfigurationReadResult> LoadAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return UniTask.FromResult(
                    InputConfigurationReadResult.Failure(
                        InputConfigurationStorageStatus.IoError,
                        "The configuration read was cancelled."));
            }

            string content = _asset.text;
            if (string.IsNullOrEmpty(content))
            {
                return UniTask.FromResult(
                    InputConfigurationReadResult.Failure(
                        InputConfigurationStorageStatus.NotFound,
                        "The configuration TextAsset is empty."));
            }

            if (Encoding.UTF8.GetByteCount(content) > _maximumBytes)
            {
                return UniTask.FromResult(
                    InputConfigurationReadResult.Failure(
                        InputConfigurationStorageStatus.TooLarge,
                        "The configuration TextAsset exceeds the allowed size."));
            }

            return UniTask.FromResult(InputConfigurationReadResult.Success(content));
        }
    }
}
