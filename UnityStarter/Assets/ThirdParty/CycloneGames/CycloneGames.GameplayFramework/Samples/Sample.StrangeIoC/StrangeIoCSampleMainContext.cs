using strange.extensions.context.api;
using strange.extensions.context.impl;
using UnityEngine;
using strange.extensions.signal.impl;
using CycloneGames.Factory;

namespace CycloneGames.GameplayFramework.Sample.StrangeIoC
{
    public class StartGameSignal : Signal
    {

    }
    public class StrangeIoCSampleMainContext : MVCSContext
    {
        const string StartGameCommand = "StartGame";
        public StrangeIoCSampleMainContext(MonoBehaviour view, IWorldSettings worldSettings)
            : base(view, ContextStartupFlags.MANUAL_MAPPING)
        {
            this.worldSettingsInst = worldSettings;

            Start();
        }

        private IWorldSettings worldSettingsInst;

        protected override void mapBindings()
        {
            base.mapBindings();

            injectionBinder.Bind<IFactory<MonoBehaviour, MonoBehaviour>>().To<StrangeIoCSampleObjectSpawner>().ToSingleton();
            injectionBinder.Bind<IWorldSettings>().ToValue(worldSettingsInst);
            IFactory<MonoBehaviour, MonoBehaviour> objectSpawner = injectionBinder.GetInstance<IFactory<MonoBehaviour, MonoBehaviour>>();
            StrangeIoCSampleGameMode strangeIoCSampleGameMode = objectSpawner.Create(((WorldSettings)worldSettingsInst).GameModeClass) as StrangeIoCSampleGameMode;
            injectionBinder.Bind<IGameMode>().ToValue(strangeIoCSampleGameMode);

            injectionBinder.Bind<StartGameSignal>().ToSingleton();
            commandBinder.Bind<StartGameSignal>().To<StrangeIoCSampleLaunchCommand>();
        }

        public override void Launch()
        {
            base.Launch();
            var gameMode = injectionBinder.GetInstance<IGameMode>();
            IFactory<MonoBehaviour, MonoBehaviour> objectSpawner = injectionBinder.GetInstance<IFactory<MonoBehaviour, MonoBehaviour>>();
            ((StrangeIoCSampleGameMode)gameMode).Initialize(objectSpawner, worldSettingsInst);
            var startGameSignal = injectionBinder.GetInstance<StartGameSignal>();
            startGameSignal.Dispatch();
        }
    }
}