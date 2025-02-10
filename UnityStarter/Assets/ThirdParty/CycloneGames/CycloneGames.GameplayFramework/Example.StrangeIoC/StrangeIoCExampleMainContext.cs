using CycloneGames.Core;
using strange.extensions.context.api;
using strange.extensions.context.impl;
using UnityEngine;
using strange.extensions.signal.impl;

namespace CycloneGames.GameplayFramework.Example.StrangeIoC
{
    public class StartGameSignal : Signal
    {

    }
    public class StrangeIoCExampleMainContext : MVCSContext
    {
        const string StartGameCommand = "StartGame";
        public StrangeIoCExampleMainContext(MonoBehaviour view, IWorldSettings worldSettings)
            : base(view, ContextStartupFlags.MANUAL_MAPPING)
        {
            this.worldSettingsInst = worldSettings;

            Start();
        }

        private IWorldSettings worldSettingsInst;

        protected override void mapBindings()
        {
            base.mapBindings();

            injectionBinder.Bind<IObjectSpawner>().To<StrangeIoCExampleObjectSpawner>().ToSingleton();
            injectionBinder.Bind<IWorldSettings>().ToValue(worldSettingsInst);
            var objectSpawner = injectionBinder.GetInstance<IObjectSpawner>();
            StrangeIoCExampleGameMode strangeIoCExampleGameMode = objectSpawner.SpawnObject(((WorldSettings)worldSettingsInst).GameModeClass) as StrangeIoCExampleGameMode;
            injectionBinder.Bind<IGameMode>().ToValue(strangeIoCExampleGameMode);

            injectionBinder.Bind<StartGameSignal>().ToSingleton();
            commandBinder.Bind<StartGameSignal>().To<StrangeIoCExampleLaunchCommand>();
        }

        public override void Launch()
        {
            base.Launch();
            var gameMode = injectionBinder.GetInstance<IGameMode>();
            var objectSpawner = injectionBinder.GetInstance<IObjectSpawner>();
            ((StrangeIoCExampleGameMode)gameMode).Initialize(objectSpawner, worldSettingsInst);
            var startGameSignal = injectionBinder.GetInstance<StartGameSignal>();
            startGameSignal.Dispatch();
        }
    }
}