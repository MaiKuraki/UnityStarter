namespace CycloneGames.BehaviorTree.Runtime.Core
{
    public interface IRuntimeBTCommand
    {
        RuntimeState Execute(RuntimeBlackboard blackboard);
    }

    public interface IRuntimeBTConditionStrategy
    {
        bool Evaluate(RuntimeBlackboard blackboard);
    }
}
