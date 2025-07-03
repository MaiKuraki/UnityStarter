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
    /// <summary>
    /// The central Pure C# singleton that manages the dynamic joining of players.
    /// It can listen for players joining dynamically or join a player programmatically.
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
        private bool _isListening = false;

        // Caching for performance to avoid repeated string operations.
        private static readonly char[] layoutDelimiters = { '<', '>' };

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

        public void StartListeningForPlayers()
        {
            if (!_isInitialized || _isListening) return;

            var joinActionConfig = _configuration.JoinAction;
            _joinAction = new InputAction(name: joinActionConfig.ActionName, type: InputActionType.Button);
            foreach (var binding in joinActionConfig.DeviceBindings)
            {
                _joinAction.AddBinding(binding);
            }

            _joinAction.performed += OnJoinAction;
            _joinAction.Enable();
            _isListening = true;
            Debug.Log("[InputManager] Listening for new players to join...");
        }

        public void StopListeningForPlayers()
        {
            if (!_isListening) return;

            if (_joinAction != null)
            {
                _joinAction.performed -= OnJoinAction;
                _joinAction.Dispose();
                _joinAction = null;
            }
            _isListening = false;
        }

        /// <summary>
        /// Programmatically joins a player for a single-player context.
        /// This method intelligently finds ALL devices required by the player's configuration
        /// (e.g., Keyboard and Mouse) and pairs them together to the new user.
        /// </summary>
        /// <param name="playerIdToJoin">The ID for the player slot to use (typically 0 for single-player).</param>
        /// <returns>The created IInputService for the new player, or null if it fails.</returns>
        public IInputService JoinSinglePlayer(int playerIdToJoin = 0)
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

            var playerConfig = _configuration.PlayerSlots.FirstOrDefault(p => p.PlayerId == playerIdToJoin);
            if (playerConfig == null)
            {
                Debug.LogError($"[InputManager] No configuration found for Player ID {playerIdToJoin}.");
                return null;
            }

            // --- Intelligent Device Discovery ---
            var requiredDeviceLayouts = new HashSet<string>();
            foreach (var context in playerConfig.Contexts)
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

            if (requiredDeviceLayouts.Count == 0)
            {
                Debug.LogWarning($"[InputManager] No device bindings found for Player {playerIdToJoin}, cannot determine which devices to pair.");
                return null;
            }

            // --- Group Device Pairing ---
            var user = new InputUser();
            bool atLeastOneDevicePaired = false;
            foreach (string layout in requiredDeviceLayouts)
            {
                InputDevice deviceToPair = FindAvailableDeviceByLayout(layout);
                if (deviceToPair != null)
                {
                    // Pair the found device to our user.
                    user = InputUser.PerformPairingWithDevice(deviceToPair, user);
                    atLeastOneDevicePaired = true;
                }
                else
                {
                    // This is no longer a fatal error. We just warn the developer.
                    Debug.LogWarning($"[InputManager] Could not find an available device with layout '{layout}' for Player {playerIdToJoin}. This device will be ignored.");
                }
            }

            // If we couldn't pair ANY device, then the join fails.
            if (!atLeastOneDevicePaired)
            {
                Debug.LogError($"[InputManager] Failed to pair any devices for Player {playerIdToJoin}. Aborting join.");
                user.UnpairDevicesAndRemoveUser(); // Safely clean up the invalid user.
                return null;
            }

            // --- Finalize Player Creation ---
            var inputService = new InputService(playerIdToJoin, user, playerConfig);
            _playerServices[playerIdToJoin] = inputService;
            
            Debug.Log($"[InputManager] Programmatically joined Player {playerIdToJoin} with devices: {string.Join(", ", user.pairedDevices.Select(d => d.displayName))}");
            
            OnPlayerJoined?.Invoke(inputService);
            return inputService;
        }

        private void OnJoinAction(InputAction.CallbackContext context)
        {
            var device = context.control.device;
            if (InputUser.all.Any(user => user.pairedDevices.Contains(device))) return;

            int nextPlayerId = _playerServices.Count;
            var playerConfig = _configuration.PlayerSlots.FirstOrDefault(p => p.PlayerId == nextPlayerId);
            if (playerConfig == null)
            {
                Debug.LogWarning("[InputManager] Max players reached.");
                return;
            }

            var user = InputUser.PerformPairingWithDevice(device);
            var inputService = new InputService(nextPlayerId, user, playerConfig);
            _playerServices[nextPlayerId] = inputService;

            Debug.Log($"[InputManager] Player {nextPlayerId} joined with device '{device.displayName}'.");
            OnPlayerJoined?.Invoke(inputService);
        }

        private InputDevice FindAvailableDeviceByLayout(string layoutName)
        {
            // Use a direct foreach loop for performance and to avoid LINQ/delegate allocations.
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

        public IInputService GetInputForPlayer(int playerId)
        {
            _playerServices.TryGetValue(playerId, out var service);
            return service;
        }

        public void LeavePlayer(int playerId)
        {
            if (_playerServices.TryGetValue(playerId, out var service))
            {
                (service as IDisposable)?.Dispose();
                _playerServices.Remove(playerId);
            }
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