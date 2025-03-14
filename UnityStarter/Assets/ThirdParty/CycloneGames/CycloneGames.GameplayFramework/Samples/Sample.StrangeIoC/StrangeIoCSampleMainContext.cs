using CycloneGames.Core;
using strange.extensions.context.api;
using strange.extensions.context.impl;
using UnityEngine;
using strange.extensions.signal.impl;

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

            injectionBinder.Bind<IObjectSpawner>().To<StrangeIoCSampleObjectSpawner>().ToSingleton();
            injectionBinder.Bind<IWorldSettings>().ToValue(worldSettingsInst);
            var objectSpawner = injectionBinder.GetInstance<IObjectSpawner>();
            StrangeIoCSampleGameMode strangeIoCSampleGameMode = objectSpawner.SpawnObject(((WorldSettings)worldSettingsInst).GameModeClass) as StrangeIoCSampleGameMode;
            injectionBinder.Bind<IGameMode>().ToValue(strangeIoCSampleGameMode);

            injectionBinder.Bind<StartGameSignal>().ToSingleton();
            commandBinder.Bind<StartGameSignal>().To<StrangeIoCSampleLaunchCommand>();
        }

        public override void Launch()
        {
            base.Launch();
            var gameMode = injectionBinder.GetInstance<IGameMode>();
            var objectSpawner = injectionBinder.GetInstance<IObjectSpawner>();
            ((StrangeIoCSampleGameMode)gameMode).Initialize(objectSpawner, worldSettingsInst);
            var startGameSignal = injectionBinder.GetInstance<StartGameSignal>();
            startGameSignal.Dispatch();
        }
    }
}