namespace CycloneGames.BehaviorTree.Runtime.Core
{
    public interface IRuntimeBTServiceResolver
    {
        T Resolve<T>() where T : class;
    }
}
