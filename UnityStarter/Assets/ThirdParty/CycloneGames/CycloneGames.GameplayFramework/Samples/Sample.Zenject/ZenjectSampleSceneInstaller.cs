using CycloneGames.Core;
using Zenject;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Sample.Zenject
{
    public class ZenjectSampleSceneInstaller : MonoInstaller
    {
        [SerializeField] WorldSettings worldSettings;

        public override void InstallBindings()
        {
            Container.Bind<IObjectSpawner>().To<ZenjectSampleObjectSpawner>().AsSingle().NonLazy();
            Container.BindInstance<IWorldSettings>(worldSettings).AsSingle().NonLazy();
            Container.Bind<IGameMode>().FromComponentInNewPrefab((ZenjectSampleGameMode)worldSettings.GameModeClass).AsSingle().NonLazy();
            UnityEngine.Debug.Log($"Install bindings for Zenject");

            //  Initialize GameMode
            IWorldSettings zenjectWorldSettings = Container.Resolve<IWorldSettings>();
            IObjectSpawner objectSpawner = Container.Resolve<IObjectSpawner>();
            IGameMode gameMode = Container.Resolve<IGameMode>();
            ((ZenjectSampleGameMode)gameMode).Initialize(objectSpawner, zenjectWorldSettings);
        }
    }
}