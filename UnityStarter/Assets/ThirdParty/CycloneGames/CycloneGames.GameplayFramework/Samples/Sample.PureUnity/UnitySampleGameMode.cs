using CycloneGames.Logging;

namespace CycloneGames.GameplayFramework.Runtime.Sample.PureUnity
{
    public sealed class UnitySampleGameMode : GameMode
    {
        private static readonly LogChannel Log = GameplayFrameworkSampleLog.Channel;

        public override void Initialize(World targetWorld, IGameSession session = null)
        {
            base.Initialize(targetWorld, session);
            Log.Info("Unity sample authoritative rules initialized.");
        }
    }
}
