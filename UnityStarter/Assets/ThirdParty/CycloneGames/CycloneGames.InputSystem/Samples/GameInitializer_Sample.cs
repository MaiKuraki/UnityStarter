using CycloneGames.InputSystem.Runtime;
using CycloneGames.InputSystem.Runtime.Generated;
using CycloneGames.Utility.Runtime;
using Cysharp.Threading.Tasks;
using R3;
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
        [SerializeField] private StartupMode startupMode = StartupMode.AutoJoinLockedSinglePlayer;

        [Header("Game Setup")]
        [SerializeField] private GameObject _playerPrefab;
        [SerializeField] private Transform[] _spawnPoints;
        [SerializeField] private Color[] _playerColors;

        [Header("Input Configuration")]
        [SerializeField] private string _defaultConfigName = "input_config.yaml";
        [SerializeField] private string _userConfigName = "user_input_settings.yaml";

        private static bool isInitialized = false;

        // Use async UniTaskVoid for a fire-and-forget async Start method.
        private async void Start()
        {
            if (isInitialized)
            {
                Destroy(gameObject);
                return;
            }
            isInitialized = true;
            DontDestroyOnLoad(gameObject);

            string defaultConfigUri = FilePathUtility.GetUnityWebRequestUri(_defaultConfigName, UnityPathSource.StreamingAssets);
            string userConfigUri = FilePathUtility.GetUnityWebRequestUri(_userConfigName, UnityPathSource.PersistentData);
            await InputSystemLoader.InitializeAsync(defaultConfigUri, userConfigUri);

            InputManager.Instance.OnPlayerJoined += HandlePlayerJoined;

            switch (startupMode)
            {
                case StartupMode.AutoJoinLockedSinglePlayer:
                    // Await the new patient, asynchronous join method.
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
                    if (Keyboard.current != null) InputManager.Instance.JoinPlayerAndLockDevice(0, Keyboard.current);
                    if (Mouse.current != null) InputManager.Instance.JoinPlayerAndLockDevice(1, Mouse.current);
                    break;
            }
        }

        private void OnDestroy()
        {
            if (isInitialized && InputManager.Instance != null)
            {
                InputManager.Instance.OnPlayerJoined -= HandlePlayerJoined;
                InputManager.Instance.Dispose();
            }
        }

        private void HandlePlayerJoined(IInputService playerInput)
        {
            int playerId = (playerInput as InputService).PlayerId;

            if (_playerPrefab == null)
            {
                Debug.LogError("Player Prefab is not set in the GameInitializer_Sample.");
                return;
            }
            if (_spawnPoints.Length <= playerId)
            {
                Debug.LogError($"Not enough spawn points for Player {playerId}.");
                return;
            }

            Transform spawnPoint = _spawnPoints[playerId];
            GameObject playerInstance = Instantiate(_playerPrefab, spawnPoint.position, spawnPoint.rotation);
            var controller = playerInstance.GetComponent<SimplePlayerController>();

            if (controller)
            {
                Color playerColor = _playerColors.Length > playerId ? _playerColors[playerId] : Color.white;
                controller.Initialize(playerId, playerColor);

                // --- Context and Command setup using the new Zero-GC API ---
                
                // Create commands that link input events to controller methods
                var moveCommand = new MoveCommand(controller.OnMove);
                var confirmCommand = new ActionCommand(controller.OnConfirm);
                var confirmLongPressCommand = new ActionCommand(controller.OnConfirmLongPress);

                // Create an input context for gameplay
                var gameplayContext = new InputContext("Gameplay", "PlayerActions")
                    // Bind the 'Move' action using the generated constant ID. The name is Context_Action.
                    .AddBinding(playerInput.GetVector2Observable(InputActions.Actions.Gameplay_Move), moveCommand)
                    // Bind the 'Confirm' action's short press event
                    .AddBinding(playerInput.GetButtonObservable(InputActions.Actions.Gameplay_Confirm), confirmCommand)
                    // Bind the 'Confirm' action's long press event
                    .AddBinding(playerInput.GetLongPressObservable(InputActions.Actions.Gameplay_Confirm), confirmLongPressCommand);

                // Register the context with the service and push it to the top of the stack to activate it.
                playerInput.RegisterContext(gameplayContext);
                playerInput.PushContext("Gameplay");
                
                // --- Example of subscribing to device changes ---
                playerInput.ActiveDeviceKind.Subscribe(kind =>
                {
                    Debug.Log($"Player {playerId} active device changed to: {kind}");
                    // Here you could update UI prompts, e.g., show "Press [A]" vs "Press [Space]"
                }).AddTo(controller.destroyCancellationToken); // Manage subscription lifetime on the MonoBehaviour component, not the GameObject.
            }
        }
    }
}
