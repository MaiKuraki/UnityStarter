using CycloneGames.Logger;
using Cysharp.Threading.Tasks;
using R3;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Users;
using VYaml.Serialization;

namespace CycloneGames.InputSystem.Runtime
{
    /// <summary>
    /// Explicitly owned, main-thread-confined input session. The static Instance is a compatibility facade.
    /// </summary>
    public sealed class InputManager : IDisposable
    {
        private const string DEBUG_FLAG = "[InputManager]";
        private const int MaxOverrideJsonPerPlayer = 1024 * 1024;
        private const int MaxOverrideProfileBytes = 4 * 1024 * 1024;
        private const int MaxJoinTimeoutSeconds = 300;
        private const int MaxBatchJoinTotalTimeoutSeconds = 300;
        private static readonly ProfilerMarker InitializeMarker = new ProfilerMarker("CycloneGames.Input.Initialize");
        private static readonly ProfilerMarker JoinMarker = new ProfilerMarker("CycloneGames.Input.JoinPlayer");
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        private static InputManager _compatibilityInstance;
        private static int _listeningManagerCount;
        private static bool _isSubsystemResetting;
        private static readonly HashSet<InputManager> ActiveManagers = new HashSet<InputManager>();

        public static InputManager Instance
        {
            get
            {
                EnsureMainThread();
                return _compatibilityInstance ??= new InputManager();
            }
        }

