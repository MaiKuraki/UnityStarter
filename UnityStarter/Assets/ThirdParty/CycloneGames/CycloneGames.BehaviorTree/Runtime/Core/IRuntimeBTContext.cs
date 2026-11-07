namespace CycloneGames.BehaviorTree.Runtime.Core
{
    public interface IRuntimeBTTimeProvider
    {
        double TimeAsDouble { get; }
        double UnscaledTimeAsDouble { get; }
    }

    public interface IRuntimeBTRandomProvider
    {
        float Range(float minInclusive, float maxInclusive);
    }

    public interface IRuntimeBTContext
    {
        IRuntimeBTServiceResolver ServiceResolver { get; }

        T GetOwner<T>() where T : class;
        T GetService<T>() where T : class;
    }
}
