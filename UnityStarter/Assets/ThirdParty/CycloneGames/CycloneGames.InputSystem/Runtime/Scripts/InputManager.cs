using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Users;
using VYaml.Serialization;

namespace CycloneGames.InputSystem.Runtime
{
    /// <summary>
    /// The definitive central singleton for managing all player input.
    /// Supports dynamic and programmatic joining with optional and intelligent device locking.
    /// </summary>
    public sealed class InputManager : IDisposable
    {
        public static InputManager Instance { get; } = new InputManager();
        private InputManager() { }

        public event Action<IInputService> OnPlayerJoined;

        private readonly Dictionary<int, IInputService> _playerServices = new();
        private InputConfiguration _configuration;
        private InputAction _joinAction;
        private string _userConfigUri;
        private bool _isInitialized = false;
        private bool _isDeviceLockingOnJoinEnabled = false;

        public void Initialize(string yamlContent, string userConfigUri)
        {
            if (_isInitialized) return;
            if (string.IsNullOrEmpty(yamlContent)) return;

            try
            {
                _configuration = YamlSerializer.Deserialize<InputConfiguration>(System.Text.Encoding.UTF8.GetBytes(yamlContent));
                _userConfigUri = userConfigUri;
                _isInitialized = true;
                Debug.Log("[InputManager] Initialized successfully.");
            }
            catch (Exception e) { Debug.LogError($"[InputManager] Failed to parse YAML: {e.Message}"); }
        }

        public async UniTask SaveUserConfigurationAsync()
        {
            if (!_isInitialized || string.IsNullOrEmpty(_userConfigUri)) return;

            try
            {
                byte[] yamlBytes = YamlSerializer.Serialize(_configuration).ToArray();
                string filePath = new Uri(_userConfigUri).LocalPath;
                await UniTask.RunOnThreadPool(() => File.WriteAllBytes(filePath, yamlBytes));
                Debug.Log($"[InputManager] User configuration saved to: {filePath}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[InputManager] Failed to save user configuration: {e.Message}");
            }
        }

        public void StartListeningForPlayers(bool lockDeviceOnJoin)
        {
            if (!_isInitialized) return;
            _isDeviceLockingOnJoinEnabled = lockDeviceOnJoin;
            if (_joinAction != null) _joinAction.Dispose();

            var joinActionConfig = _configuration.JoinAction;
            _joinAction = new InputAction(name: joinActionConfig.ActionName, type: InputActionType.Button);
            foreach (var binding in joinActionConfig.DeviceBindings) _joinAction.AddBinding(binding);

            _joinAction.performed += OnJoinAction;
            _joinAction.Enable();
            Debug.Log($"[InputManager] Listening for players... Device Locking: {_isDeviceLockingOnJoinEnabled}");
        }

        public void StopListeningForPlayers()
        {
            if (_joinAction != null)
            {
                _joinAction.performed -= OnJoinAction;
                _joinAction.Dispose();
                _joinAction = null;
            }
        }

        /// <summary>
        /// Programmatically joins a player for a locked single-player experience.
        /// It intelligently pairs all *currently available* devices required by the configuration,
        /// ensuring that Keyboard and Mouse are treated as a single unit.
        /// </summary>
        public IInputService JoinSinglePlayer(int playerIdToJoin = 0)
        {
            var playerConfig = GetPlayerConfig(playerIdToJoin);
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

            bool hasKeyboard = devicesToPair.Any(d => d is Keyboard);
            bool hasMouse = devicesToPair.Any(d => d is Mouse);

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
                Debug.LogError($"[InputManager] Failed to find ANY required devices (e.g., Keyboard, Mouse) for Player {playerIdToJoin}. Aborting join.");
                return null;
            }

            var user = InputUser.PerformPairingWithDevice(devicesToPair[0]);
            for (int i = 1; i < devicesToPair.Count; i++)
            {
                InputUser.PerformPairingWithDevice(devicesToPair[i], user);
            }

            return CreatePlayerService(playerIdToJoin, user, playerConfig);
        }

        public async UniTask<IInputService> JoinSinglePlayerAsync(int playerIdToJoin = 0, int timeoutInSeconds = 5)
        {
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
                Debug.LogWarning($"[InputManager] Waiting for required devices to connect: {string.Join(", ", layoutsToWaitFor)}");
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
                bool success = await tcs.Task;
                UnityEngine.InputSystem.InputSystem.onDeviceChange -= deviceChangeHandler;

                if (!success)
                {
                    Debug.LogError($"[InputManager] Timed out waiting for devices: {string.Join(", ", layoutsToWaitFor)}. Aborting join.");
                    return null;
                }
            }

            if (devicesToPair.Count == 0)
            {
                Debug.LogError($"[InputManager] Failed to find ANY required devices for Player {playerIdToJoin}.");
                return null;
            }

