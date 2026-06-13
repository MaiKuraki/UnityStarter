using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using CycloneGames.Logger;
using Cysharp.Threading.Tasks;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Users;
using VYaml.Serialization;
using UnityEngine;
using R3;

namespace CycloneGames.InputSystem.Runtime
{
    public sealed class InputManager : IDisposable
    {
        private const string DEBUG_FLAG = "[InputManager]";
        public static InputManager Instance { get; } = new InputManager();
        private InputManager() { }

        public static bool IsListeningForPlayers { get; private set; }
        public event Action<IInputPlayer> OnPlayerInputReady;
        public event Action OnConfigurationReloaded;

        private readonly Dictionary<int, IInputPlayer> _registerPlayers = new();
        private InputConfiguration _configuration;
        private InputAction _joinAction;
        private string _userConfigUri;
        private bool _isInitialized = false;
        private bool _isDisposed = false;
        private bool _isDeviceLockingOnJoinEnabled = false;
        private IDisposable _cursorSubscription;

        public bool ManageCursorVisibility { get; set; } = true;
        public bool ResetCursorToCenter { get; set; } = false;

        private static bool ValidateMainThread(string operationName)
        {
            if (Cysharp.Threading.Tasks.PlayerLoopHelper.IsMainThread)
            {
                return true;
            }

            CLogger.LogError($"{DEBUG_FLAG} {operationName} must be called on Unity main thread. Use the async API or await UniTask.SwitchToMainThread before calling it.");
            return false;
        }

        private static async UniTask SwitchToUnityMainThreadAsync(CancellationToken cancellationToken = default)
        {
            if (!Cysharp.Threading.Tasks.PlayerLoopHelper.IsMainThread)
            {
                await UniTask.SwitchToMainThread(PlayerLoopTiming.Update, cancellationToken);
            }
        }

        public void Initialize(string yamlContent, string userConfigUri)
        {
            if (_isInitialized) return;
            if (string.IsNullOrEmpty(yamlContent)) return;

            using (InputPerformanceProfiler.BeginScope("Initialize"))
            {
                try
                {
                    // Normalize line endings for cross-platform compatibility
                    string normalizedContent = NormalizeLineEndings(yamlContent);
                    _configuration = YamlSerializer.Deserialize<InputConfiguration>(System.Text.Encoding.UTF8.GetBytes(normalizedContent));
                    _userConfigUri = userConfigUri;
                    _isDisposed = false;
                    _isInitialized = true;
                    CLogger.LogInfo($"{DEBUG_FLAG} Initialized successfully.");
                }
                catch (Exception e) { CLogger.LogError($"{DEBUG_FLAG} Failed to parse YAML: {e.Message}"); }
            }
        }


        /// <summary>
        /// Reinitializes InputManager with new configuration. Allows reinitialization even if already initialized.
        /// Useful for hot-update scenarios or when switching configurations.
        /// </summary>
        public void Reinitialize(string yamlContent, string userConfigUri)
        {
            if (string.IsNullOrEmpty(yamlContent)) return;

            using (InputPerformanceProfiler.BeginScope("Reinitialize"))
            {
                try
                {
                    // Normalize line endings for cross-platform compatibility
                    string normalizedContent = NormalizeLineEndings(yamlContent);
                    _configuration = YamlSerializer.Deserialize<InputConfiguration>(System.Text.Encoding.UTF8.GetBytes(normalizedContent));
                    _userConfigUri = userConfigUri;
                    _isDisposed = false;
                    _isInitialized = true;
                    CLogger.LogInfo($"{DEBUG_FLAG} Reinitialized successfully.");
                    OnConfigurationReloaded?.Invoke();
                }
                catch (Exception e) { CLogger.LogError($"{DEBUG_FLAG} Failed to reinitialize: {e.Message}"); }
            }
        }

