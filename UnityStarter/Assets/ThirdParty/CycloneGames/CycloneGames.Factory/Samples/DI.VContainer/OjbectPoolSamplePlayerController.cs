using UnityEngine;
using VContainer;
using CycloneGames.Factory.Runtime;

namespace CycloneGames.Factory.Samples.VContainer
{
    public class PlayerController : MonoBehaviour
    {
        [Inject]
        private readonly IMemoryPool<ObjectPoolSampleEnemyData, ObjectPoolSampleEnemy> _enemyPool;

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.E))
            {
                var data = new ObjectPoolSampleEnemyData
                {
                    Health = 100,
                    SpawnPosition = new Vector3(Random.Range(-5, 5), 0, Random.Range(-5, 5))
                };
                _enemyPool.Spawn(data);
            }
        }
    }
}