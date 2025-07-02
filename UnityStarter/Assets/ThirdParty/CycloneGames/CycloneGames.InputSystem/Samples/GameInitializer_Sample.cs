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

            // 1. Use your FilePathUtility to generate the full, platform-correct URIs.
            string defaultConfigUri = FilePathUtility.GetUnityWebRequestUri(_defaultConfigName, UnityPathSource.StreamingAssets);
            string userConfigUri = FilePathUtility.GetUnityWebRequestUri(_userConfigName, UnityPathSource.PersistentData);

            // 2. Call the pure C# loader with both paths to initialize the InputManager.
            await InputSystemLoader.InitializeAsync(defaultConfigUri, userConfigUri);
            
            // 3. Once initialized, subscribe to the player join event.
            InputManager.Instance.OnPlayerJoined += HandlePlayerJoined;
            
            // 4. Start listening for players.
            InputManager.Instance.StartListeningForPlayers();
            
            Debug.Log("Game Initialized. Waiting for players to press 'Enter' or 'Start' to join...");
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