using CycloneGames.BehaviorTree.Runtime.Data;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Actions
{
    public class WaitNode : ActionNode
    {
        public bool UseRandomBetweenTwoConstants => _useRandomBetweenTwoConstants;
        [SerializeField] private bool _useRandomBetweenTwoConstants = false;
        [SerializeField] private Vector2 _range = new Vector2(1f, 2f);
        [SerializeField] private float _duration = 1f;
        private float _time;
        protected override void OnStart(BlackBoard blackBoard)
        {
            _time = 0;
            if (_useRandomBetweenTwoConstants)
            {
                _duration = Random.Range(_range.x, _range.y);
            }
        }
        protected override BTState OnRun(BlackBoard blackBoard)
        {
            _time += Time.deltaTime;
            return _time < _duration ? BTState.RUNNING : BTState.SUCCESS;
        }
    }
}