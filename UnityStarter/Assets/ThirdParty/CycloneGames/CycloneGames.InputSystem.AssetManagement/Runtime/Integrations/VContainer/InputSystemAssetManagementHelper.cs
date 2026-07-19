#if VCONTAINER_PRESENT
using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

using Cysharp.Threading.Tasks;

using CycloneGames.AssetManagement.Runtime;

namespace CycloneGames.InputSystem.Runtime.Integrations.VContainer
{
    /// <summary>
    /// Helper methods for loading input configuration from AssetManagement (YooAsset/Addressables).
    /// All methods work directly with IAssetPackage, without requiring IObjectResolver or IAssetModule.
    /// </summary>
    public static class InputSystemAssetManagementHelper
    {
        private const int MaximumConfigurationLocationCharacters = 512;
        private const int MaximumConfigurationLocationUtf8Bytes = 1024;
        private const int MaximumProviderFilePathCharacters = 4096;
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        private enum ConfigurationLoadAttemptStatus : byte
        {
            Success,
            CapabilityUnavailable,
            ProviderLoadFailed,
            PolicyRejected
        }

        private readonly struct ConfigurationLoadAttempt
        {
            public ConfigurationLoadAttemptStatus Status { get; }
            public string Content { get; }

            private ConfigurationLoadAttempt(
                ConfigurationLoadAttemptStatus status,
                string content)
            {
                Status = status;
                Content = content;
            }

            public static ConfigurationLoadAttempt Success(string content)
            {
                return new ConfigurationLoadAttempt(
                    ConfigurationLoadAttemptStatus.Success,
                    content ?? string.Empty);
            }

            public static ConfigurationLoadAttempt Failure(
                ConfigurationLoadAttemptStatus status)
            {
                return new ConfigurationLoadAttempt(status, null);
            }
        }

        /// <summary>
        /// Creates a default config loader from AssetManagement package.
        /// Note: User config is always loaded from PersistentData automatically by InputSystemVContainerInstaller.
        /// This method only creates the default config loader.
        /// Supports both TextAsset (for Addressables/Resources) and RawFile (for YooAsset) loading.
        /// </summary>
        /// <param name="package">AssetManagement package instance. Must be provided directly, not resolved from DI.</param>
        /// <param name="defaultConfigLocation">Location of default config in AssetManagement (e.g., "input_config.yaml" or "Assets/Config/input_config.yaml")</param>
        /// <param name="useTextAsset">If true, loads as TextAsset. If false, tries RawFile and falls back to TextAsset only when bounded raw-file capability is unavailable.</param>
        /// <param name="maximumBytes">Maximum accepted UTF-8 payload size.</param>
        /// <returns>Default config loader function that doesn't require resolver</returns>
        public static InputSystemDefaultConfigurationLoader CreateDefaultConfigLoader(
            IAssetPackage package,
            string defaultConfigLocation,
            bool useTextAsset = false,
            int maximumBytes = FileInputConfigurationStore.DefaultMaximumBytes)
        {
            ValidateMaximumBytes(maximumBytes);
            if (package == null)
            {
                CycloneGames.Logger.CLogger.LogError("[InputSystemAssetManagementHelper] Package cannot be null.");
                return _ => UniTask.FromResult<string>(null);
            }

            ValidateConfigurationLocation(defaultConfigLocation, nameof(defaultConfigLocation));

            return cancellationToken => LoadConfigWithFallback(
                package,
                defaultConfigLocation,
                useTextAsset,
                maximumBytes,
                cancellationToken);
        }

        private static async UniTask<string> LoadConfigAsTextAsset(
            IAssetPackage package,
            string location,
            int maximumBytes,
            CancellationToken cancellationToken)
        {
            IAssetHandle<UnityEngine.TextAsset> handle = null;
            try
            {
                await SwitchToUnityMainThreadAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                handle = package.LoadAssetAsync<UnityEngine.TextAsset>(
                    location,
                    cancellationToken: cancellationToken);
                await handle.Task;
                cancellationToken.ThrowIfCancellationRequested();
                await SwitchToUnityMainThreadAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                UnityEngine.TextAsset asset = handle.Asset;
                if (asset == null)
                {
                    CycloneGames.Logger.CLogger.LogWarning(
                        "[InputSystemAssetManagementHelper] TextAsset load completed without an asset.");
                    return null;
                }

                // This is a post-acquisition acceptance limit. The provider owns download,
                // decompression, cache, and native allocation budgets before this point.
                if (asset.dataSize > maximumBytes)
                {
                    CycloneGames.Logger.CLogger.LogError(
                        $"[InputSystemAssetManagementHelper] TextAsset exceeds the {maximumBytes}-byte acceptance limit.");
                    return null;
                }

                byte[] bytes = asset.bytes;
                if (bytes == null || bytes.Length > maximumBytes)
                {
                    CycloneGames.Logger.CLogger.LogError(
                        $"[InputSystemAssetManagementHelper] TextAsset payload exceeds the {maximumBytes}-byte acceptance limit.");
                    return null;
                }

                string content = StrictUtf8.GetString(bytes);
                CycloneGames.Logger.CLogger.LogInfo(
                    "[InputSystemAssetManagementHelper] Loaded configuration as TextAsset.");
                return content;
            }
            catch (Exception e) when (IsRecoverableException(e))
            {
                await SwitchToUnityMainThreadForCleanupAsync();
                CycloneGames.Logger.CLogger.LogError(
                    $"[InputSystemAssetManagementHelper] TextAsset loading failed ({e.GetType().Name}).");
                return null;
            }
            finally
            {
                await DisposeOnUnityMainThreadAsync(handle);
            }
        }


