using CycloneGames.BehaviorTree.Runtime.Data;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Decorators
{
    public class InvertNode : DecoratorNode
    {
        protected override void OnStart(BlackBoard blackBoard) { }
        protected override BTState OnEvaluate(BlackBoard blackBoard)
        {
            var result = Child.Evaluate(blackBoard);
            if (result == BTState.SUCCESS)
            {
                return BTState.FAILURE;
            }
            return BTState.SUCCESS;
        }

        protected override BTState OnRun(BlackBoard blackBoard)
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
        protected override void OnStop(BlackBoard blackBoard) { }
    }
}