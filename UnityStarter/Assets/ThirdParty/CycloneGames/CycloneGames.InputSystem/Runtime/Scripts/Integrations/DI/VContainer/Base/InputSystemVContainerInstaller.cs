#if VCONTAINER_PRESENT
using System;
using System.Threading;

using UnityEngine;

using Cysharp.Threading.Tasks;
using VContainer;
using VContainer.Unity;

using CycloneGames.IO.Unity;
using CycloneGames.Logger;

namespace CycloneGames.InputSystem.Runtime.Integrations.VContainer
{
    public delegate UniTask<string> InputSystemDefaultConfigurationLoader(
        CancellationToken cancellationToken);

    public delegate UniTask<string> InputSystemPackageConfigurationLoader(
        object package,
        string configLocation,
        CancellationToken cancellationToken);

    /// <summary>
    /// Registers a container-owned InputManager and lifecycle-safe initialization adapters.
    /// </summary>
    public sealed class InputSystemVContainerInstaller : IInstaller
    {
        private readonly string _defaultConfigFileName;
        private readonly string _userConfigFileName;
        private readonly InputSystemDefaultConfigurationLoader _defaultConfigLoader;
        private readonly InputSystemPackageConfigurationLoader _packageConfigurationLoader;
        private readonly InputSystemBootstrapOptions _bootstrapOptions;
        private readonly Func<IObjectResolver, UniTask> _postInitCallback;
        private readonly bool _autoInitialize;

        public InputSystemVContainerInstaller(
            InputSystemBootstrapOptions bootstrapOptions,
            bool autoInitialize = true,
            Func<IObjectResolver, UniTask> postInitCallback = null)
        {
            _bootstrapOptions = bootstrapOptions ??
                throw new ArgumentNullException(nameof(bootstrapOptions));
            _autoInitialize = autoInitialize;
            _postInitCallback = postInitCallback;
        }

        public InputSystemVContainerInstaller(
            string defaultConfigFileName = "input_config.yaml",
            string userConfigFileName = "user_input_settings.yaml",
            bool autoInitialize = true,
            Func<IObjectResolver, UniTask> postInitCallback = null)
            : this(
                defaultConfigFileName,
                userConfigFileName,
                null,
                autoInitialize,
                postInitCallback)
        {
        }

        public InputSystemVContainerInstaller(
            string defaultConfigFileName,
            string userConfigFileName,
            InputSystemPackageConfigurationLoader packageConfigurationLoader,
            bool autoInitialize = true,
            Func<IObjectResolver, UniTask> postInitCallback = null)
        {
            _defaultConfigFileName = defaultConfigFileName;
            _userConfigFileName = userConfigFileName;
            _packageConfigurationLoader = packageConfigurationLoader;
            _autoInitialize = autoInitialize;
            _postInitCallback = postInitCallback;
        }

        public InputSystemVContainerInstaller(
            InputSystemDefaultConfigurationLoader defaultConfigLoader,
            string userConfigFileName = "user_input_settings.yaml",
            bool autoInitialize = true,
            Func<IObjectResolver, UniTask> postInitCallback = null)
            : this(
                defaultConfigLoader,
                userConfigFileName,
                null,
                autoInitialize,
                postInitCallback)
        {
        }

        public InputSystemVContainerInstaller(
            InputSystemDefaultConfigurationLoader defaultConfigLoader,
            string userConfigFileName,
            InputSystemPackageConfigurationLoader packageConfigurationLoader,
            bool autoInitialize = true,
            Func<IObjectResolver, UniTask> postInitCallback = null)
        {
            _defaultConfigLoader = defaultConfigLoader ??
                throw new ArgumentNullException(nameof(defaultConfigLoader));
            _userConfigFileName = userConfigFileName;
            _packageConfigurationLoader = packageConfigurationLoader;
            _autoInitialize = autoInitialize;
            _postInitCallback = postInitCallback;
        }

