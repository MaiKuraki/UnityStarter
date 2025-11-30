namespace CycloneGames.BehaviorTree.Runtime.Nodes.Compositors
{
    public enum ConditionalAbortType
    {
        NONE,
        /// <summary>
        /// End the current node if the condition returns failure
        /// </summary>
        SELF,
        /// <summary>
        /// End the lower priority nodes and change the current node to the selected node
        /// </summary>
        LOWER_PRIORITY,
        /// <summary>
        /// Use both SELF and LOWER_PRIORITY
        /// </summary>
        BOTH
    }
}