using System;
using System.Threading;
using CycloneGames.Logger;
using Cysharp.Threading.Tasks;

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
                options.Mode,
                options.PersistDefaultToUser,
                forceReinitialize,
                cancellationToken);
        }

        private static async UniTask<InputSystemLoadResult> LoadAndInitializeCoreAsync(
            IInputConfigurationSource defaultSource,
            string defaultKey,
            IInputConfigurationStore userStore,
            string userKey,
            InputManager manager,
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
                ? manager.ReinitializeWithResult(selectedContent)
                : manager.InitializeWithResult(selectedContent);
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
                    ? manager.ReinitializeWithResult(selectedContent)
                    : manager.InitializeWithResult(selectedContent);
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

    }
}
