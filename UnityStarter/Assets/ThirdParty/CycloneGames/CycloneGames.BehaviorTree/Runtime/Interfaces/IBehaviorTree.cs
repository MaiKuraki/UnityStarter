using System;
using System.Collections.Generic;
using CycloneGames.BehaviorTree.Runtime.Data;
using CycloneGames.BehaviorTree.Runtime.Nodes;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Interfaces
{
    public interface IBehaviorTree
    {
        BTState TreeState { get; }
        GameObject Owner { get; }
        bool IsCloned { get; }
        
        BTState BTUpdate(BlackBoard blackBoard);
        void Stop();
        
        /// <summary>
        /// Inject dependencies into the tree and its nodes.
        /// </summary>
        /// <param name="container">Dependency container</param>
        void Inject(object container);

        IBehaviorTree Clone(GameObject owner);
    }
}
