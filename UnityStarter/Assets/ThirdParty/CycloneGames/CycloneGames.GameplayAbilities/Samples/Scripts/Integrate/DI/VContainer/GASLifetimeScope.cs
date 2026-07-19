using VContainer;
using VContainer.Unity;

using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayAbilities.Runtime;

namespace CycloneGames.GameplayAbilities.Integrate.VContainer
{
    /// <summary>
    /// Example VContainer startup scope for the GameplayAbilities sample.
    /// Copy this pattern into the project's composition root and register project-specific services there.
    /// </summary>
    public class GASLifetimeScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            var cuePoolConfig = new GameObjectPoolManager.PoolConfig(
                maxAssetPools: 128,
                maxActiveLeases: 2048,
                maxActiveLeasesPerPool: 256,
                maxRetainedInstancesPerPool: 128,
                minRetainedInstancesPerPool: 0,
                idleExpirationTime: 60f);

            builder.Register(_ => new GameplayCueManager(cuePoolConfig), Lifetime.Singleton)
                .As<GameplayCueManager>();
            builder.Register(
                    resolver => new GASRuntimeContext(cueManager: resolver.Resolve<GameplayCueManager>()),
                    Lifetime.Singleton)
                .As<GASRuntimeContext>();
        }
    }
}
