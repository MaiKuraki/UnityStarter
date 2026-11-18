#if VCONTAINER_PRESENT
using System;
using System.Threading;

using Cysharp.Threading.Tasks;
using VContainer;
using VContainer.Unity;

using CycloneGames.Logging;

namespace CycloneGames.InputSystem.Runtime.Integrations.VContainer
{
    /// <summary>
    /// Registers container-owned input services. Configuration sources, storage roots,
    /// startup timing, failure handling, and runtime update permission are supplied by
    /// the composition root through <see cref="InputSystemVContainerOptions"/>.
    /// </summary>
    public sealed class InputSystemVContainerInstaller : IInstaller
    {
        private readonly InputSystemVContainerOptions _options;
        private readonly Func<IObjectResolver, UniTask> _postInitCallback;

        public InputSystemVContainerInstaller(
            InputSystemVContainerOptions options,
            Func<IObjectResolver, UniTask> postInitCallback = null)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _postInitCallback = postInitCallback;
        }

        public void Install(IContainerBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            InputSystemRegistrationPlan plan = _options.GetRegistrationPlan();

            builder.Register(_ => new InputManager(), Lifetime.Singleton)
                .As<InputManager>();
            builder.Register(
                    resolver => new InputPlayerResolver(resolver.Resolve<InputManager>()),
                    Lifetime.Singleton)
                .As<IInputPlayerResolver>();
            builder.Register(
                    resolver => new InputSystemInitializer(
                        resolver.Resolve<InputManager>(),
                        _options,
                        _postInitCallback),
                    Lifetime.Singleton)
                .As<IInputSystemInitializer>()
                .As<IInputSystemInitializerDiagnostics>();

            if (plan.RegisterEntryPoint)
            {
                builder.RegisterEntryPoint(
                    resolver => new InputSystemAutoStartable(
                        resolver.Resolve<IInputSystemInitializerDiagnostics>(),
                        _options,
                        resolver),
                    Lifetime.Singleton);
            }
        }
    }

    public interface IInputSystemInitializer
    {
        UniTask InitializeAsync(IObjectResolver resolver);
        UniTask UpdateConfigurationAsync(
            string yamlContent,
            bool saveToUserConfig = false,
            CancellationToken cancellationToken = default);
        UniTask ReloadUserConfigurationAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Diagnostics contract for callers that need the authoritative initialization result.
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
        private readonly InputSystemVContainerOptions _options;
        private readonly IObjectResolver _resolver;

        internal InputSystemAutoStartable(
            IInputSystemInitializerDiagnostics initializer,
            InputSystemVContainerOptions options,
            IObjectResolver resolver)
        {
            _initializer = initializer ?? throw new ArgumentNullException(nameof(initializer));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        }

        public async UniTask StartAsync(CancellationToken cancellationToken = default)
        {
            InputSystemLoadResult result =
                await _initializer.InitializeWithResultAsync(_resolver, cancellationToken);

            if (_options.ShouldEscalateStartupFailure(result))
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
        private static readonly LogChannel Log = InputSystemVContainerLog.Channel;

        private const string LogPrefix = "[InputSystemInitializer]";

        private readonly InputManager _inputManager;
        private readonly InputSystemVContainerOptions _options;
        private readonly Func<IObjectResolver, UniTask> _postInitCallback;

        public InputSystemLoadResult? LastResult { get; private set; }
        public InputManagerInitializationResult LastConfigurationResult { get; private set; }

        internal InputSystemInitializer(
            InputManager inputManager,
            InputSystemVContainerOptions options,
            Func<IObjectResolver, UniTask> postInitCallback)
        {
            _inputManager = inputManager ?? throw new ArgumentNullException(nameof(inputManager));
            _options = options ?? throw new ArgumentNullException(nameof(options));
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

            InputSystemLoadResult result = await InputSystemLoader.LoadAndInitializeAsync(
                _options.Bootstrap,
                _inputManager,
                _inputManager.IsInitialized,
                cancellationToken);
            LastResult = result;

            if (!result.IsBootstrapComplete)
            {
                Log.Error($"{LogPrefix} Initialization failed: {result.Status}. {result.Error}");
                return result;
            }

            if (!result.IsSuccess)
            {
                Log.Info($"{LogPrefix} Automatic initialization is not configured.");
                return result;
            }

            Log.Info(
                $"{LogPrefix} Initialized with status {result.Status}; " +
                $"user storage status was {result.UserStorageStatus}.");
            if (_postInitCallback != null)
            {
                await _postInitCallback(resolver);
            }

            return result;
        }

        public async UniTask UpdateConfigurationAsync(
            string yamlContent,
            bool saveToUserConfig = false,
            CancellationToken cancellationToken = default)
        {
            if (!_options.AllowRuntimeConfigurationUpdates)
            {
                Log.Warning($"{LogPrefix} Runtime configuration updates are disabled by policy.");
                return;
            }

            await TryUpdateConfigurationAsync(
                yamlContent,
                saveToUserConfig,
                cancellationToken);
        }

        public async UniTask ReloadUserConfigurationAsync(
            CancellationToken cancellationToken = default)
        {
            if (!_options.AllowRuntimeConfigurationUpdates)
            {
                Log.Warning($"{LogPrefix} Runtime configuration updates are disabled by policy.");
                return;
            }

            IInputConfigurationStore userStore = _options.Bootstrap.UserStore;
            string userKey = _options.Bootstrap.UserKey;
            if (userStore == null || string.IsNullOrEmpty(userKey))
            {
                Log.Warning($"{LogPrefix} No user configuration store is configured.");
                return;
            }

            InputConfigurationReadResult read = await userStore.LoadAsync(
                userKey,
                cancellationToken);
            if (!read.IsSuccess)
            {
                Log.Warning(
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
                Log.Warning(
                    $"{LogPrefix} User configuration is invalid and was preserved: " +
                    $"{initialization.Status}. {initialization.Message}");
                return;
            }

            Log.Info(
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
                Log.Error($"{LogPrefix} Configuration content is empty.");
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
                Log.Error(
                    $"{LogPrefix} Configuration update failed: " +
                    $"{initialization.Status}. {initialization.Message}");
                return false;
            }

            if (!saveToUserConfig)
            {
                Log.Info($"{LogPrefix} Configuration updated for this session.");
                return true;
            }

            string persistenceContent = yamlContent;
            if (initialization.Validation?.WasMigrated == true &&
                !InputConfigurationYamlCodec.TrySerialize(
                    initialization.Validation.Configuration,
                    out persistenceContent,
                    out string serializationError))
            {
                Log.Warning(
                    $"{LogPrefix} Configuration is active but migrated YAML serialization failed: " +
                    serializationError);
                return false;
            }

            IInputConfigurationStore userStore = _options.Bootstrap.UserStore;
            string userKey = _options.Bootstrap.UserKey;
            if (userStore == null || string.IsNullOrEmpty(userKey))
            {
                Log.Warning(
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
                Log.Warning(
                    $"{LogPrefix} Configuration is active but persistence was canceled.");
                return false;
            }
            catch (Exception exception) when (IsRecoverableException(exception))
            {
                Log.Error(
                    exception,
                    $"{LogPrefix} Configuration is active but the persistence provider failed.");
                return false;
            }
            if (!save.IsSuccess)
            {
                Log.Warning(
                    $"{LogPrefix} Configuration is active but persistence failed: " +
                    $"{save.Status}. {save.Error}");
                return false;
            }

            Log.Info($"{LogPrefix} Configuration updated and atomically persisted.");
            return true;
        }

        private static bool IsRecoverableException(Exception exception)
        {
            return exception is not OperationCanceledException &&
                   exception is not OutOfMemoryException &&
                   exception is not AccessViolationException &&
                   exception is not StackOverflowException;
        }
    }

    internal sealed class InputPlayerResolver : IInputPlayerResolver
    {
        private static readonly LogChannel Log = InputSystemVContainerLog.Channel;

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
                Log.Error(
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