        /// <summary>
        /// Normalizes line endings to Unix-style (LF only) for cross-platform compatibility.
        /// </summary>
        private static string NormalizeLineEndings(string content)
        {
            if (string.IsNullOrEmpty(content)) return content;
            return content.Replace("\r\n", "\n").Replace("\r", "\n");
        }


        /// <summary>
        /// Hot-reloads configuration at runtime. Existing players keep current config; new players use reloaded config.
        /// </summary>
        public UniTask<bool> ReloadConfigurationAsync()
        {
            return ReloadConfigurationAsync(default);
        }

        /// <summary>
        /// Hot-reloads configuration at runtime. Existing players keep current config; new players use reloaded config.
        /// </summary>
        public async UniTask<bool> ReloadConfigurationAsync(CancellationToken cancellationToken)
        {
            if (!_isInitialized || string.IsNullOrEmpty(_userConfigUri))
            {
                CLogger.LogWarning($"{DEBUG_FLAG} Cannot reload: not initialized.");
                return false;
            }

            using (InputPerformanceProfiler.BeginScope("ReloadConfiguration"))
            {
                try
                {
                    (bool success, string yamlContent) = await InputConfigurationFileLoader.LoadTextFromUriAsync(_userConfigUri, DEBUG_FLAG, cancellationToken);
                    if (!success || string.IsNullOrEmpty(yamlContent))
                    {
                        CLogger.LogError($"{DEBUG_FLAG} Configuration file not found or empty: {_userConfigUri}");
                        return false;
                    }

                    string normalizedContent = NormalizeLineEndings(yamlContent);
                    byte[] yamlBytes = Encoding.UTF8.GetBytes(normalizedContent);
                    var newConfig = YamlSerializer.Deserialize<InputConfiguration>(yamlBytes);

                    _configuration = newConfig;
                    CLogger.LogInfo($"{DEBUG_FLAG} Configuration reloaded successfully.");
                    OnConfigurationReloaded?.Invoke();
                    return true;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    CLogger.LogError($"{DEBUG_FLAG} Failed to reload configuration: {e.Message}");
                    return false;
                }
            }
        }

        /// <summary>
        /// Batch joins multiple players. Optimized for local multiplayer scenarios.
        /// </summary>
        public List<IInputPlayer> JoinPlayersBatch(List<int> playerIds)
        {
            if (playerIds == null || playerIds.Count == 0) return new List<IInputPlayer>();
            if (!ValidateMainThread(nameof(JoinPlayersBatch))) return new List<IInputPlayer>();

            using (InputPerformanceProfiler.BeginScope("JoinPlayersBatch"))
            {
                var results = new List<IInputPlayer>(playerIds.Count);
                foreach (int playerId in playerIds)
                {
                    var service = JoinSinglePlayer(playerId);
                    if (service != null)
                    {
                        results.Add(service);
                    }
                }
                return results;
            }
        }

        /// <summary>
        /// Async batch player joining with timeout support.
        /// </summary>
        public async UniTask<List<IInputPlayer>> JoinPlayersBatchAsync(List<int> playerIds, int timeoutPerPlayerInSeconds = 5)
        {
            if (playerIds == null || playerIds.Count == 0) return new List<IInputPlayer>();
            await SwitchToUnityMainThreadAsync();

            using (InputPerformanceProfiler.BeginScope("JoinPlayersBatchAsync"))
            {
                var results = new List<IInputPlayer>(playerIds.Count);
                var tasks = new List<UniTask<IInputPlayer>>(playerIds.Count);

                foreach (int playerId in playerIds)
                {
                    tasks.Add(JoinSinglePlayerAsync(playerId, timeoutPerPlayerInSeconds));
                }

                var completedTasks = await UniTask.WhenAll(tasks);
                foreach (var service in completedTasks)
                {
                    if (service != null)
                    {
                        results.Add(service);
                    }
                }

                return results;
            }
        }

        public UniTask SaveUserConfigurationAsync()
        {
            return SaveUserConfigurationAsync(default);
        }

