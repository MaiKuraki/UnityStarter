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
    /// It listens for input on un-paired devices and creates player services on demand.
    /// </summary>
    public sealed class InputManager : IDisposable
    {
        public static InputManager Instance { get; } = new InputManager();
        private InputManager() { }

        public event Action<IInputService> OnPlayerJoined;

        private readonly Dictionary<int, IInputService> _playerServices = new();
        private InputConfiguration _configuration;
        private InputAction _joinAction;
        private string _userConfigUri; // Path to the writable user config file.
        private bool _isInitialized = false;
        private bool _isListening = false;

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

        /// <summary>
        /// Asynchronously saves the current input configuration to the user's config file path.
        /// This should be called after settings are changed in-game.
        /// </summary>
        public async Task SaveUserConfigurationAsync()
        {
            if (!_isInitialized || string.IsNullOrEmpty(_userConfigUri))
            {
                Debug.LogWarning("[InputManager] Cannot save configuration, not initialized or user path is not set.");
                return;
            }

            try
            {
                // Serialize the current configuration object back into a YAML string.
                byte[] yamlBytes = YamlSerializer.Serialize(_configuration).ToArray();
                string yamlContent = System.Text.Encoding.UTF8.GetString(yamlBytes);

                // Asynchronously write the content to the file.
                // Note: File I/O might not be suitable for all platforms (e.g., WebGL).
                // This assumes a platform with a writable file system.
                // The URI must be a local file path for this to work.
                string filePath = new Uri(_userConfigUri).LocalPath;
                await File.WriteAllTextAsync(filePath, yamlContent);

                Debug.Log($"[InputManager] User configuration saved to: {filePath}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[InputManager] Failed to save user configuration: {e.Message}");
            }
        }

        /// <summary>
        /// Starts listening for new players attempting to join.
        /// </summary>
        public void StartListeningForPlayers()
        {
            if (!_isInitialized || _isListening) return;

            // Build the special "join" action from the configuration.
            var joinActionConfig = _configuration.JoinAction;
            _joinAction = new InputAction(name: joinActionConfig.ActionName, type: InputActionType.Button);
            foreach (var binding in joinActionConfig.DeviceBindings)
            {
                _joinAction.AddBinding(binding);
            }

            // Subscribe to the action's 'performed' event. This is the core of the join logic.
            _joinAction.performed += OnJoinAction;
            _joinAction.Enable();
            _isListening = true;
            Debug.Log("[InputManager] Listening for new players to join...");
        }

        /// <summary>
        /// Stops listening for new players.
        /// </summary>
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

        private void OnJoinAction(InputAction.CallbackContext context)
        {
            // Check if the device that triggered the action is already in use.
            if (InputUser.all.Any(user => user.pairedDevices.Contains(context.control.device)))
            {
                return; // Device is already paired, so ignore this join attempt.
            }

            // Find the next available player ID and its corresponding configuration.
            int nextPlayerId = _playerServices.Count;
            var playerConfig = _configuration.PlayerSlots.FirstOrDefault(p => p.PlayerId == nextPlayerId);
            if (playerConfig == null)
            {
                Debug.LogWarning("[InputManager] Max players reached. No available slot.");
                // Optionally stop listening if all slots are filled.
                if (_playerServices.Count >= _configuration.PlayerSlots.Count)
                {
                    StopListeningForPlayers();
                }
                return;
            }

            // Pair the device to a new user and create the player's dedicated service.
            var user = InputUser.PerformPairingWithDevice(context.control.device);
            var inputService = new InputService(nextPlayerId, user, playerConfig);
            _playerServices[nextPlayerId] = inputService;

            Debug.Log($"[InputManager] Player {nextPlayerId} joined with device '{context.control.device.displayName}'.");

            // Fire the event to notify the rest of the game.
            OnPlayerJoined?.Invoke(inputService);
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
                (service as IDisposable)?.Dispose(); // This also unpairs the device via InputService.Dispose()
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