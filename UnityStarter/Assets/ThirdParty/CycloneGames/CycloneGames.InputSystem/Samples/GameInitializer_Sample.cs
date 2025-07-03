using CycloneGames.InputSystem.Runtime;
using CycloneGames.Utility.Runtime;
using System.Threading.Tasks;
using UnityEngine;

namespace CycloneGames.InputSystem.Sample
{
    public class GameInitializer_Sample : MonoBehaviour
    {
        [Header("Game Setup")]
        [SerializeField] private GameObject _playerPrefab;
        [SerializeField] private Transform[] _spawnPoints;
        [SerializeField] private Color[] _playerColors;

        [Header("Input Configuration")]
        [Tooltip("The default, read-only config file (relative to StreamingAssets).")]
        [SerializeField] private string _defaultConfigName = "input_config.yaml";

        [Tooltip("The user-specific, writable config file (relative to PersistentDataPath).")]
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

            string defaultConfigUri = FilePathUtility.GetUnityWebRequestUri(_defaultConfigName, UnityPathSource.StreamingAssets);
            string userConfigUri = FilePathUtility.GetUnityWebRequestUri(_userConfigName, UnityPathSource.PersistentData);

            await InputSystemLoader.InitializeAsync(defaultConfigUri, userConfigUri);

            InputManager.Instance.OnPlayerJoined += HandlePlayerJoined;

            #region EXAMPLE OF HOW TO WAIT FOR INPUT
            {
                // InputManager.Instance.StartListeningForPlayers();       //  With waiting input for lisitening multiple players.
                // Debug.Log("Game Initialized. Waiting for players to press 'Enter' or 'Start' to join...");
            }
            #endregion

            InputManager.Instance.JoinSinglePlayer();               //  No input waiting immediately let Player 0 join
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
            // This logic for spawning and setting up a player remains the same.
            int playerId = (playerInput as InputService).PlayerId;
            // ... (Spawn player, get controller, etc.)

            // Example: Set up commands and context
            // ... (Create commands, create context, register, push context)
        }

        // --- EXAMPLE OF HOW TO SAVE ---
        // You would call this from your settings UI, for example.
        public async Task Example_OnKeyRebindFinished()
        {
            // Let's assume you have a UI that modified the _configuration object
            // inside the InputManager (this would require making _configuration public
            // or providing specific methods to alter it).

            Debug.Log("Settings changed, saving new user configuration...");
            await InputManager.Instance.SaveUserConfigurationAsync();
        }
    }
}