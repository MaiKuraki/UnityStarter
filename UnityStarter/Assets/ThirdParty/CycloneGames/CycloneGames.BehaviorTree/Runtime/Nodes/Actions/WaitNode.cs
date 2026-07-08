using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Actions
{
    public class WaitNode : ActionNode
    {
        public bool UseRandomBetweenTwoConstants => _useRandomBetweenTwoConstants;
        [SerializeField] private bool _useRandomBetweenTwoConstants = false;
        [SerializeField] private Vector2 _range = new Vector2(1f, 2f);
        [SerializeField] private float _duration = 1f;
        [SerializeField] private bool _useUnscaledTime = false;

        internal Vector2 Range => _range;
        internal bool UseUnscaledTime => _useUnscaledTime;

        public float Duration { get => _duration; set => _duration = value; }

        public override BTNode Clone()
        {
            WaitNode node = (WaitNode)base.Clone();
            node.Duration = Duration;
            node._useRandomBetweenTwoConstants = _useRandomBetweenTwoConstants;
            node._range = _range;
            node._useUnscaledTime = _useUnscaledTime;
            return node;
        }

    }
}
