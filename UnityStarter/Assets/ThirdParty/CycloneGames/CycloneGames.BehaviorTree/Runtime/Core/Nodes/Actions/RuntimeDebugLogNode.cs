namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Actions
{
    public class RuntimeDebugLogNode : RuntimeNode
    {
        public string Message { get; set; }

        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!string.IsNullOrEmpty(Message))
            {
#if UNITY_5_3_OR_NEWER
                UnityEngine.Debug.Log("[RuntimeBT] " + Message);
#else
                System.Console.WriteLine("[RuntimeBT] " + Message);
#endif
            }
#endif
            return RuntimeState.Success;
        }
    }
}
