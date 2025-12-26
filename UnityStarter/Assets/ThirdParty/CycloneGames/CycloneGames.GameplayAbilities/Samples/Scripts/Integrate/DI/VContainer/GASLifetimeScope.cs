#if VCONTAINER_PRESENT

using VContainer;
using VContainer.Unity;
using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayAbilities.Runtime;

namespace CycloneGames.GameplayAbilities.Integrate.VContainer
{
    /// <summary>
    /// Example VContainer integration for GAS module.
    /// Shows how to register GameplayCueManager for DI.
    /// 
    /// NOTE: This class just sample for VContainer initialize, you must implement your own GAS initialize
    /// </summary>
    public class GASLifetimeScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            // Register GameplayCueManager as singleton implementing IGameplayCueManager
            builder.Register<GameplayCueManager>(Lifetime.Singleton)
                .As<IGameplayCueManager>();
            
            // Set up the static instance and GASServices on container build
            builder.RegisterBuildCallback(resolver =>
            {
                var cueManager = resolver.Resolve<IGameplayCueManager>();
                GameplayCueManager.SetInstance(cueManager);
            });
        }
    }
}

#endif