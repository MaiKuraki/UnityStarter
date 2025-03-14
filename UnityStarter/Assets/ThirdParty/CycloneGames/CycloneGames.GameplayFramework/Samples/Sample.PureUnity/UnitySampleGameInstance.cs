using System;

namespace CycloneGames.GameplayFramework.Sample.PureUnity
{
    /// <summary>
    /// Example GameInstance Singleton
    /// </summary>
    public class UnitySampleGameInstance
    {
        private static readonly Lazy<UnitySampleGameInstance> _instance = new Lazy<UnitySampleGameInstance>(() => new UnitySampleGameInstance());
        public static UnitySampleGameInstance Instance => _instance.Value;

        public World World { get; private set; }
        public void InitializeWorld()
        {
            World = new World();
        }
    }
}