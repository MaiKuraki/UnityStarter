namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Actions
{
    public class RuntimeDebugLogNode : RuntimeNode
    {
        private string _message;

        public string Message
        {
            get => _message;
            set => SetSetupValue(ref _message, value);
        }

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
