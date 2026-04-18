using UnityEngine;
using CycloneGames.Factory.Runtime;

namespace CycloneGames.Factory.Samples.PureUnity
{
    /// <summary>
    /// Demonstrates the usage of the configurable main-thread ObjectPool.
    /// Use this pool when you need deterministic ownership tracking and clear capacity policy.
    /// </summary>
    public class AdvancedObjectPoolSample : MonoBehaviour
    {
        [SerializeField] private Bullet BulletPrefab;

        private ObjectPool<BulletData, Bullet> _advancedPool;
        private IFactory<Bullet> _factory;

        void Start()
        {
            Debug.Log("Initializing Advanced Object Pool...");

            //    DefaultUnityObjectSpawner -> MonoPrefabFactory -> ObjectPool
            var spawner = new DefaultUnityObjectSpawner();
            _factory = new MonoPrefabFactory<Bullet>(spawner, BulletPrefab, transform);

            _advancedPool = new ObjectPool<BulletData, Bullet>(_factory, new PoolCapacitySettings(
                softCapacity: 20,
                hardCapacity: 100,
                overflowPolicy: PoolOverflowPolicy.Throw,
                trimPolicy: PoolTrimPolicy.TrimOnDespawn));

            Debug.Log($"Advanced Pool Ready. Total: {_advancedPool.CountAll}");
        }

        void Update()
        {
            if (Input.GetMouseButtonDown(1))
            {
                SpawnFromAdvancedPool();
            }

            if (Input.GetKeyDown(KeyCode.Space))
            {
                _advancedPool.ForEachActive(bullet =>
                {
                    Debug.Log($"Processing active bullet at {bullet.transform.position}");
                });
            }
        }

        private void SpawnFromAdvancedPool()
        {
            var data = new BulletData
            {
                InitialPosition = transform.position + Vector3.up * 2,
                Direction = transform.up,
                Speed = 5f
            };

            try
            {
                var bullet = _advancedPool.Spawn(data);
                Debug.Log($"[Advanced] Spawned Bullet. Active Count: {_advancedPool.CountActive}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Spawn failed (maybe MaxCapacity reached): {e.Message}");
            }
        }

        void OnDestroy()
        {
            _advancedPool?.Dispose();
        }

        void OnGUI()
        {
            GUILayout.Label("Right Click: Spawn from Advanced Pool");
            GUILayout.Label("Space: Iterate Active Items (Check Console)");
            if (_advancedPool != null)
            {
                var profile = _advancedPool.Profile;
                GUILayout.Label($"Pool Stats: {profile.CountActive} Active / {profile.CountInactive} Inactive / {profile.CountAll} Total");
                GUILayout.Label($"Capacity: soft {profile.CapacitySettings.SoftCapacity}, hard {profile.CapacitySettings.HardCapacity}");
                GUILayout.Label($"Diagnostics: peak active {profile.Diagnostics.PeakCountActive}, peak total {profile.Diagnostics.PeakCountAll}, rejected {profile.Diagnostics.RejectedSpawns}");
            }
        }
    }
}
