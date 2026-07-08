using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Nodes
{
    public class BTRootNode : BTNode
    {
        [HideInInspector] public BTNode Child;

        public override BTNode Clone()
        {
            var clone = (BTRootNode)base.Clone();
            if (Child != null)
            {
                clone.Child = Child.Clone();
            }
            return clone;
        }

    }
}
