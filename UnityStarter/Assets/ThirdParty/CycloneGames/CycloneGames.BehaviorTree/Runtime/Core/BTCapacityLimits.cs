namespace CycloneGames.BehaviorTree.Runtime.Core
{
    /// <summary>
    /// Shared ceilings for the managed and data-oriented runtimes.
    /// Graph validation, scheduling and flat-tree validation consume the same values so
    /// depth and capacity bounds cannot drift between execution paths.
    /// </summary>
    public static class BTCapacityLimits
    {
        public const int MaxTreeDepth = 256;
        public const int MaxNodeCount = 65_536;
        public const int DefaultNodeCount = 4_096;

        public const int MaxAgentCount = 1_048_576;
        public const int DefaultAgentCount = 65_536;

        public const int PriorityLevelCount = 8;
        public const int DefaultTickManagerCapacity = 1_024;
        public const int DefaultPriorityBucketCapacity = 256;
    }
}
