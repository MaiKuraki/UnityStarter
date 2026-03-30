using CycloneGames.BehaviorTree.Runtime.Attributes;
using CycloneGames.BehaviorTree.Runtime.Data;
using CycloneGames.BehaviorTree.Runtime.Interfaces;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Decorators
{
    [BTInfo("Flow Control", "Executes the child only once. Subsequent ticks return the cached result.")]
    public class RunOnceNode : DecoratorNode
    {
        private bool _hasRun;
        private BTState _cachedResult;

        protected override void OnStart(IBlackBoard blackBoard)
        {
            _hasRun = false;
            _cachedResult = BTState.NOT_ENTERED;
        }

        protected override BTState OnRun(IBlackBoard blackBoard)
        {
            if (_hasRun) return _cachedResult;
            var state = Child.Run(blackBoard);
            if (state != BTState.RUNNING)
            {
                _hasRun = true;
                _cachedResult = state;
            }
            return state;
        }

        public override CycloneGames.BehaviorTree.Runtime.Core.RuntimeNode CreateRuntimeNode()
        {
            var node = new CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators.RuntimeRunOnceNode();
            node.GUID = GUID;
            if (Child != null) node.Child = Child.CreateRuntimeNode();
            return node;
        }
    }
}
