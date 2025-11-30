using CycloneGames.BehaviorTree.Runtime.Data;
using CycloneGames.BehaviorTree.Runtime.Interfaces;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Decorators
{
    public class RepeatNode : DecoratorNode
    {
        public bool RepeatForever => _repeatForever;
        public bool UseRandomRepeatCount => _useRandomRepeatCount;

        [SerializeField] private bool _repeatForever = true;
        [SerializeField] private bool _useRandomRepeatCount = false;
        [SerializeField] private int _repeatCount = 1;
        [SerializeField] private Vector2 _randomRepeatCountRange = new Vector2(1, 3);
        private int _currentRepeatCount = 0;

        protected override void OnStart(IBlackBoard blackBoard)
        {
            if (_repeatForever) return;
            _repeatCount = _useRandomRepeatCount ? Random.Range((int)_randomRepeatCountRange.x, (int)_randomRepeatCountRange.y) : _repeatCount;
            _currentRepeatCount = 0;
        }

        protected override BTState OnRun(IBlackBoard blackBoard)
        {
            var state = Child.Run(blackBoard);
            if (_repeatForever)
            {
                return BTState.RUNNING;
            }
            if (state is BTState.SUCCESS or BTState.FAILURE)
            {
                _currentRepeatCount++;
            }
            return _currentRepeatCount >= _repeatCount ? BTState.SUCCESS : BTState.RUNNING;
        }

        protected override void CheckIntegrity()
        {
            base.CheckIntegrity();
            if (_repeatForever)
            {
                _useRandomRepeatCount = false;
            }

            // Validate random range
            if (_useRandomRepeatCount && _randomRepeatCountRange.x > _randomRepeatCountRange.y)
            {
                float temp = _randomRepeatCountRange.x;
                _randomRepeatCountRange.x = _randomRepeatCountRange.y;
                _randomRepeatCountRange.y = temp;
            }
        }

        public override BTNode Clone()
        {
            var clone = (RepeatNode)base.Clone();
            clone._repeatForever = _repeatForever;
            clone._useRandomRepeatCount = _useRandomRepeatCount;
            clone._repeatCount = _repeatCount;
            clone._randomRepeatCountRange = _randomRepeatCountRange;
            return clone;
        }
    }
}