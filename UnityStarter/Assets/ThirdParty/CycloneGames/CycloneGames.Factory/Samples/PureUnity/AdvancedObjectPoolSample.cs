using UnityEngine;
using CycloneGames.Factory.Runtime;

namespace CycloneGames.Factory.Samples.PureUnity
{
    /// <summary>
    /// Demonstrates the usage of the heavy-duty 'ObjectPool'.
    /// Use this pool when you need:
    /// - Thread safety (access from multiple threads).
    /// - Automatic tracking of active items (UpdateActiveItems).
    /// - Complex factory composition (IFactory decorators), maybe integrate DI framework
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

            _advancedPool = new ObjectPool<BulletData, Bullet>(
                _factory,
                initialCapacity: 20,
                expansionFactor: 0.5f,
                shrinkBufferFactor: 0.2f,
                shrinkCooldownTicks: 600
            );

            _advancedPool.MaxCapacity = 100;

            Debug.Log($"Advanced Pool Ready. Total: {_advancedPool.NumTotal}");
        }

        void Update()
        {
            if (Input.GetMouseButtonDown(1))
            {
                SpawnFromAdvancedPool();
            }

            if (Input.GetKeyDown(KeyCode.Space))
            {
                _advancedPool.UpdateActiveItems(bullet =>
                {
                    Debug.Log($"Processing active bullet at {bullet.transform.position}");
                });
            }

            _advancedPool.Maintenance();
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
                Debug.Log($"[Advanced] Spawned Bullet. Active Count: {_advancedPool.NumActive}");
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
                GUILayout.Label($"Pool Stats: {_advancedPool.NumActive} Active / {_advancedPool.NumInactive} Inactive");
            }
        }
    }
}