        public void Install(IContainerBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.Register(_ => new InputManager(), Lifetime.Singleton)
                .As<InputManager>();
            builder.Register(
                    resolver => new InputPlayerResolver(resolver.Resolve<InputManager>()),
                    Lifetime.Singleton)
                .As<IInputPlayerResolver>();
            builder.Register(
                    resolver => new InputSystemInitializer(
                        resolver.Resolve<InputManager>(),
                        _defaultConfigFileName,
                        _userConfigFileName,
                        _defaultConfigLoader,
                        _packageConfigurationLoader,
                        _bootstrapOptions,
                        _postInitCallback),
                    Lifetime.Singleton)
                .As<IInputSystemInitializer>()
                .As<IInputSystemInitializerDiagnostics>();

            if (_autoInitialize)
            {
                builder.RegisterEntryPoint(
                    resolver => new InputSystemAutoStartable(
                        resolver.Resolve<IInputSystemInitializerDiagnostics>(),
                        resolver),
                    Lifetime.Singleton);
            }
        }
    }

    public interface IInputSystemInitializer
    {
        UniTask InitializeAsync(IObjectResolver resolver);
        UniTask ReinitializeFromPackageAsync(
            object package,
            string configLocation,
            bool saveToUserConfig = true,
            CancellationToken cancellationToken = default);
        UniTask UpdateConfigurationAsync(
            string yamlContent,
            bool saveToUserConfig = false,
            CancellationToken cancellationToken = default);
        UniTask ReloadUserConfigurationAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Additive diagnostics contract for callers that need an authoritative initialization result.
    /// </summary>
    public interface IInputSystemInitializerDiagnostics
    {
        InputSystemLoadResult? LastResult { get; }
        InputManagerInitializationResult LastConfigurationResult { get; }
        UniTask<InputSystemLoadResult> InitializeWithResultAsync(
            IObjectResolver resolver,
            CancellationToken cancellationToken = default);
    }

    public interface IInputPlayerResolver
    {
        IInputPlayer GetInputPlayer(int playerId);
        UniTask<IInputPlayer> GetInputPlayerAsync(int playerId, int timeoutInSeconds = 5);
        bool TryGetInputPlayer(int playerId, out IInputPlayer inputPlayer);
        UniTask<IInputPlayer> TryGetInputPlayerAsync(int playerId, int timeoutInSeconds = 5);
    }

    internal sealed class InputSystemAutoStartable : IAsyncStartable
    {
        private readonly IInputSystemInitializerDiagnostics _initializer;
        private readonly IObjectResolver _resolver;

        internal InputSystemAutoStartable(
            IInputSystemInitializerDiagnostics initializer,
            IObjectResolver resolver)
        {
            _initializer = initializer ?? throw new ArgumentNullException(nameof(initializer));
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        }

        public async UniTask StartAsync(CancellationToken cancellationToken = default)
        {
            InputSystemLoadResult result =
                await _initializer.InitializeWithResultAsync(_resolver, cancellationToken);
            if (!result.IsBootstrapComplete)
            {
                throw new InvalidOperationException(
                    $"Input System startup failed with status {result.Status}: {result.Error}");
            }
        }
    }

