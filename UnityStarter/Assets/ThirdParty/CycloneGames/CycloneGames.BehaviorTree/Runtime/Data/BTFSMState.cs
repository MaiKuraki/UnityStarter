using System;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Data
{
    [Serializable]
    public class BTFSMState
    {
        public string ID => _id;
        [SerializeField] private string _id;
        [SerializeField] private BehaviorTree _tree;

        public BehaviorTree GetTree()
        {
            return _tree;
        }
    }
}