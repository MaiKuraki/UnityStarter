using strange.extensions.command.impl;
using Cysharp.Threading.Tasks;
using System.Threading;
using CycloneGames.GameplayFramework.Runtime;

namespace CycloneGames.GameplayFramework.Sample.StrangeIoC
{
    public class StrangeIoCSampleLaunchCommand : Command
    {
        [Inject] public GameInstance GameInstance { get; set; }

        [Inject] public WorldSettings WorldSettings { get; set; }

        public override void Execute()
        {
            Retain();
            LaunchWorldAsync(CancellationToken.None).Forget();
        }

        private async UniTask LaunchWorldAsync(CancellationToken cancellation)
        {
            try
            {
                await GameInstance.StartWorldAsync(
                    WorldSettings,
                    WorldNetMode.Standalone,
                    cancellationToken: cancellation);
            }
            finally
            {
                Release();
            }
        }
    }
}
