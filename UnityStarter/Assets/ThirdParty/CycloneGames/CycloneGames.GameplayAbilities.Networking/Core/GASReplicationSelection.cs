using CycloneGames.Networking;
using CycloneGames.Networking.Replication;

namespace CycloneGames.GameplayAbilities.Networking
{
    /// <summary>
    /// GAS-facing selection returned by <see cref="GASReplicationPlanner"/>.
    /// </summary>
    public readonly struct GASReplicationSelection
    {
        public readonly GASReplicationSource Source;
        public readonly NetworkReplicationSelection NetworkSelection;

        public GASReplicationSelection(in GASReplicationSource source, in NetworkReplicationSelection networkSelection)
        {
            Source = source;
            NetworkSelection = networkSelection;
        }

        public uint NetworkId => Source.NetworkId;
        public int SourceIndex => NetworkSelection.SourceIndex;
        public NetworkChannel Channel => NetworkSelection.Channel;
        public NetworkInterestReason Reason => NetworkSelection.Reason;
        public int EstimatedPayloadBytes => NetworkSelection.EstimatedPayloadBytes;
        public bool RequiresFullState => NetworkSelection.RequiresFullState;
        public float Score => NetworkSelection.Score;
    }
}
