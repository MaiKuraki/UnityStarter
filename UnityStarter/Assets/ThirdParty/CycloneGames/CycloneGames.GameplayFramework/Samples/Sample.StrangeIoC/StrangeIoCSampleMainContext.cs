using strange.extensions.context.api;
using strange.extensions.context.impl;
using UnityEngine;
using strange.extensions.signal.impl;
using CycloneGames.Factory.Runtime;
using CycloneGames.GameplayFramework.Runtime;

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

            injectionBinder.Bind<IUnityObjectSpawner>().To<StrangeIoCSampleObjectSpawner>().ToSingleton();
            injectionBinder.Bind<IWorldSettings>().ToValue(worldSettingsInst);
            IUnityObjectSpawner objectSpawner = injectionBinder.GetInstance<IUnityObjectSpawner>();
            StrangeIoCSampleGameMode strangeIoCSampleGameMode = objectSpawner.Create(((WorldSettings)worldSettingsInst).GameModeClass) as StrangeIoCSampleGameMode;
            injectionBinder.Bind<IGameMode>().ToValue(strangeIoCSampleGameMode);

            injectionBinder.Bind<StartGameSignal>().ToSingleton();
            commandBinder.Bind<StartGameSignal>().To<StrangeIoCSampleLaunchCommand>();
        }

        public override void Launch()
        {
            base.Launch();
            var gameMode = injectionBinder.GetInstance<IGameMode>();
            IUnityObjectSpawner objectSpawner = injectionBinder.GetInstance<IUnityObjectSpawner>();
            ((StrangeIoCSampleGameMode)gameMode).Initialize(objectSpawner, worldSettingsInst);
            var startGameSignal = injectionBinder.GetInstance<StartGameSignal>();
            startGameSignal.Dispatch();
        }
    }
}