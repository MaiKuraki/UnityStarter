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
            if (worldSettings == null)
            {
                throw new System.InvalidOperationException(
                    "ZenjectSampleSceneInstaller requires WorldSettings.");
            }

            Container.BindInstance(worldSettings);
            Container.Bind<IUnityObjectSpawner>().To<ZenjectSampleObjectSpawner>().AsSingle().NonLazy();
            Container.Bind<IActorLifetime>().To<ZenjectSampleActorLifetime>().AsSingle().NonLazy();

            // The World discovers scene Actors only through an explicit source, and creates them through an
            // IActorLifetime. Both are wired here so the container stays the composition root without
            // leaking Zenject types into the framework.
            Container.Bind<GameInstance>()
                .FromMethod(context => new GameInstance(
                    context.Container.Resolve<IActorLifetime>(),
                    actorSource: new SceneWorldActorSource(gameObject.scene)))
                .AsSingle()
                .NonLazy();
        }
    }
}
