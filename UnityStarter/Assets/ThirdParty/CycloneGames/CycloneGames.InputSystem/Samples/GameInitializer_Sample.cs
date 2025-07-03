using CycloneGames.InputSystem.Runtime;
using CycloneGames.Utility.Runtime;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CycloneGames.InputSystem.Sample
{
    public class GameInitializer_Sample : MonoBehaviour
    {
        public enum StartupMode
        {
            /// <summary>
            /// Auto-joins Player 0 with all its required devices (e.g., Keyboard and Mouse) locked.
            /// </summary>
            AutoJoinLockedSinglePlayer,
            /// <summary>
            /// Auto-joins two players on a shared keyboard. Requires different keybindings in YAML.
            /// </summary>
            AutoJoinSharedKeyboard,
            /// <summary>
            /// Listens for any device to press 'Join', locking each device to the joining player.
            /// </summary>
            LobbyWithDeviceLocking,
            /// <summary>
            /// Listens for any device to press 'Join', allowing multiple players to use one device.
            /// </summary>
            LobbyWithSharedDevices,
            /// <summary>
            /// Explicitly locks Keyboard to Player 0 and Mouse to Player 1 for asymmetrical co-op.
            /// </summary>
            AsymmetricalKeyboardMouse
        }

        [Header("Game Mode")]
        [Tooltip("Defines how players join at the start of the game.")]
        [SerializeField] private StartupMode startupMode = StartupMode.AutoJoinLockedSinglePlayer;
        
        [Header("Game Setup")]
        [SerializeField] private GameObject _playerPrefab;
        [SerializeField] private Transform[] _spawnPoints;
        [SerializeField] private Color[] _playerColors;
        
        [Header("Input Configuration")]
        [SerializeField] private string _defaultConfigName = "input_config.yaml";
        [SerializeField] private string _userConfigName = "user_input_settings.yaml";
        
        private static bool isInitialized = false;

        private async void Awake()
        {
            if (isInitialized)
            {
                Destroy(gameObject);
                return;
            }
            isInitialized = true;
            DontDestroyOnLoad(gameObject);

            // --- Initialization ---
            string defaultConfigUri = FilePathUtility.GetUnityWebRequestUri(_defaultConfigName, UnityPathSource.StreamingAssets);
            string userConfigUri = FilePathUtility.GetUnityWebRequestUri(_userConfigName, UnityPathSource.PersistentData);
            await InputSystemLoader.InitializeAsync(defaultConfigUri, userConfigUri);
            
            InputManager.Instance.OnPlayerJoined += HandlePlayerJoined;

            // --- Player Joining Logic ---
            switch (startupMode)
            {
                case StartupMode.AutoJoinLockedSinglePlayer:
                    InputManager.Instance.JoinSinglePlayer(0);
                    break;
                
                case StartupMode.AutoJoinSharedKeyboard:
                    InputManager.Instance.JoinPlayerOnSharedDevice(0);
                    InputManager.Instance.JoinPlayerOnSharedDevice(1);
                    break;

                case StartupMode.LobbyWithDeviceLocking:
                    InputManager.Instance.StartListeningForPlayers(true);
                    break;
                    
                case StartupMode.LobbyWithSharedDevices:
                    InputManager.Instance.StartListeningForPlayers(false);
                    break;
                    
                case StartupMode.AsymmetricalKeyboardMouse:
                    Debug.Log("Startup Mode: Locking Keyboard to P0 and Mouse to P1.");
                    if (Keyboard.current != null)
                    {
                        InputManager.Instance.JoinPlayerAndLockDevice(0, Keyboard.current);
                    }
                    else
                    {
                        Debug.LogError("Asymmetrical Mode failed: No Keyboard found.");
                    }
                    
                    if (Mouse.current != null)
                    {
                        InputManager.Instance.JoinPlayerAndLockDevice(1, Mouse.current);
                    }
                    else
                    {
                         Debug.LogError("Asymmetrical Mode failed: No Mouse found.");
                    }
                    break;
            }
        }

        private void OnDestroy()
        {
            if (InputManager.Instance != null)
            {
                InputManager.Instance.OnPlayerJoined -= HandlePlayerJoined;
                InputManager.Instance.Dispose();
            }
        }

        private void HandlePlayerJoined(IInputService playerInput)
        {
            int playerId = (playerInput as InputService).PlayerId;

            //  Your Game logic here ...
            
            // if (_playerPrefab == null || _spawnPoints.Length <= playerId)
            // {
            //     Debug.LogError($"Cannot spawn Player {playerId}: Prefab or spawn point is missing.");
            //     return;
            // }

            // Transform spawnPoint = _spawnPoints[playerId];
            // GameObject playerInstance = Instantiate(_playerPrefab, spawnPoint.position, spawnPoint.rotation);
            // PlayerController controller = playerInstance.GetComponent<PlayerController>();

            // if (controller)
            // {
            //     controller.Initialize(playerId, _playerColors[playerId]);

            //     var moveCommand = new MoveCommand(controller.OnMove);
            //     var jumpCommand = new ActionCommand(controller.OnJump);

            //     // Assuming your YAML config has a "Gameplay" context defined in the PlayerSlots.
            //     var gameplayContext = new InputContext("Gameplay", "PlayerActions")
            //         .AddBinding(playerInput.GetVector2Observable("Move"), moveCommand)
            //         .AddBinding(playerInput.GetButtonObservable("Jump"), jumpCommand);

            //     playerInput.RegisterContext(gameplayContext);
            //     playerInput.PushContext("Gameplay");
            // }
        }
    }
}