using CycloneGames.BehaviorTree.Runtime.Core;
using CycloneGames.BehaviorTree.Runtime.Core.Nodes;
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

        public override RuntimeNode CreateRuntimeNode()
        {
            var node = new RuntimeRootNode();
            node.GUID = GUID;
            node.Child = CreateRequiredRuntimeNode(Child, "root child");
            return node;
        }
    }
}
