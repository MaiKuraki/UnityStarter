using System;
using CycloneGames.Factory.Runtime;
using CycloneGames.GameplayFramework.Runtime;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace CycloneGames.GameplayFramework.Sample.VContainer
{
    public sealed class VContainerSampleLifetimeScope : LifetimeScope
    {
        [SerializeField] private WorldSettings worldSettings;

        protected override void Configure(IContainerBuilder builder)
        {
            if (worldSettings == null)
            {
                throw new InvalidOperationException(
                    "VContainerSampleLifetimeScope requires WorldSettings.");
            }

            builder.RegisterInstance(worldSettings);
            builder.Register<IUnityObjectSpawner, VContainerSampleObjectSpawner>(Lifetime.Singleton);
            builder.Register<IActorLifetime, VContainerActorLifetime>(Lifetime.Singleton);

            // A World only discovers scene Actors through an explicit source; without one, PlayerStart is
            // never registered and the default Pawn spawns at the world origin instead of the start spot.
            builder.Register(
                resolver => new GameInstance(
                    resolver.Resolve<IActorLifetime>(),
                    actorSource: new SceneWorldActorSource(gameObject.scene)),
                Lifetime.Singleton);

            builder.UseEntryPoints(Lifetime.Singleton, entryPoints =>
            {
                entryPoints.Add<VContainerSampleEntryPoints>();
            });
        }
    }
}