            var user = InputUser.PerformPairingWithDevice(devicesToPair[0]);
            for (int i = 1; i < devicesToPair.Count; i++) InputUser.PerformPairingWithDevice(devicesToPair[i], user);

            return CreatePlayerService(playerIdToJoin, user, playerConfig);
        }

        public IInputService JoinPlayerOnSharedDevice(int playerIdToJoin)
        {
            var playerConfig = GetPlayerConfig(playerIdToJoin);
            if (playerConfig == null) return null;
            var user = InputUser.CreateUserWithoutPairedDevices();
            return CreatePlayerService(playerIdToJoin, user, playerConfig);
        }

        public IInputService JoinPlayerAndLockDevice(int playerIdToJoin, InputDevice deviceToLock)
        {
            if (!_isInitialized)
            {
                Debug.LogError("[InputManager] Cannot join player, manager is not initialized.");
                return null;
            }
            if (deviceToLock == null)
            {
                Debug.LogError($"[InputManager] Cannot join player {playerIdToJoin} because the provided device is null.");
                return null;
            }

            var playerConfig = GetPlayerConfig(playerIdToJoin, false);
            if (playerConfig == null) return null;

            if (_playerServices.ContainsKey(playerIdToJoin))
            {
                Debug.LogWarning($"[InputManager] Player {playerIdToJoin} has already joined.");
                return _playerServices[playerIdToJoin];
            }
            if (InputUser.all.Any(user => user.pairedDevices.Contains(deviceToLock)))
            {
                Debug.LogWarning($"[InputManager] Cannot join Player {playerIdToJoin}. The device '{deviceToLock.displayName}' is already in use.");
                return null;
            }

            var user = InputUser.PerformPairingWithDevice(deviceToLock);
            return CreatePlayerService(playerIdToJoin, user, playerConfig);
        }

        private void OnJoinAction(InputAction.CallbackContext context)
        {
            var joiningDevice = context.control.device;
            if (_isDeviceLockingOnJoinEnabled && InputUser.all.Any(user => user.pairedDevices.Contains(joiningDevice))) return;

            int nextPlayerId = _playerServices.Count;
            var playerConfig = GetPlayerConfig(nextPlayerId);
            if (playerConfig == null) return;

            InputUser user;
            if (_isDeviceLockingOnJoinEnabled)
            {
                user = InputUser.PerformPairingWithDevice(joiningDevice);
                if (joiningDevice is Keyboard && Mouse.current != null && !InputUser.all.Any(u => u.pairedDevices.Contains(Mouse.current)))
                {
                    InputUser.PerformPairingWithDevice(Mouse.current, user);
                }
                else if (joiningDevice is Mouse && Keyboard.current != null && !InputUser.all.Any(u => u.pairedDevices.Contains(Keyboard.current)))
                {
                    InputUser.PerformPairingWithDevice(Keyboard.current, user);
                }
            }
            else
            {
                user = InputUser.CreateUserWithoutPairedDevices();
            }
            CreatePlayerService(nextPlayerId, user, playerConfig);
        }

        private PlayerSlotConfig GetPlayerConfig(int playerId, bool checkIfAlreadyJoined = true)
        {
            if (!_isInitialized) return null;
            if (checkIfAlreadyJoined && _playerServices.ContainsKey(playerId)) return null;
            var playerConfig = _configuration.PlayerSlots.FirstOrDefault(p => p.PlayerId == playerId);
            if (playerConfig == null) Debug.LogError($"[InputManager] No configuration found for Player ID {playerId}.");
            return playerConfig;
        }

        private IInputService CreatePlayerService(int playerId, InputUser user, PlayerSlotConfig config)
        {
            var inputService = new InputService(playerId, user, config);
            _playerServices[playerId] = inputService;
            string devices = user.pairedDevices.Count > 0 ? string.Join(", ", user.pairedDevices.Select(d => d.displayName)) : "All (Shared)";
            Debug.Log($"[InputManager] Player {playerId} created with devices: [{devices}].");
            OnPlayerJoined?.Invoke(inputService);
            return inputService;
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

        private InputDevice FindAvailableDeviceByLayout(string layoutName)
        {
            foreach (var device in UnityEngine.InputSystem.InputSystem.devices)
            {
                if (UnityEngine.InputSystem.InputSystem.IsFirstLayoutBasedOnSecond(device.layout, layoutName))
                {
                    bool isPaired = false;
                    foreach (var user in InputUser.all)
                    {
                        if (user.pairedDevices.Contains(device))
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

        public void Dispose()
        {
            StopListeningForPlayers();
            foreach (var service in _playerServices.Values)
            {
                (service as IDisposable)?.Dispose();
            }
            _playerServices.Clear();
            _isInitialized = false;
        }
    }
}