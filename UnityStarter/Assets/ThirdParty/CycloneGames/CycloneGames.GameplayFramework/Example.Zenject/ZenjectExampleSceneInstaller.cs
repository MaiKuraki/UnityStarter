using CycloneGames.Core;
using Zenject;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Example.Zenject
{
    public class ZenjectExampleSceneInstaller : MonoInstaller
    {
        [SerializeField] WorldSettings worldSettings;

        public override void InstallBindings()
        {
            Container.Bind<IObjectSpawner>().To<ZenjectExampleObjectSpawner>().AsSingle().NonLazy();
            Container.BindInstance<IWorldSettings>(worldSettings).AsSingle().NonLazy();
            Container.Bind<IGameMode>().FromComponentInNewPrefab((ZenjectExampleGameMode)worldSettings.GameModeClass).AsSingle().NonLazy();
            UnityEngine.Debug.Log($"Install bindings for Zenject");

            //  Initialize GameMode
            IWorldSettings zenjectWorldSettings = Container.Resolve<IWorldSettings>();
            IObjectSpawner objectSpawner = Container.Resolve<IObjectSpawner>();
            IGameMode gameMode = Container.Resolve<IGameMode>();
            ((ZenjectExampleGameMode)gameMode).Initialize(objectSpawner, zenjectWorldSettings);
        }
    }
}