        /// <summary>
        /// Creates a loader function that loads config from AssetManagement package.
        /// Useful for hot-update scenarios where you can call this after the package is ready.
        /// Supports both TextAsset (for Addressables/Resources) and RawFile (for YooAsset) loading.
        /// </summary>
        /// <param name="package">AssetManagement package instance (IAssetPackage)</param>
        /// <param name="configLocation">Location of config in AssetManagement</param>
        /// <param name="useTextAsset">If true, loads as TextAsset. If false, tries RawFile and falls back to TextAsset only when bounded raw-file capability is unavailable.</param>
        /// <returns>Async function that loads and returns config content</returns>
        /// <param name="maximumBytes">Maximum accepted UTF-8 payload size.</param>
        public static InputSystemDefaultConfigurationLoader CreateConfigLoader(
            IAssetPackage package,
            string configLocation,
            bool useTextAsset = false,
            int maximumBytes = FileInputConfigurationStore.DefaultMaximumBytes)
        {
            ValidateMaximumBytes(maximumBytes);
            if (package == null)
            {
                CycloneGames.Logger.CLogger.LogError("[InputSystemAssetManagementHelper] Package cannot be null.");
                return _ => UniTask.FromResult<string>(null);
            }

            ValidateConfigurationLocation(configLocation, nameof(configLocation));

            return cancellationToken => LoadConfigWithFallback(
                package,
                configLocation,
                useTextAsset,
                maximumBytes,
                cancellationToken);
        }

        private static async UniTask<string> LoadConfigWithFallback(
            IAssetPackage package,
            string location,
            bool useTextAsset,
            int maximumBytes,
            CancellationToken cancellationToken)
        {
            if (useTextAsset)
            {
                return await LoadConfigAsTextAsset(
                    package,
                    location,
                    maximumBytes,
                    cancellationToken);
            }

            ConfigurationLoadAttempt rawAttempt;
            if (package is IAssetRawFileLoader rawFileLoader)
            {
                rawAttempt = await LoadConfigAsRawFile(
                    rawFileLoader,
                    location,
                    maximumBytes,
                    cancellationToken);
            }
            else
            {
                rawAttempt = ConfigurationLoadAttempt.Failure(
                    ConfigurationLoadAttemptStatus.CapabilityUnavailable);
            }

            switch (rawAttempt.Status)
            {
                case ConfigurationLoadAttemptStatus.Success:
                    await SwitchToUnityMainThreadAsync(cancellationToken);
                    CycloneGames.Logger.CLogger.LogInfo(
                        "[InputSystemAssetManagementHelper] Loaded configuration as RawFile.");
                    return rawAttempt.Content;

                case ConfigurationLoadAttemptStatus.CapabilityUnavailable:
                    CycloneGames.Logger.CLogger.LogInfo(
                        "[InputSystemAssetManagementHelper] Bounded RawFile capability is unavailable; trying TextAsset.");
                    return await LoadConfigAsTextAsset(
                        package,
                        location,
                        maximumBytes,
                        cancellationToken);

                case ConfigurationLoadAttemptStatus.ProviderLoadFailed:
                    CycloneGames.Logger.CLogger.LogError(
                        "[InputSystemAssetManagementHelper] RawFile provider load failed; TextAsset fallback was not attempted.");
                    return null;

                case ConfigurationLoadAttemptStatus.PolicyRejected:
                    CycloneGames.Logger.CLogger.LogError(
                        "[InputSystemAssetManagementHelper] RawFile content was rejected by bounded-read policy; TextAsset fallback was not attempted.");
                    return null;

                default:
                    throw new InvalidOperationException("Unknown RawFile configuration load status.");
            }
        }

