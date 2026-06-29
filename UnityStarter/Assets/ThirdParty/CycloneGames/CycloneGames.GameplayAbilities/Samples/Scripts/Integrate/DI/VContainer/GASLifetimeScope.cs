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
            builder.Register<GameplayCueManager>(Lifetime.Singleton)
                .As<IGameplayCueManager>();

            builder.RegisterBuildCallback(resolver =>
            {
                var cueManager = resolver.Resolve<IGameplayCueManager>();
                GameplayCueManager.SetInstance(cueManager);
            });
        }
    }
}
