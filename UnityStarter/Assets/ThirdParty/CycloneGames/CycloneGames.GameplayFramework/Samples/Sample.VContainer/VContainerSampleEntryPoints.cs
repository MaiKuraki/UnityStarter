using System.Threading;
using CycloneGames.GameplayFramework.Runtime;
using Cysharp.Threading.Tasks;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace CycloneGames.GameplayFramework.Sample.VContainer
{
    public sealed class VContainerSampleEntryPoints :
        IAsyncStartable,
        ITickable,
        IFixedTickable,
        ILateTickable
    {
        private readonly GameInstance gameInstance;
        private readonly WorldSettings worldSettings;

        [Inject]
        public VContainerSampleEntryPoints(GameInstance gameInstance, WorldSettings worldSettings)
        {
            this.gameInstance = gameInstance;
            this.worldSettings = worldSettings;
        }

        public async UniTask StartAsync(CancellationToken cancellation)
        {
            await gameInstance.StartWorldAsync(
                worldSettings,
                WorldNetMode.Standalone,
                cancellationToken: cancellation);
        }

        public void Tick()
        {
            gameInstance.Tick(ActorTickPhase.Update, Time.deltaTime);
        }

        public void FixedTick()
        {
            gameInstance.Tick(ActorTickPhase.FixedUpdate, Time.fixedDeltaTime);
        }

        public void LateTick()
        {
            gameInstance.Tick(ActorTickPhase.LateUpdate, Time.deltaTime);
        }
    }
}
