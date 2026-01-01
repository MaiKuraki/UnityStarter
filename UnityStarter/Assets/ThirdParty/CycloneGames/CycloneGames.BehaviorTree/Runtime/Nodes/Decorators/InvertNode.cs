using CycloneGames.BehaviorTree.Runtime.Data;
using CycloneGames.BehaviorTree.Runtime.Interfaces;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Decorators
{
    public class InvertNode : DecoratorNode
    {
        protected override void OnStart(IBlackBoard blackBoard) { }
        protected override BTState OnEvaluate(IBlackBoard blackBoard)
        {
            var result = Child.Evaluate(blackBoard);
            if (result == BTState.SUCCESS)
            {
                return BTState.FAILURE;
            }
            return BTState.SUCCESS;
        }

        protected override BTState OnRun(IBlackBoard blackBoard)
        {
            switch (Child.Run(blackBoard))
            {
                case BTState.FAILURE: return BTState.SUCCESS;
                case BTState.SUCCESS: return BTState.FAILURE;
                case BTState.RUNNING: return BTState.RUNNING;
            }
            return BTState.FAILURE;
        }

        public override CycloneGames.BehaviorTree.Runtime.Core.RuntimeNode CreateRuntimeNode()
        {
            var node = new CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators.RuntimeInverterNode();
            node.GUID = GUID;
            if (Child != null)
            {
                node.Child = Child.CreateRuntimeNode();
            }
            return node;
        }
        protected override void OnStop(IBlackBoard blackBoard) { }
    }
}