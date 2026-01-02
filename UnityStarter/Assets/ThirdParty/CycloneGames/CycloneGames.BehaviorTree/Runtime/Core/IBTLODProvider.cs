namespace CycloneGames.BehaviorTree.Runtime.Core
{
    public interface IBTLODProvider
    {
        int GetPriority(RuntimeBehaviorTree tree);
        int GetTickInterval(RuntimeBehaviorTree tree);
        void BoostPriority(RuntimeBehaviorTree tree, float duration);
        void UpdateLOD(RuntimeBehaviorTree tree);
    }
}