        public static bool IsListeningForPlayers
        {
            get
            {
                EnsureMainThread();
                return _listeningManagerCount > 0;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnSubsystemRegistration()
        {
            // UniTask captures its main-thread id at AfterAssembliesLoaded, which runs after this hook.
            // Unity invokes SubsystemRegistration on the main thread, so use the internal teardown path
            // and invalidate every manager left from a domain-reload-disabled play session.
            ResetAllManagersForSubsystemRegistration();
        }

        internal static void ResetGlobalStateForDomainReload()
        {
            EnsureMainThread();
            ResetAllManagersForSubsystemRegistration();
        }

        private static void ResetAllManagersForSubsystemRegistration()
        {
            if (_isSubsystemResetting) return;
            _isSubsystemResetting = true;
            try
            {
                if (ActiveManagers.Count > 0)
                {
                    var snapshot = new InputManager[ActiveManagers.Count];
                    ActiveManagers.CopyTo(snapshot);
                    for (int i = 0; i < snapshot.Length; i++) snapshot[i]?.DisposeCore();
                }

                ActiveManagers.Clear();
                _compatibilityInstance = null;
                _listeningManagerCount = 0;
            }
            finally
            {
                _isSubsystemResetting = false;
            }
        }

        public event Action<IInputPlayer> OnPlayerInputReady;
        public event Action OnConfigurationReloaded;

        private readonly Dictionary<int, IInputPlayer> _registeredPlayers = new Dictionary<int, IInputPlayer>();
        private readonly HashSet<int> _joinsInProgress = new HashSet<int>();
        private readonly HashSet<int> _reservedDeviceIds = new HashSet<int>();
        private readonly Dictionary<int, string> _bindingOverridesByPlayer = new Dictionary<int, string>();
        private readonly InputConfigurationLimits _limits;
        private InputConfiguration _configuration;
        private RuntimeInputConfiguration _runtimeConfiguration;
        private InputAction _joinAction;
        private CancellationTokenSource _shutdown = new CancellationTokenSource();
        private string _userConfigUri;
        private bool _isInitialized;
        private bool _isDisposed;
        private bool _isListening;
        private bool _isConfigurationTransitioning;
        private int _configurationOperationInProgress;
        private bool _isDeviceLockingOnJoinEnabled;
        private bool _deviceChangeSubscribed;
        private long _deviceRevision;
        private long _configurationRevision;

        public InputManager()
            : this(null)
        {
        }

        public InputManager(InputConfigurationLimits limits)
        {
            EnsureMainThread();
            if (_isSubsystemResetting)
                throw new InvalidOperationException("InputManager cannot be constructed during subsystem teardown.");
            _limits = limits ?? InputConfigurationLimits.Default;
            UnityEngine.InputSystem.InputSystem.onDeviceChange += OnDeviceTopologyChanged;
            _deviceChangeSubscribed = true;
            ActiveManagers.Add(this);
        }

        public bool IsInitialized
        {
            get { EnsureMainThread(); return _isInitialized; }
        }

        public bool IsDisposed
        {
            get { EnsureMainThread(); return _isDisposed; }
        }

        public int ActivePlayerCount
        {
            get { EnsureMainThread(); return _registeredPlayers.Count; }
        }

        private InputManagerInitializationResult _lastInitializationResult;
        public InputManagerInitializationResult LastInitializationResult
        {
            get { EnsureMainThread(); return _lastInitializationResult; }
            private set { _lastInitializationResult = value; }
        }

        public void Initialize(string yamlContent, string userConfigUri)
        {
            if (!ValidateMainThread(nameof(Initialize))) return;
            if (_isInitialized) return;
            InputManagerInitializationResult result = InitializeWithResult(yamlContent, userConfigUri);
            if (!result.IsSuccess) CLogger.LogError($"{DEBUG_FLAG} {result.Message}");
        }

        public InputManagerInitializationResult InitializeWithResult(
            string yamlContent,
            string userConfigUri = null)
        {
            if (!Cysharp.Threading.Tasks.PlayerLoopHelper.IsMainThread)
                return new InputManagerInitializationResult(
                    InputManagerInitializationStatus.NotMainThread,
                    "Initialization must run on the Unity main thread.",
                    null);
            if (_isInitialized)
            {
                LastInitializationResult = new InputManagerInitializationResult(
                    InputManagerInitializationStatus.Success,
                    "InputManager is already initialized.",
                    null);
                return LastInitializationResult;
            }

            return PrepareAndCommit(yamlContent, userConfigUri, notifyReload: false);
        }

        public void Reinitialize(string yamlContent, string userConfigUri)
        {
            InputManagerInitializationResult result = ReinitializeWithResult(yamlContent, userConfigUri);
            if (!result.IsSuccess) CLogger.LogError($"{DEBUG_FLAG} {result.Message}");
        }

        public InputManagerInitializationResult ReinitializeWithResult(
            string yamlContent,
            string userConfigUri = null)
        {
            if (!Cysharp.Threading.Tasks.PlayerLoopHelper.IsMainThread)
                return new InputManagerInitializationResult(
                    InputManagerInitializationStatus.NotMainThread,
                    "Reinitialization must run on the Unity main thread.",
                    null);
            if (_registeredPlayers.Count > 0)
            {
                return SetInitializationFailure(
                    InputManagerInitializationStatus.ActivePlayers,
                    "Cannot replace configuration while players are active. Remove players before reinitializing.");
            }
            if (_joinsInProgress.Count > 0)
            {
                return SetInitializationFailure(
                    InputManagerInitializationStatus.JoinInProgress,
                    "Cannot replace configuration while player joins are in progress. Cancel or complete joins first.");
            }
            if (Volatile.Read(ref _configurationOperationInProgress) != 0)
            {
                return SetInitializationFailure(
                    InputManagerInitializationStatus.ConfigurationOperationInProgress,
                    "Cannot replace configuration while a configuration load or save is in progress.");
            }

            return PrepareAndCommit(yamlContent, userConfigUri, notifyReload: _isInitialized);
        }

        private InputManagerInitializationResult PrepareAndCommit(
            string yamlContent,
            string userConfigUri,
            bool notifyReload)
        {
            if (_isConfigurationTransitioning)
            {
                return SetInitializationFailure(
                    InputManagerInitializationStatus.ConfigurationOperationInProgress,
                    "A configuration transition is already in progress.");
            }

            _isConfigurationTransitioning = true;
            try
            {
                using (InitializeMarker.Auto())
                {
                if (!Cysharp.Threading.Tasks.PlayerLoopHelper.IsMainThread)
                {
                    return new InputManagerInitializationResult(
                        InputManagerInitializationStatus.NotMainThread,
                        "Initialization must run on the Unity main thread.",
                        null);
                }

                if (_isDisposed)
                {
                    return SetInitializationFailure(InputManagerInitializationStatus.Disposed,
                        "A disposed InputManager cannot be reinitialized. Construct a new instance.");
                }

                if (yamlContent == null || yamlContent.Length == 0 ||
                    (yamlContent.Length <= FileInputConfigurationStore.DefaultMaximumBytes &&
                     string.IsNullOrWhiteSpace(yamlContent)))
                {
                    return SetInitializationFailure(InputManagerInitializationStatus.EmptyContent,
                        "Configuration content is empty.");
                }

                if (!InputConfigurationYamlPreflight.TryValidate(yamlContent, out string yamlSafetyError))
                {
                    return SetInitializationFailure(
                        InputManagerInitializationStatus.ValidationFailed,
                        yamlSafetyError);
                }

                InputConfiguration parsed;
                try
                {
                    string normalized = NormalizeLineEndings(yamlContent);
                    parsed = YamlSerializer.Deserialize<InputConfiguration>(StrictUtf8.GetBytes(normalized));
                }
                catch (Exception exception) when (IsRecoverableException(exception))
                {
                    return SetInitializationFailure(InputManagerInitializationStatus.ParseFailed,
                        $"Configuration YAML parsing failed ({exception.GetType().Name}).");
                }

                InputConfigurationValidationResult validation = InputConfigurationValidator.ValidateAndPrepare(parsed, _limits);
                if (!validation.IsValid)
                {
                    string message = validation.Issues.Count == 0
                        ? "Configuration validation failed."
                        : validation.Issues[0].ToString();
                    LastInitializationResult = new InputManagerInitializationResult(
                        InputManagerInitializationStatus.ValidationFailed,
                        message,
                        validation);
                    return LastInitializationResult;
                }

                InputConfigurationPreflightResult preflight =
                    InputSystemConfigurationPreflight.Run(validation.RuntimeConfiguration);
                if (!preflight.IsSuccess)
                {
                    string message = preflight.Issues.Count == 0
                        ? "Input System configuration preflight failed."
                        : preflight.Issues[0].ToString();
                    LastInitializationResult = new InputManagerInitializationResult(
                        InputManagerInitializationStatus.InputSystemPreflightFailed,
                        message,
                        validation,
                        preflight);
                    return LastInitializationResult;
                }

                InputConfiguration ownedConfiguration =
                    InputConfigurationCloner.DeepClone(validation.Configuration);

                bool rebuildJoinAction = _isListening;
                InputAction replacementJoinAction = null;
                if (rebuildJoinAction)
                {
                    try
                    {
                        replacementJoinAction = CreatePreparedJoinAction(validation.RuntimeConfiguration);
                    }
                    catch (Exception exception) when (IsRecoverableException(exception))
                    {
                        replacementJoinAction?.Dispose();
                        LastInitializationResult = new InputManagerInitializationResult(
                            InputManagerInitializationStatus.ValidationFailed,
                            $"Configuration join bindings could not be prepared ({exception.GetType().Name}).",
                            validation,
                            preflight);
                        return LastInitializationResult;
                    }
                }

                if (_isDisposed)
                {
                    replacementJoinAction?.Dispose();
                    return SetInitializationFailure(
                        InputManagerInitializationStatus.Disposed,
                        "The InputManager was disposed during configuration preparation.");
                }
                if (_registeredPlayers.Count > 0 || _joinsInProgress.Count > 0)
                {
                    replacementJoinAction?.Dispose();
                    return SetInitializationFailure(
                        _registeredPlayers.Count > 0
                            ? InputManagerInitializationStatus.ActivePlayers
                            : InputManagerInitializationStatus.JoinInProgress,
                        "Configuration state changed during preparation; retry after players and joins are cleared.");
                }

                // Keep manager-owned persistence state isolated from the mutable diagnostic DTO exposed by the result.
                _configuration = ownedConfiguration;
                _runtimeConfiguration = validation.RuntimeConfiguration;
                _userConfigUri = userConfigUri;
                _isInitialized = true;
                _bindingOverridesByPlayer.Clear();
                var successResult = new InputManagerInitializationResult(
                    InputManagerInitializationStatus.Success,
                    validation.WasMigrated ? "Configuration migrated and initialized." : "Configuration initialized.",
                    validation,
                    preflight);
                LastInitializationResult = successResult;
                long committedRevision = unchecked(++_configurationRevision);

                if (rebuildJoinAction) ReplaceJoinAction(replacementJoinAction);
                if (_isDisposed)
                {
                    return SetInitializationFailure(
                        InputManagerInitializationStatus.Disposed,
                        "The InputManager was disposed while committing the configuration.");
                }

                if (notifyReload)
                {
                    _isConfigurationTransitioning = false;
                    NotifyConfigurationReloaded(committedRevision);
                }

                return successResult;
                }
            }
            finally
            {
                _isConfigurationTransitioning = false;
            }
        }

        private InputManagerInitializationResult SetInitializationFailure(InputManagerInitializationStatus status, string message)
        {
            LastInitializationResult = new InputManagerInitializationResult(status, message, null);
            return LastInitializationResult;
        }

        public UniTask<bool> ReloadConfigurationAsync()
        {
            return ReloadConfigurationAsync(default);
        }

        public async UniTask<bool> ReloadConfigurationAsync(CancellationToken cancellationToken)
        {
            await SwitchToUnityMainThreadAsync(cancellationToken);
            if (!TryBeginConfigurationOperation()) return false;
            try
            {
                if (!_isInitialized || string.IsNullOrEmpty(_userConfigUri) ||
                    _registeredPlayers.Count > 0 || _joinsInProgress.Count > 0)
                    return false;

                string sourceUri = _userConfigUri;
                long sourceRevision = _configurationRevision;
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
#pragma warning disable CS0618 // Compatibility URI API; new composition uses InputSystemLoader source/store contracts.
                (bool success, string yamlContent) = await InputConfigurationFileLoader.LoadTextFromUriAsync(
                    sourceUri,
                    DEBUG_FLAG,
                    linked.Token);
#pragma warning restore CS0618
                await SwitchToUnityMainThreadAsync(linked.Token);
                if (!success || string.IsNullOrEmpty(yamlContent) || _isDisposed ||
                    sourceRevision != _configurationRevision ||
                    !string.Equals(sourceUri, _userConfigUri, StringComparison.Ordinal) ||
                    _registeredPlayers.Count > 0 || _joinsInProgress.Count > 0)
                    return false;

                return PrepareAndCommit(yamlContent, sourceUri, notifyReload: true).IsSuccess;
            }
            finally
            {
                EndConfigurationOperation();
            }
        }

        public UniTask SaveUserConfigurationAsync()
        {
            return SaveUserConfigurationAsync(default);
        }

        public async UniTask SaveUserConfigurationAsync(CancellationToken cancellationToken)
        {
            await SwitchToUnityMainThreadAsync(cancellationToken);
            if (!TryBeginConfigurationOperation()) return;
            try
            {
                if (!_isInitialized || string.IsNullOrEmpty(_userConfigUri)) return;
                string destinationUri = _userConfigUri;
                long sourceRevision = _configurationRevision;
                if (!InputConfigurationYamlCodec.TrySerialize(_configuration, out string yaml, out string error))
                {
                    CLogger.LogError($"{DEBUG_FLAG} User configuration serialization failed: {error}");
                    return;
                }
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
#pragma warning disable CS0618 // Compatibility URI API; new composition uses InputSystemLoader source/store contracts.
                if (!_isDisposed && sourceRevision == _configurationRevision &&
                    string.Equals(destinationUri, _userConfigUri, StringComparison.Ordinal))
                {
                    await InputConfigurationFileLoader.SaveTextToUriAsync(
                        destinationUri,
                        yaml,
                        DEBUG_FLAG,
                        linked.Token);
                }
#pragma warning restore CS0618
            }
            finally
            {
                EndConfigurationOperation();
            }
        }

        public List<IInputPlayer> JoinPlayersBatch(List<int> playerIds)
        {
            if (!ValidateMainThread(nameof(JoinPlayersBatch)) ||
                !TrySnapshotBatchPlayerIds(playerIds, out int[] playerIdSnapshot))
                return new List<IInputPlayer>();

            var result = new List<IInputPlayer>(playerIdSnapshot.Length);
            for (int i = 0; i < playerIdSnapshot.Length; i++)
            {
                IInputPlayer player = JoinSinglePlayer(playerIdSnapshot[i]);
                if (player != null) result.Add(player);
            }

            return result;
        }

        public UniTask<List<IInputPlayer>> JoinPlayersBatchAsync(List<int> playerIds, int timeoutPerPlayerInSeconds = 5)
        {
            return JoinPlayersBatchAsync(playerIds, timeoutPerPlayerInSeconds, default);
        }

        public async UniTask<List<IInputPlayer>> JoinPlayersBatchAsync(
            List<int> playerIds,
            int timeoutPerPlayerInSeconds,
            CancellationToken cancellationToken)
        {
            await SwitchToUnityMainThreadAsync(cancellationToken);
            if (timeoutPerPlayerInSeconds <= 0 || timeoutPerPlayerInSeconds > MaxJoinTimeoutSeconds ||
                !TrySnapshotBatchPlayerIds(playerIds, out int[] playerIdSnapshot))
                return new List<IInputPlayer>();

            var result = new List<IInputPlayer>(playerIdSnapshot.Length);
            int totalTimeoutSeconds = Math.Min(
                MaxBatchJoinTotalTimeoutSeconds,
                playerIdSnapshot.Length * timeoutPerPlayerInSeconds);
            using var batchTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(totalTimeoutSeconds));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _shutdown.Token,
                batchTimeout.Token);
            var newlyRegistered = new List<KeyValuePair<int, IInputPlayer>>(playerIdSnapshot.Length);
            try
            {
                for (int i = 0; i < playerIdSnapshot.Length; i++)
                {
                    int playerId = playerIdSnapshot[i];
                    bool wasRegistered = _registeredPlayers.ContainsKey(playerId);
                    IInputPlayer player = await JoinSinglePlayerAsync(
                        playerId,
                        timeoutPerPlayerInSeconds,
                        linked.Token);
                    if (player != null)
                    {
                        result.Add(player);
                        if (!wasRegistered && IsPlayerRegistrationCurrent(playerId, player))
                            newlyRegistered.Add(new KeyValuePair<int, IInputPlayer>(playerId, player));
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                }
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    RollbackBatchPlayers(newlyRegistered);
                    throw;
                }

                // A manager shutdown or the bounded aggregate timeout returns the successfully joined prefix.
            }

            return result;
        }