        private static async UniTask<ConfigurationLoadAttempt> LoadConfigAsRawFile(
            IAssetRawFileLoader rawFileLoader,
            string location,
            int maximumBytes,
            CancellationToken cancellationToken)
        {
            IRawFileHandle rawFileHandle = null;
            try
            {
                await SwitchToUnityMainThreadAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                rawFileHandle = rawFileLoader.LoadRawFileAsync(
                    location,
                    cancellationToken: cancellationToken);
                if (rawFileHandle == null)
                {
                    return ConfigurationLoadAttempt.Failure(
                        ConfigurationLoadAttemptStatus.ProviderLoadFailed);
                }

                await rawFileHandle.Task;
                cancellationToken.ThrowIfCancellationRequested();
                await SwitchToUnityMainThreadAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (!rawFileHandle.IsDone || !string.IsNullOrEmpty(rawFileHandle.Error))
                {
                    return ConfigurationLoadAttempt.Failure(
                        ConfigurationLoadAttemptStatus.ProviderLoadFailed);
                }

                return await ReadBoundedRawFileAsync(
                    rawFileHandle.FilePath,
                    maximumBytes,
                    cancellationToken);
            }
            catch (Exception exception) when (IsRecoverableException(exception))
            {
                await SwitchToUnityMainThreadForCleanupAsync();
                CycloneGames.Logger.CLogger.LogWarning(
                    $"[InputSystemAssetManagementHelper] RawFile provider load failed ({exception.GetType().Name}).");
                return ConfigurationLoadAttempt.Failure(
                    ConfigurationLoadAttemptStatus.ProviderLoadFailed);
            }
            finally
            {
                await DisposeOnUnityMainThreadAsync(rawFileHandle);
            }
        }

        private static async UniTask<ConfigurationLoadAttempt> ReadBoundedRawFileAsync(
            string path,
            int maximumBytes,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                CycloneGames.Logger.CLogger.LogWarning(
                    "[InputSystemAssetManagementHelper] RawFile provider did not expose a local path for bounded reading.");
                return ConfigurationLoadAttempt.Failure(
                    ConfigurationLoadAttemptStatus.CapabilityUnavailable);
            }

