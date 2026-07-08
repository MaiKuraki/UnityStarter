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

    }
}
