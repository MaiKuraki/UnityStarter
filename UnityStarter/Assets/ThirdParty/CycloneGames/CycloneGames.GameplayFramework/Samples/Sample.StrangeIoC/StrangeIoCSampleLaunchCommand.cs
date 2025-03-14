using CycloneGames.GameplayFramework;
using strange.extensions.command.impl;

public class StrangeIoCSampleLaunchCommand : Command
{
    [Inject] public IGameMode GameMode { get; set; }
    public override void Execute()
    {
        GameMode.LaunchGameMode();
    }
}
