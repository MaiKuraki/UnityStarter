using UnityEngine;
using CycloneGames.Factory.Runtime;
using CycloneGames.Logging;
using VContainer;

namespace CycloneGames.Factory.Samples.VContainer
{
    public class ObjectPoolSampleScoreService
    {
        private static readonly LogChannel Log = FactoryVContainerSamplesLog.Channel;

        public void AddScore(int amount) => Log.Info($"Added {amount} score!");
    }

    public struct ObjectPoolSampleEnemyData
    {
        public float Health;
        public Vector3 SpawnPosition;
    }

    public class ObjectPoolSampleEnemy : MonoBehaviour, IPoolable<ObjectPoolSampleEnemyData, ObjectPoolSampleEnemy>, ITickable
    {
        private static readonly LogChannel Log = FactoryVContainerSamplesLog.Channel;

        [Inject] private readonly ObjectPoolSampleScoreService _scoreService;
        private IDespawnableMemoryPool<ObjectPoolSampleEnemy> _pool;

        public void OnSpawned(ObjectPoolSampleEnemyData data, IDespawnableMemoryPool<ObjectPoolSampleEnemy> pool)
        {
            _pool = pool;
            transform.position = data.SpawnPosition;
            gameObject.SetActive(true);
            Log.Info("Enemy spawned with DI-injected ScoreService!");
        }

        public void OnDespawned() => gameObject.SetActive(false);

        public void Tick() { /* Enemy logic here */ }

        // Call this when the enemy is defeated
        public void Defeated()
        {
            _scoreService.AddScore(10);
            _pool.Despawn(this);
        }

        public void Dispose() => Destroy(gameObject);
    }
}