            try
            {
                if (path.Length > MaximumProviderFilePathCharacters)
                {
                    CycloneGames.Logger.CLogger.LogWarning(
                        "[InputSystemAssetManagementHelper] RawFile provider path exceeds the bounded-read policy.");
                    return ConfigurationLoadAttempt.Failure(
                        ConfigurationLoadAttemptStatus.PolicyRejected);
                }

                if (path.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                {
                    path = new Uri(path).LocalPath;
                }

                if (!Path.IsPathRooted(path))
                {
                    CycloneGames.Logger.CLogger.LogWarning(
                        "[InputSystemAssetManagementHelper] RawFile provider path is not an absolute local path.");
                    return ConfigurationLoadAttempt.Failure(
                        ConfigurationLoadAttemptStatus.PolicyRejected);
                }

                if (!File.Exists(path))
                {
                    CycloneGames.Logger.CLogger.LogWarning(
                        "[InputSystemAssetManagementHelper] RawFile provider path does not exist.");
                    return ConfigurationLoadAttempt.Failure(
                        ConfigurationLoadAttemptStatus.ProviderLoadFailed);
                }

                using (var stream = new FileStream(
                           path,
                           FileMode.Open,
                           FileAccess.Read,
                           FileShare.Read,
                           4096,
                           FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    if (stream.Length > maximumBytes)
                    {
                        CycloneGames.Logger.CLogger.LogError(
                            $"[InputSystemAssetManagementHelper] RawFile exceeds the {maximumBytes}-byte limit.");
                        return ConfigurationLoadAttempt.Failure(
                            ConfigurationLoadAttemptStatus.PolicyRejected);
                    }

                    int length = checked((int)stream.Length);
                    var bytes = new byte[length];
                    int totalRead = 0;
                    while (totalRead < length)
                    {
                        int read = await stream.ReadAsync(
                            bytes,
                            totalRead,
                            length - totalRead,
                            cancellationToken);
                        if (read == 0) break;
                        totalRead += read;
                    }

                    if (totalRead != length || stream.Length != length)
                    {
                        CycloneGames.Logger.CLogger.LogWarning(
                            "[InputSystemAssetManagementHelper] RawFile changed while it was being read.");
                        return ConfigurationLoadAttempt.Failure(
                            ConfigurationLoadAttemptStatus.PolicyRejected);
                    }

                    return ConfigurationLoadAttempt.Success(
                        StrictUtf8.GetString(bytes, 0, totalRead));
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (DecoderFallbackException)
            {
                CycloneGames.Logger.CLogger.LogWarning(
                    "[InputSystemAssetManagementHelper] RawFile content is not valid UTF-8.");
                return ConfigurationLoadAttempt.Failure(
                    ConfigurationLoadAttemptStatus.PolicyRejected);
            }
            catch (Exception exception) when (
                exception is UriFormatException ||
                exception is ArgumentException ||
                exception is NotSupportedException)
            {
                CycloneGames.Logger.CLogger.LogWarning(
                    $"[InputSystemAssetManagementHelper] RawFile path was rejected ({exception.GetType().Name}).");
                return ConfigurationLoadAttempt.Failure(
                    ConfigurationLoadAttemptStatus.PolicyRejected);
            }
            catch (Exception exception) when (IsRecoverableException(exception))
            {
                CycloneGames.Logger.CLogger.LogWarning(
                    $"[InputSystemAssetManagementHelper] Bounded RawFile read failed ({exception.GetType().Name}).");
                return ConfigurationLoadAttempt.Failure(
                    ConfigurationLoadAttemptStatus.ProviderLoadFailed);
            }
        }

        private static void ValidateConfigurationLocation(string location, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(location))
            {
                throw new ArgumentException("A configuration location is required.", parameterName);
            }

            // Reject by character count before Unicode categorization or provider parsing.
            if (location.Length > MaximumConfigurationLocationCharacters)
            {
                throw new ArgumentException(
                    $"Configuration locations cannot exceed {MaximumConfigurationLocationCharacters} characters.",
                    parameterName);
            }

            for (int index = 0; index < location.Length; index++)
            {
                char current = location[index];
                UnicodeCategory category;
                if (char.IsHighSurrogate(current))
                {
                    if (index + 1 >= location.Length || !char.IsLowSurrogate(location[index + 1]))
                    {
                        throw new ArgumentException(
                            "Configuration locations must contain valid Unicode text.",
                            parameterName);
                    }

                    category = CharUnicodeInfo.GetUnicodeCategory(location, index);
                    index++;
                }
                else if (char.IsLowSurrogate(current))
                {
                    throw new ArgumentException(
                        "Configuration locations must contain valid Unicode text.",
                        parameterName);
                }
                else
                {
                    category = char.GetUnicodeCategory(current);
                }

                if (category == UnicodeCategory.Control ||
                    category == UnicodeCategory.Format ||
                    category == UnicodeCategory.PrivateUse ||
                    category == UnicodeCategory.LineSeparator ||
                    category == UnicodeCategory.ParagraphSeparator)
                {
                    throw new ArgumentException(
                        "Configuration locations cannot contain control, format, line-separator, or private-use characters.",
                        parameterName);
                }
            }

            int utf8Bytes;
            try
            {
                utf8Bytes = StrictUtf8.GetByteCount(location);
            }
            catch (EncoderFallbackException)
            {
                throw new ArgumentException(
                    "Configuration locations must contain valid Unicode text.",
                    parameterName);
            }

            if (utf8Bytes > MaximumConfigurationLocationUtf8Bytes)
            {
                throw new ArgumentException(
                    $"Configuration locations cannot exceed {MaximumConfigurationLocationUtf8Bytes} UTF-8 bytes.",
                    parameterName);
            }
        }

        private static async UniTask SwitchToUnityMainThreadAsync(
            CancellationToken cancellationToken)
        {
            if (!PlayerLoopHelper.IsMainThread)
            {
                await UniTask.SwitchToMainThread(PlayerLoopTiming.Update, cancellationToken);
            }
        }

        private static async UniTask SwitchToUnityMainThreadForCleanupAsync()
        {
            if (!PlayerLoopHelper.IsMainThread)
            {
                await UniTask.SwitchToMainThread(PlayerLoopTiming.Update);
            }
        }

        private static async UniTask DisposeOnUnityMainThreadAsync(IDisposable disposable)
        {
            if (disposable == null)
            {
                return;
            }

            await SwitchToUnityMainThreadForCleanupAsync();
            try
            {
                disposable.Dispose();
            }
            catch (Exception exception) when (IsRecoverableException(exception))
            {
                CycloneGames.Logger.CLogger.LogError(
                    $"[InputSystemAssetManagementHelper] Provider handle disposal failed ({exception.GetType().Name}).");
            }
        }

        private static void ValidateMaximumBytes(int maximumBytes)
        {
            if (maximumBytes <= 0 || maximumBytes > FileInputConfigurationStore.MaximumSupportedBytes)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumBytes));
            }
        }

        private static bool IsRecoverableException(Exception exception)
        {
            return exception is not OperationCanceledException &&
                   exception is not OutOfMemoryException &&
                   exception is not AccessViolationException &&
                   exception is not StackOverflowException;
        }

    }
}
#endif
