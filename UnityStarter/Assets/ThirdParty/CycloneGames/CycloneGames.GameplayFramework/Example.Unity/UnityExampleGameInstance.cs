using System;
using CycloneGames.GameplayFramework;

/// <summary>
/// Example GameInstance Singleton
/// </summary>
public class UnityExampleGameInstance
{
    private static readonly Lazy<UnityExampleGameInstance> _instance = new Lazy<UnityExampleGameInstance>(() => new UnityExampleGameInstance());
    public static UnityExampleGameInstance Instance => _instance.Value;

    public World World { get; private set; }
    public void InitializeWorld()
    {
        World = new World();
    }
}
