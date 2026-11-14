using strange.extensions.context.api;
using strange.extensions.context.impl;
using strange.extensions.signal.impl;
using UnityEngine;
using CycloneGames.Factory.Runtime;
using CycloneGames.GameplayFramework.Runtime;

namespace CycloneGames.GameplayFramework.Sample.StrangeIoC
{
    public class StartGameSignal : Signal
    {
    }

    public class StrangeIoCSampleMainContext : MVCSContext
    {
        private readonly MonoBehaviour view;
        private readonly WorldSettings worldSettings;

        public StrangeIoCSampleMainContext(MonoBehaviour view, WorldSettings worldSettings)
            : base(view, ContextStartupFlags.MANUAL_MAPPING)
        {
            if (worldSettings == null)
            {
                throw new System.InvalidOperationException(
                    "StrangeIoCSampleMainContext requires WorldSettings.");
            }

            this.view = view;
            this.worldSettings = worldSettings;

            Start();
        }

        protected override void mapBindings()
        {
            base.mapBindings();

            injectionBinder.Bind<WorldSettings>().ToValue(worldSettings);
            injectionBinder.Bind<IUnityObjectSpawner>().To<StrangeIoCSampleObjectSpawner>().ToSingleton();

            // The world discovers scene Actors only through an explicit source, and creates them through an
            // IActorLifetime rather than the container directly. Both are wired here so the container stays
            // the composition root without leaking StrangeIoC types into the framework.
            IUnityObjectSpawner objectSpawner = injectionBinder.GetInstance<IUnityObjectSpawner>();
            var gameInstance = new GameInstance(
                new StrangeIoCSampleActorLifetime(objectSpawner),
                actorSource: new SceneWorldActorSource(view.gameObject.scene));

            injectionBinder.Bind<GameInstance>().ToValue(gameInstance);
            injectionBinder.Bind<StartGameSignal>().ToSingleton();
            commandBinder.Bind<StartGameSignal>().To<StrangeIoCSampleLaunchCommand>();
        }

        public override void Launch()
        {
            base.Launch();
            injectionBinder.GetInstance<StartGameSignal>().Dispatch();
        }
    }
}
