using CycloneGames.GameplayFramework;
using CycloneGames.Logger;
using strange.extensions.command.impl;

public class StrangeIoCExampleLaunchCommand : Command
{
    [Inject] public IGameMode GameMode { get; set; }
    public override void Execute()
    {
        GameMode.LaunchGameMode();
    }
}
