using Zenject;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Sample.Zenject
{
    public class ZenjectSampleSceneInstaller : MonoInstaller
    {
        [SerializeField] WorldSettings worldSettings;

        public override void InstallBindings()
        {
            Container.Bind<CycloneGames.Factory.IFactory<MonoBehaviour, MonoBehaviour>>().To<ZenjectSampleObjectSpawner>().AsSingle().NonLazy();
            Container.BindInstance<IWorldSettings>(worldSettings).AsSingle().NonLazy();
            Container.Bind<IGameMode>().FromComponentInNewPrefab((ZenjectSampleGameMode)worldSettings.GameModeClass).AsSingle().NonLazy();
            UnityEngine.Debug.Log($"Install bindings for Zenject");

            //  Initialize GameMode
            IWorldSettings zenjectWorldSettings = Container.Resolve<IWorldSettings>();
            CycloneGames.Factory.IFactory<MonoBehaviour, MonoBehaviour> objectSpawner = Container.Resolve<CycloneGames.Factory.IFactory<MonoBehaviour, MonoBehaviour>>();
            IGameMode gameMode = Container.Resolve<IGameMode>();
            ((ZenjectSampleGameMode)gameMode).Initialize(objectSpawner, zenjectWorldSettings);
        }
    }
}