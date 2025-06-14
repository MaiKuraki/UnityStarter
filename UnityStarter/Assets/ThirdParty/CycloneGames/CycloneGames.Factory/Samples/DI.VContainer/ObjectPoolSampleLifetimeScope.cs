using UnityEngine;
using VContainer;
using VContainer.Unity;
using CycloneGames.Factory.Runtime;

namespace CycloneGames.Factory.Samples.VContainer
{
    public class ObjectPoolSampleLifetimeScope : LifetimeScope
    {
        public ObjectPoolSampleEnemy EnemyPrefab; // Assign in Inspector

        protected override void Configure(IContainerBuilder builder)
        {
            // Register a simple service
            builder.Register<ObjectPoolSampleScoreService>(Lifetime.Singleton);

            // Register your DI-aware spawner
            builder.Register<IUnityObjectSpawner, VContainerSampleObjectSpawner>(Lifetime.Singleton);

            // Register the factory, VContainer will automatically provide the IUnityObjectSpawner
            builder.Register<IFactory<ObjectPoolSampleEnemy>>(container =>
                new MonoPrefabFactory<ObjectPoolSampleEnemy>(
                    container.Resolve<IUnityObjectSpawner>(),
                    EnemyPrefab
                ), Lifetime.Singleton);

            // Register the pool, VContainer will provide the IFactory<Enemy>
            builder.Register<IMemoryPool<ObjectPoolSampleEnemyData, ObjectPoolSampleEnemy>>(container =>
                new ObjectPool<ObjectPoolSampleEnemyData, ObjectPoolSampleEnemy>(
                    container.Resolve<IFactory<ObjectPoolSampleEnemy>>(), 10),
                Lifetime.Singleton);
        }
    }
}