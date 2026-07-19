using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime.Sample.PureUnity
{
    public sealed class UnitySampleGameMode : GameMode
    {
        private const string DebugFlag = "<color=cyan>[UnitySampleGameMode]</color>";

        public override void Initialize(World targetWorld, IGameSession session = null)
        {
            base.Initialize(targetWorld, session);
            Debug.Log($"{DebugFlag} Authoritative rules initialized.", this);
        }
    }
}
