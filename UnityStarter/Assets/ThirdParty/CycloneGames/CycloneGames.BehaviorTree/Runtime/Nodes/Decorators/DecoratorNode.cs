using CycloneGames.BehaviorTree.Runtime.Data;
using CycloneGames.BehaviorTree.Runtime.Interfaces;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Decorators
{
    public abstract class DecoratorNode : BTNode
    {
        public override bool CanReEvaluate => Child != null && Child.CanReEvaluate;
        public override bool EnableHijack => Child != null && Child.EnableHijack;
        [HideInInspector] public BTNode Child;

        public override void Inject(object container)
        {
            base.Inject(container);
            Child?.Inject(container);
        }

        public override BTNode Clone()
        {
            var clone = (DecoratorNode)base.Clone();
            if (Child != null)
            {
                clone.Child = Application.isPlaying ? Child.Clone() : null;
            }
            return clone;
        }
        public override BTState Evaluate(IBlackBoard blackBoard)
        {
            if (Child == null) return BTState.SUCCESS;
            if (!Child.CanReEvaluate) return BTState.SUCCESS;
            return OnEvaluate(blackBoard);
        }
        protected virtual BTState OnEvaluate(IBlackBoard blackBoard) => Child.Evaluate(blackBoard);
        protected override void OnStop(IBlackBoard blackBoard)
        {
            Child?.BTStop(blackBoard);
        }
    }
}