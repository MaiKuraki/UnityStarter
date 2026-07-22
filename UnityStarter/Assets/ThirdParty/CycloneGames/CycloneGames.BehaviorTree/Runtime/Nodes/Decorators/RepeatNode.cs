using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Decorators
{
    public class RepeatNode : DecoratorNode
    {
        [SerializeField] private bool _repeatForever = true;
        [SerializeField] private bool _useRandomRepeatCount = false;
        [SerializeField] private int _repeatCount = 1;
        [SerializeField] private Vector2 _randomRepeatCountRange = new Vector2(1, 3);

        public bool RepeatForever => _repeatForever;
        public bool UseRandomRepeatCount => _useRandomRepeatCount;
        public int RepeatCount => _repeatCount;
        public Vector2 RandomRepeatCountRange => _randomRepeatCountRange;

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
