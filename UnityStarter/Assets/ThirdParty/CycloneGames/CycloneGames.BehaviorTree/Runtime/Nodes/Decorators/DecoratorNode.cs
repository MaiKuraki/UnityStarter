using CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Decorators
{
    public abstract class DecoratorNode : BTNode
    {
        [HideInInspector] public BTNode Child;

        public override BTNode Clone()
        {
            var clone = (DecoratorNode)base.Clone();
            if (Child != null)
            {
                clone.Child = Application.isPlaying ? Child.Clone() : null;
            }
            return clone;
        }

        protected void SetRuntimeChild(RuntimeDecoratorNode runtimeNode)
        {
            runtimeNode.Child = CreateRequiredRuntimeNode(Child, "decorator child");
        }
    }
}
