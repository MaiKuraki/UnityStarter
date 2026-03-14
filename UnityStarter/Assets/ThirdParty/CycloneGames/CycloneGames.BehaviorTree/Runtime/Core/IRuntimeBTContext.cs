using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Core
{
    public interface IRuntimeBTContext
    {
        GameObject OwnerGameObject { get; }
        IRuntimeBTServiceResolver ServiceResolver { get; }

        T GetOwner<T>() where T : class;
        T GetService<T>() where T : class;
    }
}