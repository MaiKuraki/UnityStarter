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
                case BTState.RUNNING:
                    return BTState.RUNNING;
                case BTState.FAILURE:
                    return BTState.SUCCESS;
                case BTState.SUCCESS:
                    return BTState.FAILURE;
            }
            return BTState.SUCCESS;
        }
        protected override void OnStop(IBlackBoard blackBoard) { }
    }
}