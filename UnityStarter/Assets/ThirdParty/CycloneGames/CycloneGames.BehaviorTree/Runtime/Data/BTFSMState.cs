using System;
using CycloneGames.BehaviorTree.Runtime;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Data
{
    [Serializable]
    public class BTFSMState
    {
        public string ID => _id;
        [SerializeField] private string _id;
        [SerializeField] private BehaviorTree _tree;
        private BehaviorTree _behaviorTree = null;
        public BehaviorTree GetTree(GameObject owner)
        {
            if (_behaviorTree == null)
            {
                _behaviorTree = _tree.Clone(owner);
            }
            return _behaviorTree;
        }
    }
}