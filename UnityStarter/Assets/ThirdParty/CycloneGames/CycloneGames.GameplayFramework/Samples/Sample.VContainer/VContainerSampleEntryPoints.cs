using System.Threading;
using Cysharp.Threading.Tasks;
using VContainer;
using VContainer.Unity;
using CycloneGames.GameplayFramework.Runtime;

namespace CycloneGames.GameplayFramework.Sample.VContainer
{
    public class VContainerSampleEntryPoints : IAsyncStartable
    {
        [Inject] private IGameMode gameMode;
        
        public async UniTask StartAsync(CancellationToken cancellation)
        {
            await gameMode.LaunchGameModeAsync(cancellation);
        }
    }
}
