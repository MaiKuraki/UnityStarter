using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Zenject;
using CycloneGames.GameplayFramework.Runtime;

namespace CycloneGames.GameplayFramework.Sample.Zenject
{
    public class ZenjectSampleSceneLogic : MonoBehaviour
    {
        [Inject] GameInstance gameInstance;
        [Inject] WorldSettings worldSettings;

        // Starting from Start, not Awake: World Actor discovery requires the Scene to be loaded, which
        // Unity only marks after every Awake and OnEnable in it has run.
        void Start()
        {
            LaunchWorldAsync(this.GetCancellationTokenOnDestroy()).Forget();
        }

        async UniTask LaunchWorldAsync(CancellationToken cancellation)
        {
            await gameInstance.StartWorldAsync(
                worldSettings,
                WorldNetMode.Standalone,
                cancellationToken: cancellation);
        }
    }
}
