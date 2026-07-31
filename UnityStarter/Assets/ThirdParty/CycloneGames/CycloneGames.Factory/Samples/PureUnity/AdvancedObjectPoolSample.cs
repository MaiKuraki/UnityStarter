using UnityEngine;
using CycloneGames.Factory.Runtime;
using CycloneGames.Logging;

namespace CycloneGames.Factory.Samples.PureUnity
{
    /// <summary>
    /// Demonstrates the usage of the configurable main-thread ObjectPool.
    /// Use this pool when you need deterministic ownership tracking and clear capacity policy.
    /// </summary>
    public class AdvancedObjectPoolSample : MonoBehaviour
    {
        private static readonly LogChannel Log = FactorySamplesLog.Channel;

        [SerializeField] private Bullet BulletPrefab;

        private ObjectPool<BulletData, Bullet> _advancedPool;
        private IFactory<Bullet> _factory;

        void Start()
        {
            Log.Info("Initializing Advanced Object Pool...");

            //    DefaultUnityObjectSpawner -> MonoPrefabFactory -> ObjectPool
            var spawner = new DefaultUnityObjectSpawner();
            _factory = new MonoPrefabFactory<Bullet>(spawner, BulletPrefab, transform);

            _advancedPool = new ObjectPool<BulletData, Bullet>(_factory, new PoolCapacitySettings(
                softCapacity: 20,
                hardCapacity: 100,
                overflowPolicy: PoolOverflowPolicy.Throw,
                trimPolicy: PoolTrimPolicy.TrimOnDespawn));

            Log.Info($"Advanced Pool Ready. Total: {_advancedPool.CountAll}");
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
                    Log.Debug($"Processing active bullet at {bullet.transform.position}");
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
                Log.Info($"Spawned Bullet. Active Count: {_advancedPool.CountActive}");
            }
            catch (System.Exception e)
            {
                Log.Error(e, "Spawn failed; the pool may have reached MaxCapacity.");
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
