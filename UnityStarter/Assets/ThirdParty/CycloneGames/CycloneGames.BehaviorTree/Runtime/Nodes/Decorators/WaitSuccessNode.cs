using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Decorators
{
    public class WaitSuccessNode : DecoratorNode
    {
        [SerializeField] private bool _useRandomBetweenTwoConstants = false;
        [SerializeField] private Vector2 _waitTimeRange = new Vector2(1f, 2f);
        [SerializeField] private float _waitTime = 1f;
        [SerializeField] private bool _useUnscaledTime = false;

        public bool UseRandomBetweenTwoConstants => _useRandomBetweenTwoConstants;
        public Vector2 WaitTimeRange => _waitTimeRange;
        public float WaitTime => _waitTime;
        public bool UseUnscaledTime => _useUnscaledTime;

        public override BTNode Clone()
        {
            var clone = (WaitSuccessNode)base.Clone();
            clone._waitTime = _waitTime;
            clone._useRandomBetweenTwoConstants = _useRandomBetweenTwoConstants;
            clone._waitTimeRange = _waitTimeRange;
            clone._useUnscaledTime = _useUnscaledTime;
            return clone;
        }

    }
}
