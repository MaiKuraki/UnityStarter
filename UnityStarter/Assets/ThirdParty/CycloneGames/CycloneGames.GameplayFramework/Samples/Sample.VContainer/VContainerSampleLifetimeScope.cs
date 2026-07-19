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
            builder.Register(
                resolver => new GameInstance(resolver.Resolve<IUnityObjectSpawner>()),
                Lifetime.Singleton);

            builder.UseEntryPoints(Lifetime.Singleton, entryPoints =>
            {
                entryPoints.Add<VContainerSampleEntryPoints>();
            });
        }
    }
}