        public async UniTask SaveUserConfigurationAsync(CancellationToken cancellationToken)
        {
            if (!_isInitialized || string.IsNullOrEmpty(_userConfigUri)) return;

            try
            {
                byte[] yamlBytes = YamlSerializer.Serialize(_configuration).ToArray();

                // Normalize line endings to Unix-style (LF) for cross-platform compatibility
                // This ensures configs saved on Windows work correctly on Linux/SteamDeck/macOS
                string yamlContent = Encoding.UTF8.GetString(yamlBytes);
                yamlContent = NormalizeLineEndings(yamlContent);
                await InputConfigurationFileLoader.SaveTextToUriAsync(_userConfigUri, yamlContent, DEBUG_FLAG, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                CLogger.LogError($"{DEBUG_FLAG} Failed to save user configuration: {e.Message}");
            }
        }

        public void StartListeningForPlayers(bool lockDeviceOnJoin)
        {
            if (!_isInitialized) return;
            if (!ValidateMainThread(nameof(StartListeningForPlayers))) return;

            _isDeviceLockingOnJoinEnabled = lockDeviceOnJoin;
            if (_joinAction != null) _joinAction.Dispose();

            _joinAction = new InputAction(name: "CombinedJoin", type: InputActionType.Button);

            foreach (var playerConfig in _configuration.PlayerSlots)
            {
                if (playerConfig.JoinAction != null)
                {
                    foreach (var binding in playerConfig.JoinAction.DeviceBindings)
                    {
                        _joinAction.AddBinding(binding);
                    }
                }
            }

            _joinAction.performed += OnJoinAction;
            _joinAction.Enable();
            IsListeningForPlayers = true;
            CLogger.LogInfo($"{DEBUG_FLAG} Listening for players... Device Locking: {_isDeviceLockingOnJoinEnabled}");
        }

        public void StopListeningForPlayers()
        {
            if (_joinAction != null)
            {
                _joinAction.performed -= OnJoinAction;
                _joinAction.Dispose();
                _joinAction = null;
            }
            IsListeningForPlayers = false;
        }

        /// <summary>
        /// Joins a player with all currently available required devices. Treats Keyboard and Mouse as a unit.
        /// If the player is already joined, returns the existing service without triggering OnPlayerInputReady event.
        /// </summary>
        public IInputPlayer JoinSinglePlayer(int playerIdToJoin = 0)
        {
            if (!ValidateMainThread(nameof(JoinSinglePlayer))) return null;

            if (_registerPlayers.TryGetValue(playerIdToJoin, out var existingService))
            {
                CLogger.LogInfo($"{DEBUG_FLAG} Player {playerIdToJoin} already joined. Returning existing service.");
                return existingService;
            }

            var playerConfig = GetPlayerConfig(playerIdToJoin, checkIfAlreadyJoined: false);
            if (playerConfig == null) return null;

            var requiredDeviceLayouts = GetRequiredLayoutsForConfig(playerConfig);
            if (requiredDeviceLayouts.Count == 0) return null;

            var devicesToPair = new List<InputDevice>();

            foreach (string layout in requiredDeviceLayouts)
            {
                InputDevice device = FindAvailableDeviceByLayout(layout);
                if (device != null)
                {
                    devicesToPair.Add(device);
                }
            }

            bool hasKeyboard = ContainsKeyboard(devicesToPair);
            bool hasMouse = ContainsMouse(devicesToPair);

            if (hasKeyboard && !hasMouse)
            {
                InputDevice mouse = FindAvailableDeviceByLayout("Mouse");
                if (mouse != null) devicesToPair.Add(mouse);
            }
            else if (hasMouse && !hasKeyboard)
            {
                InputDevice keyboard = FindAvailableDeviceByLayout("Keyboard");
                if (keyboard != null) devicesToPair.Add(keyboard);
            }

            if (devicesToPair.Count == 0)
            {
                CLogger.LogError($"{DEBUG_FLAG} Failed to find required devices for Player {playerIdToJoin}.");
                return null;
            }

            return JoinPlayerWithDevices(playerIdToJoin, playerConfig, devicesToPair);
        }

        public async UniTask<IInputPlayer> JoinSinglePlayerAsync(int playerIdToJoin = 0, int timeoutInSeconds = 5)
        {
            await SwitchToUnityMainThreadAsync();

            var playerConfig = GetPlayerConfig(playerIdToJoin);
            if (playerConfig == null) return null;

            var requiredDeviceLayouts = GetRequiredLayoutsForConfig(playerConfig);
            if (requiredDeviceLayouts.Count == 0) return null;

            var devicesToPair = new List<InputDevice>();
            var layoutsToWaitFor = new HashSet<string>();

            foreach (string layout in requiredDeviceLayouts)
            {
                InputDevice device = FindAvailableDeviceByLayout(layout);
                if (device != null) devicesToPair.Add(device);
                else layoutsToWaitFor.Add(layout);
            }

            if (layoutsToWaitFor.Count > 0)
            {
                CLogger.LogWarning($"{DEBUG_FLAG} Waiting for required devices to connect: {string.Join(", ", layoutsToWaitFor)}");
                var tcs = new UniTaskCompletionSource<bool>();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutInSeconds));
                cts.Token.Register(() => tcs.TrySetResult(false));

                Action<InputDevice, InputDeviceChange> deviceChangeHandler = (device, change) =>
                {
                    if (change == InputDeviceChange.Added && layoutsToWaitFor.Contains(device.layout))
                    {
                        layoutsToWaitFor.Remove(device.layout);
                        devicesToPair.Add(device);
                        if (layoutsToWaitFor.Count == 0) tcs.TrySetResult(true);
                    }
                };

                UnityEngine.InputSystem.InputSystem.onDeviceChange += deviceChangeHandler;
                bool success;
                try
                {
                    success = await tcs.Task;
                }
                finally
                {
                    UnityEngine.InputSystem.InputSystem.onDeviceChange -= deviceChangeHandler;
                }

                if (!success)
                {
                    CLogger.LogError($"{DEBUG_FLAG} Timed out waiting for devices: {string.Join(", ", layoutsToWaitFor)}. Aborting join.");
                    return null;
                }
            }

