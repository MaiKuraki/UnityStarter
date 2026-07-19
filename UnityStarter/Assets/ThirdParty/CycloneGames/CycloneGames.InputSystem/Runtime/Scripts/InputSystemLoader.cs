using System;
using System.IO;
using System.Threading;
using CycloneGames.Logger;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CycloneGames.InputSystem.Runtime
{
    /// <summary>
    /// Controls whether bootstrap is skipped, tolerates an absent configuration, or requires one.
    /// </summary>
    public enum InputSystemBootstrapMode
    {
        Disabled,
        Optional,
        Required
    }

    /// <summary>
    /// Immutable composition-root policy for configuration discovery and optional user persistence.
    /// The runtime core does not assign meaning to source keys or assume a Unity asset location.
    /// </summary>
    public sealed class InputSystemBootstrapOptions
    {
        public InputSystemBootstrapMode Mode { get; }
        public IInputConfigurationSource DefaultSource { get; }
        public string DefaultKey { get; }
        public IInputConfigurationStore UserStore { get; }
        public string UserKey { get; }
        public bool PersistDefaultToUser { get; }

        public static InputSystemBootstrapOptions Disabled { get; } =
            new InputSystemBootstrapOptions(InputSystemBootstrapMode.Disabled);

        public InputSystemBootstrapOptions(
            InputSystemBootstrapMode mode,
            IInputConfigurationSource defaultSource = null,
            string defaultKey = null,
            IInputConfigurationStore userStore = null,
            string userKey = null,
            bool persistDefaultToUser = false)
        {
            if ((uint)mode > (uint)InputSystemBootstrapMode.Required)
            {
                throw new ArgumentOutOfRangeException(nameof(mode));
            }

            if (mode != InputSystemBootstrapMode.Disabled)
            {
                ValidateSourceKey(defaultSource, defaultKey, nameof(defaultSource), nameof(defaultKey));
                ValidateSourceKey(userStore, userKey, nameof(userStore), nameof(userKey));
                if (defaultSource == null && userStore == null)
                {
                    throw new ArgumentException(
                        "Optional and required bootstrap modes need at least one configuration source.");
                }
            }

            if (persistDefaultToUser && userStore == null)
            {
                throw new ArgumentException(
                    "Default persistence requires an explicit user store.",
                    nameof(persistDefaultToUser));
            }

            Mode = mode;
            DefaultSource = defaultSource;
            DefaultKey = defaultKey;
            UserStore = userStore;
            UserKey = userKey;
            PersistDefaultToUser = persistDefaultToUser;
        }

        private static void ValidateSourceKey(
            object source,
            string key,
            string sourceParameter,
            string keyParameter)
        {
            if (source == null)
            {
                if (!string.IsNullOrEmpty(key))
                {
                    throw new ArgumentException(
                        "A configuration key cannot be supplied without its source.",
                        keyParameter);
                }
                return;
            }

            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException(
                    "A configuration source requires a non-empty logical key.",
                    sourceParameter);
            }
        }
    }

    public enum InputSystemLoadStatus
    {
        SuccessFromUserConfiguration,
        SuccessFromDefaultConfiguration,
        DefaultConfigurationUnavailable,
        ConfigurationInvalid,
        InitializationFailed,
        NotConfigured
    }

    public enum InputSystemPersistenceStatus
    {
        NotRequested,
        Succeeded,
        Failed,
        Canceled,
        SerializationFailed
    }

    public readonly struct InputSystemLoadResult
    {
        public InputSystemLoadStatus Status { get; }
        public InputConfigurationStorageStatus UserStorageStatus { get; }
        public string Error { get; }
        public InputSystemPersistenceStatus PersistenceStatus { get; }
        public string PersistenceError { get; }
        public bool IsSuccess =>
            Status == InputSystemLoadStatus.SuccessFromUserConfiguration ||
            Status == InputSystemLoadStatus.SuccessFromDefaultConfiguration;
        public bool IsBootstrapComplete =>
            IsSuccess || Status == InputSystemLoadStatus.NotConfigured;
        public bool IsPersistenceComplete =>
            PersistenceStatus == InputSystemPersistenceStatus.NotRequested ||
            PersistenceStatus == InputSystemPersistenceStatus.Succeeded;

        public InputSystemLoadResult(
            InputSystemLoadStatus status,
            InputConfigurationStorageStatus userStorageStatus,
            string error = null)
            : this(
                status,
                userStorageStatus,
                error,
                InputSystemPersistenceStatus.NotRequested,
                null)
        {
        }

        public InputSystemLoadResult(
            InputSystemLoadStatus status,
            InputConfigurationStorageStatus userStorageStatus,
            string error,
            InputSystemPersistenceStatus persistenceStatus,
            string persistenceError)
        {
            Status = status;
            UserStorageStatus = userStorageStatus;
            Error = error;
            PersistenceStatus = persistenceStatus;
            PersistenceError = persistenceError;
        }
    }

    /// <summary>
    /// Coordinates bounded configuration loading and commits only a validated configuration to an InputManager.
    /// </summary>
    public static class InputSystemLoader
    {
        private const string LogPrefix = "[InputSystemLoader]";

        public static UniTask InitializeAsync(string defaultConfigUri, string userConfigUri)
        {
            return InitializeAsync(defaultConfigUri, userConfigUri, default);
        }

        /// <summary>
        /// Compatibility entry point. Default files must be inside StreamingAssets and user files must be inside
        /// persistentDataPath. Use the source/store overload for WebGL or platform-specific persistence.
        /// </summary>
        public static async UniTask InitializeAsync(
            string defaultConfigUri,
            string userConfigUri,
            CancellationToken cancellationToken)
        {
            if (!TryCreateCompatibilityStorage(
                    userConfigUri,
                    out IInputConfigurationStore userStore,
                    out string userKey,
                    out string storageError))
            {
                CLogger.LogWarning($"{LogPrefix} User configuration storage is unavailable: {storageError}");
            }

            InputSystemLoadResult result = await LoadAndInitializeCompatibilityAsync(
                new UriInputConfigurationSource(),
                defaultConfigUri,
                userStore,
                userKey,
                InputManager.Instance,
                userStore == null ? null : userConfigUri,
                false,
                cancellationToken);

            if (!result.IsSuccess)
            {
                CLogger.LogError($"{LogPrefix} Initialization failed: {result.Status}. {result.Error}");
            }
        }

        public static UniTask<InputSystemLoadResult> LoadAndInitializeAsync(
            IInputConfigurationSource defaultSource,
            string defaultKey,
            IInputConfigurationStore userStore,
            string userKey,
            InputManager manager,
            bool forceReinitialize = false,
            CancellationToken cancellationToken = default)
        {
            if (defaultSource == null)
            {
                throw new ArgumentNullException(nameof(defaultSource));
            }

            return LoadAndInitializeCoreAsync(
                defaultSource,
                defaultKey,
                userStore,
                userKey,
                manager,
                null,
                InputSystemBootstrapMode.Required,
                true,
                forceReinitialize,
                cancellationToken);
        }

        public static UniTask<InputSystemLoadResult> LoadAndInitializeAsync(
            InputSystemBootstrapOptions options,
            InputManager manager,
            bool forceReinitialize = false,
            CancellationToken cancellationToken = default)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            return LoadAndInitializeCoreAsync(
                options.DefaultSource,
                options.DefaultKey,
                options.UserStore,
                options.UserKey,
                manager,
                null,
                options.Mode,
                options.PersistDefaultToUser,
                forceReinitialize,
                cancellationToken);
        }

        internal static UniTask<InputSystemLoadResult> LoadAndInitializeCompatibilityAsync(
            IInputConfigurationSource defaultSource,
            string defaultKey,
            IInputConfigurationStore userStore,
            string userKey,
            InputManager manager,
            string managerUserConfigUri,
            bool forceReinitialize = false,
            CancellationToken cancellationToken = default)
        {
            return LoadAndInitializeCoreAsync(
                defaultSource,
                defaultKey,
                userStore,
                userKey,
                manager,
                managerUserConfigUri,
                InputSystemBootstrapMode.Required,
                true,
                forceReinitialize,
                cancellationToken);
        }

        private static async UniTask<InputSystemLoadResult> LoadAndInitializeCoreAsync(
            IInputConfigurationSource defaultSource,
            string defaultKey,
            IInputConfigurationStore userStore,
            string userKey,
            InputManager manager,
            string managerUserConfigUri,
            InputSystemBootstrapMode bootstrapMode,
            bool persistDefaultToUser,
            bool forceReinitialize,
            CancellationToken cancellationToken)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }

            if (bootstrapMode == InputSystemBootstrapMode.Disabled)
            {
                return new InputSystemLoadResult(
                    InputSystemLoadStatus.NotConfigured,
                    InputConfigurationStorageStatus.Unsupported);
            }

            InputConfigurationReadResult userRead = userStore == null || string.IsNullOrEmpty(userKey)
                ? InputConfigurationReadResult.Failure(InputConfigurationStorageStatus.Unsupported)
                : await userStore.LoadAsync(userKey, cancellationToken);

            string userValidationError = null;
            bool useUserConfiguration = userRead.IsSuccess;
            if (useUserConfiguration && userRead.WasRecoveredFromBackup)
            {
                CLogger.LogWarning(
                    $"{LogPrefix} The primary user configuration was unavailable; " +
                    "the last committed backup is active for this session.");
            }

            InputConfigurationReadResult defaultRead = default;
            string selectedContent;
            if (useUserConfiguration)
            {
                selectedContent = userRead.Content;
            }
            else
            {
                defaultRead = defaultSource == null
                    ? InputConfigurationReadResult.Failure(
                        InputConfigurationStorageStatus.NotFound,
                        "No default configuration source is configured.")
                    : await defaultSource.LoadAsync(defaultKey, cancellationToken);
                if (!defaultRead.IsSuccess)
                {
                    if (bootstrapMode == InputSystemBootstrapMode.Optional &&
                        defaultRead.Status == InputConfigurationStorageStatus.NotFound &&
                        (userRead.Status == InputConfigurationStorageStatus.NotFound ||
                         userRead.Status == InputConfigurationStorageStatus.Unsupported))
                    {
                        return new InputSystemLoadResult(
                            InputSystemLoadStatus.NotConfigured,
                            userRead.Status);
                    }

                    return new InputSystemLoadResult(
                        InputSystemLoadStatus.DefaultConfigurationUnavailable,
                        userRead.Status,
                        defaultRead.Error);
                }

                selectedContent = defaultRead.Content;
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!PlayerLoopHelper.IsMainThread)
            {
                await UniTask.SwitchToMainThread(PlayerLoopTiming.Update, cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();

            if (!forceReinitialize && manager.IsInitialized)
            {
                return new InputSystemLoadResult(
                    InputSystemLoadStatus.InitializationFailed,
                    userRead.Status,
                    "InputManager is already initialized. Set forceReinitialize only after removing active players.");
            }

            InputManagerInitializationResult initialization = forceReinitialize
                ? manager.ReinitializeWithResult(selectedContent, managerUserConfigUri)
                : manager.InitializeWithResult(selectedContent, managerUserConfigUri);
            if (!initialization.IsSuccess &&
                useUserConfiguration &&
                IsConfigurationContentFailure(initialization.Status))
            {
                userValidationError =
                    $"{initialization.Status}: {initialization.Message}";
                defaultRead = defaultSource == null
                    ? InputConfigurationReadResult.Failure(
                        InputConfigurationStorageStatus.NotFound,
                        "No default configuration source is configured.")
                    : await defaultSource.LoadAsync(defaultKey, cancellationToken);
                if (!defaultRead.IsSuccess)
                {
                    return new InputSystemLoadResult(
                        InputSystemLoadStatus.DefaultConfigurationUnavailable,
                        userRead.Status,
                        defaultRead.Error);
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (!PlayerLoopHelper.IsMainThread)
                {
                    await UniTask.SwitchToMainThread(PlayerLoopTiming.Update, cancellationToken);
                }
                cancellationToken.ThrowIfCancellationRequested();

                selectedContent = defaultRead.Content;
                useUserConfiguration = false;
                initialization = forceReinitialize
                    ? manager.ReinitializeWithResult(selectedContent, managerUserConfigUri)
                    : manager.InitializeWithResult(selectedContent, managerUserConfigUri);
            }

            if (!initialization.IsSuccess)
            {
                return new InputSystemLoadResult(
                    !useUserConfiguration && IsConfigurationContentFailure(initialization.Status)
                        ? InputSystemLoadStatus.ConfigurationInvalid
                        : InputSystemLoadStatus.InitializationFailed,
                    userRead.Status,
                    $"{initialization.Status}: {initialization.Message}");
            }

            if (persistDefaultToUser &&
                !useUserConfiguration &&
                userRead.Status == InputConfigurationStorageStatus.NotFound &&
                userStore != null &&
                !string.IsNullOrEmpty(userKey))
            {
                InputSystemPersistenceStatus persistenceStatus;
                string persistenceError = null;
                string persistenceContent = defaultRead.Content;
                if (initialization.Validation?.WasMigrated == true &&
                    !InputConfigurationYamlCodec.TrySerialize(
                        initialization.Validation.Configuration,
                        out persistenceContent,
                        out string serializationError))
                {
                    CLogger.LogWarning(
                        $"{LogPrefix} Initialized from migrated defaults but could not serialize the prepared configuration: " +
                        serializationError);
                    persistenceStatus = InputSystemPersistenceStatus.SerializationFailed;
                    persistenceError = serializationError;
                    persistenceContent = null;
                }
                else
                {
                    persistenceStatus = InputSystemPersistenceStatus.NotRequested;
                }

                if (persistenceContent != null)
                {
                    try
                    {
                        // Runtime commit is the final caller-cancellation point. Keep the token on the
                        // storage operation so a custom implementation remains stoppable, but convert
                        // post-commit cancellation into an explicit persistence status.
                        InputConfigurationStoreResult saveResult =
                            await userStore.SaveAsync(userKey, persistenceContent, cancellationToken);
                        persistenceStatus = saveResult.IsSuccess
                            ? InputSystemPersistenceStatus.Succeeded
                            : InputSystemPersistenceStatus.Failed;
                        persistenceError = saveResult.Error;
                        if (!saveResult.IsSuccess)
                        {
                            CLogger.LogWarning(
                                $"{LogPrefix} Runtime initialization succeeded, but user configuration persistence failed: " +
                                $"{saveResult.Status}. {saveResult.Error}");
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        persistenceStatus = InputSystemPersistenceStatus.Canceled;
                        persistenceError = "Persistence was canceled after runtime commit.";
                        CLogger.LogWarning(
                            $"{LogPrefix} Runtime initialization succeeded, but user configuration persistence was canceled.");
                    }
                    catch (Exception exception) when (IsRecoverableException(exception))
                    {
                        persistenceStatus = InputSystemPersistenceStatus.Failed;
                        persistenceError = $"Persistence provider failed ({exception.GetType().Name}).";
                        CLogger.LogWarning(
                            $"{LogPrefix} Runtime initialization succeeded, but the persistence provider failed " +
                            $"({exception.GetType().Name}).");
                    }
                }

                return new InputSystemLoadResult(
                    InputSystemLoadStatus.SuccessFromDefaultConfiguration,
                    userRead.Status,
                    null,
                    persistenceStatus,
                    persistenceError);
            }
            else if (userRead.IsSuccess && !useUserConfiguration)
            {
                CLogger.LogWarning(
                    $"{LogPrefix} User configuration is invalid and was preserved. " +
                    $"Defaults were used for this session. {userValidationError}");
            }

            return new InputSystemLoadResult(
                useUserConfiguration
                    ? InputSystemLoadStatus.SuccessFromUserConfiguration
                    : InputSystemLoadStatus.SuccessFromDefaultConfiguration,
                userRead.Status);
        }

        public static UniTask<bool> ResetToDefaultAsync(string defaultConfigUri, string userConfigUri)
        {
            return ResetToDefaultAsync(defaultConfigUri, userConfigUri, default);
        }

        /// <summary>
        /// Validates and commits the default to the runtime, then atomically persists it as user configuration.
        /// A persistence failure leaves the validated default active for the current session and retains the prior file.
        /// </summary>
        public static async UniTask<bool> ResetToDefaultAsync(
            string defaultConfigUri,
            string userConfigUri,
            CancellationToken cancellationToken)
        {
            if (!TryCreateCompatibilityStorage(
                    userConfigUri,
                    out IInputConfigurationStore store,
                    out string userKey,
                    out string error))
            {
                CLogger.LogError($"{LogPrefix} Cannot reset user configuration: {error}");
                return false;
            }

            var source = new UriInputConfigurationSource();
            InputConfigurationReadResult defaultRead = await source.LoadAsync(defaultConfigUri, cancellationToken);
            if (!defaultRead.IsSuccess)
            {
                CLogger.LogError(
                    $"{LogPrefix} Cannot reset because the default configuration is unavailable. " +
                    $"{defaultRead.Error}");
                return false;
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!PlayerLoopHelper.IsMainThread)
            {
                await UniTask.SwitchToMainThread(PlayerLoopTiming.Update, cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();

            InputManagerInitializationResult initialization =
                InputManager.Instance.ReinitializeWithResult(defaultRead.Content, null);
            if (!initialization.IsSuccess)
            {
                CLogger.LogError(
                    $"{LogPrefix} Runtime reset was rejected before persistence: " +
                    $"{initialization.Status}. {initialization.Message}");
                return false;
            }

            string persistenceContent = defaultRead.Content;
            if (initialization.Validation?.WasMigrated == true &&
                !InputConfigurationYamlCodec.TrySerialize(
                    initialization.Validation.Configuration,
                    out persistenceContent,
                    out string serializationError))
            {
                CLogger.LogError(
                    $"{LogPrefix} The migrated default is active for this session, but serialization failed: " +
                    serializationError);
                return false;
            }

            InputConfigurationStoreResult save;
            try
            {
                // Runtime commit is the final caller-cancellation point; see LoadAndInitializeAsync.
                save = await store.SaveAsync(userKey, persistenceContent, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                CLogger.LogWarning(
                    $"{LogPrefix} The default is active for this session, but persistence was canceled.");
                return false;
            }
            catch (Exception exception) when (IsRecoverableException(exception))
            {
                CLogger.LogError(
                    $"{LogPrefix} The default is active for this session, but the persistence provider failed " +
                    $"({exception.GetType().Name}).");
                return false;
            }
            if (!save.IsSuccess)
            {
                CLogger.LogError(
                    $"{LogPrefix} The default is active for this session, but persistence failed: " +
                    $"{save.Status}. {save.Error}");
                return false;
            }

            return true;
        }

        [Obsolete("Use an explicit IInputConfigurationStore. This compatibility API is confined to persistentDataPath.")]
        public static bool TryDeleteUserConfigFile(string userConfigUri)
        {
            if (!TryCreateCompatibilityStorage(
                    userConfigUri,
                    out IInputConfigurationStore store,
                    out string userKey,
                    out _))
            {
                return false;
            }

            // The obsolete synchronous facade must never queue work behind an async path gate: doing
            // so can deadlock the Unity main thread or outlive a false return. Busy paths fail closed.
            return store is FileInputConfigurationStore fileStore &&
                   fileStore.TryDeleteSynchronously(userKey).IsSuccess;
        }

        private static bool IsConfigurationContentFailure(InputManagerInitializationStatus status)
        {
            return status == InputManagerInitializationStatus.EmptyContent ||
                   status == InputManagerInitializationStatus.ParseFailed ||
                   status == InputManagerInitializationStatus.ValidationFailed ||
                   status == InputManagerInitializationStatus.InputSystemPreflightFailed;
        }

        private static bool IsRecoverableException(Exception exception)
        {
            return exception is not OutOfMemoryException &&
                   exception is not AccessViolationException &&
                   exception is not StackOverflowException;
        }

        private static bool TryCreateCompatibilityStorage(
            string userConfigUri,
            out IInputConfigurationStore store,
            out string key,
            out string error)
        {
            store = null;
            key = null;
            error = null;

#if UNITY_WEBGL && !UNITY_EDITOR
            error = "WebGL requires an explicit IInputConfigurationStore backed by browser storage.";
            return false;
#else
            if (string.IsNullOrWhiteSpace(userConfigUri))
            {
                error = "A user configuration path is required.";
                return false;
            }

            if (!TryGetLocalPath(userConfigUri, out string candidate))
            {
                error = "User configuration must be a local path.";
                return false;
            }

            string root;
            try
            {
                root = Path.GetFullPath(Application.persistentDataPath);
                candidate = Path.GetFullPath(candidate);
            }
            catch (Exception exception) when (
                exception is ArgumentException ||
                exception is NotSupportedException ||
                exception is PathTooLongException)
            {
                error = $"The user configuration path is invalid ({exception.GetType().Name}).";
                return false;
            }

            string rootPrefix = root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? root
                : root + Path.DirectorySeparatorChar;
            StringComparison comparison =
                Application.platform == RuntimePlatform.WindowsEditor ||
                Application.platform == RuntimePlatform.WindowsPlayer
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;
            if (!candidate.StartsWith(rootPrefix, comparison))
            {
                error = "User configuration must be inside persistentDataPath.";
                return false;
            }

            key = candidate.Substring(rootPrefix.Length)
                .Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/');
            store = new FileInputConfigurationStore(root);
            return true;
#endif
        }

        private static bool TryGetLocalPath(string value, out string path)
        {
            path = null;
            try
            {
                if (value.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                {
                    var parsed = new Uri(value);
                    if (!parsed.IsFile)
                    {
                        return false;
                    }

                    path = parsed.LocalPath;
                    return true;
                }

                if (Path.IsPathRooted(value))
                {
                    path = value;
                    return true;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }
    }

    /// <summary>
    /// Restricted compatibility facade. Writes and deletes are accepted only inside persistentDataPath.
    /// </summary>
    [Obsolete("Use IInputConfigurationSource and IInputConfigurationStore with InputSystemLoader.LoadAndInitializeAsync.")]
    public static class InputConfigurationFileLoader
    {
        public static async UniTask<(bool Success, string Content)> LoadTextFromUriAsync(
            string uri,
            string logPrefix,
            CancellationToken cancellationToken = default)
        {
            InputConfigurationReadResult result;
            if (TryCreatePersistentStore(uri, out IInputConfigurationStore store, out string key))
            {
                result = await store.LoadAsync(key, cancellationToken);
            }
            else
            {
                var source = new UriInputConfigurationSource();
                result = await source.LoadAsync(uri, cancellationToken);
            }

            if (!result.IsSuccess && result.Status != InputConfigurationStorageStatus.NotFound)
            {
                CLogger.LogWarning($"{logPrefix} Configuration load failed: {result.Status}. {result.Error}");
            }

            return (result.IsSuccess, result.Content);
        }

        public static async UniTask<bool> SaveTextToUriAsync(
            string uri,
            string content,
            string logPrefix,
            CancellationToken cancellationToken = default)
        {
            if (!TryCreatePersistentStore(uri, out IInputConfigurationStore store, out string key))
            {
                CLogger.LogWarning($"{logPrefix} Write rejected because the path is outside persistentDataPath.");
                return false;
            }

            InputConfigurationStoreResult result = await store.SaveAsync(key, content, cancellationToken);
            if (!result.IsSuccess)
            {
                CLogger.LogWarning($"{logPrefix} Configuration save failed: {result.Status}. {result.Error}");
            }

            return result.IsSuccess;
        }

        public static async UniTask<bool> DeleteTextAtUriAsync(
            string uri,
            string logPrefix,
            CancellationToken cancellationToken = default)
        {
            if (!TryCreatePersistentStore(uri, out IInputConfigurationStore store, out string key))
            {
                CLogger.LogWarning($"{logPrefix} Delete rejected because the path is outside persistentDataPath.");
                return false;
            }

            InputConfigurationStoreResult result = await store.DeleteAsync(key, cancellationToken);
            return result.IsSuccess;
        }

        public static bool TryDeleteTextAtUri(string uri, string logPrefix)
        {
            return DeleteTextAtUriAsync(uri, logPrefix).GetAwaiter().GetResult();
        }

        public static bool TryGetLocalFilePath(string uri, out string filePath)
        {
            filePath = null;
            if (!TryCreatePersistentStore(uri, out _, out _))
            {
                return false;
            }

            try
            {
                filePath = uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
                    ? new Uri(uri).LocalPath
                    : Path.GetFullPath(uri);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryCreatePersistentStore(
            string uri,
            out IInputConfigurationStore store,
            out string key)
        {
            store = null;
            key = null;
#if UNITY_WEBGL && !UNITY_EDITOR
            return false;
#else
            if (string.IsNullOrWhiteSpace(uri))
            {
                return false;
            }

            string path;
            try
            {
                path = uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
                    ? new Uri(uri).LocalPath
                    : Path.GetFullPath(uri);
            }
            catch
            {
                return false;
            }

            string root = Path.GetFullPath(Application.persistentDataPath);
            string rootPrefix = root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? root
                : root + Path.DirectorySeparatorChar;
            StringComparison comparison =
                Application.platform == RuntimePlatform.WindowsEditor ||
                Application.platform == RuntimePlatform.WindowsPlayer
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;
            if (!path.StartsWith(rootPrefix, comparison))
            {
                return false;
            }

            key = path.Substring(rootPrefix.Length);
            store = new FileInputConfigurationStore(root);
            return true;
#endif
        }
    }
}
