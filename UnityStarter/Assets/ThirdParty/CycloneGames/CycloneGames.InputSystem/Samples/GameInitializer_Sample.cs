using System;
using System.Collections.Generic;
using System.Threading;

using UnityEngine;

using Cysharp.Threading.Tasks;
using R3;

using CycloneGames.InputSystem.Runtime;
using CycloneGames.InputSystem.Runtime.Generated;
using CycloneGames.IO.Unity;

namespace CycloneGames.InputSystem.Sample
{
    public class GameInitializer_Sample : MonoBehaviour
    {
        public enum StartupMode
        {
            AutoJoinLockedSinglePlayer,
            AutoJoinSharedKeyboard,
            LobbyWithDeviceLocking,
            LobbyWithSharedDevices
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

        private static GameInitializer_Sample _owner;
        private static bool isInitialized;

        private readonly Dictionary<int, GameObject> _spawnedPlayers = new();
        private bool _isOwner;
        private bool _ownsInputManager;
        private bool _isSubscribed;
        private bool _isShuttingDown;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            _owner = null;
            isInitialized = false;
        }

        private void Awake()
        {
            if (_owner != null && _owner != this)
            {
                Destroy(gameObject);
                return;
            }

            _owner = this;
            _isOwner = true;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            if (!_isOwner)
            {
                return;
            }

            InitializeAsync(this.GetCancellationTokenOnDestroy()).Forget();
        }

        private async UniTask InitializeAsync(CancellationToken cancellationToken)
        {
            if (isInitialized || !_isOwner)
            {
                return;
            }

            _ownsInputManager = true;
            try
            {
                string defaultConfigUri = UnityFileUri.Create(
                    _defaultConfigName,
                    UnityFileLocation.StreamingAssets);
                var userStore = new FileInputConfigurationStore(
                    Application.persistentDataPath);
                InputSystemLoadResult loadResult = await InputSystemLoader.LoadAndInitializeAsync(
                    new UriInputConfigurationSource(),
                    defaultConfigUri,
                    userStore,
                    _userConfigName,
                    InputManager.Instance,
                    false,
                    cancellationToken);
                if (!loadResult.IsSuccess)
                {
                    throw new InvalidOperationException(
                        $"Input initialization failed with status {loadResult.Status}: {loadResult.Error}");
                }

                cancellationToken.ThrowIfCancellationRequested();
                await UniTask.SwitchToMainThread(PlayerLoopTiming.Update, cancellationToken);

                InputManager.Instance.OnPlayerInputReady += HandlePlayerInputReady;
                _isSubscribed = true;

                await ConfigureStartupModeAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                isInitialized = true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                ShutdownOwnedState();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                ShutdownOwnedState();
                if (this != null)
                {
                    Destroy(gameObject);
                }
            }
        }

