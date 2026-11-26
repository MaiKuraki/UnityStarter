using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

using UnityEngine;

using Cysharp.Threading.Tasks;
using R3;

using CycloneGames.InputSystem.Runtime;
using CycloneGames.InputSystem.Runtime.Generated;
using CycloneGames.Logging;

namespace CycloneGames.InputSystem.Sample
{
    public class GameInitializer_Sample : MonoBehaviour
    {
        private static readonly LogChannel Log = InputSystemSampleLog.Channel;

        public enum StartupMode
        {
            AutoJoinLockedSinglePlayer,
            AutoJoinSharedKeyboard,
            LobbyWithDeviceLocking,
            LobbyWithSharedDevices,
            AutoJoinMixedDevices,
            // Append-only: existing serialized integer values must keep their meaning.
            AutoJoinKeyboardMouseVsGamepad
        }

        [Header("Game Mode")]
        [SerializeField] private StartupMode startupMode = StartupMode.AutoJoinLockedSinglePlayer;

        [Header("Game Setup")]
        [SerializeField] private GameObject _playerPrefab;
        [SerializeField] private Transform[] _spawnPoints;
        [SerializeField] private Color[] _playerColors;

        [Header("Input Configuration")]
        [SerializeField] private TextAsset _configuration;
        [SerializeField] private TextAsset _mixedConfiguration;
        [SerializeField] private string _userConfigName = "user_input_settings.yaml";

        private static GameInitializer_Sample _owner;
        private static bool isInitialized;

        private readonly Dictionary<int, GameObject> _spawnedPlayers = new();
        private bool _isOwner;
        private InputManager _inputManager;
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

