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

        protected override void CheckIntegrity()
        {
            base.CheckIntegrity();
            if (_repeatForever)
            {
                _useRandomRepeatCount = false;
            }

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

        public override CycloneGames.BehaviorTree.Runtime.Core.RuntimeNode CreateRuntimeNode()
        {
            var node = new CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators.RuntimeRepeatNode();
            node.GUID = GUID;
            node.RepeatForever = _repeatForever;
            node.RepeatCount = _repeatCount;
            node.UseRandomRepeatCount = _useRandomRepeatCount;
            node.RandomRangeMin = (int)_randomRepeatCountRange.x;
            node.RandomRangeMax = (int)_randomRepeatCountRange.y;
            SetRuntimeChild(node);
            return node;
        }
    }
}
