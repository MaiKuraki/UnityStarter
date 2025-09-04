using UnityEngine;
using UnityEngine.UI;

namespace CycloneGames.Factory.OOPBullet
{
    /// <summary>
    /// Simple UI controller to display bullet spawner statistics
    /// </summary>
    public class BulletSpawnerUI : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private Text statsText;
        [SerializeField] private Button spawnButton;
        [SerializeField] private Button despawnAllButton;
        [SerializeField] private Button toggleSpawnButton;
        [SerializeField] private Slider spawnRateSlider;
        [SerializeField] private Text spawnRateText;
        
        [Header("Spawner Reference")]
        [SerializeField] private BulletSpawner bulletSpawner;
        
        private bool _autoSpawnEnabled = true;
        private float _updateInterval = 0.1f; // Update UI every 100ms
        private float _lastUpdateTime;

        private void Start()
        {
            SetupUI();
        }

        private void Update()
        {
            // Update UI at specified interval
            if (Time.time - _lastUpdateTime >= _updateInterval)
            {
                UpdateStats();
                _lastUpdateTime = Time.time;
            }
        }

        private void SetupUI()
        {
            if (bulletSpawner == null)
            {
                bulletSpawner = FindObjectOfType<BulletSpawner>();
                if (bulletSpawner == null)
                {
                    Debug.LogError("BulletSpawner not found! Please assign it in the inspector or ensure one exists in the scene.");
                    return;
                }
            }

            // Setup button events
            if (spawnButton != null)
            {
                spawnButton.onClick.AddListener(() => bulletSpawner.SpawnBullet());
            }

            if (despawnAllButton != null)
            {
                despawnAllButton.onClick.AddListener(() => bulletSpawner.DespawnAllBullets());
            }

            if (toggleSpawnButton != null)
            {
                toggleSpawnButton.onClick.AddListener(ToggleAutoSpawn);
            }

            if (spawnRateSlider != null)
            {
                spawnRateSlider.onValueChanged.AddListener(OnSpawnRateChanged);
                spawnRateSlider.value = 200f; // Default spawn rate
            }

            // Initial UI update
            UpdateStats();
        }

        private void UpdateStats()
        {
            if (bulletSpawner == null) return;

            if (statsText != null)
            {
                statsText.text = bulletSpawner.GetPoolStats();
            }

            if (spawnRateText != null && spawnRateSlider != null)
            {
                spawnRateText.text = $"Spawn Rate: {spawnRateSlider.value:F0}/sec";
            }
        }

        private void ToggleAutoSpawn()
        {
            _autoSpawnEnabled = !_autoSpawnEnabled;
            
            // We need to modify the BulletSpawner to support this
            // For now, we'll just update the button text
            if (toggleSpawnButton != null)
            {
                var buttonText = toggleSpawnButton.GetComponentInChildren<Text>();
                if (buttonText != null)
                {
                    buttonText.text = _autoSpawnEnabled ? "Stop Auto Spawn" : "Start Auto Spawn";
                }
            }
        }

        private void OnSpawnRateChanged(float value)
        {
            // We would need to add a method to BulletSpawner to change spawn rate at runtime
            // For now, this is just for UI display
            UpdateStats();
        }

        private void OnDestroy()
        {
            // Clean up button events
            if (spawnButton != null)
            {
                spawnButton.onClick.RemoveAllListeners();
            }

            if (despawnAllButton != null)
            {
                despawnAllButton.onClick.RemoveAllListeners();
            }

            if (toggleSpawnButton != null)
            {
                toggleSpawnButton.onClick.RemoveAllListeners();
            }

            if (spawnRateSlider != null)
            {
                spawnRateSlider.onValueChanged.RemoveAllListeners();
            }
        }
    }
}
