using Zenject;
using UnityEngine;
using CycloneGames.Factory.Runtime;
using CycloneGames.GameplayFramework.Runtime;

namespace CycloneGames.GameplayFramework.Sample.Zenject
{
    public class ZenjectSampleSceneInstaller : MonoInstaller
    {
        [SerializeField] WorldSettings worldSettings;

        public override void InstallBindings()
        {
            Container.Bind<IUnityObjectSpawner>().To<ZenjectSampleObjectSpawner>().AsSingle().NonLazy();
            Container.BindInstance<IWorldSettings>(worldSettings).AsSingle().NonLazy();
            Container.Bind<IGameMode>().FromComponentInNewPrefab((ZenjectSampleGameMode)worldSettings.GameModeClass).AsSingle().NonLazy();
            UnityEngine.Debug.Log($"Install bindings for Zenject");

            //  Initialize GameMode
            IWorldSettings zenjectWorldSettings = Container.Resolve<IWorldSettings>();
            IUnityObjectSpawner objectSpawner = Container.Resolve<IUnityObjectSpawner>();
            IGameMode gameMode = Container.Resolve<IGameMode>();
            ((ZenjectSampleGameMode)gameMode).Initialize(objectSpawner, zenjectWorldSettings);
        }
    }
}