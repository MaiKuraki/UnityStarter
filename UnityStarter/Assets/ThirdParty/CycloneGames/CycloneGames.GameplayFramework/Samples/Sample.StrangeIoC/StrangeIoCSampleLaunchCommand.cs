using strange.extensions.command.impl;
using Cysharp.Threading.Tasks;
using System.Threading;
using CycloneGames.GameplayFramework.Runtime;

public class StrangeIoCSampleLaunchCommand : Command
{
    [Inject] public IGameMode GameMode { get; set; }
    public override void Execute()
    {
        Retain();
        LaunchGameModeAsync(CancellationToken.None).Forget();
    }

    async UniTask LaunchGameModeAsync(CancellationToken cancel)
    {
        await GameMode.LaunchGameModeAsync(cancel);
        Release();
    }
}