        public IInputPlayer JoinSinglePlayer(int playerIdToJoin = 0)
        {
            if (!ValidateMainThread(nameof(JoinSinglePlayer))) return null;
            if (_isDisposed || !_isInitialized || _isConfigurationTransitioning) return null;
            if (_registeredPlayers.TryGetValue(playerIdToJoin, out IInputPlayer existing)) return existing;
            if (!_joinsInProgress.Add(playerIdToJoin)) return null;

            try
            {
                long configurationRevision = _configurationRevision;
                RuntimePlayerSlotConfig config = GetRuntimePlayerConfig(playerIdToJoin, false);
                if (config == null || !TrySelectDevices(config, null, out List<InputDevice> devices, out string schemeName))
                    return null;
                return JoinPlayerWithDevices(
                    playerIdToJoin,
                    config,
                    devices,
                    schemeName,
                    configurationRevision);
            }
            finally
            {
                _joinsInProgress.Remove(playerIdToJoin);
            }
        }

        public UniTask<IInputPlayer> JoinSinglePlayerAsync(int playerIdToJoin = 0, int timeoutInSeconds = 5)
        {
            return JoinSinglePlayerAsync(playerIdToJoin, timeoutInSeconds, default);
        }

        public async UniTask<IInputPlayer> JoinSinglePlayerAsync(
            int playerIdToJoin,
            int timeoutInSeconds,
            CancellationToken cancellationToken)
        {
            await SwitchToUnityMainThreadAsync(cancellationToken);
            if (_isDisposed || !_isInitialized || _isConfigurationTransitioning) return null;
            if (timeoutInSeconds <= 0 || timeoutInSeconds > MaxJoinTimeoutSeconds) return null;
            if (_registeredPlayers.TryGetValue(playerIdToJoin, out IInputPlayer existing)) return existing;
            if (!_joinsInProgress.Add(playerIdToJoin)) return null;

            try
            {
                long configurationRevision = _configurationRevision;
                RuntimePlayerSlotConfig config = GetRuntimePlayerConfig(playerIdToJoin, false);
                if (config == null) return null;

                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutInSeconds));
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token, timeout.Token);
                while (!linked.IsCancellationRequested)
                {
                    if (TrySelectDevices(config, null, out List<InputDevice> devices, out string schemeName))
                        return JoinPlayerWithDevices(
                            playerIdToJoin,
                            config,
                            devices,
                            schemeName,
                            configurationRevision);
                    await WaitForDeviceChangeAsync(linked.Token);
                }

