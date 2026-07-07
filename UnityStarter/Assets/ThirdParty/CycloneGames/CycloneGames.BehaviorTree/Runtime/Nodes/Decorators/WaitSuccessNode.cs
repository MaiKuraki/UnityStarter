using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Decorators
{
    public class WaitSuccessNode : DecoratorNode
    {
        public bool UseRandomBetweenTwoConstants => _useRandomBetweenTwoConstants;

        [SerializeField] private bool _useRandomBetweenTwoConstants = false;
        [SerializeField] private Vector2 _waitTimeRange = new Vector2(1f, 2f);
        [SerializeField] private float _waitTime = 1f;
        [SerializeField] private bool _useUnscaledTime = false;

        public override BTNode Clone()
        {
            var clone = (WaitSuccessNode)base.Clone();
            clone._waitTime = _waitTime;
            clone._useRandomBetweenTwoConstants = _useRandomBetweenTwoConstants;
            clone._waitTimeRange = _waitTimeRange;
            clone._useUnscaledTime = _useUnscaledTime;
            return clone;
        }

        public override CycloneGames.BehaviorTree.Runtime.Core.RuntimeNode CreateRuntimeNode()
        {
            var node = new CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators.RuntimeWaitSuccessNode();
            node.GUID = GUID;
            node.WaitTime = _waitTime;
            node.UseRandomRange = _useRandomBetweenTwoConstants;
            node.RangeMin = _waitTimeRange.x;
            node.RangeMax = _waitTimeRange.y;
            node.UseUnscaledTime = _useUnscaledTime;
            SetRuntimeChild(node);
            return node;
        }
    }
}
