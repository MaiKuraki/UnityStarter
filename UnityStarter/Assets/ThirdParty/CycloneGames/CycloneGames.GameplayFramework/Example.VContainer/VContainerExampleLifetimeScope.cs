using VContainer;
using VContainer.Unity;
using UnityEngine;
using CycloneGames.Core;

namespace CycloneGames.GameplayFramework.VContainer
{
    public class VContainerExampleLifetimeScope : LifetimeScope
    {
        [SerializeField] private WorldSettings worldSettings;

        protected override void Configure(IContainerBuilder builder)
        {
            builder.Register<IObjectSpawner, VcontainerExampleObjectSpawner>(Lifetime.Singleton);
            builder.RegisterInstance<IWorldSettings>(worldSettings); //  Register the instance as interface, don't register as class
            builder.RegisterComponentInNewPrefab<IGameMode, VContainerExampleGameMode>(prefab => (VContainerExampleGameMode)worldSettings.GameModeClass, Lifetime.Singleton);
  
            builder.UseEntryPoints(Lifetime.Singleton, entryPoints =>
            {
                //  Start Game Logic
                entryPoints.Add<VContainerExampleEntryPoints>();
            });
        }
    }
}