        private async UniTask ConfigureStartupModeAsync(CancellationToken cancellationToken)
        {
            switch (startupMode)
            {
                case StartupMode.AutoJoinLockedSinglePlayer:
                    EnsurePlayerJoined(
                        await InputManager.Instance.JoinSinglePlayerAsync(
                            0,
                            5,
                            cancellationToken),
                        0);
                    cancellationToken.ThrowIfCancellationRequested();
                    break;

                case StartupMode.AutoJoinSharedKeyboard:
                    EnsurePlayerJoined(
                        await InputManager.Instance.JoinPlayerOnSharedDeviceAsync(0),
                        0);
                    cancellationToken.ThrowIfCancellationRequested();
                    EnsurePlayerJoined(
                        await InputManager.Instance.JoinPlayerOnSharedDeviceAsync(1),
                        1);
                    cancellationToken.ThrowIfCancellationRequested();
                    break;

                case StartupMode.LobbyWithDeviceLocking:
                    InputManager.Instance.StartListeningForPlayers(true);
                    break;

                case StartupMode.LobbyWithSharedDevices:
                    InputManager.Instance.StartListeningForPlayers(false);
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private static void EnsurePlayerJoined(IInputPlayer inputPlayer, int playerId)
        {
            if (inputPlayer == null)
            {
                throw new InvalidOperationException(
                    $"Input player {playerId} could not be joined. Verify the sample configuration and connected devices.");
            }
        }

        private void OnDestroy()
        {
            if (_isOwner)
            {
                ShutdownOwnedState();
            }
        }

        private void HandlePlayerInputReady(IInputPlayer inputPlayer)
        {
            if (!_ownsInputManager || inputPlayer == null)
            {
                return;
            }

            int playerId = inputPlayer.PlayerId;
            if (_spawnedPlayers.TryGetValue(playerId, out GameObject existingPlayer))
            {
                if (existingPlayer != null)
                {
                    return;
                }

                _spawnedPlayers.Remove(playerId);
            }

            if (_playerPrefab == null)
            {
                Debug.LogError("Player Prefab is not set in the GameInitializer_Sample.", this);
                return;
            }

            if (_spawnPoints == null || playerId < 0 || playerId >= _spawnPoints.Length ||
                _spawnPoints[playerId] == null)
            {
                Debug.LogError($"No valid spawn point is configured for Player {playerId}.", this);
                return;
            }

            Transform spawnPoint = _spawnPoints[playerId];
            GameObject playerInstance = Instantiate(
                _playerPrefab,
                spawnPoint.position,
                spawnPoint.rotation);
            SimplePlayerController controller = playerInstance.GetComponent<SimplePlayerController>();
            if (controller == null)
            {
                Debug.LogError("The sample player prefab requires SimplePlayerController.", playerInstance);
                Destroy(playerInstance);
                return;
            }

            _spawnedPlayers.Add(playerId, playerInstance);
            Color playerColor = _playerColors != null && playerId < _playerColors.Length
                ? _playerColors[playerId]
                : Color.white;
            controller.Initialize(playerId, playerColor);

            var gameplayContext = new InputContext(
                    InputActions.ActionMaps.PlayerActions,
                    InputActions.Contexts.Gameplay)
                .AddBinding(
                    inputPlayer.GetVector2Observable(InputActions.Actions.Gameplay_Move),
                    new MoveCommand(controller.OnMove))
                .AddBinding(
                    inputPlayer.GetButtonObservable(InputActions.Actions.Gameplay_Confirm),
                    new ActionCommand(controller.OnConfirm))
                .AddBinding(
                    inputPlayer.GetLongPressObservable(InputActions.Actions.Gameplay_Confirm),
                    new ActionCommand(controller.OnConfirmLongPress));

            gameplayContext.AddTo(controller.destroyCancellationToken);
            inputPlayer.PushContext(gameplayContext);
            inputPlayer.ActiveDeviceKind
                .Subscribe(kind =>
                    Debug.Log($"Player {playerId} active device changed to: {kind}", controller))
                .AddTo(controller.destroyCancellationToken);
        }

        private void ShutdownOwnedState()
        {
            if (_isShuttingDown)
            {
                return;
            }

            _isShuttingDown = true;
            if (_isSubscribed)
            {
                InputManager.Instance.OnPlayerInputReady -= HandlePlayerInputReady;
                _isSubscribed = false;
            }

            InputManager.Instance.StopListeningForPlayers();
            DestroySpawnedPlayers();

            if (_ownsInputManager)
            {
                InputManager.Instance.Dispose();
                _ownsInputManager = false;
            }

            isInitialized = false;
            if (_owner == this)
            {
                _owner = null;
            }

            _isOwner = false;
        }

        private void DestroySpawnedPlayers()
        {
            foreach (KeyValuePair<int, GameObject> entry in _spawnedPlayers)
            {
                if (entry.Value != null)
                {
                    Destroy(entry.Value);
                }
            }

            _spawnedPlayers.Clear();
        }
    }
}
