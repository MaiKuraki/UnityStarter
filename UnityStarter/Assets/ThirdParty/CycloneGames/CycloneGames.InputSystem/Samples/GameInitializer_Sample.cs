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
            AutoJoinLockedSinglePlayer,
            AutoJoinSharedKeyboard,
            LobbyWithDeviceLocking,
            LobbyWithSharedDevices,
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

        private void HandlePlayerJoined(IInputPlayer inputPlayer)
        {
            int playerId = (inputPlayer as InputPlayer).PlayerId;

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

                var moveCommand = new MoveCommand(controller.OnMove);
                var confirmCommand = new ActionCommand(controller.OnConfirm);
                var confirmLongPressCommand = new ActionCommand(controller.OnConfirmLongPress);

                var gameplayContext = new InputContext("Gameplay", "PlayerActions")
                    .AddBinding(inputPlayer.GetVector2Observable(InputActions.Actions.Gameplay_Move), moveCommand)
                    .AddBinding(inputPlayer.GetButtonObservable(InputActions.Actions.Gameplay_Confirm), confirmCommand)
                    .AddBinding(inputPlayer.GetLongPressObservable(InputActions.Actions.Gameplay_Confirm), confirmLongPressCommand);

                inputPlayer.RegisterContext(gameplayContext);
                inputPlayer.PushContext("Gameplay");

                inputPlayer.ActiveDeviceKind.Subscribe(kind =>
                {
                    Debug.Log($"Player {playerId} active device changed to: {kind}");
                }).AddTo(controller.destroyCancellationToken);
            }
        }
    }
}