using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Users;
using VYaml.Serialization;

namespace CycloneGames.InputSystem.Runtime
{
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

        public async Task SaveUserConfigurationAsync()
        {
            if (!_isInitialized || string.IsNullOrEmpty(_userConfigUri))
            {
                Debug.LogWarning("[InputManager] Cannot save configuration, not initialized or user path is not set.");
                return;
            }

            try
            {
                byte[] yamlBytes = YamlSerializer.Serialize(_configuration).ToArray();
                string yamlContent = System.Text.Encoding.UTF8.GetString(yamlBytes);
                string filePath = new Uri(_userConfigUri).LocalPath;
                await File.WriteAllTextAsync(filePath, yamlContent);
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
            foreach (var binding in joinActionConfig.DeviceBindings)
            {
                _joinAction.AddBinding(binding);
            }

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

        public IInputService JoinSinglePlayer(int playerIdToJoin = 0)
        {
            var playerConfig = GetPlayerConfig(playerIdToJoin);
            if (playerConfig == null) return null;

            var requiredDeviceLayouts = GetRequiredLayoutsForConfig(playerConfig);
            if (requiredDeviceLayouts.Count == 0)
            {
                Debug.LogWarning($"[InputManager] No device bindings found for Player {playerIdToJoin}, cannot determine which devices to pair.");
                return null;
            }

            var user = InputUser.CreateUserWithoutPairedDevices(); // Create a valid user first.
            bool atLeastOneDevicePaired = false;

            foreach (string layout in requiredDeviceLayouts)
            {
                InputDevice deviceToPair = FindAvailableDeviceByLayout(layout);
                if (deviceToPair != null)
                {
                    user = InputUser.PerformPairingWithDevice(deviceToPair, user);
                    atLeastOneDevicePaired = true;
                }
                else
                {
                    Debug.LogWarning($"[InputManager] Could not find an available device with layout '{layout}' for Player {playerIdToJoin}. This device will be ignored.");
                }
            }

            if (!atLeastOneDevicePaired)
            {
                Debug.LogError($"[InputManager] Failed to pair any devices for Player {playerIdToJoin}. Aborting join.");
                user.UnpairDevicesAndRemoveUser();
                return null;
            }

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
            if (_playerServices.ContainsKey(playerIdToJoin))
            {
                Debug.LogWarning($"[InputManager] Player {playerIdToJoin} has already joined.");
                return _playerServices[playerIdToJoin];
            }
            if (deviceToLock == null)
            {
                Debug.LogError($"[InputManager] Cannot join player {playerIdToJoin} because the provided device is null.");
                return null;
            }

            if (InputUser.all.Any(user => user.pairedDevices.Contains(deviceToLock)))
            {
                Debug.LogWarning($"[InputManager] Cannot join Player {playerIdToJoin}. The device '{deviceToLock.displayName}' is already in use.");
                return null;
            }

            var playerConfig = GetPlayerConfig(playerIdToJoin, false); // Get config without checking if joined
            if (playerConfig == null) return null;

            var user = InputUser.PerformPairingWithDevice(deviceToLock);
            return CreatePlayerService(playerIdToJoin, user, playerConfig);
        }

        private void OnJoinAction(InputAction.CallbackContext context)
        {
            var joiningDevice = context.control.device;

            if (_isDeviceLockingOnJoinEnabled && InputUser.all.Any(user => user.pairedDevices.Contains(joiningDevice)))
            {
                return;
            }

            int nextPlayerId = _playerServices.Count;
            var playerConfig = GetPlayerConfig(nextPlayerId);
            if (playerConfig == null)
            {
                Debug.LogWarning("[InputManager] Max players reached.");
                return;
            }

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
            if (!_isInitialized)
            {
                Debug.LogError("[InputManager] Cannot get player config, manager is not initialized.");
                return null;
            }
            if (checkIfAlreadyJoined && _playerServices.ContainsKey(playerId))
            {
                Debug.LogWarning($"[InputManager] Player {playerId} has already joined.");
                return null;
            }

            var playerConfig = _configuration.PlayerSlots.FirstOrDefault(p => p.PlayerId == playerId);
            if (playerConfig == null)
            {
                Debug.LogError($"[InputManager] No configuration found for Player ID {playerId}.");
            }
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
            var requiredDeviceLayouts = new HashSet<string>();
            foreach (var context in config.Contexts)
            {
                foreach (var binding in context.Bindings)
                {
                    foreach (var deviceBinding in binding.DeviceBindings)
                    {
                        int startIndex = deviceBinding.IndexOf('<');
                        if (startIndex != -1)
                        {
                            int endIndex = deviceBinding.IndexOf('>');
                            if (endIndex > startIndex)
                            {
                                string layout = deviceBinding.Substring(startIndex + 1, endIndex - startIndex - 1);
                                requiredDeviceLayouts.Add(layout);
                            }
                        }
                    }
                }
            }
            return requiredDeviceLayouts;
        }

        private InputDevice FindAvailableDeviceByLayout(string layoutName)
        {
            foreach (var device in UnityEngine.InputSystem.InputSystem.devices)
            {
                if (device.layout.Equals(layoutName, StringComparison.OrdinalIgnoreCase))
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