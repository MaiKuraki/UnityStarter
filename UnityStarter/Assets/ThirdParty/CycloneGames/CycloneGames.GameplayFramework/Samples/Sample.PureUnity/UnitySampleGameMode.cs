using CycloneGames.Factory.Runtime;
using CycloneGames.Logger;

namespace CycloneGames.GameplayFramework.Runtime.Sample.PureUnity
{
    public class UnitySampleGameMode : GameMode
    {
        private const string DEBUG_FLAG = "<color=cyan>[UnitySampleGameMode]</color>";

        public override void Initialize(IUnityObjectSpawner objectSpawner, IWorldSettings worldSettings)
        {
            base.Initialize(objectSpawner, worldSettings);
            CLogger.LogInfo($"{DEBUG_FLAG} Initialize completed. Override this method to plug custom match rules.");
        }
    }
}
