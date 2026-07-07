using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Decorators
{
    public class CoolDownNode : DecoratorNode
    {
        [SerializeField] private float _coolDown = 1f;
        [SerializeField] private bool _resetOnSuccess = false;

        public override BTNode Clone()
        {
            var clone = (CoolDownNode)base.Clone();
            clone._coolDown = _coolDown;
            clone._resetOnSuccess = _resetOnSuccess;
            return clone;
        }

        public override CycloneGames.BehaviorTree.Runtime.Core.RuntimeNode CreateRuntimeNode()
        {
            var node = new CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators.RuntimeCoolDownNode();
            node.GUID = GUID;
            node.CoolDown = _coolDown;
            node.ResetOnSuccess = _resetOnSuccess;
            SetRuntimeChild(node);
            return node;
        }
    }
}