    internal sealed class InputSystemInitializer :
        IInputSystemInitializer,
        IInputSystemInitializerDiagnostics
    {
        private const string LogPrefix = "[InputSystemInitializer]";
        private const string DefaultUserConfigurationKey = "user_input_settings.yaml";
        private const string CustomDefaultConfigurationKey = "custom-default";

        private readonly InputManager _inputManager;
        private readonly string _defaultConfigFileName;
        private readonly string _userConfigKey;
        private readonly InputSystemDefaultConfigurationLoader _defaultConfigLoader;
        private readonly InputSystemPackageConfigurationLoader _packageConfigurationLoader;
        private readonly InputSystemBootstrapOptions _bootstrapOptions;
        private readonly Func<IObjectResolver, UniTask> _postInitCallback;

        public InputSystemLoadResult? LastResult { get; private set; }
        public InputManagerInitializationResult LastConfigurationResult { get; private set; }

        internal InputSystemInitializer(
            InputManager inputManager,
            string defaultConfigFileName,
            string userConfigFileName,
            InputSystemDefaultConfigurationLoader defaultConfigLoader,
            InputSystemPackageConfigurationLoader packageConfigurationLoader,
            InputSystemBootstrapOptions bootstrapOptions,
            Func<IObjectResolver, UniTask> postInitCallback)
        {
            _inputManager = inputManager ?? throw new ArgumentNullException(nameof(inputManager));
            _defaultConfigFileName = defaultConfigFileName;
            _userConfigKey = string.IsNullOrWhiteSpace(userConfigFileName)
                ? DefaultUserConfigurationKey
                : userConfigFileName;
            _defaultConfigLoader = defaultConfigLoader;
            _packageConfigurationLoader = packageConfigurationLoader;
            _bootstrapOptions = bootstrapOptions;
            _postInitCallback = postInitCallback;
        }

        public async UniTask InitializeAsync(IObjectResolver resolver)
        {
            await InitializeWithResultAsync(resolver);
        }

        public async UniTask<InputSystemLoadResult> InitializeWithResultAsync(
            IObjectResolver resolver,
            CancellationToken cancellationToken = default)
        {
            if (resolver == null)
            {
                throw new ArgumentNullException(nameof(resolver));
            }

            InputSystemLoadResult result;
            if (_bootstrapOptions != null)
            {
                result = await InputSystemLoader.LoadAndInitializeAsync(
                    _bootstrapOptions,
                    _inputManager,
                    _inputManager.IsInitialized,
                    cancellationToken);
            }
            else
            {
                IInputConfigurationSource defaultSource = CreateDefaultSource(out string defaultKey);
                var userStore = new FileInputConfigurationStore(Application.persistentDataPath);
                result = await InputSystemLoader.LoadAndInitializeAsync(
                    defaultSource,
                    defaultKey,
                    userStore,
                    _userConfigKey,
                    _inputManager,
                    _inputManager.IsInitialized,
                    cancellationToken);
            }
            LastResult = result;

            if (!result.IsBootstrapComplete)
            {
                CLogger.LogError(
                    $"{LogPrefix} Initialization failed: {result.Status}. {result.Error}");
                return result;
            }

            if (!result.IsSuccess)
            {
                CLogger.LogInfo($"{LogPrefix} Automatic initialization is not configured.");
                return result;
            }

            CLogger.LogInfo(
                $"{LogPrefix} Initialized with status {result.Status}; " +
                $"user storage status was {result.UserStorageStatus}.");
            if (_postInitCallback != null)
            {
                await _postInitCallback(resolver);
            }

            return result;
        }

        public async UniTask ReinitializeFromPackageAsync(
            object package,
            string configLocation,
            bool saveToUserConfig = true,
            CancellationToken cancellationToken = default)
        {
            if (_packageConfigurationLoader == null)
            {
                CLogger.LogError(
                    $"{LogPrefix} No package configuration loader is registered. " +
                    "Install an explicit package integration adapter.");
                return;
            }

            try
            {
                string yamlContent = await _packageConfigurationLoader(
                    package,
                    configLocation,
                    cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (yamlContent == null || yamlContent.Length == 0)
                {
                    CLogger.LogWarning($"{LogPrefix} Package configuration was unavailable.");
                    return;
                }

                if (await TryUpdateConfigurationAsync(
                        yamlContent,
                        saveToUserConfig,
                        cancellationToken))
                {
                    CLogger.LogInfo($"{LogPrefix} Reinitialized from the package configuration.");
                }
            }
            catch (Exception exception) when (IsRecoverableException(exception))
            {
                CLogger.LogError(
                    $"{LogPrefix} Package configuration load failed ({exception.GetType().Name}).");
            }
        }

        public async UniTask UpdateConfigurationAsync(
            string yamlContent,
            bool saveToUserConfig = false,
            CancellationToken cancellationToken = default)
        {
            await TryUpdateConfigurationAsync(
                yamlContent,
                saveToUserConfig,
                cancellationToken);
        }

        public async UniTask ReloadUserConfigurationAsync(
            CancellationToken cancellationToken = default)
        {
            IInputConfigurationStore userStore = CreateUserStore(out string userKey);
            if (userStore == null)
            {
                CLogger.LogWarning(
                    $"{LogPrefix} No user configuration store is configured.");
                return;
            }

            InputConfigurationReadResult read = await userStore.LoadAsync(
                userKey,
                cancellationToken);
            if (!read.IsSuccess)
            {
                CLogger.LogWarning(
                    $"{LogPrefix} User configuration reload failed: {read.Status}. {read.Error}");
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
            await UniTask.SwitchToMainThread(PlayerLoopTiming.Update, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            InputManagerInitializationResult initialization =
                _inputManager.ReinitializeWithResult(read.Content);
            LastConfigurationResult = initialization;
            if (!initialization.IsSuccess)
            {
                CLogger.LogWarning(
                    $"{LogPrefix} User configuration is invalid and was preserved: " +
                    $"{initialization.Status}. {initialization.Message}");
                return;
            }

            CLogger.LogInfo(
                $"{LogPrefix} Reloaded user configuration. " +
                $"Recovered from backup: {read.WasRecoveredFromBackup}.");
        }

        private async UniTask<bool> TryUpdateConfigurationAsync(
            string yamlContent,
            bool saveToUserConfig,
            CancellationToken cancellationToken)
        {
            if (yamlContent == null || yamlContent.Length == 0 ||
                (yamlContent.Length <= FileInputConfigurationStore.DefaultMaximumBytes &&
                 string.IsNullOrWhiteSpace(yamlContent)))
            {
                CLogger.LogError($"{LogPrefix} Configuration content is empty.");
                return false;
            }

            cancellationToken.ThrowIfCancellationRequested();
            await UniTask.SwitchToMainThread(PlayerLoopTiming.Update, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            InputManagerInitializationResult initialization =
                _inputManager.ReinitializeWithResult(yamlContent);
            LastConfigurationResult = initialization;
            if (!initialization.IsSuccess)
            {
                CLogger.LogError(
                    $"{LogPrefix} Configuration update failed: " +
                    $"{initialization.Status}. {initialization.Message}");
                return false;
            }

            if (!saveToUserConfig)
            {
                CLogger.LogInfo($"{LogPrefix} Configuration updated for this session.");
                return true;
            }

            string persistenceContent = yamlContent;
            if (initialization.Validation?.WasMigrated == true &&
                !InputConfigurationYamlCodec.TrySerialize(
                    initialization.Validation.Configuration,
                    out persistenceContent,
                    out string serializationError))
            {
                CLogger.LogWarning(
                    $"{LogPrefix} Configuration is active but migrated YAML serialization failed: " +
                    serializationError);
                return false;
            }

            IInputConfigurationStore userStore = CreateUserStore(out string userKey);
            if (userStore == null)
            {
                CLogger.LogWarning(
                    $"{LogPrefix} Configuration is active but no user store is configured.");
                return false;
            }

            InputConfigurationStoreResult save;
            try
            {
                save = await userStore.SaveAsync(
                    userKey,
                    persistenceContent,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                CLogger.LogWarning(
                    $"{LogPrefix} Configuration is active but persistence was canceled.");
                return false;
            }
            catch (Exception exception) when (IsRecoverableException(exception))
            {
                CLogger.LogWarning(
                    $"{LogPrefix} Configuration is active but the persistence provider failed " +
                    $"({exception.GetType().Name}).");
                return false;
            }
            if (!save.IsSuccess)
            {
                CLogger.LogWarning(
                    $"{LogPrefix} Configuration is active but persistence failed: " +
                    $"{save.Status}. {save.Error}");
                return false;
            }

            CLogger.LogInfo(
                $"{LogPrefix} Configuration updated and atomically persisted.");
            return true;
        }

        private IInputConfigurationStore CreateUserStore(out string key)
        {
            if (_bootstrapOptions != null)
            {
                key = _bootstrapOptions.UserKey;
                return _bootstrapOptions.UserStore;
            }

            key = _userConfigKey;
            return new FileInputConfigurationStore(Application.persistentDataPath);
        }

        private IInputConfigurationSource CreateDefaultSource(out string key)
        {
            if (_defaultConfigLoader != null)
            {
                key = CustomDefaultConfigurationKey;
                return new DelegateInputConfigurationSource(_defaultConfigLoader);
            }

            key = UnityFileUri.Create(
                _defaultConfigFileName,
                UnityFileLocation.StreamingAssets);
            return new UriInputConfigurationSource();
        }

        private static bool IsRecoverableException(Exception exception)
        {
            return exception is not OperationCanceledException &&
                   exception is not OutOfMemoryException &&
                   exception is not AccessViolationException &&
                   exception is not StackOverflowException;
        }
    }

    internal sealed class DelegateInputConfigurationSource : IInputConfigurationSource
    {
        private readonly InputSystemDefaultConfigurationLoader _loader;

        internal DelegateInputConfigurationSource(InputSystemDefaultConfigurationLoader loader)
        {
            _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        }

        public async UniTask<InputConfigurationReadResult> LoadAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                string content = await _loader(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                return string.IsNullOrWhiteSpace(content)
                    ? InputConfigurationReadResult.Failure(
                        InputConfigurationStorageStatus.NotFound,
                        "The custom default configuration loader returned no content.")
                    : InputConfigurationReadResult.Success(content);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is not OutOfMemoryException &&
                exception is not AccessViolationException &&
                exception is not StackOverflowException)
            {
                return InputConfigurationReadResult.Failure(
                    InputConfigurationStorageStatus.IoError,
                    $"Custom default configuration loading failed ({exception.GetType().Name}).");
            }
        }
    }

    internal sealed class InputPlayerResolver : IInputPlayerResolver
    {
        private readonly InputManager _inputManager;

        internal InputPlayerResolver(InputManager inputManager)
        {
            _inputManager = inputManager ?? throw new ArgumentNullException(nameof(inputManager));
        }

        public IInputPlayer GetInputPlayer(int playerId)
        {
            if (!TryGetInputPlayer(playerId, out IInputPlayer inputPlayer))
            {
                throw new InvalidOperationException(
                    $"Player {playerId} is unavailable. Initialize InputManager and verify the slot.");
            }

            return inputPlayer;
        }

        public async UniTask<IInputPlayer> GetInputPlayerAsync(
            int playerId,
            int timeoutInSeconds = 5)
        {
            IInputPlayer inputPlayer =
                await TryGetInputPlayerAsync(playerId, timeoutInSeconds);
            if (inputPlayer == null)
            {
                throw new InvalidOperationException(
                    $"Player {playerId} is unavailable. Initialize InputManager and verify the slot.");
            }

            return inputPlayer;
        }

        public bool TryGetInputPlayer(int playerId, out IInputPlayer inputPlayer)
        {
            inputPlayer = _inputManager.GetInputPlayer(playerId);
            if (inputPlayer != null)
            {
                return true;
            }

            if (!PlayerLoopHelper.IsMainThread)
            {
                CLogger.LogError(
                    "[InputPlayerResolver] Synchronous auto-join requires the Unity main thread.");
                return false;
            }

            inputPlayer = _inputManager.JoinSinglePlayer(playerId);
            return inputPlayer != null;
        }

        public async UniTask<IInputPlayer> TryGetInputPlayerAsync(
            int playerId,
            int timeoutInSeconds = 5)
        {
            IInputPlayer inputPlayer = _inputManager.GetInputPlayer(playerId);
            return inputPlayer ??
                await _inputManager.JoinSinglePlayerAsync(playerId, timeoutInSeconds);
        }
    }
}
#endif
