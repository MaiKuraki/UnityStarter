using CycloneGames.BehaviorTree.Runtime.Data;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Interfaces
{
    public interface IBehaviorTree
    {
        BTState TreeState { get; }
        GameObject Owner { get; }
        bool IsCloned { get; }

        BTState BTUpdate(IBlackBoard blackBoard);
        void Stop();

        /// <summary>
        /// Inject dependencies into the tree and its nodes.
        /// </summary>
        /// <param name="container">Dependency container</param>
        void Inject(object container);

        IBehaviorTree Clone(GameObject owner);
    }
}