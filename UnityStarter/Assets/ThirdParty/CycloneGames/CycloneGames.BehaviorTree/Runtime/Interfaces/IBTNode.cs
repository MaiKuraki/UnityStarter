using CycloneGames.BehaviorTree.Runtime.Data;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Interfaces
{
    public interface IBTNode
    {
        BTState State { get; }
        bool IsStarted { get; }
        bool CanReEvaluate { get; }
        bool EnableHijack { get; }
        Vector2 Position { get; set; }
        string GUID { get; }

        BTState Run(IBlackBoard blackBoard);
        BTState Evaluate(IBlackBoard blackBoard);
        void BTStop(IBlackBoard blackBoard);

        /// <summary>
        /// Inject dependencies into the node.
        /// </summary>
        /// <param name="container">Dependency container (e.g. VContainer, Zenject)</param>
        void Inject(object container);

        void OnAwake();
        void OnValidate();
        void OnDrawGizmos();
    }
}