using CycloneGames.Logging;

namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Actions
{
    public class RuntimeDebugLogNode : RuntimeNode
    {
        private static readonly LogChannel Log = BehaviorTreeRuntimeLog.Channel;

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
                Log.Debug(Message);
            }
#endif
            return RuntimeState.Success;
        }
    }
}
