using CycloneGames.BehaviorTree.Runtime.Attributes;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Decorators
{
    [BTInfo("Flow Control", "Waits for a specified delay before executing the child node.")]
    public class DelayNode : DecoratorNode
    {
        [SerializeField] private float _delaySeconds = 1f;
        [SerializeField] private bool _useUnscaledTime = false;

        public override BTNode Clone()
        {
            var clone = (DelayNode)base.Clone();
            clone._delaySeconds = _delaySeconds;
            clone._useUnscaledTime = _useUnscaledTime;
            return clone;
        }

        public override CycloneGames.BehaviorTree.Runtime.Core.RuntimeNode CreateRuntimeNode()
        {
            var node = new CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators.RuntimeDelayNode();
            node.GUID = GUID;
            node.DelaySeconds = _delaySeconds;
            node.UseUnscaledTime = _useUnscaledTime;
            SetRuntimeChild(node);
            return node;
        }
    }
}
