using CycloneGames.BehaviorTree.Runtime.Data;
using CycloneGames.BehaviorTree.Runtime.Nodes;
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

        BTState Run(BlackBoard blackBoard);
        BTState Evaluate(BlackBoard blackBoard);
        void BTStop(BlackBoard blackBoard);
        
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
