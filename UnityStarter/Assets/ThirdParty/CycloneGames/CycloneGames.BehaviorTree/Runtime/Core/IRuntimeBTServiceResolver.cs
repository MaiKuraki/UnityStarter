namespace CycloneGames.BehaviorTree.Core
{
    public interface IRuntimeBTServiceResolver
    {
        T Resolve<T>() where T : class;
    }
}
