using CycloneGames.BehaviorTree.Runtime.Data;
using CycloneGames.BehaviorTree.Runtime.Interfaces;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Actions
{
    public class WaitNode : ActionNode
    {
        public bool UseRandomBetweenTwoConstants => _useRandomBetweenTwoConstants;
        [SerializeField] private bool _useRandomBetweenTwoConstants = false;
        [SerializeField] private Vector2 _range = new Vector2(1f, 2f);
        [SerializeField] private float _duration = 1f;

        public float Duration { get => _duration; set => _duration = value; }

        private float _time;

        protected override void OnStart(IBlackBoard blackBoard)
        {
            _time = 0;
            if (_useRandomBetweenTwoConstants)
            {
                _duration = Random.Range(_range.x, _range.y);
            }
        }
        public override BTNode Clone()
        {
            WaitNode node = (WaitNode)base.Clone();
            node.Duration = Duration;
            return node;
        }

        public override CycloneGames.BehaviorTree.Runtime.Core.RuntimeNode CreateRuntimeNode()
        {
            var node = new CycloneGames.BehaviorTree.Runtime.Core.Nodes.Actions.RuntimeWaitNode();
            node.GUID = GUID;
            node.Duration = _useRandomBetweenTwoConstants ? Random.Range(_range.x, _range.y) : _duration;
            return node;
        }
        protected override BTState OnRun(IBlackBoard blackBoard)
        {
            _time += Time.deltaTime;
            return _time < _duration ? BTState.RUNNING : BTState.SUCCESS;
        }
    }
}