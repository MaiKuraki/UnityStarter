using UnityEngine;
using CycloneGames.Factory.Runtime;
using VContainer;

namespace CycloneGames.Factory.Samples.VContainer
{
    public class ObjectPoolSampleScoreService { public void AddScore(int amount) => Debug.Log($"Added {amount} score!"); }

    public struct ObjectPoolSampleEnemyData
    {
        public float Health;
        public Vector3 SpawnPosition;
    }

    public class ObjectPoolSampleEnemy : MonoBehaviour, IPoolable<ObjectPoolSampleEnemyData, IMemoryPool>, ITickable
    {
        [Inject] private readonly ObjectPoolSampleScoreService _scoreService;
        private IMemoryPool _pool;

        public void OnSpawned(ObjectPoolSampleEnemyData data, IMemoryPool pool)
        {
            _pool = pool;
            transform.position = data.SpawnPosition;
            gameObject.SetActive(true);
            Debug.Log("Enemy spawned with DI-injected ScoreService!");
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