            _inputManager = new InputManager();
            try
            {
                TextAsset configurationAsset = UsesMixedConfiguration()
                    ? _mixedConfiguration
                    : _configuration;
                if (configurationAsset == null)
                {
                    throw new InvalidOperationException(
                        "Assign a configuration TextAsset on the InputManagerSample component.");
                }

                // Each startup mode owns a distinct user key; the authored fixture is never persisted.
                string userKey = BuildUserKey();
                var userStore = new FileInputConfigurationStore(
                    Application.persistentDataPath);
                var options = new InputSystemBootstrapOptions(
                    InputSystemBootstrapMode.Required,
                    new TextAssetInputConfigurationSource(configurationAsset),
                    configurationAsset.name,
                    userStore,
                    userKey,
                    persistDefaultToUser: false);
                InputSystemLoadResult loadResult = await InputSystemLoader.LoadAndInitializeAsync(
                    options,
                    _inputManager,
                    false,
                    cancellationToken);
                if (!loadResult.IsSuccess)
                {
                    throw new InvalidOperationException(
                        $"Input initialization failed with status {loadResult.Status}: {loadResult.Error}");
                }

                Log.Info(
                    (Mode: startupMode.ToString(), Fixture: configurationAsset.name, Key: userKey,
                        Source: loadResult.Status.ToString()),
                    static (state, builder) => builder
                        .Append("[GameInitializer_Sample] mode=").Append(state.Mode)
                        .Append(" fixture=").Append(state.Fixture)
                        .Append(" userKey=").Append(state.Key)
                        .Append(" source=").Append(state.Source));

                cancellationToken.ThrowIfCancellationRequested();
                await UniTask.SwitchToMainThread(PlayerLoopTiming.Update, cancellationToken);

                _inputManager.OnPlayerInputReady += HandlePlayerInputReady;
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
                Log.Error(
                    exception,
                    "[GameInitializer_Sample] Startup failed; owned state will be shut down.");
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
                case StartupMode.AutoJoinMixedDevices:
                    EnsurePlayerJoined(
                        await _inputManager.JoinSinglePlayerAsync(
                            0,
                            5,
                            cancellationToken),
                        0);
                    cancellationToken.ThrowIfCancellationRequested();
                    break;

                case StartupMode.AutoJoinSharedKeyboard:
                    EnsurePlayerJoined(
                        await _inputManager.JoinPlayerOnSharedDeviceAsync(0),
                        0);
                    cancellationToken.ThrowIfCancellationRequested();
                    EnsurePlayerJoined(
                        await _inputManager.JoinPlayerOnSharedDeviceAsync(1),
                        1);
                    cancellationToken.ThrowIfCancellationRequested();
                    break;

                case StartupMode.LobbyWithDeviceLocking:
                    _inputManager.StartListeningForPlayers(true);
                    break;

                case StartupMode.LobbyWithSharedDevices:
                    _inputManager.StartListeningForPlayers(false);
                    break;

                case StartupMode.AutoJoinKeyboardMouseVsGamepad:
                    // Player 0 naturally claims keyboard/mouse with the first matching control scheme;
                    // with those devices unavailable, player 1 falls through to the Gamepad scheme.
                    EnsurePlayerJoined(_inputManager.JoinSinglePlayer(0), 0);
                    cancellationToken.ThrowIfCancellationRequested();
                    IInputPlayer secondPlayer = _inputManager.JoinSinglePlayer(1);
                    if (secondPlayer == null)
                    {
                        Log.Warning(
                            "[GameInitializer_Sample] Player 1 could not join with a gamepad; " +
                            "connect a gamepad and restart the scene for this mode.");
                    }

                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private bool UsesMixedConfiguration()
        {
            return startupMode == StartupMode.AutoJoinMixedDevices;
        }

        private string BuildUserKey()
        {
            string configured = string.IsNullOrWhiteSpace(_userConfigName)
                ? "user_input_settings.yaml"
                : _userConfigName;
            string extension = Path.GetExtension(configured);
            if (string.IsNullOrEmpty(extension)) extension = ".yaml";
            string stem = Path.GetFileNameWithoutExtension(configured);
            if (string.IsNullOrEmpty(stem)) stem = "user_input_settings";
            return stem + "_" + startupMode + extension;
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
            if (_inputManager == null || inputPlayer == null)
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
                Log.Error("Player Prefab is not set in the GameInitializer_Sample.");
                return;
            }

            if (_spawnPoints == null || playerId < 0 || playerId >= _spawnPoints.Length ||
                _spawnPoints[playerId] == null)
            {
                Log.Error($"No valid spawn point is configured for Player {playerId}.");
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
                Log.Error("The sample player prefab requires SimplePlayerController.");
                Destroy(playerInstance);
                return;
            }

            _spawnedPlayers.Add(playerId, playerInstance);
            Color playerColor = _playerColors != null && playerId < _playerColors.Length
                ? _playerColors[playerId]
                : Color.white;
            controller.Initialize(playerId, playerColor);

            if (inputPlayer is InputPlayer concretePlayer)
            {
                var pairedBuilder = new System.Text.StringBuilder();
                var pairedDevices = concretePlayer.User.pairedDevices;
                for (int i = 0; i < pairedDevices.Count; i++)
                {
                    if (i > 0) pairedBuilder.Append(", ");
                    pairedBuilder.Append(pairedDevices[i].layout);
                }

                string pairedText = pairedBuilder.Length == 0 ? "<none>" : pairedBuilder.ToString();
                Log.Info(
                    (PlayerId: playerId, Paired: pairedText),
                    static (state, builder) => builder
                        .Append("[Sample] Player ")
                        .Append(state.PlayerId)
                        .Append(" paired devices: ")
                        .Append(state.Paired));
            }

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
                {
                    Log.Info(
                        (PlayerId: playerId, Kind: kind),
                        static (state, builder) => builder
                            .Append("Player ")
                            .Append(state.PlayerId)
                            .Append(" active device changed to: ")
                            .Append(state.Kind));
                    if (controller != null) controller.ApplyDeviceTint(kind);
                })
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
                _inputManager.OnPlayerInputReady -= HandlePlayerInputReady;
                _isSubscribed = false;
            }

            _inputManager.StopListeningForPlayers();
            DestroySpawnedPlayers();

            if (_inputManager != null)
            {
                _inputManager.Dispose();
                _inputManager = null;
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