            if (devicesToPair.Count == 0)
            {
                CLogger.LogError($"{DEBUG_FLAG} Failed to find ANY required devices for Player {playerIdToJoin}.");
                return null;
            }

            return JoinPlayerWithDevices(playerIdToJoin, playerConfig, devicesToPair);
        }

        public IInputPlayer JoinPlayerOnSharedDevice(int playerIdToJoin)
        {
            if (!ValidateMainThread(nameof(JoinPlayerOnSharedDevice))) return null;

            var playerConfig = GetPlayerConfig(playerIdToJoin);
            if (playerConfig == null) return null;

            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                CLogger.LogError($"{DEBUG_FLAG} Cannot JoinPlayerOnSharedDevice: no keyboard connected.");
                return null;
            }

            var user = InputUser.PerformPairingWithDevice(keyboard);
            if (Mouse.current != null)
            {
                InputUser.PerformPairingWithDevice(Mouse.current, user);
            }

            return CreatePlayerService(playerIdToJoin, user, playerConfig, keyboard);
        }

        public async UniTask<IInputPlayer> JoinPlayerOnSharedDeviceAsync(int playerIdToJoin)
        {
            await SwitchToUnityMainThreadAsync();
            return JoinPlayerOnSharedDevice(playerIdToJoin);
        }

        public IInputPlayer JoinPlayerAndLockDevice(int playerIdToJoin, InputDevice deviceToLock)
        {
            if (!ValidateMainThread(nameof(JoinPlayerAndLockDevice))) return null;

            if (!_isInitialized)
            {
                CLogger.LogError($"{DEBUG_FLAG} Cannot join player, manager is not initialized.");
                return null;
            }
            if (deviceToLock == null)
            {
                CLogger.LogError($"{DEBUG_FLAG} Cannot join player {playerIdToJoin}: device is null.");
                return null;
            }

            var playerConfig = GetPlayerConfig(playerIdToJoin, false);
            if (playerConfig == null) return null;

            if (_registerPlayers.ContainsKey(playerIdToJoin))
            {
                CLogger.LogWarning($"{DEBUG_FLAG} Player {playerIdToJoin} already joined.");
                return _registerPlayers[playerIdToJoin];
            }

            var allUsers = InputUser.all;
            int userCount = allUsers.Count;
            for (int i = 0; i < userCount; i++)
            {
                if (IsDevicePairedToUser(allUsers[i], deviceToLock))
                {
                    CLogger.LogWarning($"{DEBUG_FLAG} Device '{deviceToLock.displayName}' already in use.");
                    return null;
                }
            }

            var user = InputUser.PerformPairingWithDevice(deviceToLock);
            return CreatePlayerService(playerIdToJoin, user, playerConfig, deviceToLock);
        }

        public async UniTask<IInputPlayer> JoinPlayerAndLockDeviceAsync(int playerIdToJoin, InputDevice deviceToLock)
        {
            await SwitchToUnityMainThreadAsync();
            return JoinPlayerAndLockDevice(playerIdToJoin, deviceToLock);
        }

        private void OnJoinAction(InputAction.CallbackContext context)
        {
            var joiningDevice = context.control.device;

            var allUsers = InputUser.all;
            int userCount = allUsers.Count;
            for (int i = 0; i < userCount; i++)
            {
                if (IsDevicePairedToUser(allUsers[i], joiningDevice))
                {
                    CLogger.LogWarning($"{DEBUG_FLAG} Device '{joiningDevice.displayName}' already paired.");
                    return;
                }
            }

            if (_isDeviceLockingOnJoinEnabled)
            {
                if (_registerPlayers.TryGetValue(0, out var existingService))
                {
                    if (existingService is InputPlayer inputPlayer)
                    {
                        InputUser.PerformPairingWithDevice(joiningDevice, inputPlayer.User);
                        CLogger.LogInfo($"{DEBUG_FLAG} Paired device '{joiningDevice.displayName}' to Player 0.");
                    }
                }
                else
                {
                    var playerConfig = GetPlayerConfig(0);
                    if (playerConfig == null) return;

                    var user = InputUser.PerformPairingWithDevice(joiningDevice);
                    if (joiningDevice is Keyboard && Mouse.current != null) InputUser.PerformPairingWithDevice(Mouse.current, user);
                    else if (joiningDevice is Mouse && Keyboard.current != null) InputUser.PerformPairingWithDevice(Keyboard.current, user);

                    CreatePlayerService(0, user, playerConfig, joiningDevice);
                }
            }
            else
            {
                int playerIdToJoin = -1;
                for (int i = 0; i < _configuration.PlayerSlots.Count; i++)
                {
                    if (!_registerPlayers.ContainsKey(i))
                    {
                        var slotConfig = _configuration.PlayerSlots[i];
                        if (slotConfig.JoinAction != null &&
                            MatchesAnyJoinBinding(slotConfig.JoinAction.DeviceBindings, joiningDevice))
                        {
                            playerIdToJoin = i;
                            break;
                        }
                    }
                }

                if (playerIdToJoin == -1)
                {
                    for (int i = 0; i < _configuration.PlayerSlots.Count; i++)
                    {
                        if (!_registerPlayers.ContainsKey(i))
                        {
                            playerIdToJoin = i;
                            break;
                        }
                    }
                }

                if (playerIdToJoin == -1)
                {
                    CLogger.LogWarning($"{DEBUG_FLAG} No available player slots to join.");
                    return;
                }

                var playerConfig = GetPlayerConfig(playerIdToJoin);
                if (playerConfig == null) return;

                var user = InputUser.PerformPairingWithDevice(joiningDevice);
                if (joiningDevice is Keyboard && Mouse.current != null) InputUser.PerformPairingWithDevice(Mouse.current, user);
                else if (joiningDevice is Mouse && Keyboard.current != null) InputUser.PerformPairingWithDevice(Keyboard.current, user);

                CreatePlayerService(playerIdToJoin, user, playerConfig, joiningDevice);
            }
        }

        private PlayerSlotConfig GetPlayerConfig(int playerId, bool checkIfAlreadyJoined = true)
        {
            if (!_isInitialized) return null;
            if (checkIfAlreadyJoined && _registerPlayers.ContainsKey(playerId)) return null;
            var playerSlots = _configuration.PlayerSlots;
            int playerSlotCount = playerSlots.Count;
            for (int i = 0; i < playerSlotCount; i++)
            {
                var playerSlot = playerSlots[i];
                if (playerSlot.PlayerId == playerId)
                {
                    return playerSlot;
                }
            }

            return null;
        }

        /// <summary>
        /// Batch pairs all devices to a player in a single operation.
        /// </summary>
        private IInputPlayer JoinPlayerWithDevices(int playerId, PlayerSlotConfig config, List<InputDevice> devices)
        {
            if (devices == null || devices.Count == 0)
            {
                CLogger.LogError($"{DEBUG_FLAG} No devices for Player {playerId}.");
                return null;
            }

            using (InputPerformanceProfiler.BeginScope("JoinPlayerWithDevices"))
            {
                var user = InputUser.PerformPairingWithDevice(devices[0]);
                for (int i = 1; i < devices.Count; i++)
                {
                    InputUser.PerformPairingWithDevice(devices[i], user);
                }

                return CreatePlayerService(playerId, user, config, devices[0]);
            }
        }

        private IInputPlayer CreatePlayerService(int playerId, InputUser user, PlayerSlotConfig config, InputDevice initialDevice = null)
        {
            using (InputPerformanceProfiler.BeginScope("CreatePlayerService"))
            {
                var inputPlayer = new InputPlayer(playerId, user, config, initialDevice);
                _registerPlayers[playerId] = inputPlayer;
                string devices = FormatPairedDeviceNames(user);
                CLogger.LogInfo($"{DEBUG_FLAG} Player {playerId} created with devices: [{devices}].");
                
                if (playerId == 0)
                {
                    SetupCursorControl(inputPlayer);
                }

                OnPlayerInputReady?.Invoke(inputPlayer);
                return inputPlayer;
            }
        }

        private void SetupCursorControl(IInputPlayer player)
        {
            _cursorSubscription?.Dispose();
            _cursorSubscription = player.ActiveDeviceKind.Subscribe(OnDeviceChanged);
            UpdateCursorState(player.ActiveDeviceKind.CurrentValue);
        }

        private void OnDeviceChanged(InputDeviceKind deviceKind)
        {
            if (ManageCursorVisibility)
            {
                UpdateCursorState(deviceKind);
            }
        }

        private void UpdateCursorState(InputDeviceKind kind)
        {
            bool showCursor = kind == InputDeviceKind.KeyboardMouse;
            
            Cursor.visible = showCursor;
            
            if (showCursor)
            {
                Cursor.lockState = CursorLockMode.None;
                
                if (ResetCursorToCenter)
                {
                    WarpCursorToCenter();
                }
            }
        }

        private void WarpCursorToCenter()
        {
#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Mouse.current != null)
            {
                var center = new UnityEngine.Vector2(UnityEngine.Screen.width / 2f, UnityEngine.Screen.height / 2f);
                UnityEngine.InputSystem.Mouse.current.WarpCursorPosition(center);
            }
#endif
        }

        public static UnityEngine.Vector2 GetMousePosition()
        {
            UnityEngine.Vector2 mousePos = UnityEngine.Vector2.zero;
#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Mouse.current != null)
                mousePos = UnityEngine.InputSystem.Mouse.current.position.ReadValue();
            else
                mousePos = UnityEngine.Input.mousePosition;
#else
            mousePos = UnityEngine.Input.mousePosition;
#endif
            return mousePos;
        }

        private HashSet<string> GetRequiredLayoutsForConfig(PlayerSlotConfig config)
        {
            var layouts = new HashSet<string>();
            foreach (var context in config.Contexts)
                foreach (var binding in context.Bindings)
                    foreach (var deviceBinding in binding.DeviceBindings)
                    {
                        int startIndex = deviceBinding.IndexOf('<');
                        if (startIndex != -1)
                        {
                            int endIndex = deviceBinding.IndexOf('>');
                            if (endIndex > startIndex) layouts.Add(deviceBinding.Substring(startIndex + 1, endIndex - startIndex - 1));
                        }
                    }
            return layouts;
        }

        /// <summary>
        /// Finds an available device matching the layout. Uses cached collections to minimize allocations.
        /// </summary>
        private InputDevice FindAvailableDeviceByLayout(string layoutName)
        {
            var devices = UnityEngine.InputSystem.InputSystem.devices;
            int deviceCount = devices.Count;
            var allUsers = InputUser.all;
            int userCount = allUsers.Count;

            for (int i = 0; i < deviceCount; i++)
            {
                var device = devices[i];
                if (UnityEngine.InputSystem.InputSystem.IsFirstLayoutBasedOnSecond(device.layout, layoutName))
                {
                    bool isPaired = false;
                    for (int j = 0; j < userCount; j++)
                    {
                        if (IsDevicePairedToUser(allUsers[j], device))
                        {
                            isPaired = true;
                            break;
                        }
                    }
                    if (!isPaired) return device;
                }
            }
            return null;
        }

        private static bool ContainsKeyboard(List<InputDevice> devices)
        {
            int count = devices.Count;
            for (int i = 0; i < count; i++)
            {
                if (devices[i] is Keyboard)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsDevicePairedToUser(InputUser user, InputDevice device)
        {
            var pairedDevices = user.pairedDevices;
            int deviceCount = pairedDevices.Count;
            for (int i = 0; i < deviceCount; i++)
            {
                if (ReferenceEquals(pairedDevices[i], device))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsMouse(List<InputDevice> devices)
        {
            int count = devices.Count;
            for (int i = 0; i < count; i++)
            {
                if (devices[i] is Mouse)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool MatchesAnyJoinBinding(List<string> bindings, InputDevice joiningDevice)
        {
            int count = bindings.Count;
            for (int i = 0; i < count; i++)
            {
                string binding = bindings[i];
                if (binding.Contains(joiningDevice.layout) ||
                    (joiningDevice is Keyboard && binding.Contains("Keyboard")) ||
                    (joiningDevice is Mouse && binding.Contains("Mouse")))
                {
                    return true;
                }
            }

            return false;
        }

        private static string FormatPairedDeviceNames(InputUser user)
        {
            var pairedDevices = user.pairedDevices;
            int deviceCount = pairedDevices.Count;
            if (deviceCount == 0)
            {
                return "All (Shared)";
            }

            var builder = new StringBuilder(64);
            for (int i = 0; i < deviceCount; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(pairedDevices[i].displayName);
            }

            return builder.ToString();
        }

        /// <summary>
        /// Gets an existing input player for the specified player ID, or null if not joined.
        /// </summary>
        public IInputPlayer GetInputPlayer(int playerId)
        {
            return _registerPlayers.TryGetValue(playerId, out var service) ? service : null;
        }

        /// <summary>
        /// Gets an existing input player or joins it on the Unity main thread.
        /// </summary>
        public async UniTask<IInputPlayer> GetOrJoinInputPlayerAsync(int playerId, int timeoutInSeconds = 5)
        {
            if (_registerPlayers.TryGetValue(playerId, out var service))
            {
                return service;
            }

            return await JoinSinglePlayerAsync(playerId, timeoutInSeconds);
        }

        /// <summary>
        /// Refreshes player input by triggering OnPlayerInputReady event for an already joined player.
        /// Useful when you dynamically bind input contexts after the player has already joined (e.g., in a different scene).
        /// This allows the InputSystem to recognize and manage newly bound input contexts.
        /// Returns true if the player exists and event was triggered, false otherwise.
        /// </summary>
        public bool RefreshPlayerInput(int playerId)
        {
            if (_registerPlayers.TryGetValue(playerId, out var service))
            {
                OnPlayerInputReady?.Invoke(service);
                CLogger.LogInfo($"{DEBUG_FLAG} Refreshed input for Player {playerId}.");
                return true;
            }
            CLogger.LogWarning($"{DEBUG_FLAG} Cannot refresh input for Player {playerId}: player not found.");
            return false;
        }

        /// <summary>
        /// Removes a player from the manager and disposes their input resources.
        /// </summary>
        public bool RemovePlayer(int playerId)
        {
            if (!_registerPlayers.TryGetValue(playerId, out var service))
            {
                return false;
            }

            (service as IDisposable)?.Dispose();
            _registerPlayers.Remove(playerId);
            CLogger.LogInfo($"{DEBUG_FLAG} Player {playerId} removed.");
            return true;
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            _cursorSubscription?.Dispose();
            _cursorSubscription = null;
            StopListeningForPlayers();
            foreach (var service in _registerPlayers.Values)
            {
                (service as IDisposable)?.Dispose();
            }
            _registerPlayers.Clear();
            _isInitialized = false;
        }

        /// <summary>
        /// Overrides a specific device binding for an action on the given player.
        /// Returns true if the old binding was found and overridden.
        /// </summary>
        public bool RebindAction(int playerId, string actionMapName, string actionName, string oldBinding, string newBinding)
        {
            if (!_registerPlayers.TryGetValue(playerId, out var service)) return false;
            return service.RebindAction(actionMapName, actionName, oldBinding, newBinding);
        }

        /// <summary>
        /// Resets a specific action's bindings to their original configuration for the given player.
        /// </summary>
        public bool ResetActionBinding(int playerId, string actionMapName, string actionName)
        {
            if (!_registerPlayers.TryGetValue(playerId, out var service)) return false;
            return service.ResetActionBinding(actionMapName, actionName);
        }

        /// <summary>
        /// Resets all actions' bindings for the given player to their original configuration.
        /// </summary>
        public void ResetAllActionBindings(int playerId)
        {
            if (_registerPlayers.TryGetValue(playerId, out var service))
            {
                service.ResetAllActionBindings();
            }
        }

        /// <summary>
        /// Returns the current effective binding paths for an action on the given player.
        /// </summary>
        public string[] GetActionBindings(int playerId, string actionMapName, string actionName)
        {
            if (!_registerPlayers.TryGetValue(playerId, out var service)) return System.Array.Empty<string>();
            return service.GetActionBindings(actionMapName, actionName);
        }

        /// <summary>
        /// Returns a chord observable for two button actions on the given player.
        /// </summary>
        public Observable<Unit> GetChordObservable(int playerId, string actionMapName, string actionName1, string actionName2, float windowMs = 300f)
        {
            if (!_registerPlayers.TryGetValue(playerId, out var service)) return EmptyObservables.Unit;
            return service.GetChordObservable(actionMapName, actionName1, actionName2, windowMs);
        }

        public List<BindingConflict> CheckBindingConflicts(int playerId)
        {
            var config = GetPlayerConfig(playerId, false);
            return InputBindingValidator.DetectConflicts(config);
        }

        public List<BindingConflict> CheckBindingConflicts(int playerId, string contextName)
        {
            var config = GetPlayerConfig(playerId, false);
            return InputBindingValidator.DetectConflicts(config, contextName);
        }

        public static string FormatConflictsReport(List<BindingConflict> conflicts)
        {
            return InputBindingValidator.FormatConflictsReport(conflicts);
        }
    }
}