                return null;
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested) throw;
                return null;
            }
            finally
            {
                _joinsInProgress.Remove(playerIdToJoin);
            }
        }

        public IInputPlayer JoinPlayerOnSharedDevice(int playerIdToJoin)
        {
            if (!ValidateMainThread(nameof(JoinPlayerOnSharedDevice))) return null;
            if (_isDisposed || !_isInitialized || _isConfigurationTransitioning) return null;
            if (_registeredPlayers.TryGetValue(playerIdToJoin, out IInputPlayer existing)) return existing;
            if (!_joinsInProgress.Add(playerIdToJoin)) return null;

            InputUser user = default;
            try
            {
                long configurationRevision = _configurationRevision;
                RuntimePlayerSlotConfig config = GetRuntimePlayerConfig(playerIdToJoin, false);
                if (config == null || Keyboard.current == null) return null;
                user = InputUser.PerformPairingWithDevice(Keyboard.current);
                if (Mouse.current != null) InputUser.PerformPairingWithDevice(Mouse.current, user);
                IInputPlayer player = CreatePlayerService(
                    playerIdToJoin,
                    user,
                    config,
                    Keyboard.current,
                    null,
                    configurationRevision);
                if (player == null && user.valid) user.UnpairDevicesAndRemoveUser();
                return player;
            }
            catch (Exception exception) when (IsRecoverableException(exception))
            {
                if (user.valid) user.UnpairDevicesAndRemoveUser();
                CLogger.LogError($"{DEBUG_FLAG} Shared-device join failed ({exception.GetType().Name}).");
                return null;
            }
            catch
            {
                if (user.valid) user.UnpairDevicesAndRemoveUser();
                throw;
            }
            finally
            {
                _joinsInProgress.Remove(playerIdToJoin);
            }
        }

        public async UniTask<IInputPlayer> JoinPlayerOnSharedDeviceAsync(int playerIdToJoin)
        {
            return await JoinPlayerOnSharedDeviceAsync(playerIdToJoin, default);
        }

        public async UniTask<IInputPlayer> JoinPlayerOnSharedDeviceAsync(
            int playerIdToJoin,
            CancellationToken cancellationToken)
        {
            await SwitchToUnityMainThreadAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (_shutdown.IsCancellationRequested) return null;
            return JoinPlayerOnSharedDevice(playerIdToJoin);
        }

        public IInputPlayer JoinPlayerAndLockDevice(int playerIdToJoin, InputDevice deviceToLock)
        {
            if (!ValidateMainThread(nameof(JoinPlayerAndLockDevice)) || deviceToLock == null) return null;
            if (_isDisposed || !_isInitialized || _isConfigurationTransitioning) return null;
            if (_registeredPlayers.TryGetValue(playerIdToJoin, out IInputPlayer existing)) return existing;
            if (!_joinsInProgress.Add(playerIdToJoin)) return null;

            try
            {
                long configurationRevision = _configurationRevision;
                RuntimePlayerSlotConfig config = GetRuntimePlayerConfig(playerIdToJoin, false);
                if (config == null || !IsDeviceClaimable(deviceToLock)) return null;

                if (!TrySelectDevices(config, deviceToLock, out List<InputDevice> devices, out string schemeName))
                    return null;

                return JoinPlayerWithDevices(
                    playerIdToJoin,
                    config,
                    devices,
                    schemeName,
                    configurationRevision);
            }
            finally
            {
                _joinsInProgress.Remove(playerIdToJoin);
            }
        }

        public async UniTask<IInputPlayer> JoinPlayerAndLockDeviceAsync(int playerIdToJoin, InputDevice deviceToLock)
        {
            return await JoinPlayerAndLockDeviceAsync(playerIdToJoin, deviceToLock, default);
        }

        public async UniTask<IInputPlayer> JoinPlayerAndLockDeviceAsync(
            int playerIdToJoin,
            InputDevice deviceToLock,
            CancellationToken cancellationToken)
        {
            await SwitchToUnityMainThreadAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (_shutdown.IsCancellationRequested) return null;
            return JoinPlayerAndLockDevice(playerIdToJoin, deviceToLock);
        }

        public void StartListeningForPlayers(bool lockDeviceOnJoin)
        {
            if (!_isInitialized || !ValidateMainThread(nameof(StartListeningForPlayers))) return;
            if (_isDisposed || _isConfigurationTransitioning) return;
            long configurationRevision = _configurationRevision;
            RuntimeInputConfiguration configuration = _runtimeConfiguration;
            InputAction replacement;
            try
            {
                replacement = CreatePreparedJoinAction(configuration);
            }
            catch (Exception exception) when (IsRecoverableException(exception))
            {
                CLogger.LogError(
                    $"{DEBUG_FLAG} Join listener could not be prepared ({exception.GetType().Name}).");
                return;
            }

            if (_isDisposed || !_isInitialized || _isConfigurationTransitioning ||
                configurationRevision != _configurationRevision ||
                !ReferenceEquals(configuration, _runtimeConfiguration))
            {
                DisposeJoinActionSafely(replacement, "discard a stale join listener");
                return;
            }

            _isDeviceLockingOnJoinEnabled = lockDeviceOnJoin;
            ReplaceJoinAction(replacement);
        }

        public void StopListeningForPlayers()
        {
            if (!ValidateMainThread(nameof(StopListeningForPlayers))) return;
            if (_isConfigurationTransitioning) return;
            ReplaceJoinAction(null);
        }

        private InputAction CreatePreparedJoinAction(RuntimeInputConfiguration configuration)
        {
            var action = new InputAction("CombinedJoin", InputActionType.Button);
            try
            {
                var paths = new HashSet<string>(StringComparer.Ordinal);
                AddJoinBindings(configuration.JoinAction, paths);
                for (int i = 0; i < configuration.PlayerSlots.Count; i++)
                    AddJoinBindings(configuration.PlayerSlots[i].JoinAction, paths);
                foreach (string path in paths) action.AddBinding(path);
                if (action.bindings.Count == 0)
                {
                    CleanupSafely(action.Dispose, "dispose an empty join listener");
                    return null;
                }

                action.performed += OnJoinAction;
                action.Enable();
                return action;
            }
            catch
            {
                action.performed -= OnJoinAction;
                CleanupSafely(action.Dispose, "dispose a failed join listener");
                throw;
            }
        }

        private void ReplaceJoinAction(InputAction replacement)
        {
            InputAction previous = _joinAction;
            _joinAction = replacement;
            bool isListening = replacement != null;
            if (_isListening != isListening)
            {
                _isListening = isListening;
                if (isListening)
                {
                    _listeningManagerCount++;
                }
                else if (_listeningManagerCount > 0)
                {
                    _listeningManagerCount--;
                }
            }

            if (previous != null && !ReferenceEquals(previous, replacement))
            {
                DisposeJoinActionSafely(previous, "replace the join listener");
            }
        }

        private void DisposeJoinActionSafely(InputAction action, string phase)
        {
            if (action == null) return;
            CleanupSafely(action.Disable, $"{phase}: disable");
            action.performed -= OnJoinAction;
            CleanupSafely(action.Dispose, phase);
        }

        private void OnJoinAction(InputAction.CallbackContext context)
        {
            if (_isConfigurationTransitioning || _isDisposed) return;
            InputDevice device = context.control?.device;
            if (device == null || !IsDeviceClaimable(device)) return;
            if (_isDeviceLockingOnJoinEnabled)
            {
                int playerId = GetPrimaryPlayerId();
                if (_registeredPlayers.TryGetValue(playerId, out IInputPlayer existing) && existing is InputPlayer inputPlayer)
                {
                    RuntimePlayerSlotConfig playerConfig = GetRuntimePlayerConfig(playerId, false);
                    if (playerConfig == null || !CanPairAdditionalDevice(playerConfig, device)) return;
                    try
                    {
                        InputUser.PerformPairingWithDevice(device, inputPlayer.User);
                    }
                    catch (Exception exception) when (IsRecoverableException(exception))
                    {
                        CLogger.LogError(
                            $"{DEBUG_FLAG} Additional join device could not be paired ({exception.GetType().Name}).");
                    }
                    return;
                }

                JoinPlayerAndLockDevice(playerId, device);
                return;
            }

            RuntimePlayerSlotConfig fallback = null;
            for (int i = 0; i < _runtimeConfiguration.PlayerSlots.Count; i++)
            {
                RuntimePlayerSlotConfig slot = _runtimeConfiguration.PlayerSlots[i];
                if (_registeredPlayers.ContainsKey(slot.PlayerId) || _joinsInProgress.Contains(slot.PlayerId)) continue;
                fallback ??= slot;
                if (slot.JoinAction != null && MatchesAnyJoinBinding(slot.JoinAction.DeviceBindings, device))
                {
                    TryJoinFromDevice(slot, device);
                    return;
                }
            }

            if (fallback != null) TryJoinFromDevice(fallback, device);
        }

        private void TryJoinFromDevice(RuntimePlayerSlotConfig config, InputDevice device)
        {
            if (!_joinsInProgress.Add(config.PlayerId)) return;
            try
            {
                long configurationRevision = _configurationRevision;
                if (TrySelectDevices(config, device, out List<InputDevice> devices, out string schemeName))
                    JoinPlayerWithDevices(
                        config.PlayerId,
                        config,
                        devices,
                        schemeName,
                        configurationRevision);
            }
            finally
            {
                _joinsInProgress.Remove(config.PlayerId);
            }
        }

        private IInputPlayer JoinPlayerWithDevices(
            int playerId,
            RuntimePlayerSlotConfig config,
            List<InputDevice> devices,
            string controlScheme,
            long configurationRevision)
        {
            using (JoinMarker.Auto())
            {
                if (!IsJoinConfigurationCurrent(playerId, config, configurationRevision) ||
                    devices == null || devices.Count == 0 || !TryReserveDevices(devices))
                    return null;
                InputUser user = default;
                try
                {
                    user = InputUser.PerformPairingWithDevice(devices[0]);
                    for (int i = 1; i < devices.Count; i++) InputUser.PerformPairingWithDevice(devices[i], user);
                    IInputPlayer player = CreatePlayerService(
                        playerId,
                        user,
                        config,
                        devices[0],
                        controlScheme,
                        configurationRevision);
                    if (player == null && user.valid) user.UnpairDevicesAndRemoveUser();
                    return player;
                }
                catch (Exception exception) when (IsRecoverableException(exception))
                {
                    if (user.valid) user.UnpairDevicesAndRemoveUser();
                    CLogger.LogError($"{DEBUG_FLAG} Player {playerId} join failed ({exception.GetType().Name}).");
                    return null;
                }
                catch
                {
                    if (user.valid) user.UnpairDevicesAndRemoveUser();
                    throw;
                }
                finally
                {
                    ReleaseReservedDevices(devices);
                }
            }
        }

        private IInputPlayer CreatePlayerService(
            int playerId,
            InputUser user,
            RuntimePlayerSlotConfig config,
            InputDevice initialDevice,
            string controlScheme,
            long configurationRevision)
        {
            if (!IsJoinConfigurationCurrent(playerId, config, configurationRevision)) return null;
            InputPlayer inputPlayer = null;
            try
            {
                inputPlayer = new InputPlayer(playerId, user, config, initialDevice);
                if (!IsJoinConfigurationCurrent(playerId, config, configurationRevision))
                {
                    inputPlayer.Dispose();
                    return null;
                }
                if (!string.IsNullOrEmpty(controlScheme)) inputPlayer.User.ActivateControlScheme(controlScheme);
                if (_bindingOverridesByPlayer.TryGetValue(playerId, out string overrides) &&
                    !inputPlayer.ImportBindingOverridesJson(overrides))
                    throw new InvalidOperationException($"Stored binding overrides for player {playerId} are invalid.");
                if (!IsJoinConfigurationCurrent(playerId, config, configurationRevision))
                {
                    inputPlayer.Dispose();
                    return null;
                }

                _registeredPlayers.Add(playerId, inputPlayer);
                NotifyPlayerInputReady(playerId, inputPlayer);

                if (IsPlayerRegistrationCurrent(playerId, inputPlayer)) return inputPlayer;
                if (_registeredPlayers.TryGetValue(playerId, out IInputPlayer registered) &&
                    ReferenceEquals(registered, inputPlayer))
                {
                    _registeredPlayers.Remove(playerId);
                }
                if (!inputPlayer.IsDisposed) inputPlayer.Dispose();
                return null;
            }
            catch
            {
                if (_registeredPlayers.TryGetValue(playerId, out IInputPlayer registered) &&
                    ReferenceEquals(registered, inputPlayer))
                {
                    _registeredPlayers.Remove(playerId);
                }
                if (inputPlayer != null && !inputPlayer.IsDisposed) inputPlayer.Dispose();
                throw;
            }
        }

        private bool IsJoinConfigurationCurrent(
            int playerId,
            RuntimePlayerSlotConfig config,
            long configurationRevision)
        {
            return !_isDisposed &&
                   !_isConfigurationTransitioning &&
                   configurationRevision == _configurationRevision &&
                   ReferenceEquals(GetRuntimePlayerConfig(playerId, false), config);
        }

        private bool IsPlayerRegistrationCurrent(int playerId, IInputPlayer player)
        {
            return !_isDisposed &&
                   player is InputPlayer inputPlayer &&
                   !inputPlayer.IsDisposed &&
                   _registeredPlayers.TryGetValue(playerId, out IInputPlayer registered) &&
                   ReferenceEquals(registered, player);
        }

        internal bool TrySelectDevices(
            RuntimePlayerSlotConfig config,
            InputDevice mustInclude,
            out List<InputDevice> selected,
            out string schemeName)
        {
            selected = null;
            schemeName = null;
            List<InputDevice> available = GetClaimableDevices(mustInclude);
            if (config.ControlSchemes.Count > 0)
            {
                if (!string.IsNullOrEmpty(config.DefaultControlScheme) &&
                    TryMatchControlScheme(config, config.DefaultControlScheme, available, mustInclude, out selected, out schemeName))
                    return true;
                for (int i = 0; i < config.ControlSchemes.Count; i++)
                {
                    RuntimeControlSchemeConfig scheme = config.ControlSchemes[i];
                    if (string.Equals(scheme.Name, config.DefaultControlScheme, StringComparison.Ordinal)) continue;
                    if (TryMatchControlScheme(config, scheme.Name, available, mustInclude, out selected, out schemeName)) return true;
                }

                return false;
            }

            List<string> layouts = GetCandidateLayouts(config);
            selected = new List<InputDevice>(2);
            if (mustInclude != null)
            {
                if (!MatchesAnyLayout(mustInclude, layouts) || !IsDeviceClaimable(mustInclude)) return false;
                selected.Add(mustInclude);
            }
            else
            {
                for (int i = 0; i < layouts.Count; i++)
                {
                    InputDevice device = FindAvailableDeviceByLayout(available, layouts[i], selected);
                    if (device == null) continue;
                    selected.Add(device);
                    break;
                }
            }

            if (selected.Count == 0) return false;
            AddKeyboardMouseCompanion(available, selected);
            return true;
        }

        private static bool TryMatchControlScheme(
            RuntimePlayerSlotConfig config,
            string requestedName,
            List<InputDevice> available,
            InputDevice mustInclude,
            out List<InputDevice> selected,
            out string schemeName)
        {
            selected = null;
            schemeName = null;
            RuntimeControlSchemeConfig source = null;
            for (int i = 0; i < config.ControlSchemes.Count; i++)
            {
                if (string.Equals(config.ControlSchemes[i].Name, requestedName, StringComparison.Ordinal))
                {
                    source = config.ControlSchemes[i];
                    break;
                }
            }

            if (source == null) return false;
            InputControlScheme scheme = CreateControlScheme(source);
            using InputControlScheme.MatchResult match = scheme.PickDevicesFrom(available, mustInclude);
            if (!match.isSuccessfulMatch) return false;
            selected = new List<InputDevice>(match.devices.Count);
            for (int i = 0; i < match.devices.Count; i++)
            {
                InputDevice device = match.devices[i];
                if (device != null && !ContainsDevice(selected, device)) selected.Add(device);
            }

            if (mustInclude != null && !ContainsDevice(selected, mustInclude)) return false;
            schemeName = source.Name;
            return selected.Count > 0;
        }

        private static InputControlScheme CreateControlScheme(RuntimeControlSchemeConfig source)
        {
            var requirements = new InputControlScheme.DeviceRequirement[source.DeviceRequirements.Count];
            for (int i = 0; i < requirements.Length; i++)
            {
                RuntimeControlSchemeDeviceRequirementConfig requirement = source.DeviceRequirements[i];
                requirements[i] = new InputControlScheme.DeviceRequirement
                {
                    controlPath = requirement.ControlPath,
                    isOptional = requirement.IsOptional,
                    isOR = requirement.IsOr
                };
            }

            return new InputControlScheme(source.Name, requirements, source.BindingGroup);
        }

        private List<InputDevice> GetClaimableDevices(InputDevice mustInclude)
        {
            var result = new List<InputDevice>(UnityEngine.InputSystem.InputSystem.devices.Count);
            var devices = UnityEngine.InputSystem.InputSystem.devices;
            for (int i = 0; i < devices.Count; i++)
            {
                InputDevice device = devices[i];
                if (IsDeviceClaimable(device) || ReferenceEquals(device, mustInclude)) result.Add(device);
            }

            return result;
        }

        private bool IsDeviceClaimable(InputDevice device)
        {
            if (device == null || _reservedDeviceIds.Contains(device.deviceId)) return false;
            var users = InputUser.all;
            for (int i = 0; i < users.Count; i++)
            {
                if (IsDevicePairedToUser(users[i], device)) return false;
            }

            return true;
        }

        private bool TryReserveDevices(List<InputDevice> devices)
        {
            for (int i = 0; i < devices.Count; i++)
            {
                if (!IsDeviceClaimable(devices[i])) return false;
            }

            for (int i = 0; i < devices.Count; i++) _reservedDeviceIds.Add(devices[i].deviceId);
            return true;
        }

        private void ReleaseReservedDevices(List<InputDevice> devices)
        {
            for (int i = 0; i < devices.Count; i++) _reservedDeviceIds.Remove(devices[i].deviceId);
        }

        private static List<string> GetCandidateLayouts(RuntimePlayerSlotConfig config)
        {
            var layouts = new List<string>();
            for (int i = 0; i < config.Contexts.Count; i++)
            {
                RuntimeContextDefinitionConfig context = config.Contexts[i];
                for (int j = 0; j < context.Bindings.Count; j++)
                {
                    RuntimeActionBindingConfig action = context.Bindings[j];
                    for (int k = 0; k < action.DeviceBindings.Count; k++) AddLayout(action.DeviceBindings[k], layouts);
                    for (int k = 0; k < action.CompositeBindings.Count; k++)
                    {
                        RuntimeCompositeBindingConfig composite = action.CompositeBindings[k];
                        for (int part = 0; part < composite.Parts.Count; part++) AddLayout(composite.Parts[part].Path, layouts);
                    }
                }
            }

            return layouts;
        }

        private static void AddLayout(string path, List<string> layouts)
        {
            int start = path.IndexOf('<');
            int end = start < 0 ? -1 : path.IndexOf('>', start + 1);
            if (start < 0 || end <= start) return;
            string layout = path.Substring(start + 1, end - start - 1);
            for (int i = 0; i < layouts.Count; i++)
            {
                if (string.Equals(layouts[i], layout, StringComparison.Ordinal)) return;
            }

            layouts.Add(layout);
        }

        private static bool MatchesAnyLayout(InputDevice device, List<string> layouts)
        {
            for (int i = 0; i < layouts.Count; i++)
            {
                if (UnityEngine.InputSystem.InputSystem.IsFirstLayoutBasedOnSecond(device.layout, layouts[i])) return true;
            }

            return false;
        }

        private static InputDevice FindAvailableDeviceByLayout(
            List<InputDevice> available,
            string layout,
            List<InputDevice> selected)
        {
            for (int i = 0; i < available.Count; i++)
            {
                InputDevice device = available[i];
                if (!ContainsDevice(selected, device) &&
                    UnityEngine.InputSystem.InputSystem.IsFirstLayoutBasedOnSecond(device.layout, layout))
                    return device;
            }

            return null;
        }

        private static void AddKeyboardMouseCompanion(List<InputDevice> available, List<InputDevice> selected)
        {
            bool hasKeyboard = false;
            bool hasMouse = false;
            for (int i = 0; i < selected.Count; i++)
            {
                hasKeyboard |= selected[i] is Keyboard;
                hasMouse |= selected[i] is Mouse;
            }

            if (hasKeyboard == hasMouse) return;
            for (int i = 0; i < available.Count; i++)
            {
                if ((hasKeyboard && available[i] is Mouse) || (hasMouse && available[i] is Keyboard))
                {
                    selected.Add(available[i]);
                    return;
                }
            }
        }

        private static bool ContainsDevice(List<InputDevice> devices, InputDevice target)
        {
            for (int i = 0; i < devices.Count; i++)
            {
                if (ReferenceEquals(devices[i], target)) return true;
            }

            return false;
        }

        private static bool MatchesAnyJoinBinding(IReadOnlyList<string> bindings, InputDevice device)
        {
            for (int i = 0; i < bindings.Count; i++)
            {
                string path = bindings[i];
                int start = path.IndexOf('<');
                int end = start < 0 ? -1 : path.IndexOf('>', start + 1);
                if (start >= 0 && end > start)
                {
                    string layout = path.Substring(start + 1, end - start - 1);
                    if (UnityEngine.InputSystem.InputSystem.IsFirstLayoutBasedOnSecond(device.layout, layout)) return true;
                }
            }

            return false;
        }

        private static bool CanPairAdditionalDevice(RuntimePlayerSlotConfig config, InputDevice device)
        {
            if (config.ControlSchemes.Count == 0)
                return MatchesAnyLayout(device, GetCandidateLayouts(config));

            for (int schemeIndex = 0; schemeIndex < config.ControlSchemes.Count; schemeIndex++)
            {
                RuntimeControlSchemeConfig scheme = config.ControlSchemes[schemeIndex];
                for (int requirementIndex = 0; requirementIndex < scheme.DeviceRequirements.Count; requirementIndex++)
                {
                    if (MatchesLayoutPath(device, scheme.DeviceRequirements[requirementIndex].ControlPath)) return true;
                }
            }

            return false;
        }

        private static bool MatchesLayoutPath(InputDevice device, string path)
        {
            int start = path.IndexOf('<');
            int end = start < 0 ? -1 : path.IndexOf('>', start + 1);
            return start >= 0 && end > start &&
                   UnityEngine.InputSystem.InputSystem.IsFirstLayoutBasedOnSecond(
                       device.layout,
                       path.Substring(start + 1, end - start - 1));
        }

        private static void AddJoinBindings(RuntimeActionBindingConfig action, HashSet<string> paths)
        {
            if (action == null) return;
            for (int i = 0; i < action.DeviceBindings.Count; i++) paths.Add(action.DeviceBindings[i]);
        }

        private RuntimePlayerSlotConfig GetRuntimePlayerConfig(int playerId, bool checkJoined = true)
        {
            if (!_isInitialized || (checkJoined && _registeredPlayers.ContainsKey(playerId))) return null;
            for (int i = 0; i < _runtimeConfiguration.PlayerSlots.Count; i++)
            {
                if (_runtimeConfiguration.PlayerSlots[i].PlayerId == playerId) return _runtimeConfiguration.PlayerSlots[i];
            }

            return null;
        }

        private PlayerSlotConfig GetMutablePlayerConfig(int playerId)
        {
            if (!_isInitialized) return null;
            for (int i = 0; i < _configuration.PlayerSlots.Count; i++)
            {
                if (_configuration.PlayerSlots[i].PlayerId == playerId) return _configuration.PlayerSlots[i];
            }

            return null;
        }

        private int GetPrimaryPlayerId()
        {
            if (GetRuntimePlayerConfig(0, false) != null) return 0;
            return _runtimeConfiguration.PlayerSlots.Count > 0 ? _runtimeConfiguration.PlayerSlots[0].PlayerId : 0;
        }

        private static bool IsDevicePairedToUser(InputUser user, InputDevice device)
        {
            var devices = user.pairedDevices;
            for (int i = 0; i < devices.Count; i++)
            {
                if (ReferenceEquals(devices[i], device)) return true;
            }

            return false;
        }

        private async UniTask WaitForDeviceChangeAsync(CancellationToken cancellationToken)
        {
            long revision = _deviceRevision;
            await UniTask.WaitUntil(
                () => _deviceRevision != revision,
                PlayerLoopTiming.Update,
                cancellationToken);
        }

        private void OnDeviceTopologyChanged(InputDevice _, InputDeviceChange __)
        {
            _deviceRevision++;
        }

        public IInputPlayer GetInputPlayer(int playerId)
        {
            EnsureMainThread();
            return _registeredPlayers.TryGetValue(playerId, out IInputPlayer player) ? player : null;
        }

        public async UniTask<IInputPlayer> GetOrJoinInputPlayerAsync(int playerId, int timeoutInSeconds = 5)
        {
            await SwitchToUnityMainThreadAsync();
            return _registeredPlayers.TryGetValue(playerId, out IInputPlayer player)
                ? player
                : await JoinSinglePlayerAsync(playerId, timeoutInSeconds);
        }

        public bool RefreshPlayerInput(int playerId)
        {
            EnsureMainThread();
            if (!_registeredPlayers.TryGetValue(playerId, out IInputPlayer player)) return false;
            NotifyPlayerInputReady(playerId, player);
            return IsPlayerRegistrationCurrent(playerId, player);
        }

        public bool RemovePlayer(int playerId)
        {
            EnsureMainThread();
            if (!_registeredPlayers.TryGetValue(playerId, out IInputPlayer player)) return false;
            _registeredPlayers.Remove(playerId);
            if (player is IDisposable disposable)
                CleanupSafely(disposable.Dispose, $"dispose player {playerId}");
            return true;
        }

        public bool RebindAction(int playerId, string actionMapName, string actionName, string oldBinding, string newBinding)
        {
            EnsureMainThread();
            return _registeredPlayers.TryGetValue(playerId, out IInputPlayer player) &&
                   player.RebindAction(actionMapName, actionName, oldBinding, newBinding);
        }

        public bool ResetActionBinding(int playerId, string actionMapName, string actionName)
        {
            EnsureMainThread();
            return _registeredPlayers.TryGetValue(playerId, out IInputPlayer player) &&
                   player.ResetActionBinding(actionMapName, actionName);
        }

        public void ResetAllActionBindings(int playerId)
        {
            EnsureMainThread();
            if (_registeredPlayers.TryGetValue(playerId, out IInputPlayer player)) player.ResetAllActionBindings();
        }

        public string[] GetActionBindings(int playerId, string actionMapName, string actionName)
        {
            EnsureMainThread();
            return _registeredPlayers.TryGetValue(playerId, out IInputPlayer player)
                ? player.GetActionBindings(actionMapName, actionName)
                : Array.Empty<string>();
        }

        public Observable<Unit> GetChordObservable(
            int playerId,
            string actionMapName,
            string firstAction,
            string secondAction,
            float windowMs = 300f)
        {
            EnsureMainThread();
            return _registeredPlayers.TryGetValue(playerId, out IInputPlayer player)
                ? player.GetChordObservable(actionMapName, firstAction, secondAction, windowMs)
                : EmptyObservables.Unit;
        }

        public InputBindingOverrideProfile ExportBindingOverrideProfile()
        {
            if (!TryExportBindingOverrideProfile(out InputBindingOverrideProfile profile))
            {
                throw new InvalidOperationException(
                    "Binding override profile exceeds the manager export budget.");
            }

            return profile;
        }

        public bool TryExportBindingOverrideProfile(out InputBindingOverrideProfile profile)
        {
            EnsureMainThread();
            profile = new InputBindingOverrideProfile();
            int totalBytes = 0;
            foreach (KeyValuePair<int, IInputPlayer> pair in _registeredPlayers)
            {
                if (!pair.Value.TryExportBindingOverridesJson(out string overridesJson) ||
                    !TryGetStrictUtf8ByteCount(overridesJson, out int entryBytes) ||
                    entryBytes > MaxOverrideJsonPerPlayer ||
                    entryBytes > MaxOverrideProfileBytes - totalBytes)
                {
                    profile = null;
                    return false;
                }

                totalBytes += entryBytes;
                profile.Players.Add(new InputBindingOverrideEntry
                {
                    PlayerId = pair.Key,
                    OverridesJson = overridesJson
                });
            }

            foreach (KeyValuePair<int, string> pair in _bindingOverridesByPlayer)
            {
                if (_registeredPlayers.ContainsKey(pair.Key)) continue;
                if (!TryGetStrictUtf8ByteCount(pair.Value, out int entryBytes) ||
                    entryBytes > MaxOverrideJsonPerPlayer ||
                    entryBytes > MaxOverrideProfileBytes - totalBytes)
                {
                    profile = null;
                    return false;
                }

                totalBytes += entryBytes;
                profile.Players.Add(new InputBindingOverrideEntry { PlayerId = pair.Key, OverridesJson = pair.Value });
            }

            profile.Players.Sort((left, right) => left.PlayerId.CompareTo(right.PlayerId));
            return true;
        }

        public bool ImportBindingOverrideProfile(InputBindingOverrideProfile profile)
        {
            EnsureMainThread();
            if (profile == null || profile.SchemaVersion != InputBindingOverrideProfile.CurrentSchemaVersion ||
                profile.Players == null || profile.Players.Count > _limits.MaxPlayers)
                return false;

            var staged = new Dictionary<int, string>(profile.Players.Count);
            int totalBytes = 0;
            for (int i = 0; i < profile.Players.Count; i++)
            {
                InputBindingOverrideEntry entry = profile.Players[i];
                RuntimePlayerSlotConfig playerConfig = entry == null
                    ? null
                    : GetRuntimePlayerConfig(entry.PlayerId, false);
                if (entry == null || playerConfig == null ||
                    entry.OverridesJson == null || !TryGetStrictUtf8ByteCount(entry.OverridesJson, out int entryBytes) ||
                    entryBytes > MaxOverrideJsonPerPlayer ||
                    !staged.TryAdd(entry.PlayerId, entry.OverridesJson) ||
                    !InputPlayer.ValidateBindingOverridesJsonForConfiguration(
                        entry.OverridesJson,
                        playerConfig))
                    return false;
                totalBytes += entryBytes;
                if (totalBytes > MaxOverrideProfileBytes) return false;
            }

            var rollback = new Dictionary<int, string>();
            foreach (KeyValuePair<int, string> pair in staged)
            {
                if (!_registeredPlayers.TryGetValue(pair.Key, out IInputPlayer player)) continue;
                rollback[pair.Key] = player.ExportBindingOverridesJson();
                if (!player.ImportBindingOverridesJson(pair.Value))
                {
                    foreach (KeyValuePair<int, string> previous in rollback)
                        _registeredPlayers[previous.Key].ImportBindingOverridesJson(previous.Value);
                    return false;
                }
            }

            _bindingOverridesByPlayer.Clear();
            foreach (KeyValuePair<int, string> pair in staged) _bindingOverridesByPlayer.Add(pair.Key, pair.Value);
            return true;
        }

        public string ExportBindingOverrideProfileJson()
        {
            string json = JsonUtility.ToJson(ExportBindingOverrideProfile());
            if (!TryGetStrictUtf8ByteCount(json, out int byteCount) || byteCount > MaxOverrideProfileBytes)
                throw new InvalidOperationException("Binding override profile exceeds the configured byte limit.");
            return json;
        }

        public bool ImportBindingOverrideProfileJson(string json)
        {
            EnsureMainThread();
            if (string.IsNullOrEmpty(json) || !TryGetStrictUtf8ByteCount(json, out int byteCount) ||
                byteCount > MaxOverrideProfileBytes) return false;
            try
            {
                InputBindingOverrideProfile profile = JsonUtility.FromJson<InputBindingOverrideProfile>(json);
                return ImportBindingOverrideProfile(profile);
            }
            catch (Exception exception) when (IsRecoverableException(exception))
            {
                CLogger.LogError($"{DEBUG_FLAG} Failed to import binding override profile.");
                return false;
            }
        }

        public List<BindingConflict> CheckBindingConflicts(int playerId)
        {
            EnsureMainThread();
            return InputBindingValidator.DetectConflicts(GetMutablePlayerConfig(playerId));
        }

        public List<BindingConflict> CheckBindingConflicts(int playerId, string contextName)
        {
            EnsureMainThread();
            return InputBindingValidator.DetectConflicts(GetMutablePlayerConfig(playerId), contextName);
        }

        public static string FormatConflictsReport(List<BindingConflict> conflicts)
        {
            return InputBindingValidator.FormatConflictsReport(conflicts);
        }

        public static Vector2 GetMousePosition()
        {
            EnsureMainThread();
            return Mouse.current == null ? Vector2.zero : Mouse.current.position.ReadValue();
        }

        public void Dispose()
        {
            EnsureMainThread();
            DisposeCore();
        }

        private void DisposeCore()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            ActiveManagers.Remove(this);
            _isInitialized = false;
            _isConfigurationTransitioning = true;
            if (_deviceChangeSubscribed)
            {
                UnityEngine.InputSystem.InputSystem.onDeviceChange -= OnDeviceTopologyChanged;
                _deviceChangeSubscribed = false;
            }

            var players = new List<KeyValuePair<int, IInputPlayer>>(_registeredPlayers);
            _registeredPlayers.Clear();
            _joinsInProgress.Clear();
            _reservedDeviceIds.Clear();
            _bindingOverridesByPlayer.Clear();
            _configuration = null;
            _runtimeConfiguration = null;
            _userConfigUri = null;
            OnPlayerInputReady = null;
            OnConfigurationReloaded = null;

            CleanupSafely(_shutdown.Cancel, "cancel manager operations");
            CleanupSafely(() => ReplaceJoinAction(null), "dispose the join listener");
            for (int i = 0; i < players.Count; i++)
            {
                KeyValuePair<int, IInputPlayer> entry = players[i];
                IInputPlayer player = entry.Value;
                if (player is IDisposable disposable)
                {
                    CleanupSafely(disposable.Dispose, $"dispose player {entry.Key}");
                }
            }
            CleanupSafely(_shutdown.Dispose, "dispose the manager cancellation source");
            if (ReferenceEquals(_compatibilityInstance, this)) _compatibilityInstance = null;
        }

        private static bool ValidateMainThread(string operation)
        {
            if (Cysharp.Threading.Tasks.PlayerLoopHelper.IsMainThread) return true;
            CLogger.LogError($"{DEBUG_FLAG} {operation} must run on the Unity main thread.");
            return false;
        }

        private static void EnsureMainThread()
        {
            if (!Cysharp.Threading.Tasks.PlayerLoopHelper.IsMainThread)
                throw new InvalidOperationException("InputManager operations must run on the Unity main thread.");
        }

        private static async UniTask SwitchToUnityMainThreadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Cysharp.Threading.Tasks.PlayerLoopHelper.IsMainThread)
                await UniTask.SwitchToMainThread(PlayerLoopTiming.Update, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }

        private void RollbackBatchPlayers(List<KeyValuePair<int, IInputPlayer>> players)
        {
            for (int i = players.Count - 1; i >= 0; i--)
            {
                KeyValuePair<int, IInputPlayer> entry = players[i];
                if (!_registeredPlayers.TryGetValue(entry.Key, out IInputPlayer current) ||
                    !ReferenceEquals(current, entry.Value))
                    continue;

                _registeredPlayers.Remove(entry.Key);
                if (current is IDisposable disposable)
                    CleanupSafely(disposable.Dispose, $"roll back batch player {entry.Key}");
            }
        }

        private bool TryBeginConfigurationOperation()
        {
            if (_isDisposed ||
                Interlocked.CompareExchange(ref _configurationOperationInProgress, 1, 0) != 0)
                return false;
            if (_isDisposed)
            {
                EndConfigurationOperation();
                return false;
            }

            return true;
        }

        private void EndConfigurationOperation()
        {
            Interlocked.Exchange(ref _configurationOperationInProgress, 0);
        }

        private bool TrySnapshotBatchPlayerIds(List<int> playerIds, out int[] snapshot)
        {
            snapshot = null;
            if (playerIds == null || playerIds.Count > _limits.MaxPlayers) return false;
            int count = playerIds.Count;
            if (count == 0)
            {
                snapshot = Array.Empty<int>();
                return true;
            }

            var uniqueIds = new HashSet<int>();
            snapshot = new int[count];
            for (int i = 0; i < count; i++)
            {
                int playerId = playerIds[i];
                if (!uniqueIds.Add(playerId))
                {
                    snapshot = null;
                    return false;
                }

                snapshot[i] = playerId;
            }

            return true;
        }

        private static string NormalizeLineEndings(string value)
        {
            return string.IsNullOrEmpty(value) ? value : value.Replace("\r\n", "\n").Replace("\r", "\n");
        }

        private static bool TryGetStrictUtf8ByteCount(string value, out int byteCount)
        {
            try
            {
                byteCount = StrictUtf8.GetByteCount(value);
                return true;
            }
            catch (EncoderFallbackException)
            {
                byteCount = 0;
                return false;
            }
        }

        private static bool IsRecoverableException(Exception exception)
        {
            return exception is not OutOfMemoryException &&
                   exception is not AccessViolationException &&
                   exception is not StackOverflowException;
        }

        private static void CleanupSafely(Action cleanup, string phase)
        {
            try
            {
                cleanup();
            }
            catch (Exception exception) when (IsRecoverableException(exception))
            {
                CLogger.LogError(
                    $"{DEBUG_FLAG} Failed to {phase} during teardown ({exception.GetType().Name}).");
            }
        }

        private void NotifyConfigurationReloaded(long committedRevision)
        {
            Action handlers = OnConfigurationReloaded;
            if (handlers == null) return;
            Delegate[] invocationList = handlers.GetInvocationList();
            for (int i = 0; i < invocationList.Length; i++)
            {
                try
                {
                    ((Action)invocationList[i]).Invoke();
                }
                catch (Exception exception) when (IsRecoverableException(exception))
                {
                    CLogger.LogError(
                        $"{DEBUG_FLAG} Configuration reload subscriber failed ({exception.GetType().Name}).");
                }

                if (_isDisposed || committedRevision != _configurationRevision) return;
            }
        }

        private void NotifyPlayerInputReady(int playerId, IInputPlayer inputPlayer)
        {
            Action<IInputPlayer> handlers = OnPlayerInputReady;
            if (handlers == null) return;
            Delegate[] invocationList = handlers.GetInvocationList();
            for (int i = 0; i < invocationList.Length; i++)
            {
                try
                {
                    ((Action<IInputPlayer>)invocationList[i]).Invoke(inputPlayer);
                }
                catch (Exception exception) when (IsRecoverableException(exception))
                {
                    CLogger.LogError(
                        $"{DEBUG_FLAG} Player-ready subscriber failed ({exception.GetType().Name}).");
                }


                if (!IsPlayerRegistrationCurrent(playerId, inputPlayer)) return;
            }
        }
    }
}
