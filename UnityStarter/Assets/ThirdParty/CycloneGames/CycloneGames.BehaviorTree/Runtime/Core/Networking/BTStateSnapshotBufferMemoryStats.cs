namespace CycloneGames.BehaviorTree.Runtime.Core.Networking
{
    public readonly struct BTStateSnapshotBufferMemoryStats
    {
        public BTStateSnapshotBufferMemoryStats(
            int nodeStateCapacity,
            int nodeAuxiliaryCapacity,
            int traversalNodeCapacity,
            int traversalStackCapacity,
            int blackboardStreamCapacityBytes,
            int snapshotStreamCapacityBytes)
        {
            NodeStateCapacity = nodeStateCapacity;
            NodeAuxiliaryCapacity = nodeAuxiliaryCapacity;
            TraversalNodeCapacity = traversalNodeCapacity;
            TraversalStackCapacity = traversalStackCapacity;
            BlackboardStreamCapacityBytes = blackboardStreamCapacityBytes;
            SnapshotStreamCapacityBytes = snapshotStreamCapacityBytes;
        }

        public int NodeStateCapacity { get; }
        public int NodeAuxiliaryCapacity { get; }
        public int TraversalNodeCapacity { get; }
        public int TraversalStackCapacity { get; }
        public int BlackboardStreamCapacityBytes { get; }
        public int SnapshotStreamCapacityBytes { get; }
    }
}
