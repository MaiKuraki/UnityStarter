using VContainer;
using VContainer.Unity;
using UnityEngine;
using CycloneGames.Factory.Runtime;

namespace CycloneGames.GameplayFramework.Sample.VContainer
{
    public class VContainerSampleLifetimeScope : LifetimeScope
    {
        [SerializeField] private WorldSettings worldSettings;

        protected override void Configure(IContainerBuilder builder)
        {
            builder.Register<IUnityObjectSpawner, VContainerSampleObjectSpawner>(Lifetime.Singleton);
            builder.RegisterInstance<IWorldSettings>(worldSettings); //  Register the instance as interface, don't register as class
            builder.RegisterComponentInNewPrefab<IGameMode, VContainerSampleGameMode>(prefab => (VContainerSampleGameMode)worldSettings.GameModeClass, Lifetime.Singleton);

            builder.UseEntryPoints(Lifetime.Singleton, entryPoints =>
            {
                //  Start Game Logic
                entryPoints.Add<VContainerSampleEntryPoints>();
            });
        }
    }
}