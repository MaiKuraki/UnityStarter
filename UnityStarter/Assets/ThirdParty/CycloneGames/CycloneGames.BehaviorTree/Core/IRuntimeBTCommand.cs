namespace CycloneGames.BehaviorTree.Core
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
