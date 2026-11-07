namespace CycloneGames.BehaviorTree.Runtime.Core
{
    /// <summary>
    /// Implemented by nodes that subscribe to blackboard keys. Binding happens once during
    /// graph commit; unbinding happens on disposal or on a rejected graph construction.
    /// </summary>
    internal interface IRuntimeBTObserverNode
    {
        void BindObservers(RuntimeBlackboard blackboard, RuntimeBehaviorTree tree);
        void UnbindObservers();
    }
}
